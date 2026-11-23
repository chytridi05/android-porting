using System.IO.Compression;
using System.Security.Cryptography;
using Porting.Core.Generation;

namespace Porting.Core.Inspection;

/// <summary>Reads APK archives and companion files without extracting them.</summary>
public sealed class ApkInspector
{
    /// <summary>Inventories the archive and supplied files, including files in companion directories.</summary>
    /// <param name="apkPath">The path to the primary APK.</param>
    /// <param name="companionPaths">Companion files or directories to inventory.</param>
    /// <param name="cancellationToken">Cancels reads and hashing.</param>
    /// <returns>The readable input inventory and any failures encountered.</returns>
    public Task<ApkInventory> InspectAsync(string apkPath, IReadOnlyList<string> companionPaths,
        CancellationToken cancellationToken = default)
    {
        return InspectInputsAsync(apkPath, companionPaths, [], cancellationToken);
    }

    /// <summary>Inventories the base APK, selected splits, and attachments for a port.</summary>
    public Task<ApkInventory> InspectPortAsync(string apkPath, ApkPortOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        string[] companions = options.GetCompanionPaths().Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return InspectInputsAsync(apkPath, companions, options.SplitPaths, cancellationToken);
    }

    private static async Task<ApkInventory> InspectInputsAsync(string apkPath, IReadOnlyList<string> companionPaths,
        IReadOnlyList<string> splitPaths, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apkPath);
        ArgumentNullException.ThrowIfNull(companionPaths);
        cancellationToken.ThrowIfCancellationRequested();

        string fullApkPath = Path.GetFullPath(apkPath);
        List<PortDiagnostic> diagnostics = [];
        List<ApkInputFile> entries = [];
        List<ApkInputFile> companions = [];
        List<ApkSplitInventory> splits = [];
        ApkInputFile? package = null;
        try
        {
            await using FileStream input = OpenFile(fullApkPath);
            package = await ReadFileAsync(input, input.Length, fullApkPath, null, cancellationToken);
            input.Position = 0;
            await ReadArchiveAsync(input, fullApkPath, entries, diagnostics, requireDex: true, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            diagnostics.Add(new("apk.read-failed", DiagnosticSeverity.Error, exception.Message, fullApkPath));
        }

        foreach (string path in companionPaths)
        {
            await ReadCompanionAsync(Path.GetFullPath(path), null, companions, diagnostics, cancellationToken);
        }

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase) { fullApkPath };
        foreach (string path in splitPaths.Select(Path.GetFullPath).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!seen.Add(path))
            {
                diagnostics.Add(new("apk.split-conflict", DiagnosticSeverity.Error, "Each APK can be supplied only once.", path));
                continue;
            }

            try
            {
                await using FileStream input = OpenFile(path);
                ApkInputFile file = await ReadFileAsync(input, input.Length, path, null, cancellationToken);
                input.Position = 0;
                List<ApkInputFile> splitEntries = [];
                await ReadArchiveAsync(input, path, splitEntries, diagnostics, requireDex: false, cancellationToken);
                splits.Add(new(file, splitEntries.AsReadOnly()));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                diagnostics.Add(new("apk.split-read", DiagnosticSeverity.Error, exception.Message, path));
            }
        }

        return new(fullApkPath, package, entries.AsReadOnly(), companions.AsReadOnly(), diagnostics.AsReadOnly())
        {
            Splits = splits.AsReadOnly()
        };
    }

    private static async Task ReadArchiveAsync(Stream input, string apkPath, List<ApkInputFile> files,
        List<PortDiagnostic> diagnostics, bool requireDex, CancellationToken cancellationToken)
    {
        using ZipArchive archive = new(input, ZipArchiveMode.Read, leaveOpen: true);
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool directory = entry.FullName.EndsWith('/');
            string path = directory ? entry.FullName[..^1] : entry.FullName;
            if (!WindowsArchivePath.IsSafe(path))
            {
                diagnostics.Add(new("apk.archive-path", DiagnosticSeverity.Error,
                    "The APK contains an entry that cannot be safely recovered to Windows.", $"{apkPath}!/{entry.FullName}"));
                continue;
            }
            if (directory)
            {
                continue;
            }

            if (!names.Add(entry.FullName))
            {
                diagnostics.Add(new("apk.entry-conflict", DiagnosticSeverity.Error,
                    "The APK contains more than one entry with this path.", $"{apkPath}!/{entry.FullName}"));
                continue;
            }

            try
            {
                await using Stream contents = entry.Open();
                files.Add(await ReadFileAsync(contents, entry.Length, entry.FullName, apkPath, cancellationToken));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or NotSupportedException)
            {
                diagnostics.Add(new("apk.entry-read-failed", DiagnosticSeverity.Error,
                    exception.Message, $"{apkPath}!/{entry.FullName}"));
            }
        }

        if (!names.Contains("AndroidManifest.xml"))
        {
            diagnostics.Add(new("apk.manifest-missing", DiagnosticSeverity.Error,
                "Choose an Android APK containing a root AndroidManifest.xml.", apkPath));
        }

        if (requireDex && !names.Contains("classes.dex"))
        {
            diagnostics.Add(new("apk.dex-missing", DiagnosticSeverity.Error,
                "Choose the base APK containing application DEX code.", apkPath));
        }
    }

    private static async Task ReadCompanionAsync(string path, string? root, List<ApkInputFile> files,
        List<PortDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                diagnostics.Add(new("apk.companion-link", DiagnosticSeverity.Error,
                    "Supply the file or directory directly instead of a link or junction.", path));
                return;
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                foreach (string child in Directory.EnumerateFileSystemEntries(path).Order(StringComparer.Ordinal))
                {
                    await ReadCompanionAsync(child, root ?? path, files, diagnostics, cancellationToken);
                }

                return;
            }

            await using FileStream input = OpenFile(path);
            string originalPath = root is null ? path : Path.GetRelativePath(root, path);
            files.Add(await ReadFileAsync(input, input.Length, originalPath, root, cancellationToken));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new("apk.companion-read-failed", DiagnosticSeverity.Error, exception.Message, path));
        }
    }

    private static FileStream OpenFile(string path)
    {
        return new(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
    }

    private static async Task<ApkInputFile> ReadFileAsync(Stream input, long length, string originalPath, string? containerPath,
        CancellationToken cancellationToken)
    {
        byte[] digest = await SHA256.HashDataAsync(input, cancellationToken);
        return new(originalPath, containerPath, length, Convert.ToHexStringLower(digest));
    }
}
