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

                string source = ReadRequiredArgument(args, 1, "source project directory, APK, or APK set");
                string? output = command == "generate" ? ReadRequiredArgument(args, 2, "output project directory") : null;
                if (Path.GetExtension(source).ToLowerInvariant() is ".apk" or ".apks" && !Directory.Exists(source))
                {
                    return command == "inspect"
                        ? await RunApkInspectAsync(source, args, cancellationToken)
                        : await RunApkPortAsync(source, output, args.AsMemory(output is null ? 2 : 3), cancellationToken);
                }

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

        private static async Task<int> RunApkInspectAsync(string source, string[] args, CancellationToken cancellationToken)
        {
            ApkPortOptions options = ReadApkPortOptions(args.AsSpan(2));
            ApkInventory inventory = await new ApkInspector().InspectPortAsync(source, options, cancellationToken);
            Console.WriteLine($"APK: {inventory.ApkPath}");
            if (inventory.Package is ApkInputFile package)
            {
                Console.WriteLine($"APK SHA-256: {package.Sha256}");
            }
            if (inventory.BaseApk is ApkInputFile baseApk)
            {
                Console.WriteLine($"Base APK: {baseApk.Sha256}  {baseApk.OriginalPath}");
            }

            WriteList("DEX entries", inventory.Entries.Select(static entry => entry.OriginalPath)
                .Where(static path => path.StartsWith("classes", StringComparison.Ordinal)
                    && path.EndsWith(".dex", StringComparison.Ordinal) && !path.Contains('/')).ToArray());
            WriteList("APK assets", inventory.Entries.Select(static entry => entry.OriginalPath)
                .Where(static path => path.StartsWith("assets/", StringComparison.Ordinal)).ToArray());
            WriteList("Native libraries", inventory.Entries.Select(static entry => entry.OriginalPath)
                .Where(static path => path.StartsWith("lib/", StringComparison.Ordinal)).ToArray());
            if (inventory.Splits.Count > 0)
            {
                WriteList("Split APKs", inventory.Splits.Select(static split => split.File.ContainerPath is string container
                    ? $"{split.File.Sha256}  {container}!/{split.File.OriginalPath}" : $"{split.File.Sha256}  {split.File.OriginalPath}").ToArray());
                WriteList("Split entries", inventory.Splits.SelectMany(static split => split.Entries)
                    .Select(static entry => $"{entry.Sha256}  {entry.ContainerPath}!/{entry.OriginalPath}").ToArray());
            }
            WriteList("Companion files", inventory.Companions.Select(static file => file.ContainerPath is null
                ? $"{file.Sha256}  {file.OriginalPath}"
                : $"{file.Sha256}  {file.ContainerPath}!/{file.OriginalPath}").ToArray());
            return WriteDiagnostics(inventory.Diagnostics);
        }

        private static async Task<int> RunApkPortAsync(string source, string? output, ReadOnlyMemory<string> args,
            CancellationToken cancellationToken)
        {
            ApkPortOptions options = ReadApkPortOptions(args.Span);
            ApkProjectPorter porter = new();
            ApkPortResult result = output is null
                ? await porter.PlanAsync(source, options, cancellationToken)
                : await porter.GenerateAsync(source, output, options, cancellationToken);
            if (result.PackageName is not null)
            {
                Console.WriteLine($"Android package: {result.PackageName}");
                Console.WriteLine($"Desktop game entry: {result.EntryType}");
                Console.WriteLine($"libGDX version: {result.LibGdxVersion}");
                Console.WriteLine($"Recovered source files: {result.SourceFiles}");
                Console.WriteLine($"Mapped resources: {result.Resources}");
            }
            if (result.OutputDirectory is string project)
            {
                Console.WriteLine($"Generated project: {project}");
            }

            return WriteDiagnostics(result.Diagnostics);
        }

        private static ApkPortOptions ReadApkPortOptions(ReadOnlySpan<string> args)
        {
            List<string> companions = [];
            List<string> expansions = [];
            List<string> assets = [];
            List<AttachmentSelection> attachments = [];
            List<string> splits = [];
            int? variant = null;
            Dictionary<string, string> values = new(StringComparer.Ordinal);
            for (int index = 0; index < args.Length; index++)
            {
                string option = args[index];
                switch (option)
                {
                    case "--companion":
                        companions.Add(ReadValue(args, ref index));
                        break;
                    case "--split":
                        splits.Add(ReadValue(args, ref index));
                        break;
                    case "--apk-variant":
                        if (variant.HasValue || !int.TryParse(ReadValue(args, ref index), NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                        {
                            throw new FormatException("Option '--apk-variant' requires one nonnegative variant number.");
                        }
                        variant = number;
                        break;
                    case "--obb":
                        expansions.Add(ReadValue(args, ref index));
                        break;
                    case "--require-asset":
                        assets.Add(ReadValue(args, ref index));
                        break;
                    case "--map-asset":
                        string path = ReadValue(args, ref index);
                        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]) || args[index].StartsWith("--", StringComparison.Ordinal))
                        {
                            throw new FormatException("Option '--map-asset' requires a source path and a resource path.");
                        }
                        attachments.Add(AttachmentSelection.MapAsset(path, args[index]));
                        break;
                    case "--asset-pack":
                        attachments.Add(AttachmentSelection.UseAssetPack(ReadValue(args, ref index)));
                        break;
                    case "--entry":
                    case "--jadx":
                    case "--gradle-home":
                    case "--r8-mapping":
                        if (!values.TryAdd(option, ReadValue(args, ref index)))
                        {
                            throw new FormatException($"Option '{option}' can only be used once.");
                        }
                        break;
                    default:
                        throw new FormatException($"Unknown APK port option '{option}'.");
                }
            }

            return new(companions, expansions, assets, values.GetValueOrDefault("--jadx"),
                values.GetValueOrDefault("--gradle-home"), values.GetValueOrDefault("--entry"))
            {
                Attachments = attachments.AsReadOnly(),
                SplitPaths = splits.AsReadOnly(),
                ApkSetVariant = variant,
                R8MappingPath = values.GetValueOrDefault("--r8-mapping")
            };
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
            bool offline = false;
            string? applicationName = null;
            for (int index = 3; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--offline" when !offline:
                        offline = true;
                        break;
                    case "--exe" when applicationName is null:
                        applicationName = ReadValue(args, ref index);
                        break;
                    default:
                        throw new FormatException($"Unexpected argument '{args[index]}'.");
                }
            }

            PackageResult package = applicationName is null
                ? await packager.PackageAsync(project, archive, offline, Console.Out, Console.Error, cancellationToken)
                : await packager.PackageExecutableAsync(project, archive, applicationName, offline, Console.Out, Console.Error, cancellationToken);
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
                Console.WriteLine("  Porting.Cli inspect <apk-or-apks> [APK options]");
            }

            if (command is null or "plan")
            {
                Console.WriteLine("  Porting.Cli plan <source-directory> [options]");
                Console.WriteLine("  Porting.Cli plan <apk-or-apks> [APK options]");
            }

            if (command is null or "generate")
            {
                Console.WriteLine("  Porting.Cli generate <source-directory> <output-directory> [options]");
                Console.WriteLine("  Porting.Cli generate <apk-or-apks> <output-directory> [APK options]");
            }

            if (command is null or "build")
            {
                Console.WriteLine("  Porting.Cli build <output-directory> [--offline]");
            }
            if (command is null or "package")
            {
                Console.WriteLine("  Porting.Cli package <output-directory> <output-zip> [--offline] [--exe <name>]");
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
                Console.WriteLine();
                Console.WriteLine("APK options:");
                Console.WriteLine("  --companion <path>       Preserve a supplied file or directory. Repeat to add more.");
                Console.WriteLine("  --split <apk>            Include a matching split APK. Repeat to add more.");
                Console.WriteLine("  --apk-variant <number>   Select a bundletool variant when several are available.");
                Console.WriteLine("  --obb <file>             Use a supplied file as a ZIP expansion archive. Repeat to add more.");
                Console.WriteLine("  --map-asset <path> <resource>  Map a file or directory. Use '.' to map a directory at the resource root.");
                Console.WriteLine("  --asset-pack <path>      Import a supplied ZIP/APK asset pack or extracted asset root.");
                Console.WriteLine("  --r8-mapping <file>      Import the optional R8 mapping from the same APK build.");
                Console.WriteLine("  --require-asset <path>   Require a case-sensitive resource path. Repeat to add more.");
                Console.WriteLine("  --entry <type>          Override the recovered libGDX entry type.");
                Console.WriteLine("  --jadx <jar>             Use the JADX CLI JAR. Defaults to PORTING_JADX_JAR.");
                Console.WriteLine("  --gradle-home <path>     Use an unpacked Gradle 8.14.3 distribution. Defaults to GRADLE_HOME.");
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
                Console.WriteLine("Use --exe <name> to create an application executable with jpackage. Otherwise the ZIP includes Launch.cmd.");
                Console.WriteLine("Use --offline to resolve dependencies from the Gradle cache.");
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
