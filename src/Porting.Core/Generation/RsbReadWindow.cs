namespace Porting.Core.Generation;

/// <summary>Reads a bounded range from a seekable stream without taking ownership of it.</summary>
internal sealed class RsbReadWindow(Stream source, long offset, long length, bool requireCompleteStream = false) : Stream
{
    private long position;

    /// <inheritdoc />
    public override bool CanRead => true;
    /// <inheritdoc />
    public override bool CanSeek => false;
    /// <inheritdoc />
    public override bool CanWrite => false;
    /// <inheritdoc />
    public override long Length => length;
    /// <inheritdoc />
    public override long Position
    {
        get => position;
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        return Read(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        int count = PrepareRead(buffer.Length);
        if (count == 0)
        {
            return 0;
        }
        int read = source.Read(buffer[..count]);
        return Advance(read);
    }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int count = PrepareRead(buffer.Length);
        if (count == 0)
        {
            return 0;
        }
        int read = await source.ReadAsync(buffer[..count], cancellationToken);
        return Advance(read);
    }

    private int PrepareRead(int count)
    {
        if (position == length)
        {
            if (count != 0 && requireCompleteStream)
            {
                throw new InvalidDataException("The RSB zlib stream ended before reaching its trailer.");
            }
            return 0;
        }

        long start = offset + position;
        if (source.Position != start)
        {
            source.Position = start;
        }
        return (int)Math.Min(count, length - position);
    }

    private int Advance(int read)
    {
        if (read == 0)
        {
            throw new InvalidDataException("The RSB data ends inside a declared section.");
        }
        position += read;
        return read;
    }

    /// <inheritdoc />
    public override void Flush() => throw new NotSupportedException();
    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();
    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
