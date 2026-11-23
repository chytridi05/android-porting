namespace Porting.Core.Generation;

/// <summary>Maps an explicitly supplied resource pack archive or extracted asset tree.</summary>
internal sealed class SuppliedAssetPackAdapter : IAttachmentAdapter
{
    internal const string Id = "asset-pack";

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
        if (input.ResourcePath is not null)
        {
            await new LooseAssetAdapter().ReadAsync(input, writer, cancellationToken);
        }
        else
        {
            string location = input.File.ContainerPath is null ? input.File.OriginalPath : Path.Combine(input.File.ContainerPath, input.File.OriginalPath);
            await ZipObbAdapter.ReadArchiveAsync(input.SnapshotPath, location, "assets/", writer, cancellationToken, requireFiles: true);
        }
    }
}
