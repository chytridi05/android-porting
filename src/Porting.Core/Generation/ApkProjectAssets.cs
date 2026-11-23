using Porting.Core.Inspection;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Porting.Core.Generation;

/// <summary>Records a supplied file, its exact copy, and whether it was opened as an expansion archive.</summary>
internal sealed record PreservedCompanion(ApkInputFile Input, string PreservedPath, string? DecodedFormat);

/// <summary>Identifies the origin of a resource available on the desktop classpath.</summary>
internal sealed record MappedApkResource(ApkInputFile Input, string ResourcePath);

/// <summary>Preserves supplied files and maps APK and ZIP expansion assets into a desktop project.</summary>
internal sealed class ApkProjectAssets(string project, List<PortDiagnostic> diagnostics, IReadOnlyList<IAttachmentAdapter> adapters)
{
    private readonly Dictionary<string, MappedApkResource> resources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> resourceDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PreservedCompanion> companions = [];
    private readonly List<MappedApkResource> origins = [];
    private readonly List<PortDiagnostic> diagnostics = diagnostics;

    /// <summary>Gets details of the retained companion files.</summary>
    internal IReadOnlyList<PreservedCompanion> Companions => companions;

    /// <summary>Gets the origins of all successfully mapped resources, including identical duplicates.</summary>
    internal IReadOnlyList<MappedApkResource> Resources => origins;

    /// <summary>Gets the number of distinct resources available on the classpath.</summary>
    internal int ResourceCount => resources.Count;

    /// <summary>Copies companions and unpacks assets using Windows-safe relative paths.</summary>
    internal async Task ReadAsync(ApkInventory inventory, RecoveredApk recovered, ApkPortOptions options, string snapshot,
        IReadOnlyList<ApkSplitSnapshot> splits, R8Mapping? mapping, CancellationToken cancellationToken)
    {
        ILookup<string, AttachmentSelection> selections = options.ObbPaths.Select(static path => new AttachmentSelection(path, ZipObbAdapter.Id))
            .Concat(options.Attachments).ToLookup(static selection => Path.TrimEndingDirectorySeparator(Path.GetFullPath(selection.Path)),
                StringComparer.OrdinalIgnoreCase);
        Dictionary<(string Path, string? Format, string? Target), string?> opened = [];
        string? version = recovered.VersionCode;
        HashSet<string> conventionalNames = new(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(version))
        {
            conventionalNames.Add($"main.{version}.{recovered.PackageName}.obb");
            conventionalNames.Add($"patch.{version}.{recovered.PackageName}.obb");
        }

        foreach ((string path, string location) in splits.Select(static split => (split.SnapshotPath, split.Location)).Prepend((snapshot, inventory.BaseLocation)))
        {
            try
            {
                await ZipObbAdapter.ReadArchiveAsync(path, location, "assets/", new ResourceWriter(this, location), cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
            {
                diagnostics.Add(new("apk.assets-read", DiagnosticSeverity.Error, exception.Message, location));
            }
        }

        int index = 0;
        foreach (ApkInputFile file in inventory.Companions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fullPath = file.ContainerPath is null ? file.OriginalPath : Path.Combine(file.ContainerPath, file.OriginalPath);
            string relative = $"porting-inputs/{++index:D4}/contents";
            string destination = Path.Combine(project, relative);

            try
            {
                await CopyCheckedFileAsync(fullPath, destination, file.Sha256, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(new("apk.companion-copy", DiagnosticSeverity.Error, exception.Message, fullPath));
                continue;
            }

            string? requestedFormat = null;
            string? decodedFormat = null;
            try
            {
                AttachmentSelection? selection = FindSelection(file, fullPath, selections);
                if (mapping?.File.OriginalPath.Equals(fullPath, StringComparison.OrdinalIgnoreCase) == true)
                {
                    if (selection is not null)
                    {
                        throw new InvalidDataException("The R8 mapping cannot also be selected as an attachment in another format.");
                    }
                    companions.Add(new(file, relative, "r8-mapping"));
                    continue;
                }

                requestedFormat = selection?.FormatId;
                string? target = selection?.ResourcePath;
                if (selection is not null && file.ContainerPath is not null
                    && !Path.GetFullPath(selection.Path).Equals(fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    string relativePath = file.OriginalPath.Replace('\\', '/');
                    target = selection.FormatId switch
                    {
                        LooseAssetAdapter.Id when target != "." => $"{target}/{relativePath}",
                        LooseAssetAdapter.Id or SuppliedAssetPackAdapter.Id => relativePath,
                        _ => target
                    };
                }
                var key = (fullPath, requestedFormat, target);
                if (!opened.TryGetValue(key, out decodedFormat))
                {
                    AttachmentInput input = new(file, destination, recovered.PackageName, version, target);
                    IAttachmentAdapter? adapter = await FindAdapterAsync(input, requestedFormat, cancellationToken);
                    if (adapter is not null)
                    {
                        requestedFormat = adapter.FormatId;
                        await adapter.ReadAsync(input, new ResourceWriter(this, fullPath), cancellationToken);
                        decodedFormat = adapter.FormatId;
                    }

                    opened.Add(key, decodedFormat);
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
            {
                string code = requestedFormat == ZipObbAdapter.Id ? "apk.obb-decode" : "apk.attachment-decode";
                diagnostics.Add(new(code, DiagnosticSeverity.Error, exception.Message, fullPath));
                continue;
            }

            companions.Add(new(file, relative, decodedFormat));
            if (decodedFormat is null)
            {
                diagnostics.Add(new("apk.companion-preserved", DiagnosticSeverity.Information,
                    "Preserved the original bytes. Select a resource mapping or a known attachment format if the game needs this file.", fullPath));
            }
        }

        if (recovered.UsesExpansion && !companions.Any(static file => file.DecodedFormat == ZipObbAdapter.Id))
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
                    $"Supply the required resource '{asset}' in the APK or through an attachment mapping. Resource paths are case-sensitive.", inventory.ApkPath));
            }
        }
    }

    private static AttachmentSelection? FindSelection(ApkInputFile file, string path, ILookup<string, AttachmentSelection> selections)
    {
        IEnumerable<AttachmentSelection> candidates = selections[path];
        if (file.ContainerPath is string root)
        {
            candidates = candidates.Concat(selections[Path.TrimEndingDirectorySeparator(root)]);
        }

        AttachmentSelection[] distinct = candidates.DistinctBy(static selection => (selection.FormatId, selection.ResourcePath)).Take(2).ToArray();
        return distinct.Length > 1
            ? throw new InvalidDataException("The attachment has conflicting explicit mappings.")
            : distinct.FirstOrDefault();
    }

    /// <summary>Copies a source file and verifies that its bytes still match the inventory.</summary>
    internal static async Task CopyCheckedFileAsync(string source, string destination, string sha256, CancellationToken cancellationToken)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using FileStream input = File.OpenRead(source);
        await CopyAndHashAsync(input, destination, sha256, cancellationToken);
    }

    /// <summary>Extracts an APK from a verified set and checks its inventoried digest.</summary>
    internal static async Task CopyCheckedMemberAsync(ZipArchive archive, ApkInputFile file, string destination, CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.GetEntry(file.OriginalPath)
            ?? throw new InvalidDataException($"The APK set is missing '{file.OriginalPath}'.");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using Stream input = entry.Open();
        await CopyAndHashAsync(input, destination, file.Sha256, cancellationToken);
    }

    private async ValueTask<IAttachmentAdapter?> FindAdapterAsync(AttachmentInput input, string? requestedFormat,
        CancellationToken cancellationToken)
    {
        if (requestedFormat is not null)
        {
            return adapters.FirstOrDefault(adapter => adapter.FormatId == requestedFormat)
                ?? throw new InvalidDataException($"No adapter is registered for '{requestedFormat}'.");
        }

        IAttachmentAdapter? selected = null;
        foreach (IAttachmentAdapter candidate in adapters)
        {
            if (await candidate.RecognizesAsync(input, cancellationToken))
            {
                if (selected is not null)
                {
                    throw new InvalidDataException($"Both '{selected.FormatId}' and '{candidate.FormatId}' recognize this attachment.");
                }

                selected = candidate;
            }
        }

        return selected;
    }

    private async Task ReadResourceAsync(string originalPath, string container, string target, long length, Stream input, string location,
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

        MappedApkResource mapped = new(new(originalPath, container, length, digest), target);
        _ = resources.TryAdd(target, mapped);
        foreach (string parent in parents)
        {
            _ = resourceDirectories.TryAdd(parent, parent);
        }
        origins.Add(mapped);
    }

    private sealed class ResourceWriter(ApkProjectAssets owner, string container) : IAttachmentResourceWriter
    {
        private readonly HashSet<(string OriginalPath, string ResourcePath)> entries = [];

        /// <inheritdoc />
        public async Task WriteAsync(string originalPath, string resourcePath, long length, Stream contents, CancellationToken cancellationToken)
        {
            string location = $"{container}!/{originalPath}";
            if (!WindowsArchivePath.IsSafe(resourcePath))
            {
                Report(new("apk.asset-path", DiagnosticSeverity.Error, "The entry is not a valid relative Windows file path.", location));
                return;
            }
            if (!entries.Add((originalPath, resourcePath)))
            {
                Report(new("apk.asset-conflict", DiagnosticSeverity.Error, "The attachment repeats this resource entry.", location));
                return;
            }

            try
            {
                await owner.ReadResourceAsync(originalPath, container, resourcePath, length, contents, location, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
            {
                Report(new("apk.asset-read", DiagnosticSeverity.Error, exception.Message, location));
            }
        }

        /// <inheritdoc />
        public void Report(PortDiagnostic diagnostic)
        {
            owner.diagnostics.Add(diagnostic);
        }
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
