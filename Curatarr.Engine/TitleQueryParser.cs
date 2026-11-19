using System.Text.RegularExpressions;

namespace Curatarr.Engine;

/// <summary>
/// Turns a folder name into the title/year pair used for metadata lookups.
/// Handles both the library's canonical "Title (Year)" layout and release-style
/// dotted filenames such as Groundhog.Day.1993.2160p.UHD.BluRay.X265-IAMABLE.
/// Shared by the scanner and (via an equivalent client-side helper) the Match UI.
/// </summary>
public static partial class TitleQueryParser
{
    private static readonly HashSet<string> ReleaseTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "2160p", "1080p", "720p", "480p", "4k", "uhd",
        "bluray", "blu-ray", "bdrip", "brrip", "webrip", "web-dl", "hdtv",
        "x265", "x264", "h265", "h264", "hevc", "avc",
        "aac", "ac3", "dts", "dolby", "truehd",
        "remux", "extended", "unrated", "imax", "hdr", "dv", "hdr10"
    };

    public static (string Title, int? Year) Parse(string input)
    {
        var text = (input ?? "").Trim();
        if (text.Length == 0) return (text, null);

        // Canonical library layout: keep the existing behaviour verbatim.
        var yearSuffix = YearSuffixRegex().Match(text);
        if (yearSuffix.Success)
            return (yearSuffix.Groups["title"].Value.Trim(), int.Parse(yearSuffix.Groups["year"].Value));

        // Dotted/underscored release names: normalise separators first.
        var cleaned = WhitespaceRegex().Replace(text.Replace('.', ' ').Replace('_', ' ').Replace('+', ' '), " ");

        int? year = null;
        var yearMatch = StandaloneYearRegex().Match(cleaned);
        if (yearMatch.Success)
        {
            year = int.Parse(yearMatch.Value);
            cleaned = WhitespaceRegex().Replace(cleaned.Remove(yearMatch.Index, yearMatch.Length), " ");
        }

        // Trailing release group ("...-IAMABLE"). Only strip an all-caps
        // alphanumeric token so real hyphenated titles are left alone.
        var withoutGroup = ReleaseGroupRegex().Replace(cleaned.Trim(), "").Trim();
        if (withoutGroup.Length > 0) cleaned = withoutGroup;

        var kept = cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim('-'))
            .Where(token => token.Length > 0 && !ReleaseTokens.Contains(token))
            .ToArray();

        var title = WhitespaceRegex().Replace(string.Join(" ", kept), " ").Trim();
        return (title.Length > 0 ? title : cleaned.Trim(), year);
    }

    [GeneratedRegex(@"^(?<title>.+?)\s*\((?<year>\d{4})\)\s*$")]
    private static partial Regex YearSuffixRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    // 1888-2099 only: avoids treating episode numbers/resolution fragments as years.
    [GeneratedRegex(@"\b(?:18[8-9]\d|19\d{2}|20[0-9]{2})\b")]
    private static partial Regex StandaloneYearRegex();

    [GeneratedRegex(@"-\s*[A-Z0-9]{3,}\s*$")]
    private static partial Regex ReleaseGroupRegex();
}
