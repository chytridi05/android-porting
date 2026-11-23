namespace Porting.Core.Generation;

/// <summary>Maps a supplied file or directory tree to explicitly selected desktop resource paths.</summary>
internal sealed class LooseAssetAdapter : IAttachmentAdapter
{
    internal const string Id = "loose-asset";

    /// <inheritdoc />
    public string FormatId => Id;

    /// <inheritdoc />
    public ValueTask<bool> RecognizesAsync(AttachmentInput input, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(false);
    }

    /// <inheritdoc />
    public async Task ReadAsync(AttachmentInput input, IAttachmentResourceWriter writer, CancellationToken cancellationToken)
    {
        string target = input.ResourcePath ?? throw new InvalidDataException("Supply a desktop resource path for the loose attachment.");
        await using FileStream contents = File.OpenRead(input.SnapshotPath);
        string original = input.File.ContainerPath is null ? Path.GetFileName(input.File.OriginalPath) : input.File.OriginalPath.Replace('\\', '/');
        await writer.WriteAsync(original, target, input.File.Length, contents, cancellationToken);
    }
}
