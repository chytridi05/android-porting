using Porting.Core.Inspection;
using Porting.Core.Planning;
using System.Globalization;
using System.Text;

namespace Porting.Core.Generation
{
    /// <summary>Renders the launcher and the matching Gradle application module.</summary>
    internal static class Lwjgl3Templates
    {
        internal static string CreateLauncher(PortPlan plan, Lwjgl3Layout layout)
        {
            string package = layout.Package.Length == 0 ? "" : $"package {layout.Package};\n\n";
            return FormattableString.Invariant($$"""
                {{package}}import com.badlogic.gdx.backends.lwjgl3.Lwjgl3Application;
                import com.badlogic.gdx.backends.lwjgl3.Lwjgl3ApplicationConfiguration;

                public final class {{layout.LauncherClass}} {
                    private {{layout.LauncherClass}}() {
                    }

                    public static void main(String[] args) {
                        Lwjgl3ApplicationConfiguration configuration = new Lwjgl3ApplicationConfiguration();
                        configuration.setTitle("{{layout.EntryClass}}");
                        configuration.setWindowedMode({{plan.WindowWidth}}, {{plan.WindowHeight}});
                        new Lwjgl3Application(new {{layout.EntryClass}}(), configuration);
                    }
                }
                """) + "\n";
        }

        internal static string CreateBuildScript(PortPlan plan, Lwjgl3Layout layout)
        {
            bool kotlin = layout.UsesKotlinDsl;
            DesktopLibraryVersions libraries = plan.Inspection.DesktopLibraries!;
            string sharedProject = $"project({Quote(plan.Inspection.SharedModulePath!, kotlin)})";
            string backend = Quote($"com.badlogicgames.gdx:gdx-backend-lwjgl3:{libraries.LibGdx}", kotlin);
            string natives = Quote($"com.badlogicgames.gdx:gdx-platform:{libraries.LibGdx}:natives-desktop", kotlin);
            StringBuilder script = new();
            _ = script.AppendLine(kotlin ? "plugins { application }" : "plugins { id 'application' }").AppendLine();
            _ = script.AppendLine("dependencies {");
            AddDependency("implementation", sharedProject);
            AddDependency("implementation", backend);
            AddDependency("runtimeOnly", natives);
            if (libraries.UsesBox2D)
            {
                AddDependency("runtimeOnly", Quote($"com.badlogicgames.gdx:gdx-box2d-platform:{libraries.Box2D}:natives-desktop", kotlin));
            }

            string launcher = Quote(layout.LauncherType, kotlin);
            _ = script.AppendLine("}").AppendLine().AppendLine("application {")
                .AppendLine(kotlin ? $"    mainClass.set({launcher})" : $"    mainClass = {launcher}").AppendLine("}");
            if (plan.AssetDirectories.Count > 0)
            {
                _ = script.AppendLine().AppendLine(kotlin ? "tasks.processResources {" : "tasks.named('processResources') {")
                    .AppendLine("    duplicatesStrategy = DuplicatesStrategy.EXCLUDE");
                foreach (string asset in plan.AssetDirectories)
                {
                    _ = script.AppendLine(CultureInfo.InvariantCulture, $"    from(rootProject.file({Quote(asset, kotlin)}))");
                }

                _ = script.AppendLine("}");
            }

            return script.ToString();

            void AddDependency(string configuration, string notation)
            {
                _ = script.AppendLine(kotlin ? $"    {configuration}({notation})" : $"    {configuration} {notation}");
            }
        }

        private static string Quote(string value, bool kotlin)
        {
            string escaped = value.Replace("\\", "\\\\", StringComparison.Ordinal);
            return kotlin
                ? "\"" + escaped.Replace("\"", "\\\"", StringComparison.Ordinal).Replace("$", "\\$", StringComparison.Ordinal) + "\""
                : "'" + escaped.Replace("'", "\\'", StringComparison.Ordinal) + "'";
        }
    }
}