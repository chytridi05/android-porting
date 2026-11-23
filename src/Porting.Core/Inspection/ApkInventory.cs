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
    IReadOnlyList<PortDiagnostic> Diagnostics);