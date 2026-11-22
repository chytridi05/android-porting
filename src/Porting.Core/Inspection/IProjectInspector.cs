namespace Porting.Core.Inspection;

/// <summary>Inspects a Gradle libGDX source project.</summary>
public interface IProjectInspector
{
    /// <summary>Discovers the project metadata needed to plan a desktop port.</summary>
    /// <param name="sourceDirectory">The directory containing the Gradle project settings.</param>
    /// <param name="cancellationToken">A token that cancels the inspection.</param>
    /// <returns>A task that returns the detected project metadata and inspection diagnostics.</returns>
    /// <exception cref="ArgumentException"><paramref name="sourceDirectory"/> is null, empty, or whitespace.</exception>
    /// <exception cref="OperationCanceledException">The inspection was canceled.</exception>
    Task<ProjectInspection> InspectAsync(string sourceDirectory, CancellationToken cancellationToken = default);
}