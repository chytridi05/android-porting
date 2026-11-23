using System.Buffers.Binary;
using System.Text;

namespace Porting.Core.Generation;

/// <summary>Reads class descriptors and compiler markers from a standard DEX file.</summary>
internal sealed class ApkDex(byte[] data)
{
    /// <summary>Checks whether an archive entry is a root application DEX file.</summary>
    internal static bool IsDexEntry(string path)
    {
        return path == "classes.dex" || (path.StartsWith("classes", StringComparison.Ordinal)
            && path.EndsWith(".dex", StringComparison.Ordinal)
            && path.Length > "classes.dex".Length
            && path.AsSpan(7, path.Length - 11).IndexOfAnyExceptInRange('0', '9') < 0);
    }

    /// <summary>Enumerates the descriptors for classes this DEX defines.</summary>
    internal IEnumerable<string> ReadClasses()
    {
        CheckHeader();
        int typeOffset = ReadIndex(68);
        int typeCount = ReadIndex(64);
        int classCount = ReadIndex(96);
        int classOffset = ReadIndex(100);
        CheckTable(typeOffset, typeCount, 4);
        CheckTable(classOffset, classCount, 32);
        for (int index = 0; index < classCount; index++)
        {
            int typeIndex = ReadIndex(classOffset + index * 32);
            if (typeIndex >= typeCount)
            {
                throw new InvalidDataException("A DEX class references a missing type.");
            }

            yield return ReadString(ReadIndex(typeOffset + typeIndex * 4));
        }
    }

    /// <summary>Enumerates JSON payloads of compiler markers in the DEX.</summary>
    internal IEnumerable<string> ReadR8Markers()
    {
        CheckHeader();
        int count = ReadIndex(56);
        for (int index = 0; index < count; index++)
        {
            string value = ReadString(index);
            if (value.StartsWith("~~R8{", StringComparison.Ordinal))
            {
                yield return value[4..];
            }
        }
    }

    private void CheckHeader()
    {
        if (data.Length < 112 || !data.AsSpan(0, 4).SequenceEqual("dex\n"u8) || data[7] != 0
            || data.AsSpan(4, 3).SequenceCompareTo("041"u8) >= 0 || ReadIndex(40) != 0x12345678
            || ReadIndex(32) != data.Length)
        {
            throw new InvalidDataException("Expected a standard, little-endian DEX file up to version 040.");
        }
        CheckTable(ReadIndex(60), ReadIndex(56), 4);
    }

    private string ReadString(int index)
    {
        if (index >= ReadIndex(56))
        {
            throw new InvalidDataException("A DEX type references a missing string.");
        }

        int offset = ReadIndex(ReadIndex(60) + index * 4);
        for (int bytes = 0; bytes < 5 && offset < data.Length; bytes++)
        {
            if ((data[offset++] & 128) == 0)
            {
                int end = Array.IndexOf(data, (byte)0, offset);
                if (end >= offset)
                {
                    return Encoding.UTF8.GetString(data, offset, end - offset);
                }
                break;
            }
        }

        throw new InvalidDataException("A DEX string is truncated.");
    }

    private int ReadIndex(int offset)
    {
        if (offset < 0 || offset > data.Length - 4)
        {
            throw new InvalidDataException("A DEX index extends beyond the file.");
        }

        int value = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
        return value >= 0 ? value : throw new InvalidDataException("A DEX index exceeds the supported range.");
    }

    private void CheckTable(int start, int count, int itemSize)
    {
        if (start > data.Length || count > (data.Length - start) / itemSize)
        {
            throw new InvalidDataException("A DEX table extends beyond the file.");
        }
    }
}
