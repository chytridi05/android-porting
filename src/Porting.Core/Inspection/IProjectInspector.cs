namespace Porting.Core.Inspection;

public interface IProjectInspector
{
    Task<ProjectInspection> InspectAsync(string sourceDirectory, CancellationToken cancellationToken = default);
}