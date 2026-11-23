using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Porting.Core.Generation;

/// <summary>Identifies the application and desktop dependencies in a JADX export.</summary>
internal sealed record RecoveredApk(string PackageName, string? VersionCode, string EntryType, string LibGdxVersion,
    bool UsesBox2D, bool UsesExpansion, string SourceRoot, IReadOnlyList<string> SourceFiles);

/// <summary>Runs JADX and selects the libGDX application sources for the desktop project.</summary>
internal static class ApkSourceRecovery
{
    private const string TypeName = @"[A-Za-z_$][\w$]*(?:\.[A-Za-z_$][\w$]*)*";
    private static readonly XNamespace Android = "http://schemas.android.com/apk/res/android";
    private static readonly Regex Initialize = new(@"\binitialize\s*\(\s*new\s+(" + TypeName + @")\s*\(\s*\)\s*[,)]");
    private static readonly Regex LibGdxVersion = new("""\bVERSION\s*=\s*"([0-9]+\.[0-9]+\.[0-9]+)"(?:\.toString\(\))?\s*;""");
    private static readonly Regex AndroidImport = new(@"(?m)^\s*import\s+(?:static\s+)?(?:android\.|androidx\.|com\.badlogic\.gdx\.backends\.android\.)");

    /// <summary>Recovers a supported no-argument libGDX game entry and its Java source tree.</summary>
    internal static async Task<RecoveredApk?> RecoverAsync(string apk, string originalPath, IReadOnlyList<ApkSplitSnapshot> splits,
        string workspace, ApkPortOptions options, List<PortDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        string? jadx = options.JadxJar ?? Environment.GetEnvironmentVariable("PORTING_JADX_JAR");
        if (string.IsNullOrWhiteSpace(jadx) || !File.Exists(jadx))
        {
            diagnostics.Add(new("apk.jadx-missing", DiagnosticSeverity.Error,
                "Supply the JADX 1.5.3 command-line JAR with --jadx or PORTING_JADX_JAR.", jadx));
            return null;
        }

        string? javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        string java = string.IsNullOrWhiteSpace(javaHome) ? "java.exe" : Path.Combine(javaHome.Trim().Trim('"'), "bin", "java.exe");
        string decoded = Path.Combine(workspace, "decoded");
        XElement? manifest = null;
        ApkSplitDex? features = null;
        if (splits.Count > 0)
        {
            manifest = await DecodeManifestAsync(apk, originalPath, "manifest-base");
            if (manifest is null || !CheckBaseManifest(manifest, originalPath, diagnostics))
            {
                return null;
            }

            List<(XElement Manifest, string Location)> manifests = [];
            foreach (ApkSplitSnapshot split in splits)
            {
                XElement? splitManifest = await DecodeManifestAsync(split.SnapshotPath, split.Location, $"manifest-{manifests.Count:D4}");
                if (splitManifest is null)
                {
                    return null;
                }

                manifests.Add((splitManifest, split.Location));
            }

            if (!ApkSplitSet.Validate(manifest, originalPath, manifests, diagnostics))
            {
                return null;
            }

            features = await ApkSplitSet.StageDexAsync(apk, originalPath, splits, workspace, diagnostics, cancellationToken);
            if (diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return null;
            }
        }

        (int ExitCode, string Log)? run = await RunJadxAsync(decoded, originalPath, [apk, .. features?.Paths ?? []]);
        if (run is null)
        {
            return null;
        }
        int exitCode = run.Value.ExitCode;
        if (manifest is null)
        {
            manifest = await ReadManifestAsync(decoded, originalPath, exitCode, run.Value.Log, diagnostics, cancellationToken);
            if (manifest is null || !CheckBaseManifest(manifest, originalPath, diagnostics)
                || !ApkSplitSet.Validate(manifest, originalPath, [], diagnostics))
            {
                return null;
            }
        }

        string package = (string)manifest.Attribute("package")!;
        string javaRoot = Path.Combine(decoded, "sources");
        string[] launchers = FindLaunchers(manifest, package);
        string? launcher = launchers.Length == 1 ? launchers[0] : null;
        string? launcherFile = launcher is not null && IsTypeName(launcher) ? JavaPath(javaRoot, launcher) : null;
        string launcherText = launcherFile is not null && File.Exists(launcherFile)
            ? await File.ReadAllTextAsync(launcherFile, cancellationToken) : "";
        string? entry = options.EntryType ?? FindEntry(launcherText, launcher);
        if (entry is null || !IsTypeName(entry) || entry.LastIndexOf('.') < 1 || !File.Exists(JavaPath(javaRoot, entry)))
        {
            diagnostics.Add(new("apk.entry-unresolved", DiagnosticSeverity.Error,
                "Supply --entry with the fully qualified recovered libGDX listener type. The launcher must construct it without arguments.",
                launcher is null ? $"{originalPath}!/AndroidManifest.xml" : $"{originalPath}!/{launcher}"));
            return null;
        }

        string versionFile = JavaPath(javaRoot, "com.badlogic.gdx.Version");
        string version = File.Exists(versionFile)
            ? LibGdxVersion.Match(await File.ReadAllTextAsync(versionFile, cancellationToken)).Groups[1].Value : "";
        if (version.Length == 0)
        {
            diagnostics.Add(new("apk.gdx-version", DiagnosticSeverity.Error,
                "The APK does not expose a fixed libGDX version in com.badlogic.gdx.Version.", originalPath));
            return null;
        }

        string sourceDirectory = Path.GetDirectoryName(JavaPath(javaRoot, entry))!;
        string? launcherDirectory = launcherFile is null ? null : Path.GetDirectoryName(launcherFile);
        List<string> sources = [];
        bool box2D = false;
        HashSet<string> sourceCandidates = new(Directory.EnumerateFiles(sourceDirectory, "*.java", SearchOption.AllDirectories), StringComparer.OrdinalIgnoreCase);
        if (features is not null)
        {
            foreach (string type in features.Types.Where(IsTypeName))
            {
                string source = JavaPath(javaRoot, type);
                if (File.Exists(source))
                {
                    _ = sourceCandidates.Add(source);
                }
                else if (!type.Contains('$'))
                {
                    diagnostics.Add(new("apk.split-source", DiagnosticSeverity.Error,
                        $"JADX did not recover the split class '{type}'.", originalPath));
                }
            }
        }
        foreach (string file in sourceCandidates.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (file.Equals(launcherFile, StringComparison.OrdinalIgnoreCase)
                || (launcherDirectory is not null && !launcherDirectory.Equals(sourceDirectory, StringComparison.OrdinalIgnoreCase)
                    && IsBelow(launcherDirectory, sourceDirectory) && IsBelow(Path.GetDirectoryName(file)!, launcherDirectory))
                || Path.GetFileName(file) is "R.java" or "BuildConfig.java")
            {
                continue;
            }

            string contents = await File.ReadAllTextAsync(file, cancellationToken);
            string location = $"{originalPath}!/{Path.GetRelativePath(javaRoot, file).Replace('\\', '/')}";
            if (AndroidImport.IsMatch(contents))
            {
                diagnostics.Add(new("apk.android-api", DiagnosticSeverity.Error,
                    "The recovered game source imports Android APIs. Adapt the affected class to desktop APIs before building.", location));
            }
            if (contents.Contains("Method not decompiled:", StringComparison.Ordinal)
                || contents.Contains("JADX ERROR", StringComparison.Ordinal))
            {
                diagnostics.Add(new("apk.source-recovery", DiagnosticSeverity.Error,
                    "JADX did not recover this application class. Repair it in a readable source export before porting.", location));
            }

            box2D |= contents.Contains("com.badlogic.gdx.physics.box2d", StringComparison.Ordinal);
            sources.Add(file);
        }

        if (exitCode != 0 && !diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(new("apk.jadx-partial", DiagnosticSeverity.Warning,
                $"JADX exited with code {exitCode}. The selected application classes were recovered. Build to verify them.", originalPath));
        }

        return new(package, (string?)manifest.Attribute(Android + "versionCode"), entry, version, box2D,
            launcherText.Contains("setAPKExpansion(", StringComparison.Ordinal), javaRoot, sources.AsReadOnly());

        async Task<XElement?> DecodeManifestAsync(string input, string location, string name)
        {
            string directory = Path.Combine(workspace, name);
            (int ExitCode, string Log)? decodedManifest = await RunJadxAsync(directory, location, ["--no-src", input]);
            return decodedManifest is { } result
                ? await ReadManifestAsync(directory, location, result.ExitCode, result.Log, diagnostics, cancellationToken)
                : null;
        }

        async Task<(int ExitCode, string Log)?> RunJadxAsync(string directory, string location, IReadOnlyList<string> arguments)
        {
            ProcessStartInfo start = ProcessRunner.CreateStartInfo(java, workspace);
            foreach (string argument in new[] { "-cp", Path.GetFullPath(jadx), "jadx.cli.JadxCLI", "--threads-count", "4",
                "--log-level", "error", "-d", directory }.Concat(arguments))
            {
                start.ArgumentList.Add(argument);
            }

            using StringWriter log = new();
            using StringWriter errors = new();
            try
            {
                int result = await ProcessRunner.RunAsync(start, log, errors, cancellationToken);
                return (result, (log.ToString() + errors).Trim());
            }
            catch (Win32Exception exception)
            {
                diagnostics.Add(new("apk.jadx-start", DiagnosticSeverity.Error,
                    $"Set JAVA_HOME to a supported JDK. {exception.Message}", location));
                return null;
            }
        }
    }

    private static bool CheckBaseManifest(XElement manifest, string location, List<PortDiagnostic> diagnostics)
    {
        if (manifest.Name == "manifest" && (string?)manifest.Attribute("package") is string package
            && IsTypeName(package) && manifest.Attribute("split") is null)
        {
            return true;
        }

        diagnostics.Add(new("apk.manifest-package", DiagnosticSeverity.Error,
            "Supply a base APK with a valid Android application ID.", $"{location}!/AndroidManifest.xml"));
        return false;
    }

    private static async Task<XElement?> ReadManifestAsync(string decoded, string location, int exitCode, string log,
        List<PortDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        string manifest = Path.Combine(decoded, "resources", "AndroidManifest.xml");
        try
        {
            if (File.Exists(manifest))
            {
                await using FileStream input = File.OpenRead(manifest);
                return (await XDocument.LoadAsync(input, LoadOptions.None, cancellationToken)).Root;
            }

            diagnostics.Add(new("apk.manifest-decode", DiagnosticSeverity.Error,
                $"JADX did not decode the manifest (exit {exitCode}). {log}", $"{location}!/AndroidManifest.xml"));
        }
        catch (XmlException exception)
        {
            diagnostics.Add(new("apk.manifest-decode", DiagnosticSeverity.Error, exception.Message, $"{location}!/AndroidManifest.xml"));
        }

        return null;
    }

    private static string[] FindLaunchers(XElement manifest, string package)
    {
        return manifest.Elements("application").Elements()
            .Where(static element => (element.Name.LocalName is "activity" or "activity-alias")
                && element.Elements("intent-filter").Any(filter =>
                    filter.Elements("action").Any(action => (string?)action.Attribute(Android + "name") == "android.intent.action.MAIN")
                    && filter.Elements("category").Any(category => (string?)category.Attribute(Android + "name") == "android.intent.category.LAUNCHER")))
            .Select(element => (string?)element.Attribute(Android + (element.Name.LocalName == "activity-alias" ? "targetActivity" : "name")))
            .OfType<string>().Select(name => name.StartsWith('.') ? package + name : name.Contains('.') ? name : $"{package}.{name}")
            .Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string? FindEntry(string source, string? launcher)
    {
        Match match = Initialize.Match(source);
        if (!match.Success || !source.Contains("com.badlogic.gdx.backends.android", StringComparison.Ordinal))
        {
            return null;
        }

        string name = match.Groups[1].Value;
        if (name.Contains('.'))
        {
            return name;
        }

        Match import = Regex.Match(source, @"(?m)^\s*import\s+(" + TypeName + @"\." + Regex.Escape(name) + @")\s*;");
        if (import.Success)
        {
            return import.Groups[1].Value;
        }

        return launcher is not null && launcher.LastIndexOf('.') is int dot && dot > 0 ? $"{launcher[..dot]}.{name}" : null;
    }

    private static bool IsTypeName(string name) => Regex.IsMatch(name, @"\A" + TypeName + @"\z");

    private static string JavaPath(string root, string type) => Path.Combine(root, type.Replace('.', Path.DirectorySeparatorChar) + ".java");

    private static bool IsBelow(string path, string directory)
    {
        string relative = Path.GetRelativePath(directory, path);
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }
}
