using Porting.Core.Inspection;
using System.Text.RegularExpressions;

namespace Porting.Core.Planning;

/// <summary>Applies overrides to inspection results and previews the desktop project changes.</summary>
public sealed class PortPlanner : IPortPlanner
{
    private const int DefaultWindowWidth = 1280;
    private const int DefaultWindowHeight = 720;
    private const string DefaultDesktopModule = ":lwjgl3";
    private const string JvmIdentifier = @"[\p{L}_$][\p{L}\p{N}\p{M}_$]*";

    /// <inheritdoc />
    public PortPlan CreatePlan(ProjectInspection inspection, PortOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(inspection);

        string desktopModule = inspection.DesktopModulePath ?? DefaultDesktopModule;
        string? gameEntry = options?.GameEntryType ?? inspection.GameEntryType;
        int windowWidth = options?.WindowWidth ?? DefaultWindowWidth;
        int windowHeight = options?.WindowHeight ?? DefaultWindowHeight;
        List<PortDiagnostic> diagnostics = [.. inspection.Diagnostics.Where(diagnostic =>
            diagnostic.Code != "inspection.entry-unresolved"
            && (diagnostic.Code != "inspection.assets-unresolved" || options?.AssetDirectories is null))];

        if (gameEntry is null || !Regex.IsMatch(gameEntry, @"\A" + JvmIdentifier + @"(?:\." + JvmIdentifier + @")*\z"))
        {
            diagnostics.Add(new("plan.entry-invalid", DiagnosticSeverity.Error,
                "Supply GameEntryType as a fully qualified JVM type name, for example com.example.MyGame."));
            gameEntry = null;
        }

        if (windowWidth <= 0 || windowHeight <= 0)
        {
            diagnostics.Add(new("plan.window-size", DiagnosticSeverity.Error, "WindowWidth and WindowHeight must be positive pixel values."));
        }

        IReadOnlyList<string> assetDirectories = ResolveAssets(inspection, options, diagnostics);
        if (options?.AssetDirectories is null && inspection.Diagnostics.Any(static diagnostic => diagnostic.Code == "inspection.assets-unresolved"))
        {
            diagnostics.Add(new("plan.assets-unresolved", DiagnosticSeverity.Error, "Supply AssetDirectories to resolve the asset lookup order."));
        }

        if (inspection.GradleSettingsFile is null || inspection.AndroidModulePath is null || inspection.SharedModulePath is null)
        {
            if (!diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Code.StartsWith("inspection.", StringComparison.Ordinal)))
            {
                diagnostics.Add(new("plan.incomplete-inspection", DiagnosticSeverity.Error,
                    "The inspection must identify the Gradle settings file and both the Android and shared libGDX modules."));
            }
        }

        string[] filesToCreate = [];
        string[] filesToModify = [];
        if (!diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            && inspection.GradleSettingsFile is string settingsFile && gameEntry is not null)
        {
            if (inspection.DesktopModulePath is not null)
            {
                if (options is { GameEntryType: not null } or { AssetDirectories: not null } or { WindowWidth: not null } or { WindowHeight: not null })
                {
                    diagnostics.Add(new("plan.existing-desktop-options", DiagnosticSeverity.Error,
                        $"Apply overrides in the existing desktop module {desktopModule}. The port plan will not rewrite its launcher or Gradle script."));
                }
                else
                {
                    diagnostics.Add(new("plan.existing-desktop", DiagnosticSeverity.Information,
                        $"Reuse {desktopModule} with its current launcher and window configuration."));
                }
            }
            else
            {
                string desktopDirectory = DefaultDesktopModule[1..];
                string desktopPath = Path.Combine(inspection.SourceDirectory, desktopDirectory);
                if (Path.Exists(desktopPath))
                {
                    diagnostics.Add(new("plan.desktop-conflict", DiagnosticSeverity.Error,
                        $"The path '{desktopDirectory}' already exists. Move it or register its LWJGL3 module in Gradle settings.", desktopDirectory));
                }
                else
                {
                    string buildFile = settingsFile.EndsWith(".kts", StringComparison.Ordinal) ? "build.gradle.kts" : "build.gradle";
                    int packageEnd = gameEntry.LastIndexOf('.');
                    string packageDirectory = packageEnd < 0 ? "" : gameEntry[..packageEnd].Replace('.', '/') + "/";
                    filesToCreate = [$"{desktopDirectory}/{buildFile}", $"{desktopDirectory}/src/main/java/{packageDirectory}DesktopLauncher.java"];
                    filesToModify = [settingsFile];
                }
            }
        }

        string[] buildTasks = diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) ? [] : [$"{desktopModule}:build"];
        return new(inspection, desktopModule, gameEntry, assetDirectories, windowWidth, windowHeight,
            Array.AsReadOnly(filesToCreate), Array.AsReadOnly(filesToModify), Array.AsReadOnly(buildTasks), diagnostics.AsReadOnly());
    }

    private static IReadOnlyList<string> ResolveAssets(ProjectInspection inspection, PortOptions? options, List<PortDiagnostic> diagnostics)
    {
        if (options?.AssetDirectories is null)
        {
            return Array.AsReadOnly(inspection.AssetDirectories.ToArray());
        }

        List<string> directories = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in options.AssetDirectories)
        {
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                diagnostics.Add(new("plan.asset-path", DiagnosticSeverity.Error, $"Asset directory '{path}' must be relative to the source project."));
                continue;
            }

            string relative = Path.GetRelativePath(inspection.SourceDirectory, Path.Combine(inspection.SourceDirectory, path)).Replace('\\', '/');
            if (relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative)
                || !Directory.Exists(Path.Combine(inspection.SourceDirectory, relative)))
            {
                diagnostics.Add(new("plan.asset-path", DiagnosticSeverity.Error, $"Asset directory '{path}' must exist inside the source project."));
            }
            else if (seen.Add(relative))
            {
                directories.Add(relative);
            }
        }

        return directories.AsReadOnly();
    }
}