using System.Text.RegularExpressions;

namespace Porting.Core.Generation
{
    /// <summary>Derives the Gradle and Java paths for a new LWJGL3 module.</summary>
    internal sealed record Lwjgl3Layout(string SettingsFile, string EntryType)
    {
        internal const string DirectoryName = "lwjgl3";
        internal const string ModulePath = ":" + DirectoryName;
        private const string JvmIdentifier = @"[\p{L}_$][\p{L}\p{N}\p{M}_$]*";

        internal bool UsesKotlinDsl => SettingsFile.EndsWith(".kts", StringComparison.Ordinal);
        internal string BuildScript => $"{DirectoryName}/build.gradle{(UsesKotlinDsl ? ".kts" : "")}";
        internal string Package => EntryType[..Math.Max(0, EntryType.LastIndexOf('.'))];
        internal string EntryClass => EntryType[(EntryType.LastIndexOf('.') + 1)..];
        internal string LauncherClass => EntryClass == "DesktopLauncher" ? "Lwjgl3Launcher" : "DesktopLauncher";
        internal string LauncherType => Package.Length == 0 ? LauncherClass : $"{Package}.{LauncherClass}";
        internal string LauncherFile => $"{DirectoryName}/src/main/java/{LauncherType.Replace('.', '/')}.java";

        internal static bool IsGameEntry(string? name)
        {
            return name is not null && Regex.IsMatch(name, @"\A" + JvmIdentifier + @"(?:\." + JvmIdentifier + @")*\z");
        }
    }
}