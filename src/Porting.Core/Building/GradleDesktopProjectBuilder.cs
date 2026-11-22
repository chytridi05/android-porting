using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Porting.Core.Building
{
    /// <summary>Runs the generated project's Windows desktop build through its Gradle Wrapper JAR.</summary>
    public sealed class GradleDesktopProjectBuilder : IDesktopProjectBuilder
    {
        private const string WrapperJar = "gradle/wrapper/gradle-wrapper.jar";
        private const string WrapperProperties = "gradle/wrapper/gradle-wrapper.properties";

        /// <inheritdoc />
        public Task<BuildResult> BuildAsync(string projectDirectory, bool offline = false, TextWriter? standardOutput = null,
            TextWriter? standardError = null, CancellationToken cancellationToken = default)
        {
            return RunGradleAsync(projectDirectory, "build", "build", offline, null, standardOutput, standardError, cancellationToken);
        }

        /// <summary>Runs a Wrapper task on the desktop module recorded by generate.</summary>
        internal static async Task<BuildResult> RunGradleAsync(string projectDirectory, string task, string diagnosticPrefix, bool offline,
            Func<string, string[]>? additionalArguments, TextWriter? standardOutput, TextWriter? standardError, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
            cancellationToken.ThrowIfCancellationRequested();
            string project = Path.GetFullPath(projectDirectory);
            string? module = null;
            string metadataPath = Path.Combine(project, OutputProjectMetadata.FileName);

            try
            {
                if (!File.Exists(metadataPath))
                {
                    return Failed("project-metadata", "Choose an output directory created by generate.", OutputProjectMetadata.FileName);
                }

                OutputProjectMetadata? metadata;
                await using (FileStream input = File.OpenRead(metadataPath))
                {
                    metadata = await JsonSerializer.DeserializeAsync<OutputProjectMetadata>(input, OutputProjectMetadata.JsonOptions, cancellationToken);
                }

                if (metadata is not { FormatVersion: OutputProjectMetadata.CurrentVersion, DesktopModule: string desktopModule }
                    || !Regex.IsMatch(desktopModule, @"\A:[\w.-]+(?::[\w.-]+)*\z")
                    || desktopModule.Split(':').Any(static segment => segment is "." or ".."))
                {
                    return Failed("project-metadata", "Generate a project copy with a supported metadata version and desktop module path.",
                        OutputProjectMetadata.FileName);
                }

                module = desktopModule;
                foreach (string file in new[] { WrapperJar, WrapperProperties })
                {
                    if (!File.Exists(Path.Combine(project, file)))
                    {
                        return Failed("wrapper-missing", "Restore the Gradle Wrapper files from the input project before building.", file);
                    }
                }

                string java = "java.exe";
                if (Environment.GetEnvironmentVariable("JAVA_HOME") is string javaHome && !string.IsNullOrWhiteSpace(javaHome))
                {
                    java = Path.GetFullPath(Path.Combine(javaHome.Trim().Trim('"'), "bin", "java.exe"));
                    if (!File.Exists(java))
                    {
                        return Failed("java-missing", $"Set JAVA_HOME to a compatible JDK. Java was not found at '{java}'.");
                    }
                }

                ProcessStartInfo start = ProcessRunner.CreateStartInfo(java, project);

                // Invoke the Wrapper directly so Windows project paths are passed to Java without shell expansion.
                foreach (string argument in new[] { "-Dfile.encoding=UTF-8", "-Dorg.gradle.appname=gradlew", "-classpath", WrapperJar,
                    "org.gradle.wrapper.GradleWrapperMain", $"{module}:{task}", "--console=plain", "--no-daemon" })
                {
                    start.ArgumentList.Add(argument);
                }

                if (offline)
                {
                    start.ArgumentList.Add("--offline");
                }

                foreach (string argument in additionalArguments?.Invoke(module) ?? [])
                {
                    start.ArgumentList.Add(argument);
                }

                int exitCode = await ProcessRunner.RunAsync(start, standardOutput, standardError, cancellationToken);
                return exitCode == 0
                    ? new(project, module, exitCode, [])
                    : Failed("gradle-failed", $"Gradle task {module}:{task} exited with code {exitCode}. See the {diagnosticPrefix} output.", exitCode: exitCode);
            }
            catch (JsonException)
            {
                return Failed("project-metadata", "Restore the generated project metadata or regenerate the project copy.", OutputProjectMetadata.FileName);
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 2)
            {
                return Failed("java-missing", "Set JAVA_HOME to a compatible JDK or add Java to PATH.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
            {
                return Failed("run-failed", $"The Gradle Wrapper could not run. {exception.Message}");
            }

            BuildResult Failed(string code, string message, string? file = null, int? exitCode = null)
            {
                return new(project, module, exitCode, [new($"{diagnosticPrefix}.{code}", DiagnosticSeverity.Error, message, file)]);
            }
        }
    }
}
