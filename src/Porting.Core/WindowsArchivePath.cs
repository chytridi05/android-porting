using System.Text.RegularExpressions;

namespace Porting.Core;

/// <summary>Validates relative ZIP entry names before they become Windows paths.</summary>
internal static class WindowsArchivePath
{
    private static readonly char[] InvalidFileNameCharacters = Path.GetInvalidFileNameChars();
    private static readonly Regex DeviceName = new(@"^(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|COM[1-9¹²³]|LPT[1-9¹²³])$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Checks that the entry uses safe relative components and forward slashes.</summary>
    internal static bool IsSafe(string path)
    {
        return path.Split('/').All(static segment => segment.Length > 0 && segment is not ("." or "..")
            && !segment.EndsWith('.') && !segment.EndsWith(' ')
            && segment.IndexOfAny(InvalidFileNameCharacters) < 0
            && !DeviceName.IsMatch(segment.Split('.', 2)[0].TrimEnd()));
    }
}