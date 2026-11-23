using Porting.Core.Inspection;
using System.IO.Compression;
using System.Text;

namespace Porting.Core.Generation;

/// <summary>Reads embedded version 4 RSB groups and maps their resource bytes into a desktop export.</summary>
internal sealed class RsbAttachmentAdapter : IAttachmentAdapter
{
    internal const string Id = "rsb-v4";
    private const int GroupRecordLength = 0xCC;
    private const int GroupNameLength = 0x80;
    private const uint PacketHeaderLength = 0x50;

    private sealed record Group(AttachmentSourceGroup Source, uint Offset, uint Length);
    private sealed record Section(uint Offset, uint StoredLength, uint OutputLength, bool Compressed);

    /// <inheritdoc />
    public string FormatId => Id;

    /// <inheritdoc />
    public async ValueTask<bool> RecognizesAsync(AttachmentInput input, CancellationToken cancellationToken)
    {
        await using FileStream stream = OpenBundle(input.SnapshotPath);
        byte[] prefix = new byte[4];
        int read = await stream.ReadAtLeastAsync(prefix, prefix.Length, false, cancellationToken);
        return RsbBundleHeader.HasSignature(prefix.AsSpan(0, read));
    }

    /// <inheritdoc />
    public async Task ReadAsync(AttachmentInput input, IAttachmentResourceWriter writer, CancellationToken cancellationToken)
    {
        await using FileStream stream = OpenBundle(input.SnapshotPath);
        RsbBundleHeader header = await RsbBundleHeader.ProbeAsync(stream, cancellationToken)
            ?? throw new InvalidDataException("The attachment does not contain an RSB header.");
        if (header.Version != RsbBundleHeader.SupportedVersion || (header.GroupCount != 0 && header.GroupRecordLength != GroupRecordLength))
        {
            throw new InvalidDataException($"RSB version {header.Version} with a {header.GroupRecordLength}-byte group record is not supported. Supply a readable resource export.");
        }

        List<Group> groups = await ReadGroupsAsync(stream, header, cancellationToken);
        byte[] copyBuffer = new byte[81920];
        foreach (Group group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await ReadPacketAsync(stream, group, writer, copyBuffer, cancellationToken);
            }
            catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException)
            {
                throw new InvalidDataException($"RSB group {group.Source.Index} '{group.Source.Name}': {exception.Message}", exception);
            }
        }
    }

    private static async Task<List<Group>> ReadGroupsAsync(FileStream stream, RsbBundleHeader header, CancellationToken cancellationToken)
    {
        List<Group> groups = [];
        stream.Position = header.GroupTableOffset;
        byte[] record = new byte[GroupRecordLength];
        for (uint index = 0; index < header.GroupCount; index++)
        {
            await stream.ReadExactlyAsync(record, cancellationToken);
            int nameLength = Array.IndexOf(record, (byte)0, 0, GroupNameLength);
            if (nameLength <= 0)
            {
                throw new InvalidDataException($"RSB group {index} has an invalid name.");
            }

            uint offset = RsbBundleHeader.ReadUInt32(record, GroupNameLength);
            uint length = RsbBundleHeader.ReadUInt32(record, GroupNameLength + 4);
            if (offset < header.InformationLength || length < PacketHeaderLength || (long)offset + length > stream.Length)
            {
                throw new InvalidDataException($"RSB group {index} is outside the embedded packet area.");
            }
            groups.Add(new(new(Encoding.Latin1.GetString(record, 0, nameLength), index), offset, length));
        }

        long end = header.InformationLength;
        foreach (Group group in groups.OrderBy(static group => group.Offset))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (group.Offset < end)
            {
                throw new InvalidDataException($"RSB group {group.Source.Index} overlaps another embedded packet.");
            }
            end = (long)group.Offset + group.Length;
        }
        return groups;
    }

    private static async Task ReadPacketAsync(FileStream stream, Group group, IAttachmentResourceWriter writer, byte[] copyBuffer,
        CancellationToken cancellationToken)
    {
        stream.Position = group.Offset;
        byte[] header = new byte[PacketHeaderLength];
        await stream.ReadExactlyAsync(header, cancellationToken);
        if (!header.AsSpan(0, 4).SequenceEqual("pgsr"u8) || RsbBundleHeader.ReadUInt32(header, 4) != RsbBundleHeader.SupportedVersion)
        {
            throw new InvalidDataException("The packet is not an embedded RSG v4 group.");
        }

        uint flags = RsbBundleHeader.ReadUInt32(header, 0x10);
        if (flags > 3)
        {
            throw new InvalidDataException($"The packet uses unsupported compression flags {flags}.");
        }

        uint informationLength = RsbBundleHeader.ReadUInt32(header, 0x14);
        uint directoryLength = RsbBundleHeader.ReadUInt32(header, 0x48);
        uint directoryOffset = RsbBundleHeader.ReadUInt32(header, 0x4C);
        if (informationLength < PacketHeaderLength || informationLength > group.Length || directoryLength > RsbResourceDirectory.MaximumLength
            || (directoryLength != 0 && (directoryOffset < PacketHeaderLength || (long)directoryOffset + directoryLength > informationLength)))
        {
            throw new InvalidDataException("The packet resource directory is outside the supported information area.");
        }

        Section ordinary = ReadSection(header, 0x18, (flags & 2) != 0, informationLength, group.Length);
        Section textures = ReadSection(header, 0x28, (flags & 1) != 0, informationLength, group.Length);
        if (ordinary.StoredLength != 0 && textures.StoredLength != 0
            && ordinary.Offset < (long)textures.Offset + textures.StoredLength && textures.Offset < (long)ordinary.Offset + ordinary.StoredLength)
        {
            throw new InvalidDataException("The packet data sections overlap.");
        }

        byte[] directory = new byte[directoryLength];
        if (directoryLength != 0)
        {
            stream.Position = (long)group.Offset + directoryOffset;
            await stream.ReadExactlyAsync(directory, cancellationToken);
        }
        List<RsbResourceDirectory.Entry> entries = RsbResourceDirectory.Read(directory, cancellationToken);
        foreach (RsbResourceDirectory.Entry entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Section section = entry.Part == 0 ? ordinary : textures;
            if ((long)entry.Offset + entry.Length > section.OutputLength)
            {
                throw new InvalidDataException($"Resource '{entry.OriginalPath}' extends beyond its data section.");
            }
        }

        await WriteSectionAsync(stream, group, ordinary, entries.Where(static entry => entry.Part == 0), writer, copyBuffer, cancellationToken);
        await WriteSectionAsync(stream, group, textures, entries.Where(static entry => entry.Part == 1), writer, copyBuffer, cancellationToken);
    }

    private static Section ReadSection(byte[] header, int field, bool compressed, uint informationLength, uint packetLength)
    {
        uint offset = RsbBundleHeader.ReadUInt32(header, field);
        uint storedLength = RsbBundleHeader.ReadUInt32(header, field + 4);
        uint outputLength = RsbBundleHeader.ReadUInt32(header, field + 8);
        if (storedLength == 0 ? outputLength != 0
            : offset < informationLength || (long)offset + storedLength > packetLength || (!compressed && storedLength != outputLength))
        {
            throw new InvalidDataException($"The packet data section at offset {offset} has inconsistent bounds or lengths.");
        }
        return new(offset, storedLength, outputLength, compressed);
    }

    private static async Task WriteSectionAsync(FileStream bundle, Group group, Section section, IEnumerable<RsbResourceDirectory.Entry> entries,
        IAttachmentResourceWriter writer, byte[] copyBuffer, CancellationToken cancellationToken)
    {
        long dataOffset = (long)group.Offset + section.Offset;
        if (!section.Compressed || section.StoredLength == 0)
        {
            await WriteEntriesAsync(bundle, dataOffset, group.Source, entries, writer, cancellationToken);
            return;
        }

        // A single section can be larger than available memory. Keep only one decoded section on disk at a time.
        await using FileStream decoded = new(Path.Combine(Path.GetTempPath(), $"porting-rsb-{Guid.NewGuid():N}"), FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        using RsbReadWindow compressed = new(bundle, dataOffset, section.StoredLength, requireCompleteStream: true);
        using ZLibStream inflater = new(compressed, CompressionMode.Decompress);
        long written = 0;
        int read;
        while ((read = await inflater.ReadAsync(copyBuffer, cancellationToken)) != 0)
        {
            written += read;
            if (written > section.OutputLength)
            {
                throw new InvalidDataException("The RSB zlib output exceeds its declared length.");
            }
            await decoded.WriteAsync(copyBuffer.AsMemory(0, read), cancellationToken);
        }
        if (written != section.OutputLength)
        {
            throw new InvalidDataException("The RSB zlib output is shorter than its declared length.");
        }

        await WriteEntriesAsync(decoded, 0, group.Source, entries, writer, cancellationToken);
    }

    private static async Task WriteEntriesAsync(Stream source, long offset, AttachmentSourceGroup group,
        IEnumerable<RsbResourceDirectory.Entry> entries, IAttachmentResourceWriter writer, CancellationToken cancellationToken)
    {
        foreach (RsbResourceDirectory.Entry entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using RsbReadWindow contents = new(source, offset + entry.Offset, entry.Length);
            await writer.WriteAsync(entry.OriginalPath, entry.OriginalPath.Replace('\\', '/'), entry.Length, contents, group, cancellationToken);
        }
    }

    private static FileStream OpenBundle(string path)
    {
        return new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
    }
}
