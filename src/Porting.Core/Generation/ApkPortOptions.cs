namespace Porting.Core.Generation;

/// <summary>Supplies the tools, attachments, and overrides for a libGDX APK port.</summary>
/// <param name="CompanionPaths">Files or directories whose original bytes are retained in the export.</param>
/// <param name="ObbPaths">Additional files to read as ZIP expansion archives.</param>
/// <param name="RequiredAssets">Resource paths that must be available to the desktop game.</param>
/// <param name="JadxJar">The path to the JADX command-line distribution JAR, if specified.</param>
/// <param name="GradleHome">The path to an unpacked Gradle 8.14.3 distribution, if specified.</param>
/// <param name="EntryType">The game entry class to use when the Android launcher cannot identify it.</param>
public sealed record ApkPortOptions(
    IReadOnlyList<string> CompanionPaths,
    IReadOnlyList<string> ObbPaths,
    IReadOnlyList<string> RequiredAssets,
    string? JadxJar = null,
    string? GradleHome = null,
    string? EntryType = null)
{
    /// <summary>Gets explicit format and resource selections for additional attachments.</summary>
    public IReadOnlyList<AttachmentSelection> Attachments { get; init; } = [];

    /// <summary>Enumerates the originals that must be inventoried and kept outside the export directory.</summary>
    public IEnumerable<string> GetCompanionPaths()
    {
        return CompanionPaths.Concat(ObbPaths).Concat(Attachments.Select(static attachment => attachment.Path));
    }
}

/// <summary>Selects how a supplied file or directory is processed.</summary>
/// <param name="Path">The original file or directory path.</param>
/// <param name="FormatId">The identifier of the selected adapter.</param>
/// <param name="ResourcePath">The target path for a file or the target prefix for a directory. Use "." for the root.</param>
public sealed record AttachmentSelection(string Path, string FormatId, string? ResourcePath = null)
{
    /// <summary>Maps the original to a file path or directory prefix in the desktop assets.</summary>
    public static AttachmentSelection MapAsset(string path, string resourcePath)
    {
        return new(path, LooseAssetAdapter.Id, resourcePath);
    }
}

/// <summary>Reports the resolved APK port and any blocking inputs.</summary>
/// <param name="PackageName">The application ID recovered from the Android manifest.</param>
/// <param name="EntryType">The recovered game entry type.</param>
/// <param name="LibGdxVersion">The detected desktop dependency version.</param>
/// <param name="SourceFiles">The number of application Java files selected from JADX output.</param>
/// <param name="Resources">The number of mapped desktop resources.</param>
/// <param name="OutputDirectory">The exported project path when generation succeeds.</param>
/// <param name="Diagnostics">Findings from input inspection, recovery, and generation.</param>
public sealed record ApkPortResult(
    string? PackageName,
    string? EntryType,
    string? LibGdxVersion,
    int SourceFiles,
    int Resources,
    string? OutputDirectory,
    IReadOnlyList<PortDiagnostic> Diagnostics);
