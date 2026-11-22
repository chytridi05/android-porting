namespace Porting.Core.Building
{
    /// <summary>Builds an editable desktop project with its own Gradle Wrapper.</summary>
    public interface IDesktopProjectBuilder
    {
        /// <summary>Runs the desktop module's build task in the generated project.</summary>
        /// <param name="projectDirectory">The directory produced by project generation.</param>
        /// <param name="offline">Whether Gradle should use its local dependency cache.</param>
        /// <param name="standardOutput">An optional writer for the Wrapper's standard output.</param>
        /// <param name="standardError">An optional writer for the Wrapper's standard error.</param>
        /// <param name="cancellationToken">A token that stops the build and its child processes.</param>
        /// <returns>The selected module, Gradle exit code, and build diagnostics.</returns>
        Task<BuildResult> BuildAsync(string projectDirectory, bool offline = false, TextWriter? standardOutput = null,
            TextWriter? standardError = null, CancellationToken cancellationToken = default);
    }
}