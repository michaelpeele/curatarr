namespace Curatarr.Api;

/// <summary>
/// Pure helpers for deciding which rolled log files to archive and which
/// archives/raw files have aged out. Kept free of I/O so they can be tested.
/// </summary>
public static class LogRetention
{
    /// <summary>Prefix used by the Serilog rolling file sink.</summary>
    public const string FilePrefix = "curatarr-";

    /// <summary>
    /// Parse the date from a curatarr-yyyy-MM-dd....log file name.
    /// </summary>
    public static bool TryParseDate(string fileName, out DateTime date)
    {
        date = default;
        var name = Path.GetFileName(fileName);
        if (!name.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase)) return false;
        var rest = name[FilePrefix.Length..];
        if (rest.Length < 10) return false;
        return DateTime.TryParseExact(rest[..10], "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out date);
    }

    /// <summary>
    /// Groups closed days (everything except today) by their date. Only files
    /// whose name carries a parseable date are returned.
    /// </summary>
    public static IReadOnlyList<IGrouping<DateTime, string>> ArchiveCandidates(
        IEnumerable<string> logFilePaths, DateTime todayUtc)
    {
        var today = todayUtc.Date;
        return logFilePaths
            .Where(p => TryParseDate(p, out var d) && d.Date != today)
            .GroupBy(p => Path.GetFileName(p).Length >= FilePrefix.Length + 10
                ? DateTime.ParseExact(Path.GetFileName(p)[FilePrefix.Length..(FilePrefix.Length + 10)],
                    "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).Date
                : DateTime.MinValue)
            .OrderBy(g => g.Key)
            .ToList();
    }

    /// <summary>True once the item's date is older than the retention window.</summary>
    public static bool IsExpired(DateTime date, DateTime nowUtc, int retentionDays) =>
        date.Date < nowUtc.Date.AddDays(-retentionDays);
}
