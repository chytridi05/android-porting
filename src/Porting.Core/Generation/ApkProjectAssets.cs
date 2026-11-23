using Porting.Core.Inspection;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Porting.Core.Generation;

/// <summary>Records a supplied file, its exact copy, and whether it was opened as an expansion archive.</summary>
internal sealed record PreservedCompanion(ApkInputFile Input, string PreservedPath, string? DecodedFormat);

/// <summary>Identifies the origin of a resource available on the desktop classpath.</summary>
internal sealed record MappedApkResource(ApkInputFile Input, string ResourcePath);

/// <summary>Preserves supplied files and maps APK and ZIP expansion assets into a desktop project.</summary>
internal sealed class ApkProjectAssets(string project, List<PortDiagnostic> diagnostics)
{
    private readonly Dictionary<string, MappedApkResource> resources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> resourceDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PreservedCompanion> companions = [];
    private readonly List<MappedApkResource> origins = [];

    /// <summary>Gets details of the retained companion files.</summary>
    internal IReadOnlyList<PreservedCompanion> Companions => companions;

    /// <summary>Gets the origins of all successfully mapped resources, including identical duplicates.</summary>
    internal IReadOnlyList<MappedApkResource> Resources => origins;

    /// <summary>Gets the number of distinct resources available on the classpath.</summary>
    internal int ResourceCount => resources.Count;

    /// <summary>Copies companions and unpacks assets using Windows-safe relative paths.</summary>
    internal async Task ReadAsync(ApkInventory inventory, RecoveredApk recovered, ApkPortOptions options, string snapshot,
        CancellationToken cancellationToken)
    {
        HashSet<string> explicitObbs = new(options.ObbPaths.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        HashSet<string> opened = new(StringComparer.OrdinalIgnoreCase);
        string? version = recovered.VersionCode;
        HashSet<string> conventionalNames = new(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(version))
        {
            conventionalNames.Add($"main.{version}.{recovered.PackageName}.obb");
            conventionalNames.Add($"patch.{version}.{recovered.PackageName}.obb");
        }

        try
        {
            await ReadArchiveAsync(snapshot, inventory.ApkPath, isApk: true, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        {
            diagnostics.Add(new("apk.assets-read", DiagnosticSeverity.Error, exception.Message, inventory.ApkPath));
        }

        int index = 0;
        foreach (ApkInputFile file in inventory.Companions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fullPath = file.ContainerPath is null ? file.OriginalPath : Path.Combine(file.ContainerPath, file.OriginalPath);
            string relative = $"porting-inputs/{++index:D4}/contents";
            string destination = Path.Combine(project, relative);
            bool isObb = explicitObbs.Contains(fullPath) || conventionalNames.Contains(Path.GetFileName(fullPath));

            try
            {
                await CopyCheckedFileAsync(fullPath, destination, file.Sha256, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(new("apk.companion-copy", DiagnosticSeverity.Error, exception.Message, fullPath));
                continue;
            }

            if (isObb && !opened.Contains(fullPath))
            {
                try
                {
                    await ReadArchiveAsync(destination, fullPath, isApk: false, cancellationToken);
                    _ = opened.Add(fullPath);
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
                {
                    diagnostics.Add(new("apk.obb-decode", DiagnosticSeverity.Error, $"Could not read the ZIP expansion archive. {exception.Message}", fullPath));
                    continue;
                }
            }

            companions.Add(new(file, relative, isObb ? "zip-obb" : null));
            if (!isObb)
            {
                diagnostics.Add(new("apk.companion-preserved", DiagnosticSeverity.Information,
                    "Preserved the original bytes. Provide a ZIP expansion with --obb if the game needs its contents as resources.", fullPath));
            }
        }

        foreach (string path in explicitObbs.Where(Directory.Exists))
        {
            diagnostics.Add(new("apk.obb-file", DiagnosticSeverity.Error, "Supply a ZIP expansion file with --obb.", path));
        }

        if (recovered.UsesExpansion && !companions.Any(static file => file.DecodedFormat == "zip-obb"))
        {
            diagnostics.Add(new("apk.obb-missing", DiagnosticSeverity.Error,
                $"The Android launcher uses an expansion archive. Supply {string.Join(" or ", conventionalNames.DefaultIfEmpty("the original OBB"))} or use --obb.",
                inventory.ApkPath));
        }

        foreach (string asset in options.RequiredAssets)
        {
            if (!resources.TryGetValue(asset, out MappedApkResource? resource) || resource.ResourcePath != asset)
            {
                diagnostics.Add(new("apk.required-asset", DiagnosticSeverity.Error,
                    $"Supply the required resource '{asset}' in the APK or a ZIP expansion archive. Resource paths are case-sensitive.", inventory.ApkPath));
            }
        }
    }

    /// <summary>Copies a source file and verifies that its bytes still match the inventory.</summary>
    internal static async Task CopyCheckedFileAsync(string source, string destination, string sha256, CancellationToken cancellationToken)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using FileStream input = File.OpenRead(source);
        await CopyAndHashAsync(input, destination, sha256, cancellationToken);
    }

    private async Task ReadArchiveAsync(string snapshot, string container, bool isApk, CancellationToken cancellationToken)
    {
        using ZipArchive archive = ZipFile.OpenRead(snapshot);
        HashSet<string> entries = new(StringComparer.Ordinal);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith('/') || (isApk && !entry.FullName.StartsWith("assets/", StringComparison.Ordinal)))
            {
                continue;
            }

            string location = $"{container}!/{entry.FullName}";
            string target = isApk ? entry.FullName["assets/".Length..] : entry.FullName;
            if (!WindowsArchivePath.IsSafe(target))
            {
                diagnostics.Add(new("apk.asset-path", DiagnosticSeverity.Error, "The entry is not a valid relative Windows file path.", location));
                continue;
            }
            if (!entries.Add(entry.FullName))
            {
                diagnostics.Add(new("apk.asset-conflict", DiagnosticSeverity.Error, "The archive repeats this resource entry.", location));
                continue;
            }

            try
            {
                await ReadResourceAsync(entry, container, target, location, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
            {
                diagnostics.Add(new("apk.asset-read", DiagnosticSeverity.Error, exception.Message, location));
            }
        }
    }

    private async Task ReadResourceAsync(ZipArchiveEntry entry, string container, string target, string location,
        CancellationToken cancellationToken)
    {
        List<string> parents = [];
        for (int slash = target.IndexOf('/'); slash >= 0; slash = target.IndexOf('/', slash + 1))
        {
            parents.Add(target[..slash]);
        }

        if (resourceDirectories.ContainsKey(target) || parents.Any(parent => resources.ContainsKey(parent)
            || (resourceDirectories.TryGetValue(parent, out string? existing) && existing != parent)))
        {
            diagnostics.Add(new("apk.asset-conflict", DiagnosticSeverity.Error, $"The path '{target}' collides with a resource file or directory.", location));
            return;
        }

        await using Stream input = entry.Open();
        string digest;
        if (resources.TryGetValue(target, out MappedApkResource? previous))
        {
            digest = Convert.ToHexStringLower(await SHA256.HashDataAsync(input, cancellationToken));
            if (previous.ResourcePath != target || previous.Input.Sha256 != digest)
            {
                diagnostics.Add(new("apk.asset-conflict", DiagnosticSeverity.Error,
                    $"The path '{target}' conflicts with '{previous.Input.ContainerPath}!/{previous.Input.OriginalPath}'.", location));
                return;
            }
        }
        else
        {
            string destination = Path.Combine(project, "assets", target);
            _ = Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            digest = await CopyAndHashAsync(input, destination, null, cancellationToken);
        }

        MappedApkResource mapped = new(new(entry.FullName, container, entry.Length, digest), target);
        _ = resources.TryAdd(target, mapped);
        foreach (string parent in parents)
        {
            _ = resourceDirectories.TryAdd(parent, parent);
        }
        origins.Add(mapped);
    }

    private static async Task<string> CopyAndHashAsync(Stream input, string destination, string? expected,
        CancellationToken cancellationToken)
    {
        await using (FileStream output = File.Open(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        await using FileStream copy = File.OpenRead(destination);
        string digest = Convert.ToHexStringLower(await SHA256.HashDataAsync(copy, cancellationToken));
        if (expected is not null && digest != expected)
        {
            throw new IOException("The input changed after inspection. Retry with a stable copy.");
        }

        return digest;
    }
}