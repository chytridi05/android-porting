using Porting.Core.Inspection;
using System.IO.Compression;

namespace Porting.Core.Generation;

/// <summary>Checks APK engine evidence and explicit expansion formats before invoking JADX.</summary>
internal static class ApkPreflight
{
    /// <summary>Reports unsupported inputs without recovering their application source.</summary>
    internal static async Task<bool> CheckAsync(string snapshot, ApkInventory inventory, IReadOnlyList<ApkSplitSnapshot> splits, IReadOnlyList<string> obbPaths,
        List<PortDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        bool supported = await HasLibGdxReferencesAsync(snapshot, cancellationToken);
        foreach (ApkSplitSnapshot split in splits)
        {
            supported = supported || await HasLibGdxReferencesAsync(split.SnapshotPath, cancellationToken);
        }
        if (!supported)
        {
            string[] libraries = inventory.Entries.Select(static entry => entry.OriginalPath)
                .Where(static path => path.StartsWith("lib/", StringComparison.Ordinal) && path.EndsWith(".so", StringComparison.Ordinal))
                .Select(static path => Path.GetFileName(path)).Distinct(StringComparer.Ordinal).Take(4).ToArray();
            string evidence = libraries.Length == 0 ? "" : $" Android native libraries include {string.Join(", ", libraries)}.";
            diagnostics.Add(new("apk.engine-unsupported", DiagnosticSeverity.Error,
                $"The supplied APKs lack recognizable libGDX references in their DEX code.{evidence} Supply the libGDX APK and any required splits.", inventory.ApkPath));
        }

        foreach (string path in obbPaths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(path))
            {
                diagnostics.Add(new("apk.obb-file", DiagnosticSeverity.Error, "Supply a ZIP expansion file with --obb.", path));
                supported = false;
                continue;
            }

            try
            {
                using ZipArchive archive = ZipFile.OpenRead(path);
                _ = archive.Entries.Count;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
            {
                diagnostics.Add(new("apk.obb-decode", DiagnosticSeverity.Error,
                    $"Supply a readable ZIP expansion archive or provide a ZIP export of the required resources. {exception.Message}", path));
                supported = false;
            }
        }

        return supported;
    }

    private static async Task<bool> HasLibGdxReferencesAsync(string snapshot, CancellationToken cancellationToken)
    {
        int overlap = "Lcom/badlogic/gdx/"u8.Length - 1;
        byte[] buffer = new byte[65536 + overlap];
        using ZipArchive archive = ZipFile.OpenRead(snapshot);
        foreach (ZipArchiveEntry entry in archive.Entries.Where(static entry => ApkDex.IsDexEntry(entry.FullName)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using Stream input = entry.Open();
            int retained = 0;
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(retained, buffer.Length - retained), cancellationToken)) > 0)
            {
                int length = retained + read;
                if (buffer.AsSpan(0, length).IndexOf("Lcom/badlogic/gdx/"u8) >= 0)
                {
                    return true;
                }

                retained = Math.Min(overlap, length);
                buffer.AsSpan(length - retained, retained).CopyTo(buffer);
            }
        }

        return false;
    }
}
