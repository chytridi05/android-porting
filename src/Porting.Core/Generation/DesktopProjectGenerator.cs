using Porting.Core.Inspection;
using Porting.Core.Planning;
using System.Text.Json;

namespace Porting.Core.Generation
{
    /// <summary>Publishes a complete Gradle project copy with an LWJGL3 desktop target.</summary>
    public sealed class DesktopProjectGenerator : IDesktopProjectGenerator
    {
        /// <inheritdoc />
        public async Task<GenerationResult> GenerateAsync(PortPlan plan, string outputDirectory, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
            cancellationToken.ThrowIfCancellationRequested();

            string output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));
            List<PortDiagnostic> diagnostics = [.. plan.Diagnostics];
            GenerationResult result = new(output, diagnostics.AsReadOnly());
            if (diagnostics.Any(static item => item.Severity == DiagnosticSeverity.Error))
            {
                return result;
            }

            string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(plan.Inspection.SourceDirectory));
            if (IsAtOrBelow(source, output) || IsAtOrBelow(output, source))
            {
                diagnostics.Add(new("generation.output-overlap", DiagnosticSeverity.Error,
                    "Choose an output directory separate from the source project."));
                return result;
            }

            if (Path.Exists(output))
            {
                diagnostics.Add(OutputConflict(output));
                return result;
            }

            Lwjgl3Layout? layout = null;
            if (plan.Inspection.DesktopModulePath is null)
            {
                if (plan.Inspection.GradleSettingsFile is not ("settings.gradle" or "settings.gradle.kts")
                    || plan.Inspection.SharedModulePath is null || plan.GameEntryType is not string entry || !Lwjgl3Layout.IsGameEntry(entry)
                    || plan.Inspection.DesktopLibraries is not { LibGdx: not null } libraries || (libraries.UsesBox2D && libraries.Box2D is null)
                    || plan.DesktopModulePath != Lwjgl3Layout.ModulePath || plan.WindowWidth <= 0 || plan.WindowHeight <= 0)
                {
                    diagnostics.Add(new("generation.incomplete-plan", DiagnosticSeverity.Error,
                        "Inspect and plan the source project again with a valid entry, window size, and fixed libGDX dependencies."));
                    return result;
                }

                layout = new(plan.Inspection.GradleSettingsFile, entry);
            }

            string? staging = null;
            try
            {
                string parent = Path.GetDirectoryName(output) ?? throw new IOException("The output directory must have a parent directory.");
                VerifyDirectoryAncestors(source);
                VerifyDirectoryAncestors(parent);
                Directory.CreateDirectory(parent);
                staging = Path.Combine(parent, $".porting-{Guid.NewGuid():N}");
                Directory.CreateDirectory(staging);
                await CopyDirectoryAsync(new DirectoryInfo(source), staging, isRoot: true, cancellationToken);

                if (layout is not null)
                {
                    foreach (string asset in plan.AssetDirectories)
                    {
                        string assetPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(asset, staging));
                        if (assetPath.Equals(staging, StringComparison.OrdinalIgnoreCase)
                            || !IsAtOrBelow(staging, assetPath) || !Directory.Exists(assetPath))
                        {
                            throw new IOException($"Asset directory '{asset}' is missing from the output copy. Choose an asset directory inside the source project.");
                        }
                    }

                    await WriteDesktopModuleAsync(plan, layout, staging, cancellationToken);
                }

                await using (FileStream manifest = File.Open(Path.Combine(staging, OutputProjectMetadata.FileName), FileMode.CreateNew, FileAccess.Write))
                {
                    OutputProjectMetadata metadata = new(OutputProjectMetadata.CurrentVersion, plan.DesktopModulePath);
                    await JsonSerializer.SerializeAsync(manifest, metadata, OutputProjectMetadata.JsonOptions, cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                Directory.Move(staging, output);
                staging = null;
            }
            catch (IOException) when (Path.Exists(output))
            {
                diagnostics.Add(OutputConflict(output));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(new("generation.write-failed", DiagnosticSeverity.Error, exception.Message));
            }
            finally
            {
                if (staging is not null && Directory.Exists(staging))
                {
                    try
                    {
                        Directory.Delete(staging, recursive: true);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        diagnostics.Add(new("generation.cleanup-failed", DiagnosticSeverity.Warning,
                            $"Remove the temporary directory '{staging}' after releasing any open files. {exception.Message}"));
                    }
                }
            }

            return result;
        }

        private static PortDiagnostic OutputConflict(string output)
        {
            return new("generation.output-exists", DiagnosticSeverity.Error,
                $"The output path '{output}' already exists. Choose a new directory or build the existing project copy.");
        }

        private static bool IsAtOrBelow(string root, string path)
        {
            string relative = Path.GetRelativePath(root, path);
            return relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
        }

        private static void VerifyDirectoryAncestors(string directory)
        {
            for (DirectoryInfo? current = new(directory); current is not null; current = current.Parent)
            {
                if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException($"Use a regular directory instead of the link or junction '{current.FullName}'.");
                }
            }
        }

        private static async Task CopyDirectoryAsync(DirectoryInfo source, string destination, bool isRoot, CancellationToken cancellationToken)
        {
            bool hasGradleBuild = isRoot || File.Exists(Path.Combine(source.FullName, "build.gradle")) || File.Exists(Path.Combine(source.FullName, "build.gradle.kts"));
            foreach (FileSystemInfo entry in source.EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Name.Equals(".git", StringComparison.OrdinalIgnoreCase)
                    || entry.Name.Equals(".gradle", StringComparison.OrdinalIgnoreCase)
                    || entry.Name.Equals(".kotlin", StringComparison.OrdinalIgnoreCase)
                    || (isRoot && entry.Name.Equals("local.properties", StringComparison.OrdinalIgnoreCase))
                    || (hasGradleBuild && entry is DirectoryInfo && entry.Name.Equals("build", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException($"Replace the link or junction '{entry.FullName}' with regular project files before generating.");
                }

                string target = Path.Combine(destination, entry.Name);
                if (entry is DirectoryInfo directory)
                {
                    Directory.CreateDirectory(target);
                    await CopyDirectoryAsync(directory, target, isRoot: false, cancellationToken);
                }
                else
                {
                    const int bufferSize = 81920;
                    await using FileStream input = new(entry.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous);
                    await using FileStream output = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize, FileOptions.Asynchronous);
                    await input.CopyToAsync(output, cancellationToken);
                }
            }
        }

        private static async Task WriteDesktopModuleAsync(PortPlan plan, Lwjgl3Layout layout, string directory, CancellationToken cancellationToken)
        {
            string module = Path.Combine(directory, Lwjgl3Layout.DirectoryName);
            if (Path.Exists(module))
            {
                throw new IOException($"The source already contains '{Lwjgl3Layout.DirectoryName}'. Choose an input that matches the plan.");
            }

            string launcher = Path.Combine(directory, layout.LauncherFile);
            Directory.CreateDirectory(Path.GetDirectoryName(launcher)!);
            await File.WriteAllTextAsync(launcher, Lwjgl3Templates.CreateLauncher(plan, layout), cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory, layout.BuildScript), Lwjgl3Templates.CreateBuildScript(plan, layout), cancellationToken);

            string settings = Path.Combine(directory, layout.SettingsFile);
            string contents = await File.ReadAllTextAsync(settings, cancellationToken);
            string newline = contents.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            await File.AppendAllTextAsync(settings, $"{newline}include(\"{Lwjgl3Layout.ModulePath}\"){newline}", cancellationToken);
        }
    }
}