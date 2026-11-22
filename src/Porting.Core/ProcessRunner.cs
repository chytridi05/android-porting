using System.Diagnostics;
using System.Text;

namespace Porting.Core
{
    /// <summary>Runs desktop tool processes with streamed output and process-tree cancellation.</summary>
    internal static class ProcessRunner
    {
        /// <summary>Creates a shell-free start configuration with redirected UTF-8 output.</summary>
        internal static ProcessStartInfo CreateStartInfo(string executable, string workingDirectory)
        {
            return new(executable)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
        }

        /// <summary>Streams both output channels until the process exits or cancellation stops its tree.</summary>
        internal static async Task<int> RunAsync(ProcessStartInfo start, TextWriter? stdout, TextWriter? stderr,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using Process process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start '{start.FileName}'.");
            try
            {
                await Task.WhenAll(process.WaitForExitAsync(cancellationToken), ForwardAsync(process.StandardOutput, stdout ?? TextWriter.Null),
                    ForwardAsync(process.StandardError, stderr ?? TextWriter.Null));
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
                            // The process finished before the stop reached it.
                        }
                    }
                }
            }
        }
    }
}
