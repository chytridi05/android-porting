using Porting.Core.Inspection;

namespace Porting.Core.Planning;

/// <summary>Creates a desktop port plan from an inspected project and optional overrides.</summary>
public interface IPortPlanner
{
    /// <summary>Resolves a desktop target and previews the changes needed in the output project.</summary>
    /// <param name="inspection">The results of inspecting the source project.</param>
    /// <param name="options">Optional overrides for detected and default values.</param>
    /// <returns>The proposed file changes, build tasks, and diagnostics for the resolved target.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inspection"/> is null.</exception>
    PortPlan CreatePlan(ProjectInspection inspection, PortOptions? options = null);
}