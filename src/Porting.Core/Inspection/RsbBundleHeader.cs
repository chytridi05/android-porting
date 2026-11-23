using System.Buffers.Binary;

namespace Porting.Core.Inspection;

/// <summary>Identifies a little-endian PopCap bundle and locates its version 4 group table.</summary>
internal sealed record RsbBundleHeader(uint Version, uint InformationLength, uint GroupCount, uint GroupTableOffset, uint GroupRecordLength)
{
    internal const uint SupportedVersion = 4;
    private const int HeaderLength = 0x70;

    /// <summary>Checks the on-disk signature without assuming a supported layout.</summary>
    internal static bool HasSignature(ReadOnlySpan<byte> prefix)
    {
        return prefix.Length >= 4 && prefix[..4].SequenceEqual("1bsr"u8);
    }

    /// <summary>Reads the bundle header without decoding any resource groups.</summary>
    /// <exception cref="InvalidDataException">The file has an RSB signature but its header is incomplete or inconsistent.</exception>
    internal static async ValueTask<RsbBundleHeader?> ProbeAsync(Stream input, CancellationToken cancellationToken)
    {
        byte[] header = new byte[HeaderLength];
        int read = await input.ReadAtLeastAsync(header.AsMemory(0, 8), 8, false, cancellationToken);
        if (!HasSignature(header.AsSpan(0, read)))
        {
            return null;
        }
        if (read < 8)
        {
            throw new InvalidDataException("The RSB version field is truncated.");
        }

        uint version = ReadUInt32(header, 4);
        if (version != SupportedVersion)
        {
            return new(version, 0, 0, 0, 0);
        }
        if (await input.ReadAtLeastAsync(header.AsMemory(8), HeaderLength - 8, false, cancellationToken) != HeaderLength - 8)
        {
            throw new InvalidDataException("The RSB v4 header is truncated.");
        }

        uint informationLength = ReadUInt32(header, 0x0C);
        uint groupCount = ReadUInt32(header, 0x28);
        uint groupOffset = ReadUInt32(header, 0x2C);
        uint groupLength = ReadUInt32(header, 0x30);
        ulong tableLength = (ulong)groupCount * groupLength;
        if (informationLength < HeaderLength || informationLength > input.Length
            || (groupCount != 0 && (groupOffset < HeaderLength || groupOffset > informationLength
                || groupLength < 0x8C || tableLength > informationLength - groupOffset)))
        {
            throw new InvalidDataException("The RSB v4 group table is outside the bundle information section.");
        }

        return new(version, informationLength, groupCount, groupOffset, groupLength);
    }

    internal static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
    }
}
