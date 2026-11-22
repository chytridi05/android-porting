using Porting.Core;
using Porting.Core.Building;
using Porting.Core.Generation;
using Porting.Core.Inspection;
using Porting.Core.Packaging;
using Porting.Core.Planning;
using System.Globalization;
using System.Reflection;

namespace Porting.Cli
{
    /// <summary>Dispatches CLI commands and formats shared porting results for the console.</summary>
    internal sealed class PortingCommandLine(IProjectInspector inspector, IPortPlanner planner,
        IDesktopProjectGenerator generator, IDesktopProjectBuilder builder, IDesktopProjectPackager packager)
    {
        private const int OperationFailed = 1;
        private const int InvalidArguments = 2;
        private const int Canceled = 130;

        /// <summary>Runs one command and returns its process exit code.</summary>
        internal async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
        {
            if (args is [] or ["help" or "--help" or "-h"])
            {
                WriteHelp();
                return 0;
            }
            if (args is ["--version"])
            {
                string? version = typeof(PortingCommandLine).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                Console.WriteLine($"Porting.Cli {version}");
                return 0;
            }

            try
            {
                string command = args[0];
                if (command is not ("inspect" or "plan" or "generate" or "build" or "package"))
                {
                    throw new FormatException($"Unknown command '{command}'.");
                }

                if (args is [_, "--help" or "-h"])
                {
                    WriteHelp(command);
                    return 0;
                }

                if (command == "build")
                {
                    return await RunBuildAsync(args, cancellationToken);
                }
                if (command == "package")
                {
                    return await RunPackageAsync(args, cancellationToken);
                }

                string source = ReadRequiredArgument(args, 1, "source project directory");
                string? output = command == "generate" ? ReadRequiredArgument(args, 2, "output project directory") : null;
                PortOptions? options = command == "inspect" ? null : ReadPortOptions(args.AsSpan(output is null ? 2 : 3));
                if (command == "inspect" && args.Length > 2)
                {
                    throw new FormatException($"Unexpected argument '{args[2]}'.");
                }

                ProjectInspection inspection = await inspector.InspectAsync(source, cancellationToken);
                WriteProject(inspection);
                if (options is not null)
                {
                    PortPlan plan = planner.CreatePlan(inspection, options);
                    if (output is not null)
                    {
                        GenerationResult generation = await generator.GenerateAsync(plan, output, cancellationToken);
                        int exitCode = WriteDiagnostics(generation.Diagnostics);
                        if (exitCode == 0)
                        {
                            Console.WriteLine($"Generated project: {generation.OutputDirectory}");
                            Console.WriteLine($"Desktop module: {plan.DesktopModulePath}");
                        }

                        return exitCode;
                    }

                    WritePlan(plan);
                    return WriteDiagnostics(plan.Diagnostics);
                }

                WriteList("Asset directories", inspection.AssetDirectories);
                return WriteDiagnostics(inspection.Diagnostics);
            }
            catch (FormatException exception)
            {
                Console.Error.WriteLine($"{exception.Message} Run Porting.Cli --help for usage.");
                return InvalidArguments;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Console.Error.WriteLine("Operation canceled.");
                return Canceled;
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine(exception.Message);
                return OperationFailed;
            }
        }

        private async Task<int> RunBuildAsync(string[] args, CancellationToken cancellationToken)
        {
            string project = ReadRequiredArgument(args, 1, "generated project directory");
            bool offline = ReadOfflineOption(args, 2);

            BuildResult build = await builder.BuildAsync(project, offline, Console.Out, Console.Error, cancellationToken);
            int exitCode = WriteDiagnostics(build.Diagnostics);
            if (exitCode == 0)
            {
                Console.WriteLine($"Built {build.DesktopModule} in {build.ProjectDirectory}");
            }

            return exitCode;
        }

        private async Task<int> RunPackageAsync(string[] args, CancellationToken cancellationToken)
        {
            string project = ReadRequiredArgument(args, 1, "generated project directory");
            string archive = ReadRequiredArgument(args, 2, "output ZIP path");
            bool offline = ReadOfflineOption(args, 3);
            PackageResult package = await packager.PackageAsync(project, archive, offline, Console.Out, Console.Error, cancellationToken);
            int exitCode = WriteDiagnostics(package.Diagnostics);
            if (exitCode == 0)
            {
                Console.WriteLine($"Packaged ZIP: {package.ArchivePath}");
            }

            return exitCode;
        }

        private static bool ReadOfflineOption(string[] args, int index)
        {
            bool offline = args.Length > index && args[index] == "--offline";
            int expected = offline ? index + 1 : index;
            if (args.Length > expected)
            {
                throw new FormatException($"Unexpected argument '{args[expected]}'.");
            }

            return offline;
        }

        private static string ReadRequiredArgument(string[] args, int index, string description)
        {
            if (index >= args.Length || string.IsNullOrWhiteSpace(args[index]) || args[index].StartsWith('-'))
            {
                throw new FormatException($"Specify the {description}.");
            }

            return args[index];
        }

        private static PortOptions ReadPortOptions(ReadOnlySpan<string> args)
        {
            HashSet<string> seen = new(StringComparer.Ordinal);
            List<string> assets = [];
            string? entry = null;
            int? width = null;
            int? height = null;

            for (int index = 0; index < args.Length; index++)
            {
                string option = args[index];
                if (!seen.Add(option) && option != "--asset")
                {
                    throw new FormatException($"Option '{option}' can only be used once.");
                }

                switch (option)
                {
                    case "--entry":
                        entry = ReadValue(args, ref index);
                        break;
                    case "--asset":
                        assets.Add(ReadValue(args, ref index));
                        break;
                    case "--no-assets":
                        break;
                    case "--width":
                        width = ReadPixels(args, ref index);
                        break;
                    case "--height":
                        height = ReadPixels(args, ref index);
                        break;
                    default:
                        throw new FormatException($"Unknown option '{option}'.");
                }
            }

            bool noAssets = seen.Contains("--no-assets");
            if (noAssets && assets.Count > 0)
            {
                throw new FormatException("Use either --asset or --no-assets.");
            }

            return new(entry, noAssets || assets.Count > 0 ? assets.AsReadOnly() : null, width, height);
        }

        private static string ReadValue(ReadOnlySpan<string> args, ref int index)
        {
            string option = args[index++];
            if (index >= args.Length || string.IsNullOrWhiteSpace(args[index]) || args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new FormatException($"Option '{option}' requires a value.");
            }

            return args[index];
        }

        private static int ReadPixels(ReadOnlySpan<string> args, ref int index)
        {
            string option = args[index];
            if (!int.TryParse(ReadValue(args, ref index), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int pixels))
            {
                throw new FormatException($"Option '{option}' requires an integer pixel value.");
            }

            return pixels;
        }

        private static void WriteHelp(string? command = null)
        {
            Console.WriteLine("Usage:");
            if (command is null)
            {
                Console.WriteLine("  Porting.Cli --version");
            }
            if (command is null or "inspect")
            {
                Console.WriteLine("  Porting.Cli inspect <source-directory>");
            }

            if (command is null or "plan")
            {
                Console.WriteLine("  Porting.Cli plan <source-directory> [options]");
            }

            if (command is null or "generate")
            {
                Console.WriteLine("  Porting.Cli generate <source-directory> <output-directory> [options]");
            }

            if (command is null or "build")
            {
                Console.WriteLine("  Porting.Cli build <output-directory> [--offline]");
            }
            if (command is null or "package")
            {
                Console.WriteLine("  Porting.Cli package <output-directory> <output-zip> [--offline]");
            }

            if (command is null or "plan" or "generate")
            {
                Console.WriteLine();
                Console.WriteLine("Port options:");
                Console.WriteLine("  --entry <type>       Override the fully qualified game entry type.");
                Console.WriteLine("  --asset <directory>  Replace detected assets with project-relative directories. Repeat to add more.");
                Console.WriteLine("  --no-assets          Use an empty asset list.");
                Console.WriteLine("  --width <pixels>     Override the initial window width (1280 by default).");
                Console.WriteLine("  --height <pixels>    Override the initial window height (720 by default).");
            }

            Console.WriteLine();
            Console.WriteLine("Inspect and plan read project files without running Gradle.");
            if (command is null or "generate")
            {
                Console.WriteLine("Generate requires a new output directory separate from the input.");
            }

            if (command is null or "build")
            {
                Console.WriteLine("Build uses the generated project's Gradle Wrapper. Use --offline to resolve dependencies from the Gradle cache.");
            }
            if (command is null or "package")
            {
                Console.WriteLine("Package installs the Gradle desktop application and bundles a runtime from the Windows x64 JDK in JAVA_HOME.");
                Console.WriteLine("Use --offline to resolve dependencies from the Gradle cache. Extract the ZIP and run Launch.cmd on Windows x64.");
            }

            Console.WriteLine("Exit codes: 0 success, 1 operation failed, 2 invalid arguments, 130 canceled.");
        }

        private static void WriteProject(ProjectInspection inspection)
        {
            Console.WriteLine($"Source: {inspection.SourceDirectory}");
            Console.WriteLine($"Gradle settings: {inspection.GradleSettingsFile ?? "(unresolved)"}");
            Console.WriteLine($"Gradle Wrapper version: {inspection.GradleVersion ?? "(unresolved)"}");
            Console.WriteLine($"Android module: {inspection.AndroidModulePath ?? "(unresolved)"}");
            Console.WriteLine($"Shared module: {inspection.SharedModulePath ?? "(unresolved)"}");
            Console.WriteLine($"Existing desktop module: {inspection.DesktopModulePath ?? "(none)"}");
            Console.WriteLine($"Detected game entry: {inspection.GameEntryType ?? "(unresolved)"}");
        }

        private static void WritePlan(PortPlan plan)
        {
            Console.WriteLine($"Desktop module: {plan.DesktopModulePath}");
            Console.WriteLine($"Desktop game entry: {plan.GameEntryType ?? "(unresolved)"}");
            Console.WriteLine($"Window: {plan.WindowWidth} x {plan.WindowHeight}");
            WriteList("Asset directories", plan.AssetDirectories);
            WriteList("Files to create", plan.FilesToCreate);
            WriteList("Files to modify", plan.FilesToModify);
            WriteList("Gradle tasks", plan.BuildTasks);
        }

        private static void WriteList(string name, IReadOnlyList<string> items)
        {
            Console.WriteLine(items.Count == 0 ? $"{name}: (none)" : $"{name}:");
            foreach (string item in items)
            {
                Console.WriteLine($"  {item}");
            }
        }

        private static int WriteDiagnostics(IReadOnlyList<PortDiagnostic> diagnostics)
        {
            bool failed = false;
            foreach (PortDiagnostic diagnostic in diagnostics)
            {
                string location = diagnostic.RelativePath is null ? "" : diagnostic.LineNumber is int line
                    ? $" {diagnostic.RelativePath}({line}):" : $" {diagnostic.RelativePath}:";
                Console.Error.WriteLine($"{diagnostic.Severity} {diagnostic.Code}:{location} {diagnostic.Message}");
                failed |= diagnostic.Severity == DiagnosticSeverity.Error;
            }

            return failed ? OperationFailed : 0;
        }
    }
}
