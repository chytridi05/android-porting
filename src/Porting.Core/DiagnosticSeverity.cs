namespace Porting.Core;

/// <summary>Indicates the impact of an inspection or planning finding.</summary>
public enum DiagnosticSeverity
{
    /// <summary>Provides context for a finding or planning decision.</summary>
    Information,
    /// <summary>Flags an issue that may require attention.</summary>
    Warning,
    /// <summary>Identifies an issue that blocks generation.</summary>
    Error
}