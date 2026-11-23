using Porting.Core.Inspection;

namespace Porting.Core.Generation;

/// <summary>Recognizes an attachment and supplies its resources to the desktop export.</summary>
public interface IAttachmentAdapter
{
    /// <summary>Gets the unique format identifier recorded in the export.</summary>
    string FormatId { get; }

    /// <summary>Checks whether an attachment should use this adapter when no format is selected.</summary>
    ValueTask<bool> RecognizesAsync(AttachmentInput input, CancellationToken cancellationToken);

    /// <summary>Validates and maps an attachment selected explicitly or recognized by the adapter.</summary>
    /// <exception cref="InvalidDataException">The attachment does not contain valid data for this format.</exception>
    Task ReadAsync(AttachmentInput input, IAttachmentResourceWriter writer, CancellationToken cancellationToken);
}

/// <summary>Describes a stable copy of a supplied file and the application receiving it.</summary>
/// <param name="File">The original path, container, and byte identity of the attachment.</param>
/// <param name="SnapshotPath">The path to the verified copy. Adapters must treat it as read-only.</param>
/// <param name="PackageName">The Android application ID.</param>
/// <param name="VersionCode">The Android application version code, when known.</param>
/// <param name="ResourcePath">An optional desktop resource path supplied by the caller.</param>
public sealed record AttachmentInput(ApkInputFile File, string SnapshotPath, string PackageName, string? VersionCode,
    string? ResourcePath = null)
{
    /// <summary>Identifies standard expansions associated with the selected Android package and version.</summary>
    internal string? ExpansionRole
    {
        get
        {
            if (VersionCode is null)
            {
                return null;
            }

            string name = Path.GetFileName(File.OriginalPath);
            string suffix = $".{VersionCode}.{PackageName}.obb";
            return name.Equals("main" + suffix, StringComparison.OrdinalIgnoreCase) ? "main"
                : name.Equals("patch" + suffix, StringComparison.OrdinalIgnoreCase) ? "patch" : null;
        }
    }
}

/// <summary>Identifies a resource group within an attachment.</summary>
/// <param name="Name">The original group name.</param>
/// <param name="Index">The zero-based group index in the attachment.</param>
public sealed record AttachmentSourceGroup(string Name, uint Index);

/// <summary>Accepts resources and diagnostics from an attachment adapter during a port.</summary>
public interface IAttachmentResourceWriter
{
    /// <summary>Copies a resource into the export with path and collision validation.</summary>
    /// <param name="originalPath">The path of the resource within the attachment, or the original file path.</param>
    /// <param name="resourcePath">The case-sensitive, forward-slash relative desktop resource path.</param>
    /// <param name="length">The number of bytes in the source resource.</param>
    /// <param name="contents">A stream at the start of the resource. The caller retains ownership.</param>
    /// <param name="cancellationToken">Cancels copying and hashing the resource.</param>
    Task WriteAsync(string originalPath, string resourcePath, long length, Stream contents, CancellationToken cancellationToken);

    /// <summary>Copies a resource and records its group when the output tracks group provenance.</summary>
    Task WriteAsync(string originalPath, string resourcePath, long length, Stream contents, AttachmentSourceGroup sourceGroup,
        CancellationToken cancellationToken)
    {
        return WriteAsync(originalPath, resourcePath, length, contents, cancellationToken);
    }

    /// <summary>Reports a finding tied to the attachment or one of its entries.</summary>
    void Report(PortDiagnostic diagnostic);
}
