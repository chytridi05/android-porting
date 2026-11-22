namespace Porting.Core;

/// <param name="Code">A stable diagnostic identifier.</param>
/// <param name="Severity">The effect of the diagnostic on the port.</param>
/// <param name="Message">A human-readable explanation.</param>
/// <param name="RelativePath">The path of the affected file relative to the input project, when known.</param>
/// <param name="LineNumber">The one-based line number in the affected file, when known.</param>
public sealed record PortDiagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    string? RelativePath = null,
    int? LineNumber = null);