using System.ComponentModel;
using System.Diagnostics;
using System.Text;
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
        public async Task<BuildResult> BuildAsync(string projectDirectory, bool offline = false, TextWriter? standardOutput = null,
            TextWriter? standardError = null, CancellationToken cancellationToken = default)
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
                    return Failed("build.project-metadata", "Choose an output directory created by generate.", OutputProjectMetadata.FileName);
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
                    return Failed("build.project-metadata", "Generate a project copy with a supported metadata version and desktop module path.",
                        OutputProjectMetadata.FileName);
                }

                module = desktopModule;
                foreach (string file in new[] { WrapperJar, WrapperProperties })
                {
                    if (!File.Exists(Path.Combine(project, file)))
                    {
                        return Failed("build.wrapper-missing", "Restore the Gradle Wrapper files from the input project before building.", file);
                    }
                }

                string java = "java.exe";
                if (Environment.GetEnvironmentVariable("JAVA_HOME") is string javaHome && !string.IsNullOrWhiteSpace(javaHome))
                {
                    java = Path.GetFullPath(Path.Combine(javaHome.Trim().Trim('"'), "bin", "java.exe"));
                    if (!File.Exists(java))
                    {
                        return Failed("build.java-missing", $"Set JAVA_HOME to a compatible JDK. Java was not found at '{java}'.");
                    }
                }

                ProcessStartInfo start = new(java)
                {
                    WorkingDirectory = project,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                // Invoke the Wrapper directly so Windows project paths are passed to Java without shell expansion.
                foreach (string argument in new[] { "-Dfile.encoding=UTF-8", "-Dorg.gradle.appname=gradlew", "-classpath", WrapperJar,
                    "org.gradle.wrapper.GradleWrapperMain", $"{module}:build", "--console=plain", "--no-daemon" })
                {
                    start.ArgumentList.Add(argument);
                }

                if (offline)
                {
                    start.ArgumentList.Add("--offline");
                }

                int exitCode = await RunWrapperAsync(start, standardOutput ?? TextWriter.Null, standardError ?? TextWriter.Null, cancellationToken);
                return exitCode == 0
                    ? new(project, module, exitCode, [])
                    : Failed("build.gradle-failed", $"Gradle task {module}:build exited with code {exitCode}. See the build output.", exitCode: exitCode);
            }
            catch (JsonException)
            {
                return Failed("build.project-metadata", "Restore the generated project metadata or regenerate the project copy.", OutputProjectMetadata.FileName);
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 2)
            {
                return Failed("build.java-missing", "Set JAVA_HOME to a compatible JDK or add Java to PATH.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
            {
                return Failed("build.run-failed", $"The Gradle Wrapper could not run. {exception.Message}");
            }

            BuildResult Failed(string code, string message, string? file = null, int? exitCode = null)
            {
                return new(project, module, exitCode, [new(code, DiagnosticSeverity.Error, message, file)]);
            }
        }

        private static async Task<int> RunWrapperAsync(ProcessStartInfo start, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Java could not be started.");
            try
            {
                await Task.WhenAll(process.WaitForExitAsync(cancellationToken), ForwardAsync(process.StandardOutput, stdout),
                    ForwardAsync(process.StandardError, stderr));
            }
            catch
            {
                Stop();
                await process.WaitForExitAsync();
                throw;
            }

            return process.ExitCode;

            async Task ForwardAsync(StreamReader reader, TextWriter writer)
            {
                try
                {
                    while (await reader.ReadLineAsync(cancellationToken) is string line)
                    {
                        await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
                    }
                }
                catch
                {
                    Stop();
                    throw;
                }
            }

            void Stop()
            {
                lock (process)
                {
                    if (!process.HasExited)
                    {
                        try
                        {
                            process.Kill(entireProcessTree: true);
                        }
                        catch (InvalidOperationException) when (process.HasExited)
                        {
                            // The Wrapper finished before the stop reached it.
                        }
                    }
                }
            }
        }
    }
}