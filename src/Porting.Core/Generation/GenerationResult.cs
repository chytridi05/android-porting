namespace Porting.Core.Generation
{
    /// <summary>Describes the destination and findings from a generation attempt.</summary>
    /// <param name="OutputDirectory">The absolute destination path.</param>
    /// <param name="Diagnostics">Planning and generation diagnostics. An error indicates generation failed.</param>
    public sealed record GenerationResult(string OutputDirectory, IReadOnlyList<PortDiagnostic> Diagnostics);
}