namespace Porting.Core.Planning;

/// <param name="GameEntryType">A fully qualified JVM type name that takes precedence over the detected entry point.</param>
/// <param name="AssetDirectories">Project-relative asset directories that replace the detected directories when supplied.</param>
/// <param name="WindowWidth">An optional initial window width in pixels.</param>
/// <param name="WindowHeight">An optional initial window height in pixels.</param>
public sealed record PortOptions(
    string? GameEntryType = null,
    IReadOnlyList<string>? AssetDirectories = null,
    int? WindowWidth = null,
    int? WindowHeight = null);