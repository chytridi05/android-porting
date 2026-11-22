using Porting.Core.Inspection;

namespace Porting.Core.Planning;

/// <param name="Inspection">The inspection result used to plan the port.</param>
/// <param name="DesktopModulePath">The planned Gradle project path of the desktop module.</param>
/// <param name="GameEntryType">The resolved JVM game entry type, when available.</param>
/// <param name="AssetDirectories">The resolved project-relative asset directories in lookup order.</param>
/// <param name="WindowWidth">The planned initial window width in pixels.</param>
/// <param name="WindowHeight">The planned initial window height in pixels.</param>
/// <param name="FilesToCreate">The project-relative paths to create in the output copy.</param>
/// <param name="FilesToModify">The project-relative paths to edit in the output copy.</param>
/// <param name="BuildTasks">The Gradle tasks to run after generation.</param>
/// <param name="Diagnostics">The diagnostics that apply to the resolved plan. Errors block generation.</param>
public sealed record PortPlan(
    ProjectInspection Inspection,
    string DesktopModulePath,
    string? GameEntryType,
    IReadOnlyList<string> AssetDirectories,
    int WindowWidth,
    int WindowHeight,
    IReadOnlyList<string> FilesToCreate,
    IReadOnlyList<string> FilesToModify,
    IReadOnlyList<string> BuildTasks,
    IReadOnlyList<PortDiagnostic> Diagnostics);