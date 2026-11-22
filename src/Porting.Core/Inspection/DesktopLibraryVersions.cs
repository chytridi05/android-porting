namespace Porting.Core.Inspection
{
    /// <summary>Identifies libGDX and Box2D versions resolved from the shared module's dependencies.</summary>
    internal sealed record DesktopLibraryVersions(string? LibGdx, bool UsesBox2D, string? Box2D);
}