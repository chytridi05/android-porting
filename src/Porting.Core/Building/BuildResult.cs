namespace Porting.Core.Building
{
    /// <summary>Reports the outcome of building an editable desktop project.</summary>
    /// <param name="ProjectDirectory">The absolute path of the generated project.</param>
    /// <param name="DesktopModule">The selected desktop module, when the project metadata could be read.</param>
    /// <param name="GradleExitCode">The Gradle Wrapper process exit code, when the process completed.</param>
    /// <param name="Diagnostics">Errors encountered while preparing or running the build.</param>
    public sealed record BuildResult(string ProjectDirectory, string? DesktopModule, int? GradleExitCode, IReadOnlyList<PortDiagnostic> Diagnostics);
}