using System.IO;
using System.Text;

namespace Porting.Desktop
{
    /// <summary>Buffers the recent output of concurrent Gradle streams for the UI dispatcher.</summary>
    internal sealed class OperationLog : TextWriter
    {
        /// <summary>The maximum number of characters retained in the pending buffer or the visible log.</summary>
        internal const int CharacterLimit = 120_000;
        private readonly Lock gate = new();
        private readonly StringBuilder pending = new();

        /// <inheritdoc />
        public override Encoding Encoding => Encoding.UTF8;

        /// <inheritdoc />
        public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                pending.Append(buffer.Span).AppendLine();
                if (pending.Length > CharacterLimit)
                {
                    _ = pending.Remove(0, pending.Length - CharacterLimit);
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>Reads and clears any output written since the previous drain.</summary>
        internal string Drain()
        {
            lock (gate)
            {
                string output = pending.ToString();
                _ = pending.Clear();
                return output;
            }
        }
    }
}