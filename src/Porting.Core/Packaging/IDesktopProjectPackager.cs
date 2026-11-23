namespace Porting.Core.Packaging
{
    /// <summary>Packages an editable desktop project as a runnable Windows x64 ZIP.</summary>
    public interface IDesktopProjectPackager
    {
        /// <summary>Installs the selected Gradle application and bundles a Java runtime in a new ZIP.</summary>
        /// <param name="projectDirectory">The generated project directory.</param>
        /// <param name="archivePath">The path of the ZIP to create.</param>
        /// <param name="offline">Whether Gradle should use only its cached dependencies.</param>
        /// <param name="standardOutput">An optional writer for standard tool output.</param>
        /// <param name="standardError">An optional writer for standard tool errors.</param>
        /// <param name="cancellationToken">A token that cancels packaging.</param>
        /// <returns>The archive path and any diagnostics.</returns>
        Task<PackageResult> PackageAsync(string projectDirectory, string archivePath, bool offline = false,
            TextWriter? standardOutput = null, TextWriter? standardError = null, CancellationToken cancellationToken = default);

        /// <summary>Packages a Windows x64 application executable and its private Java runtime in a new ZIP.</summary>
        /// <param name="projectDirectory">The generated project directory.</param>
        /// <param name="archivePath">The path of the ZIP to create.</param>
        /// <param name="applicationName">The base filename for the executable.</param>
        /// <param name="offline">Whether Gradle should use only its cached dependencies.</param>
        /// <param name="standardOutput">An optional writer for standard tool output.</param>
        /// <param name="standardError">An optional writer for standard tool errors.</param>
        /// <param name="cancellationToken">A token that cancels packaging.</param>
        /// <returns>The archive path and any diagnostics.</returns>
        Task<PackageResult> PackageExecutableAsync(string projectDirectory, string archivePath, string applicationName,
            bool offline = false, TextWriter? standardOutput = null, TextWriter? standardError = null, CancellationToken cancellationToken = default);
    }
}