using Porting.Core;
using Porting.Core.Inspection;
using Porting.Core.Planning;
using System.Globalization;

namespace Porting.Cli
{
    /// <summary>Dispatches CLI commands and formats shared porting results for the console.</summary>
    internal sealed class PortingCommandLine(IProjectInspector inspector, IPortPlanner planner)
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

            try
            {
                string command = args[0];
                if (command is not ("inspect" or "plan"))
                {
                    throw new FormatException($"Unknown command '{command}'.");
                }

                if (args is [_, "--help" or "-h"])
                {
                    WriteHelp(command);
                    return 0;
                }

                string source = ReadDirectory(args, 1, "source project");
                PortOptions? options = command == "plan" ? ReadPortOptions(args.AsSpan(2)) : null;
                if (command == "inspect" && args.Length > 2)
                {
                    throw new FormatException($"Unexpected argument '{args[2]}'.");
                }

                ProjectInspection inspection = await inspector.InspectAsync(source, cancellationToken);
                WriteProject(inspection);
                if (options is not null)
                {
                    PortPlan plan = planner.CreatePlan(inspection, options);
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

        private static string ReadDirectory(string[] args, int index, string name)
        {
            if (index >= args.Length || string.IsNullOrWhiteSpace(args[index]) || args[index].StartsWith('-'))
            {
                throw new FormatException($"Specify the {name} directory.");
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
            if (command is null or "inspect")
            {
                Console.WriteLine("  Porting.Cli inspect <source-directory>");
            }

            if (command is null or "plan")
            {
                Console.WriteLine("  Porting.Cli plan <source-directory> [options]");
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