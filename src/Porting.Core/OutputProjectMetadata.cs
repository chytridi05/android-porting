using System.Text.Json;

namespace Porting.Core
{
    /// <summary>Identifies the desktop module in an editable project produced by the tool.</summary>
    internal sealed record OutputProjectMetadata(int FormatVersion, string DesktopModule)
    {
        internal const string FileName = "porting-project.json";
        internal const int CurrentVersion = 1;
        internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    }
}