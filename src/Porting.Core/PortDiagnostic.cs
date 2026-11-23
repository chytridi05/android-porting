namespace Porting.Core;

/// <summary>Describes a finding from project inspection or port planning.</summary>
/// <param name="Code">A stable diagnostic identifier.</param>
/// <param name="Severity">The effect of the diagnostic on the port.</param>
/// <param name="Message">A human-readable explanation.</param>
/// <param name="RelativePath">The path of the affected input file or archive entry, when known.</param>
/// <param name="LineNumber">The one-based line number in the affected file, when known.</param>
public sealed record PortDiagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    string? RelativePath = null,
    int? LineNumber = null);