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
        string name = Path.GetFileName(input.File.OriginalPath);
        return ValueTask.FromResult(input.VersionCode is not null
            && (name.Equals($"main.{input.VersionCode}.{input.PackageName}.obb", StringComparison.OrdinalIgnoreCase)
                || name.Equals($"patch.{input.VersionCode}.{input.PackageName}.obb", StringComparison.OrdinalIgnoreCase)));
    }

    /// <inheritdoc />
    public Task ReadAsync(AttachmentInput input, IAttachmentResourceWriter writer, CancellationToken cancellationToken)
    {
        string location = input.File.ContainerPath is null ? input.File.OriginalPath : Path.Combine(input.File.ContainerPath, input.File.OriginalPath);
        return ReadArchiveAsync(input.SnapshotPath, location, "", writer, cancellationToken);
    }

    /// <summary>Maps files below a ZIP path prefix through the shared resource writer.</summary>
    internal static async Task ReadArchiveAsync(string snapshot, string location, string prefix, IAttachmentResourceWriter writer,
        CancellationToken cancellationToken)
    {
        using ZipArchive archive = ZipFile.OpenRead(snapshot);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entry.FullName.EndsWith('/') && entry.FullName.StartsWith(prefix, StringComparison.Ordinal))
            {
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
    }
}
