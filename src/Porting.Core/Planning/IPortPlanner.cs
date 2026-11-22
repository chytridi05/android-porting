using Porting.Core.Inspection;

namespace Porting.Core.Planning;

public interface IPortPlanner
{
    PortPlan CreatePlan(ProjectInspection inspection, PortOptions? options = null);
}