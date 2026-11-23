namespace Porting.Core.Inspection;

/// <summary>Identifies input bytes and where they were read.</summary>
/// <param name="OriginalPath">The entry path inside its container, or the absolute path of a loose file.</param>
/// <param name="ContainerPath">The absolute path of the archive or directory containing the file, if applicable.</param>
/// <param name="Length">The number of bytes in the file or uncompressed entry.</param>
/// <param name="Sha256">The SHA-256 digest in hexadecimal.</param>
public sealed record ApkInputFile(string OriginalPath, string? ContainerPath, long Length, string Sha256);

/// <summary>Records the inputs read for an APK port.</summary>
/// <param name="ApkPath">The absolute path of the requested APK.</param>
/// <param name="Package">The source APK file when readable.</param>
/// <param name="Entries">The files read from the APK archive.</param>
/// <param name="Companions">The files read from the supplied companion paths.</param>
/// <param name="Diagnostics">Findings tied to the affected input or archive entry.</param>
public sealed record ApkInventory(
    string ApkPath,
    ApkInputFile? Package,
    IReadOnlyList<ApkInputFile> Entries,
    IReadOnlyList<ApkInputFile> Companions,
    IReadOnlyList<PortDiagnostic> Diagnostics)
{
    /// <summary>Gets the split APKs and their entries, in input order.</summary>
    public IReadOnlyList<ApkSplitInventory> Splits { get; init; } = [];

    /// <summary>Gets the base member identity when the primary file is an APK set.</summary>
    public ApkInputFile? BaseApk { get; init; }

    /// <summary>Gets the RSB headers identified among the supplied companion files.</summary>
    public IReadOnlyList<RsbAttachment> RsbAttachments { get; init; } = [];

    internal string BaseLocation => BaseApk is null ? ApkPath : $"{ApkPath}!/{BaseApk.OriginalPath}";
}

/// <summary>Records the original bytes and entries of a supplied split APK.</summary>
/// <param name="File">The split APK file identity.</param>
/// <param name="Entries">The files read from the split APK.</param>
public sealed record ApkSplitInventory(ApkInputFile File, IReadOnlyList<ApkInputFile> Entries);

/// <summary>Reports an RSB header before any attempt to decode or use its resources.</summary>
/// <param name="File">The identified companion file and its byte identity.</param>
/// <param name="Version">The bundle version read from the header.</param>
/// <param name="GroupCount">The declared number of resource groups when the header layout is known.</param>
public sealed record RsbAttachment(ApkInputFile File, uint Version, uint? GroupCount)
{
    /// <summary>Gets a description of the header for inspection output.</summary>
    public string Description => GroupCount is uint count ? $"RSB v{Version}, {count} resource groups" : $"RSB v{Version}";
}
