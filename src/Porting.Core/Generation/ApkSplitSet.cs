using Porting.Core.Inspection;
using System.IO.Compression;
using System.Xml.Linq;

namespace Porting.Core.Generation;

/// <summary>Identifies a verified split snapshot and its original archive location.</summary>
internal sealed record ApkSplitSnapshot(ApkInputFile File, string SnapshotPath, string PreservedPath)
{
    internal string Location => File.ContainerPath is null ? File.OriginalPath : $"{File.ContainerPath}!/{File.OriginalPath}";
}

/// <summary>Records staged feature DEX files and the types they define.</summary>
internal sealed record ApkSplitDex(IReadOnlyList<string> Paths, IReadOnlySet<string> Types);

/// <summary>Validates split manifests and stages feature DEX files for source recovery.</summary>
internal static class ApkSplitSet
{
    private static readonly XNamespace Android = "http://schemas.android.com/apk/res/android";

    /// <summary>Checks identity, required split names, configuration targets, and split types.</summary>
    internal static bool Validate(XElement main, string baseLocation, IReadOnlyList<(XElement Manifest, string Location)> splits,
        List<PortDiagnostic> diagnostics)
    {
        Dictionary<string, string> names = new(StringComparer.Ordinal);
        HashSet<string> providedTypes = new(StringComparer.Ordinal);
        foreach ((XElement manifest, string location) in splits)
        {
            string? name = (string?)manifest.Attribute("split");
            if (string.IsNullOrWhiteSpace(name))
            {
                diagnostics.Add(new("apk.split-manifest", DiagnosticSeverity.Error, "The supplied APK is missing its split name.", location));
                continue;
            }
            if (!names.TryAdd(name, location))
            {
                diagnostics.Add(new("apk.split-conflict", DiagnosticSeverity.Error, $"Split '{name}' was also supplied by '{names[name]}'.", location));
            }
            if ((string?)manifest.Attribute("package") != (string?)main.Attribute("package")
                || Version(manifest, "versionCode") != Version(main, "versionCode")
                || Version(manifest, "versionCodeMajor") != Version(main, "versionCodeMajor"))
            {
                diagnostics.Add(new("apk.split-identity", DiagnosticSeverity.Error,
                    "The split application ID or version differs from the base APK.", location));
            }

            providedTypes.UnionWith(Types(manifest, "splitTypes"));
        }

        if (splits.Count == 0 && (string?)main.Attribute(Android + "isSplitRequired") == "true")
        {
            diagnostics.Add(new("apk.split-missing", DiagnosticSeverity.Error, "The base APK requires splits. Supply the matching split APKs.", baseLocation));
        }

        foreach ((XElement manifest, string location) in splits.Prepend((main, baseLocation)))
        {
            string? configFor = (string?)manifest.Attribute("configForSplit") ?? (string?)manifest.Attribute(Android + "configForSplit");
            IEnumerable<string> required = manifest.Elements("uses-split").Select(element => (string?)element.Attribute(Android + "name")).OfType<string>();
            if (!string.IsNullOrEmpty(configFor))
            {
                required = required.Append(configFor);
            }

            foreach (string name in required.Distinct(StringComparer.Ordinal).Where(name => !names.ContainsKey(name)))
            {
                diagnostics.Add(new("apk.split-missing", DiagnosticSeverity.Error, $"Supply the required split '{name}'.", location));
            }
            foreach (string type in Types(manifest, "requiredSplitTypes").Where(type => !providedTypes.Contains(type)))
            {
                diagnostics.Add(new("apk.split-missing", DiagnosticSeverity.Error, $"Supply a split of the required type '{type}'.", location));
            }
        }

        return !diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>Stages split DEX input without merging Android resources or overwriting DEX entries.</summary>
    internal static async Task<ApkSplitDex> StageDexAsync(string main, string baseLocation, IReadOnlyList<ApkSplitSnapshot> splits,
        string workspace, List<PortDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        Dictionary<string, string> classes = new(StringComparer.Ordinal);
        List<string> paths = [];
        HashSet<string> types = new(StringComparer.Ordinal);
        string directory = Directory.CreateDirectory(Path.Combine(workspace, "split-dex")).FullName;
        foreach ((string snapshot, string location) in splits.Select(static split => (split.SnapshotPath, split.Location)).Prepend((main, baseLocation)))
        {
            using ZipArchive archive = ZipFile.OpenRead(snapshot);
            foreach (ZipArchiveEntry entry in archive.Entries.Where(static entry => ApkDex.IsDexEntry(entry.FullName)))
            {
                string entryLocation = $"{location}!/{entry.FullName}";
                try
                {
                    await using Stream stream = entry.Open();
                    using MemoryStream bytes = new();
                    await stream.CopyToAsync(bytes, cancellationToken);
                    byte[] dex = bytes.ToArray();
                    foreach (string type in new ApkDex(dex).ReadClasses())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!classes.TryAdd(type, entryLocation))
                        {
                            diagnostics.Add(new("apk.dex-conflict", DiagnosticSeverity.Error,
                                $"Class '{type}' is also defined in '{classes[type]}'.", entryLocation));
                        }
                        if (snapshot != main && type.Length > 2 && type[0] == 'L' && type[^1] == ';')
                        {
                            _ = types.Add(type[1..^1].Replace('/', '.'));
                        }
                    }

                    if (snapshot != main)
                    {
                        string path = Path.Combine(directory, $"split-{paths.Count + 1:D4}.dex");
                        await File.WriteAllBytesAsync(path, dex, cancellationToken);
                        paths.Add(path);
                    }
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or NotSupportedException)
                {
                    diagnostics.Add(new("apk.split-dex", DiagnosticSeverity.Error, exception.Message, entryLocation));
                }
            }
        }

        return new(paths, types);
    }

    private static string Version(XElement manifest, string name) => ((string?)manifest.Attribute(Android + name)) ?? "0";

    private static string[] Types(XElement manifest, string key)
    {
        return ((string?)manifest.Attribute(Android + key) ?? (string?)manifest.Attribute(key) ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
