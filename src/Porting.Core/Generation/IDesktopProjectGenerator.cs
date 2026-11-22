using Porting.Core.Planning;

namespace Porting.Core.Generation
{
    /// <summary>Materializes a port plan as a separate, editable Gradle project.</summary>
    public interface IDesktopProjectGenerator
    {
        /// <summary>Copies the source project and publishes the proposed desktop target.</summary>
        /// <param name="plan">The resolved plan for the input project.</param>
        /// <param name="outputDirectory">A new destination outside the source directory.</param>
        /// <param name="cancellationToken">A token that cancels generation before publication.</param>
        /// <returns>The destination path and any diagnostics from applying the plan.</returns>
        Task<GenerationResult> GenerateAsync(PortPlan plan, string outputDirectory, CancellationToken cancellationToken = default);
    }
}