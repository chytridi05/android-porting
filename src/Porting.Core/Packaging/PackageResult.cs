namespace Porting.Core.Packaging
{
    /// <summary>Reports the outcome of packaging an editable desktop project.</summary>
    /// <param name="ArchivePath">The absolute path of the requested ZIP.</param>
    /// <param name="Diagnostics">Errors encountered while preparing or writing the package.</param>
    public sealed record PackageResult(string ArchivePath, IReadOnlyList<PortDiagnostic> Diagnostics);
}
