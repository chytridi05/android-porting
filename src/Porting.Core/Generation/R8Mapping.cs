using Porting.Core.Inspection;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Porting.Core.Generation;

/// <summary>Identifies the supplied symbol mapping and its relationship to the input APK.</summary>
internal sealed record R8Mapping(ApkInputFile File, string SnapshotPath, string Match, IReadOnlyDictionary<string, string> Types)
{
    private static readonly Regex ClassLine = new(@"^(\S+) -> (\S+):$");
    private const string MappingIdPrefix = "# pg_map_id:";

    /// <summary>Loads the mapping from a checked snapshot and compares available R8 identifiers.</summary>
    internal static async Task<R8Mapping?> ReadAsync(ApkInventory inventory, string path, string apk, string workspace,
        List<PortDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(path);
        ApkInputFile? file = inventory.Companions.FirstOrDefault(input => input.ContainerPath is null
            && input.OriginalPath.Equals(fullPath, StringComparison.OrdinalIgnoreCase));
        if (file is null)
        {
            diagnostics.Add(new("apk.mapping-file", DiagnosticSeverity.Error, "Supply an R8 mapping file from the APK build.", fullPath));
            return null;
        }

        string snapshot = Path.Combine(workspace, "mapping.txt");
        try
        {
            await ApkProjectAssets.CopyCheckedFileAsync(fullPath, snapshot, file.Sha256, cancellationToken);
            (string? mapId, Dictionary<string, string> types) = await ReadMappingFileAsync(snapshot, cancellationToken);
            HashSet<string> markers = await LoadApkMarkersAsync(apk, cancellationToken);
            if (mapId is not null && markers.Count > 0 && !markers.Contains(mapId))
            {
                diagnostics.Add(new("apk.mapping-mismatch", DiagnosticSeverity.Error,
                    $"The mapping ID '{mapId}' is absent from the APK's R8 markers. Supply the mapping produced with this APK.", fullPath));
                return null;
            }

            bool matched = mapId is not null && markers.Contains(mapId);
            if (!matched)
            {
                diagnostics.Add(new("apk.mapping-unconfirmed", DiagnosticSeverity.Warning,
                    "The mapping and APK could not be linked by an R8 mapping ID. Check the recovered symbols against the original build.", fullPath));
            }

            return new(file, snapshot, matched ? "matched" : "unconfirmed", types);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or FormatException or JsonException)
        {
            diagnostics.Add(new("apk.mapping-read", DiagnosticSeverity.Error, exception.Message, fullPath));
            return null;
        }
    }

    private static async Task<(string? MapId, Dictionary<string, string> Types)> ReadMappingFileAsync(string path, CancellationToken cancellationToken)
    {
        Dictionary<string, string> types = new(StringComparer.Ordinal);
        string? id = null;
        using StreamReader reader = System.IO.File.OpenText(path);
        while (await reader.ReadLineAsync(cancellationToken) is string line)
        {
            if (line.StartsWith(MappingIdPrefix, StringComparison.Ordinal))
            {
                string supplied = line[MappingIdPrefix.Length..].Trim();
                if (supplied.Length == 0 || (id is not null && id != supplied))
                {
                    throw new InvalidDataException("The R8 mapping has an invalid or repeated mapping ID.");
                }
                id = supplied;
            }
            else if (ClassLine.Match(line) is { Success: true } match)
            {
                string original = match.Groups[1].Value;
                string obfuscated = match.Groups[2].Value;
                if (!types.TryAdd(obfuscated, original) && types[obfuscated] != original)
                {
                    throw new InvalidDataException($"The R8 mapping gives multiple class names for '{obfuscated}'.");
                }
            }
        }

        return types.Count == 0 ? throw new InvalidDataException("The R8 mapping did not contain any class mappings.") : (id, types);
    }

    private static async Task<HashSet<string>> LoadApkMarkersAsync(string apk, CancellationToken cancellationToken)
    {
        HashSet<string> ids = new(StringComparer.Ordinal);
        using ZipArchive archive = ZipFile.OpenRead(apk);
        foreach (ZipArchiveEntry entry in archive.Entries.Where(static entry => ApkDex.IsDexEntry(entry.FullName)))
        {
            await using Stream input = entry.Open();
            using MemoryStream bytes = new();
            await input.CopyToAsync(bytes, cancellationToken);
            foreach (string marker in new ApkDex(bytes.ToArray()).ReadR8Markers())
            {
                using JsonDocument json = JsonDocument.Parse(marker);
                if (json.RootElement.TryGetProperty("pg-map-id", out JsonElement id) && id.ValueKind == JsonValueKind.String
                    && id.GetString() is string value)
                {
                    _ = ids.Add(value);
                }
            }
        }

        return ids;
    }
}
