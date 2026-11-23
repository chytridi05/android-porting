using Porting.Core.Building;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Porting.Core.Packaging
{
    /// <summary>Installs a Gradle application and packages it with a Windows x64 Java image.</summary>
    public sealed class GradleDesktopProjectPackager : IDesktopProjectPackager
    {
        private const string InstallScript = """
            import groovy.json.JsonOutput
            import org.gradle.jvm.application.tasks.CreateStartScripts

            gradle.projectsEvaluated {
                def desktop = gradle.rootProject.project(System.getProperty('porting.desktopModule'))
                if (!desktop.pluginManager.hasPlugin('application')) {
                    throw new GradleException("Desktop module ${desktop.path} must apply the Gradle application plugin to use package.")
                }

                def scripts = desktop.tasks.named('startScripts', CreateStartScripts)
                def destination = new File(System.getProperty('porting.installDirectory'))
                def metadata = new File(System.getProperty('porting.launchMetadata'))
                desktop.tasks.named('installDist', Sync).configure {
                    into(destination)
                    outputs.file(metadata)
                    doLast {
                        def launcher = scripts.get()
                        metadata.setText(JsonOutput.toJson([
                            mainClass: launcher.mainClass.orNull,
                            mainModule: launcher.mainModule.orNull,
                            mainJar: desktop.tasks.named('jar').get().archiveFile.get().asFile.name,
                            jvmOptions: launcher.defaultJvmOpts ?: [],
                            libraries: launcher.classpath.files.collect { it.name }
                        ]), 'UTF-8')
                    }
                }
            }
            """;

        private const string LaunchScript = """
            @echo off
            setlocal DisableDelayedExpansion
            pushd "%~dp0" || exit /b 1
            "runtime\bin\java.exe" "@launch.args" %*
            set "exitCode=%ERRORLEVEL%"
            popd
            exit /b %exitCode%
            """;

        /// <inheritdoc />
        public Task<PackageResult> PackageAsync(string projectDirectory, string archivePath, bool offline = false,
            TextWriter? standardOutput = null, TextWriter? standardError = null, CancellationToken cancellationToken = default)
        {
            return PackageAsync(projectDirectory, archivePath, null, offline, standardOutput, standardError, cancellationToken);
        }

        /// <inheritdoc />
        public Task<PackageResult> PackageExecutableAsync(string projectDirectory, string archivePath, string applicationName, bool offline = false,
            TextWriter? standardOutput = null, TextWriter? standardError = null, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
            return PackageAsync(projectDirectory, archivePath, applicationName, offline, standardOutput, standardError, cancellationToken);
        }

        private static async Task<PackageResult> PackageAsync(string projectDirectory, string archivePath, string? applicationName, bool offline,
            TextWriter? standardOutput, TextWriter? standardError, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
            ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
            cancellationToken.ThrowIfCancellationRequested();
            string archive = Path.GetFullPath(archivePath);
            if (!Path.GetExtension(archive).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return Failed("archive-path", "Choose an output filename ending in .zip.");
            }
            if (Path.Exists(archive))
            {
                return ArchiveExists();
            }
            if (applicationName is not null && !Regex.IsMatch(applicationName, @"\A[A-Za-z][A-Za-z0-9_-]{0,63}\z"))
            {
                return Failed("application-name", "Choose an executable name starting with an ASCII letter, with up to 64 letters, digits, hyphens, or underscores.");
            }

            DirectoryInfo? staging = null;
            string? temporaryArchive = null;
            try
            {
                string? javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
                if (string.IsNullOrWhiteSpace(javaHome))
                {
                    return Failed("jdk-missing", "Set JAVA_HOME to a compatible Windows x64 JDK with jlink and a jmods directory.");
                }

                string jdk = Path.GetFullPath(javaHome.Trim().Trim('"'));
                string jlink = Path.Combine(jdk, "bin", "jlink.exe");
                string jpackage = Path.Combine(jdk, "bin", "jpackage.exe");
                string modules = Path.Combine(jdk, "jmods");
                if (!File.Exists(jlink) || !Directory.Exists(modules) || !IsX64Executable(Path.Combine(jdk, "bin", "java.exe")))
                {
                    return Failed("jdk-missing", $"Set JAVA_HOME to a compatible Windows x64 JDK with jlink and a jmods directory. Checked '{jdk}'.");
                }
                if (applicationName is not null && !IsX64Executable(jpackage))
                {
                    return Failed("jpackage-missing", "Set JAVA_HOME to a Windows x64 JDK with jpackage.exe.");
                }

                staging = Directory.CreateTempSubdirectory("porting-package-");
                string application = Path.Combine(staging.FullName, "application");
                string installScript = Path.Combine(staging.FullName, "install.gradle");
                string metadataPath = Path.Combine(staging.FullName, "launch.json");
                await File.WriteAllTextAsync(installScript, InstallScript, cancellationToken);
                BuildResult install = await GradleDesktopProjectBuilder.RunGradleAsync(projectDirectory, "installDist", "package", offline,
                    module => ["--no-configuration-cache", "--init-script", installScript, $"-Dporting.desktopModule={module}",
                        $"-Dporting.installDirectory={application}", $"-Dporting.launchMetadata={metadataPath}"],
                    standardOutput, standardError, cancellationToken);
                if (install.Diagnostics.Count > 0)
                {
                    return new(archive, install.Diagnostics);
                }

                LaunchMetadata? launcher = File.Exists(metadataPath)
                    ? JsonSerializer.Deserialize<LaunchMetadata>(await File.ReadAllTextAsync(metadataPath, cancellationToken), OutputProjectMetadata.JsonOptions)
                    : null;
                if (launcher is not { MainClass: { Length: > 0 }, MainModule: null, Libraries: { Length: > 0 } }
                    || launcher.Libraries.Any(library => !IsInstalledLibrary(application, library)))
                {
                    return Failed("distribution", "Configure a classpath-based Gradle application with a main class and runtime libraries in lib/.");
                }
                if (applicationName is not null && (launcher.MainJar is null || !launcher.Libraries.Contains(launcher.MainJar, StringComparer.Ordinal)))
                {
                    return Failed("main-jar", "The Gradle application JAR must be installed in lib/ to create an executable.");
                }

                string runtime = Path.Combine(application, "runtime");
                string launchScript = Path.Combine(application, "Launch.cmd");
                string launchArguments = Path.Combine(application, "launch.args");
                if (Path.Exists(runtime) || Path.Exists(launchScript) || Path.Exists(launchArguments))
                {
                    return Failed("distribution-conflict", "Rename the distribution entries named runtime, Launch.cmd, or launch.args before packaging.");
                }

                ProcessStartInfo link = ProcessRunner.CreateStartInfo(jlink, staging.FullName);
                foreach (string argument in new[] { "--module-path", modules, "--add-modules", "ALL-MODULE-PATH", "--no-header-files",
                    "--no-man-pages", "--output", runtime })
                {
                    link.ArgumentList.Add(argument);
                }

                int result = await ProcessRunner.RunAsync(link, standardOutput, standardError, cancellationToken);
                if (result != 0 || !IsX64Executable(Path.Combine(runtime, "bin", "java.exe")))
                {
                    return Failed("runtime", $"jlink did not produce a Windows x64 Java runtime. Exit code: {result}. See the package output.");
                }

                string archiveSource = application;
                if (applicationName is not null)
                {
                    string imageDirectory = Path.Combine(staging.FullName, "image");
                    ProcessStartInfo image = ProcessRunner.CreateStartInfo(jpackage, staging.FullName);
                    foreach (string argument in new[]
                    {
                        "--type", "app-image",
                        "--input", Path.Combine(application, "lib"),
                        "--main-jar", launcher.MainJar!,
                        "--main-class", launcher.MainClass,
                        "--runtime-image", runtime,
                        "--name", applicationName,
                        "--dest", imageDirectory
                    })
                    {
                        image.ArgumentList.Add(argument);
                    }
                    foreach (string option in launcher.JvmOptions ?? [])
                    {
                        image.ArgumentList.Add("--java-options");
                        image.ArgumentList.Add(QuoteJavaArgument(option));
                    }

                    result = await ProcessRunner.RunAsync(image, standardOutput, standardError, cancellationToken);
                    archiveSource = Path.Combine(imageDirectory, applicationName);
                    if (result != 0 || !IsX64Executable(Path.Combine(archiveSource, applicationName + ".exe")))
                    {
                        return Failed("executable", $"jpackage did not produce the Windows x64 application. Exit code: {result}. See the package output.");
                    }
                }
                else
                {
                    string classpath = string.Join(';', launcher.Libraries.Select(static library => $"lib/{library}"));
                    string[] arguments = [.. launcher.JvmOptions ?? [], "-classpath", classpath, launcher.MainClass];
                    await File.WriteAllLinesAsync(launchArguments, arguments.Select(QuoteJavaArgument), cancellationToken);
                    await File.WriteAllTextAsync(launchScript, LaunchScript.ReplaceLineEndings("\r\n") + "\r\n", cancellationToken);
                }

                string archiveDirectory = Path.GetDirectoryName(archive)!;
                _ = Directory.CreateDirectory(archiveDirectory);
                string stagedPath = Path.Combine(archiveDirectory, $".porting-{Guid.NewGuid():N}.tmp");
                await using (FileStream zip = File.Open(stagedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    temporaryArchive = stagedPath;
                    await ZipFile.CreateFromDirectoryAsync(archiveSource, zip, CompressionLevel.Optimal, includeBaseDirectory: false, cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporaryArchive, archive);
                temporaryArchive = null;
                return new(archive, []);
            }
            catch (IOException) when (Path.Exists(archive))
            {
                return ArchiveExists();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException or JsonException)
            {
                return Failed("write-failed", $"The package could not be created. {exception.Message}");
            }
            finally
            {
                if (temporaryArchive is not null)
                {
                    TryCleanUp(() => File.Delete(temporaryArchive));
                }
                if (staging is not null)
                {
                    TryCleanUp(() => staging.Delete(recursive: true));
                }
            }

            PackageResult Failed(string code, string message) => new(archive, [new($"package.{code}", DiagnosticSeverity.Error, message)]);

            PackageResult ArchiveExists() => Failed("archive-exists", $"Choose a new ZIP path. '{archive}' already exists.");
        }

        private static bool IsInstalledLibrary(string application, string library)
        {
            return !string.IsNullOrEmpty(library) && !library.Contains(';') && Path.GetFileName(library) == library
                && File.Exists(Path.Combine(application, "lib", library));
        }

        private static bool IsX64Executable(string path)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                using PEReader executable = new(File.OpenRead(path));
                return executable.PEHeaders.CoffHeader.Machine == Machine.Amd64;
            }
            catch (BadImageFormatException)
            {
                return false;
            }
        }

        private static string QuoteJavaArgument(string argument)
        {
            return "\"" + argument.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal) + "\"";
        }

        private static void TryCleanUp(Action delete)
        {
            try
            {
                delete();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A locked staging file does not change the published archive or the operation result.
            }
        }

        private sealed record LaunchMetadata(string? MainClass, string? MainModule, string? MainJar, string[]? JvmOptions, string[] Libraries);
    }
}