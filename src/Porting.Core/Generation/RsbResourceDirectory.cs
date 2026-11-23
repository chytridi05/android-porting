using Porting.Core.Inspection;

namespace Porting.Core.Generation;

/// <summary>Decodes a version 4 group directory with shared path prefixes.</summary>
internal static class RsbResourceDirectory
{
    internal const uint MaximumLength = 0x04000000;
    private const int WordLength = 4;
    private const int OrdinaryEntryLength = 12;
    private const int TextureEntryLength = 32;

    /// <summary>Locates a named resource within one uncompressed packet section.</summary>
    internal sealed record Entry(string OriginalPath, uint Part, uint Offset, uint Length);

    /// <summary>Validates the directory encoding and reads its resource paths and section ranges.</summary>
    internal static List<Entry> Read(byte[] directory, CancellationToken cancellationToken)
    {
        if (directory.Length % WordLength != 0)
        {
            throw new InvalidDataException("The RSB resource directory is not word-aligned.");
        }

        List<Entry> entries = [];
        Dictionary<int, int> branches = [];
        int[] predecessors = new int[directory.Length / WordLength];
        int prefix = -1;
        int offset = 0;
        while (offset < directory.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (branches.Remove(offset, out int branchPrefix))
            {
                prefix = branchPrefix;
            }

            uint word = RsbBundleHeader.ReadUInt32(directory, offset);
            int sibling = (int)(word >> 8) * WordLength;
            if (sibling != 0 && (sibling <= offset || sibling >= directory.Length || !branches.TryAdd(sibling, prefix)))
            {
                throw new InvalidDataException("The RSB resource directory contains an invalid path branch.");
            }

            if ((byte)word != 0)
            {
                predecessors[offset / WordLength] = prefix;
                prefix = offset;
                offset += WordLength;
                continue;
            }

            offset += WordLength;
            if (prefix < 0 || offset > directory.Length - OrdinaryEntryLength)
            {
                throw new InvalidDataException("The RSB resource directory contains an empty path or truncated entry.");
            }

            uint part = RsbBundleHeader.ReadUInt32(directory, offset);
            int entryLength = part switch
            {
                0 => OrdinaryEntryLength,
                1 => TextureEntryLength,
                _ => throw new InvalidDataException($"The RSB resource directory uses unsupported part {part}.")
            };
            if (offset > directory.Length - entryLength)
            {
                throw new InvalidDataException("The RSB resource directory contains a truncated entry.");
            }

            entries.Add(new(ReadPath(directory, predecessors, prefix), part, RsbBundleHeader.ReadUInt32(directory, offset + 4),
                RsbBundleHeader.ReadUInt32(directory, offset + 8)));
            offset += entryLength;
            prefix = -1;
        }
        if (prefix != -1 || branches.Count != 0)
        {
            throw new InvalidDataException("The RSB resource directory ends inside a path or branches into entry data.");
        }

        return entries;
    }

    private static string ReadPath(byte[] directory, int[] predecessors, int prefix)
    {
        int count = 0;
        for (int node = prefix; node >= 0; node = predecessors[node / WordLength])
        {
            if (++count > 32767)
            {
                throw new InvalidDataException("An RSB resource path exceeds the Windows path length limit.");
            }
        }

        return string.Create(count, (directory, predecessors, prefix), static (path, state) =>
        {
            int node = state.prefix;
            for (int index = path.Length - 1; index >= 0; index--)
            {
                path[index] = (char)state.directory[node];
                node = state.predecessors[node / WordLength];
            }
        });
    }
}
