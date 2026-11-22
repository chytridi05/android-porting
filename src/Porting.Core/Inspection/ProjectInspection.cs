namespace Porting.Core.Inspection;

/// <summary>Contains the detected Gradle and libGDX project metadata.</summary>
/// <param name="SourceDirectory">The absolute path of the inspected project.</param>
/// <param name="GradleSettingsFile">The project-relative path to the Gradle settings file, when found.</param>
/// <param name="GradleVersion">The Gradle Wrapper version, when detected.</param>
/// <param name="AndroidModulePath">The Gradle project path of the Android module, when resolved.</param>
/// <param name="SharedModulePath">The Gradle project path of the shared game module, when resolved.</param>
/// <param name="DesktopModulePath">The Gradle project path of an existing desktop module, when found.</param>
/// <param name="GameEntryType">The fully qualified JVM type name of the game entry point, when resolved.</param>
/// <param name="AssetDirectories">The project-relative asset directories in lookup order.</param>
/// <param name="Diagnostics">The diagnostics collected during inspection.</param>
public sealed record ProjectInspection(
    string SourceDirectory,
    string? GradleSettingsFile,
    string? GradleVersion,
    string? AndroidModulePath,
    string? SharedModulePath,
    string? DesktopModulePath,
    string? GameEntryType,
    IReadOnlyList<string> AssetDirectories,
    IReadOnlyList<PortDiagnostic> Diagnostics)
{
    /// <summary>Gets the libGDX library versions needed by a new desktop target.</summary>
    internal DesktopLibraryVersions? DesktopLibraries { get; init; }
}