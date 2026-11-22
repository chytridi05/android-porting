using System.Text.RegularExpressions;

namespace Porting.Core.Inspection;

public sealed class GradleProjectInspector : IProjectInspector
{
    private const string QuotedString = "(?<quote>['\"])(?<value>[^'\"\r\n]*)\\k<quote>";
    private const string LiteralList = @"\s*(?:" + QuotedString + @"(?:\s*,\s*" + QuotedString + @")*\s*,?\s*)?";
    private const string JvmIdentifier = @"[\p{L}_$][\p{L}\p{N}\p{M}_$]*";
    private const string JvmName = JvmIdentifier + @"(?:\." + JvmIdentifier + ")*";
    private const string ModuleName = @"\A:?[\w.-]+(?::[\w.-]+)*\z";
    private const string ProjectPath = @"\bproject\s*\(\s*" + QuotedString + @"\s*\)";
    private static readonly Regex CommentsAndStrings = new(""""(?<string>"""[\s\S]*?"""|'''[\s\S]*?'''|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*')|//[^\r\n]*|/\*[\s\S]*?\*/"""", RegexOptions.CultureInvariant);

    public async Task<ProjectInspection> InspectAsync(string sourceDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        string root = Path.GetFullPath(sourceDirectory);
        string? settingsFile = null;
        string? gradleVersion = null;
        string? gameEntry = null;
        string[] assets = [];
        GradleModule? android = null;
        GradleModule? shared = null;
        GradleModule? desktop = null;
        List<PortDiagnostic> diagnostics = [];

        try
        {
            settingsFile = FindScript(root, "settings");
            if (!Directory.Exists(root))
            {
                diagnostics.Add(new("inspection.source-missing", DiagnosticSeverity.Error, "Choose an existing project directory."));
            }
            else if (settingsFile is null)
            {
                diagnostics.Add(new("inspection.settings-missing", DiagnosticSeverity.Error, "Choose the Gradle project root containing settings.gradle or settings.gradle.kts."));
            }
            else
            {
                const string wrapperProperties = "gradle/wrapper/gradle-wrapper.properties";
                string wrapperPath = Path.Combine(root, wrapperProperties);
                if (File.Exists(wrapperPath))
                {
                    string wrapper = await File.ReadAllTextAsync(wrapperPath, cancellationToken);
                    Match distribution = Regex.Match(wrapper, @"(?m)^\s*distributionUrl\s*[:=]\s*[^\r\n]*[\\/]gradle-(?<version>[0-9][\w.-]*)-(?:bin|all)\.zip(?:[?#][^\r\n]*)?\s*$");
                    if (distribution.Success)
                    {
                        gradleVersion = distribution.Groups["version"].Value;
                    }
                }

                if (gradleVersion is null)
                {
                    diagnostics.Add(new("inspection.wrapper-unknown", DiagnosticSeverity.Warning, "The Gradle Wrapper version could not be read. Check the wrapper distribution URL before building.", wrapperProperties));
                }

                string settings = await ReadCodeAsync(Path.Combine(root, settingsFile), cancellationToken);
                GradleModule[] modules = await ReadModulesAsync(root, settingsFile, settings, diagnostics, cancellationToken);
                android = ResolveModule(modules.Where(static module => module.Script.Contains("com.badlogicgames.gdx:gdx-backend-android:", StringComparison.Ordinal)),
                    "Android libGDX", "inspection.android-module", settingsFile, diagnostics);

                GradleModule[] desktops = modules.Where(static module =>
                    module.Script.Contains("com.badlogicgames.gdx:gdx-backend-lwjgl", StringComparison.Ordinal)
                    || (module.ProjectPath.Split(':')[^1] is "desktop" or "lwjgl3")).ToArray();
                if (desktops.Length > 0)
                {
                    desktop = ResolveModule(desktops, "desktop", "inspection.desktop-module", settingsFile, diagnostics);
                    if (desktop is not null && !desktop.Script.Contains("com.badlogicgames.gdx:gdx-backend-lwjgl3:", StringComparison.Ordinal))
                    {
                        diagnostics.Add(new("inspection.desktop-backend", DiagnosticSeverity.Error,
                            $"Desktop module {desktop.ProjectPath} needs a confirmed LWJGL3 backend before it can be reused.", desktop.ScriptPath ?? settingsFile));
                    }
                }

                if (android is not null)
                {
                    HashSet<string> references = Regex.Matches(android.Script, ProjectPath)
                        .Select(static match => ":" + match.Groups["value"].Value.TrimStart(':')).ToHashSet(StringComparer.Ordinal);
                    GradleModule[] dependencies = modules.Where(module => references.Contains(module.ProjectPath) && module != android && !desktops.Contains(module)).ToArray();
                    GradleModule[] gameModules = dependencies.Where(static module => module.HasLibGdxCore).ToArray();
                    IEnumerable<GradleModule> candidates = gameModules.Length > 0 ? gameModules : dependencies.Where(static module => module.ProjectPath == ":core");
                    if (dependencies.Length == 0)
                    {
                        candidates = modules.Where(module => module != android && !desktops.Contains(module) && module.HasLibGdxCore);
                    }

                    shared = ResolveModule(candidates, "shared libGDX", "inspection.shared-module", android.ScriptPath ?? settingsFile, diagnostics);
                    assets = FindAssets(root, android, shared, diagnostics);

                    if (shared is not null)
                    {
                        gameEntry = await FindGameEntryAsync(root, android, shared, diagnostics, cancellationToken);
                        if (gameEntry is null)
                        {
                            diagnostics.Add(new("inspection.entry-unresolved", DiagnosticSeverity.Warning,
                                "The Android launcher does not identify a unique shared game type. Supply GameEntryType in the port options.", android.ScriptPath));
                        }
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new("inspection.read-failed", DiagnosticSeverity.Error, $"Project files could not be read: {exception.Message}"));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new(root, settingsFile, gradleVersion, android?.ProjectPath, shared?.ProjectPath, desktop?.ProjectPath, gameEntry, Array.AsReadOnly(assets), diagnostics.AsReadOnly());
    }

    private static async Task<GradleModule[]> ReadModulesAsync(string root, string settingsFile, string settings,
        List<PortDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        HashSet<string> includes = new(StringComparer.Ordinal);
        bool unsupported = Regex.IsMatch(settings, @"\bincludeFlat\s*[(\s]|\.buildFileName\s*=");
        foreach (Match include in Regex.Matches(settings, @"(?m)(?:^|;)\s*include\b[ \t]*(?:\((?<args>[^)]*)\)|(?<args>[^\r\n;]*))"))
        {
            string[]? paths = ReadStringList(include.Groups["args"].Value);
            if (paths is null || paths.Any(static path => !Regex.IsMatch(path, ModuleName) || path.Split(':').Any(static part => part is "." or "..")))
            {
                unsupported = true;
                continue;
            }

            includes.UnionWith(paths.Select(static path => ":" + path.TrimStart(':')));
        }

        Dictionary<string, string> locations = new(StringComparer.Ordinal);
        const string projectDirectoryProperty = @"\s*\.\s*projectDir\s*=";
        MatchCollection mappings = Regex.Matches(settings, ProjectPath + projectDirectoryProperty);
        unsupported |= Regex.Matches(settings, projectDirectoryProperty).Count != mappings.Count;
        foreach (Match assignment in mappings)
        {
            string value = settings[(assignment.Index + assignment.Length)..];
            Match literal = Regex.Match(value, @"\A\s*file\s*\(\s*" + QuotedString + @"\s*\)");
            if (literal.Success && !literal.Groups["value"].Value.Contains('$'))
            {
                locations[":" + assignment.Groups["value"].Value.TrimStart(':')] = literal.Groups["value"].Value;
            }
            else
            {
                unsupported = true;
            }
        }

        if (unsupported)
        {
            diagnostics.Add(new("inspection.settings-unsupported", DiagnosticSeverity.Error,
                "Gradle module declarations could not be fully resolved. Use literal include paths and projectDir = file(...) mappings.", settingsFile));
        }

        List<GradleModule> modules = [];
        foreach (string module in includes.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = module[1..].Replace(':', '/');
            for (string prefix = module; prefix.Length > 0; prefix = prefix[..prefix.LastIndexOf(':')])
            {
                if (locations.TryGetValue(prefix, out string? location))
                {
                    path = location + module[prefix.Length..].Replace(':', '/');
                    break;
                }
            }

            string? directory = ProjectRelativePath(root, root, path);
            if (directory is null || !Directory.Exists(Path.Combine(root, directory)))
            {
                diagnostics.Add(new("inspection.module-directory", DiagnosticSeverity.Error,
                    $"Module {module} must point to an existing directory inside the source project.", settingsFile));
                continue;
            }

            string? buildFile = FindScript(Path.Combine(root, directory), "build");
            string? scriptPath = buildFile is null ? null : $"{directory}/{buildFile}";
            string script = scriptPath is null ? "" : await ReadCodeAsync(Path.Combine(root, scriptPath), cancellationToken);
            modules.Add(new(module, directory, scriptPath, script));
        }

        return [.. modules];
    }

    private static string? FindScript(string directory, string name)
    {
        string groovy = $"{name}.gradle";
        string kotlin = $"{groovy}.kts";
        return File.Exists(Path.Combine(directory, groovy)) ? groovy
            : File.Exists(Path.Combine(directory, kotlin)) ? kotlin : null;
    }

    private static async Task<string> ReadCodeAsync(string path, CancellationToken cancellationToken, bool keepStrings = true)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        return CommentsAndStrings.Replace(text, match => keepStrings && match.Groups["string"].Success
            ? match.Value : Regex.Replace(match.Value, @"[^\r\n]+", " "));
    }

    private static string[]? ReadStringList(string text)
    {
        if (!Regex.IsMatch(text, @"\A" + LiteralList + @"\z"))
        {
            return null;
        }

        return Regex.Matches(text, QuotedString).Select(static match => match.Groups["value"].Value).ToArray();
    }

    private static GradleModule? ResolveModule(IEnumerable<GradleModule> candidates, string role, string code,
        string sourceFile, List<PortDiagnostic> diagnostics)
    {
        GradleModule[] matches = candidates.ToArray();
        if (matches.Length == 1)
        {
            return matches[0];
        }

        string message = matches.Length == 0
            ? $"No {role} module was found. Check the Gradle includes and libGDX dependencies."
            : $"Multiple {role} modules were found: {string.Join(", ", matches.Select(static module => module.ProjectPath))}.";
        diagnostics.Add(new(code, DiagnosticSeverity.Error, message, sourceFile));
        return null;
    }

    private static string[] FindAssets(string root, GradleModule android, GradleModule? shared, List<PortDiagnostic> diagnostics)
    {
        const string assetDeclaration = @"\bassets\s*\.\s*(?:srcDirs\s*(?<assignment>=)|(?<replace>setSrcDirs)\s*\("
            + @"|srcDirs?\s*\(|srcDirs?[ \t]+(?<bare>(?=['""])))";
        const string listCapture = "(?<list>" + LiteralList + ")";
        const string kotlinList = @"listOf\s*\(" + listCapture + @"\)";
        List<string> directories = [];
        bool useDefaults = true;
        foreach (Match declaration in Regex.Matches(android.Script, assetDeclaration))
        {
            bool assignment = declaration.Groups["assignment"].Success;
            if (assignment || declaration.Groups["replace"].Success)
            {
                directories.Clear();
                useDefaults = false;
            }

            string arguments = android.Script[(declaration.Index + declaration.Length)..];
            Match literal = assignment
                ? Regex.Match(arguments, @"\A\s*(?:\[" + listCapture + @"\]|" + kotlinList + @")(?=[ \t]*(?:[\r\n;}]|\z))")
                : declaration.Groups["bare"].Success
                    ? Regex.Match(arguments, @"\A" + listCapture + @"(?=[\r\n;}]|\z)")
                    : Regex.Match(arguments, @"\A\s*(?:" + kotlinList + @"|" + listCapture + @")\s*\)");
            if (!literal.Success)
            {
                diagnostics.Add(new("inspection.assets-unresolved", DiagnosticSeverity.Warning,
                    "An asset declaration uses an unsupported expression. Supply AssetDirectories to set the desktop lookup order.", android.ScriptPath));
                continue;
            }

            foreach (Match pathLiteral in Regex.Matches(literal.Groups["list"].Value, QuotedString))
            {
                string path = pathLiteral.Groups["value"].Value;
                string? relative = path.Contains('$') ? null : ProjectRelativePath(root, Path.Combine(root, android.Directory), path);
                if (relative is null || !Directory.Exists(Path.Combine(root, relative)))
                {
                    diagnostics.Add(new("inspection.assets-unresolved", DiagnosticSeverity.Warning,
                        $"Asset directory '{path}' was not found inside the source project. Check the path or supply AssetDirectories.", android.ScriptPath));
                }
                else
                {
                    directories.Add(relative);
                }
            }
        }

        if (useDefaults)
        {
            directories.AddRange([$"{android.Directory}/src/main/assets", $"{android.Directory}/assets", "assets"]);
            if (shared is not null)
            {
                directories.Add($"{shared.Directory}/assets");
            }
        }

        return directories.Where(path => Directory.Exists(Path.Combine(root, path))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string? ProjectRelativePath(string root, string baseDirectory, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return null;
        }

        string relative = Path.GetRelativePath(root, Path.GetFullPath(path, baseDirectory)).Replace('\\', '/');
        return relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? null : relative;
    }

    private static async Task<string?> FindGameEntryAsync(string root, GradleModule android, GradleModule shared,
        List<PortDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        HashSet<string> sharedTypes = new(StringComparer.Ordinal);
        foreach (string file in EnumerateJvmSources(Path.Combine(root, shared.Directory)))
        {
            string source = await ReadCodeAsync(file, cancellationToken, keepStrings: false);
            Match androidImport = Regex.Match(source, @"(?m)^[ \t]*import[ \t]+(?:static[ \t]+)?(?:androidx?|com\.badlogic\.gdx\.backends\.android)\.");
            if (androidImport.Success)
            {
                string relativePath = Path.GetRelativePath(root, file).Replace('\\', '/');
                int lineNumber = source.AsSpan(0, androidImport.Index).Count('\n') + 1;
                diagnostics.Add(new("inspection.android-import", DiagnosticSeverity.Warning,
                    "Shared source imports an Android API. Adapt this dependency before building the desktop target.", relativePath, lineNumber));
            }

            string package = ReadPackage(source);
            foreach (Match type in Regex.Matches(source, @"\b(?:class|object)\s+(?<name>" + JvmIdentifier + ")"))
            {
                sharedTypes.Add(package + type.Groups["name"].Value);
            }
        }

        HashSet<string> entries = new(StringComparer.Ordinal);
        foreach (string file in EnumerateJvmSources(Path.Combine(root, android.Directory)))
        {
            string source = await ReadCodeAsync(file, cancellationToken, keepStrings: false);
            if (!Regex.IsMatch(source, @"\bAndroid(?:Application|FragmentApplication)\b"))
            {
                continue;
            }

            foreach (Match launch in Regex.Matches(source, @"\binitialize(?:ForView)?\s*\(\s*(?:new\s+)?(?<type>" + JvmName + @")\s*(?=[(,])"))
            {
                string name = launch.Groups["type"].Value;
                if (name.Contains('.'))
                {
                    if (sharedTypes.Contains(name))
                    {
                        entries.Add(name);
                    }

                    continue;
                }

                string samePackage = ReadPackage(source) + name;
                if (sharedTypes.Contains(samePackage))
                {
                    entries.Add(samePackage);
                }

                foreach (Match import in Regex.Matches(source, @"(?m)^\s*import[ \t]+(?<type>" + JvmName + @"(?:\.\*)?)(?:[ \t]+as[ \t]+(?<alias>" + JvmIdentifier + "))?"))
                {
                    string type = import.Groups["type"].Value;
                    string importedName = import.Groups["alias"].Success ? import.Groups["alias"].Value : type[(type.LastIndexOf('.') + 1)..];
                    if (importedName == "*")
                    {
                        type = type[..^1] + name;
                        importedName = name;
                    }

                    if (importedName == name && sharedTypes.Contains(type))
                    {
                        entries.Add(type);
                    }
                }
            }
        }

        return entries.Count == 1 ? entries.Single() : null;
    }

    private static string ReadPackage(string source)
    {
        Match package = Regex.Match(source, @"(?m)^\s*package[ \t]+(?<name>" + JvmName + ")");
        return package.Success ? package.Groups["name"].Value + "." : "";
    }

    private static IEnumerable<string> EnumerateJvmSources(string directory)
    {
        string main = Path.Combine(directory, "src", "main");
        string source = Directory.Exists(main) ? main : Path.Combine(directory, "src");
        if (!Directory.Exists(source))
        {
            return [];
        }

        EnumerationOptions options = new() { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        return Directory.EnumerateFiles(source, "*", options).Where(static path => Path.GetExtension(path) is ".java" or ".kt");
    }

    private sealed record GradleModule(string ProjectPath, string Directory, string? ScriptPath, string Script)
    {
        public bool HasLibGdxCore => Script.Contains("com.badlogicgames.gdx:gdx:", StringComparison.Ordinal);
    }
}