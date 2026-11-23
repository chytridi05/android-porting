using Porting.Core.Inspection;
using System.IO.Compression;

namespace Porting.Core.Generation;

/// <summary>Maps ZIP expansion entries to libGDX resource paths.</summary>
internal sealed class ZipObbAdapter : IAttachmentAdapter
{
    internal const string Id = "zip-obb";

    /// <inheritdoc />
    public string FormatId => Id;

    /// <inheritdoc />
    public ValueTask<bool> RecognizesAsync(AttachmentInput input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string name = Path.GetFileName(input.File.OriginalPath);
        bool matchesPackage = input.VersionCode is not null
            && (name.Equals($"main.{input.VersionCode}.{input.PackageName}.obb", StringComparison.OrdinalIgnoreCase)
                || name.Equals($"patch.{input.VersionCode}.{input.PackageName}.obb", StringComparison.OrdinalIgnoreCase));
        if (!matchesPackage)
        {
            return ValueTask.FromResult(false);
        }

        try
        {
            using FileStream stream = File.OpenRead(input.SnapshotPath);
            Span<byte> prefix = stackalloc byte[4];
            int read = stream.ReadAtLeast(prefix, prefix.Length, false);
            if (RsbBundleHeader.HasSignature(prefix[..read]))
            {
                return ValueTask.FromResult(false);
            }

            stream.Position = 0;
            using ZipArchive archive = new(stream, ZipArchiveMode.Read);
            _ = archive.Entries.Count;
            return ValueTask.FromResult(true);
        }
        catch (InvalidDataException)
        {
            return ValueTask.FromResult(false);
        }
    }

    /// <summary>Checks that a ZIP expansion has a readable ZIP directory.</summary>
    internal static void CheckArchive(string path)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);
        _ = archive.Entries.Count;
    }

    /// <inheritdoc />
    public Task ReadAsync(AttachmentInput input, IAttachmentResourceWriter writer, CancellationToken cancellationToken)
    {
        string location = input.File.ContainerPath is null ? input.File.OriginalPath : Path.Combine(input.File.ContainerPath, input.File.OriginalPath);
        return ReadArchiveAsync(input.SnapshotPath, location, "", writer, cancellationToken);
    }

    /// <summary>Maps files below a ZIP path prefix through the shared resource writer.</summary>
    internal static async Task ReadArchiveAsync(string snapshot, string location, string prefix, IAttachmentResourceWriter writer,
        CancellationToken cancellationToken, bool requireFiles = false)
    {
        using ZipArchive archive = ZipFile.OpenRead(snapshot);
        bool found = false;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entry.FullName.EndsWith('/') && entry.FullName.StartsWith(prefix, StringComparison.Ordinal))
            {
                found = true;
                try
                {
                    await using Stream contents = entry.Open();
                    await writer.WriteAsync(entry.FullName, entry.FullName[prefix.Length..], entry.Length, contents, cancellationToken);
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or NotSupportedException)
                {
                    writer.Report(new("apk.asset-read", DiagnosticSeverity.Error, exception.Message, $"{location}!/{entry.FullName}"));
                }
            }
        }
        if (requireFiles && !found)
        {
            throw new InvalidDataException($"The asset pack has no files below '{prefix}'.");
        }
    }
}
