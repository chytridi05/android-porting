using System.IO.Compression;
using System.Text;

namespace Porting.Core.Inspection;

/// <summary>Reads the variant and APK identities in a bundletool toc.pb.</summary>
internal static class BundletoolApkSet
{
    private sealed record Member(string Path, bool IsBase);

    /// <summary>Resolves one explicit or unambiguous Android variant from the APK set.</summary>
    internal static async Task<(ZipArchiveEntry Base, ZipArchiveEntry[] Splits)> SelectAsync(ZipArchive archive, int? variantNumber,
        CancellationToken cancellationToken)
    {
        Dictionary<string, ZipArchiveEntry> entries = new(StringComparer.Ordinal);
        foreach (ZipArchiveEntry entry in archive.Entries.Where(static entry => !entry.FullName.EndsWith('/')))
        {
            if (!WindowsArchivePath.IsSafe(entry.FullName) || !entries.TryAdd(entry.FullName, entry))
            {
                throw new InvalidDataException($"Invalid or repeated APK set entry '{entry.FullName}'.");
            }
        }

        if (!entries.TryGetValue("toc.pb", out ZipArchiveEntry? table) || table.Length > 16 * 1024 * 1024)
        {
            throw new InvalidDataException("Expected a bundletool APK set with a readable toc.pb.");
        }
        await using Stream contents = table.Open();
        byte[] bytes = new byte[(int)table.Length];
        await contents.ReadExactlyAsync(bytes, cancellationToken);

        Dictionary<int, Member[]> variants = [];
        foreach (ReadOnlyMemory<byte> variant in Messages(bytes, 1))
        {
            int number = checked((int)ReadFields(variant).FirstOrDefault(static field => field.Number == 3).Value);
            Member[] members = Messages(variant, 2).SelectMany(ReadModule).ToArray();
            if (members.Length > 0 && !variants.TryAdd(number, members))
            {
                throw new InvalidDataException($"APK set variant {number} is repeated.");
            }
        }

        if (variantNumber is null && variants.Count == 1)
        {
            variantNumber = variants.Keys.Single();
        }
        if (variantNumber is not int selected || !variants.TryGetValue(selected, out Member[]? membersForVariant))
        {
            string available = string.Join(", ", variants.Keys.Order());
            throw new InvalidDataException(available.Length == 0 ? "The APK set contains no supported APK variants."
                : $"Select one of the available APK set variants: {available}.");
        }
        if (membersForVariant.Count(static member => member.IsBase) != 1
            || membersForVariant.DistinctBy(static member => member.Path, StringComparer.OrdinalIgnoreCase).Count() != membersForVariant.Length)
        {
            throw new InvalidDataException($"APK set variant {selected} must contain one base and unique APK member paths.");
        }

        foreach (Member member in membersForVariant)
        {
            if (!entries.ContainsKey(member.Path))
            {
                throw new InvalidDataException($"APK set variant {selected} is missing '{member.Path}'.");
            }
        }
        return (entries[membersForVariant.Single(static member => member.IsBase).Path],
            membersForVariant.Where(static member => !member.IsBase).OrderBy(static member => member.Path, StringComparer.Ordinal)
                .Select(member => entries[member.Path]).ToArray());
    }

    private static IEnumerable<Member> ReadModule(ReadOnlyMemory<byte> module)
    {
        string? name = Messages(module, 1).Select(message => ReadText(message, 1)).FirstOrDefault();
        foreach (ReadOnlyMemory<byte> description in Messages(module, 2))
        {
            string path = ReadText(description, 2);
            if (Messages(description, 3).FirstOrDefault() is { IsEmpty: false } split)
            {
                bool master = ReadFields(split).Any(static field => field.Number == 2 && field.Value == 1);
                yield return new(path, name == "base" && master);
            }
            else if (ReadFields(description).Any(static field => field.Number == 4 && field.Wire == 2))
            {
                yield return new(path, IsBase: true);
            }
        }
    }

    private static string ReadText(ReadOnlyMemory<byte> message, int number)
        => Encoding.UTF8.GetString(Messages(message, number).FirstOrDefault().Span);

    private static IEnumerable<ReadOnlyMemory<byte>> Messages(ReadOnlyMemory<byte> message, int number)
        => ReadFields(message).Where(field => field.Number == number && field.Wire == 2).Select(static field => field.Bytes);

    private static IEnumerable<(int Number, int Wire, ulong Value, ReadOnlyMemory<byte> Bytes)> ReadFields(ReadOnlyMemory<byte> message)
    {
        int offset = 0;
        while (offset < message.Length)
        {
            ulong key = ReadVarint(message.Span, ref offset);
            int wire = (int)(key & 7);
            int number = checked((int)(key >> 3));
            if (wire == 0)
            {
                yield return (number, wire, ReadVarint(message.Span, ref offset), default);
                continue;
            }

            ulong length = wire switch
            {
                1 => 8,
                2 => ReadVarint(message.Span, ref offset),
                5 => 4,
                _ => throw new InvalidDataException("APK set toc.pb contains an unsupported field.")
            };
            if (length > (ulong)(message.Length - offset))
            {
                throw new InvalidDataException("APK set toc.pb is truncated.");
            }

            yield return (number, wire, 0, message.Slice(offset, (int)length));
            offset += (int)length;
        }
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> bytes, ref int offset)
    {
        ulong value = 0;
        for (int shift = 0; shift <= 63 && offset < bytes.Length; shift += 7)
        {
            byte data = bytes[offset++];
            if (shift == 63 && data > 1)
            {
                break;
            }

            value |= (ulong)(data & 127) << shift;
            if ((data & 128) == 0)
            {
                return value;
            }
        }

        throw new InvalidDataException("APK set toc.pb contains an invalid integer.");
    }
}
