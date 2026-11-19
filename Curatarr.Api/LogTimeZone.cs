using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Curatarr.Api;

/// <summary>
/// The timezone used to format ISO 8601 timestamps on every console log line.
///
/// The console formatter (registered in <see cref="Program"/>) reads from this
/// singleton, so when <see cref="Reload"/> is called after a Settings → Server
/// save, every subsequent log line prints in the new zone without a restart.
/// </summary>
public sealed class LogTimeZone
{
    private static volatile string _raw = "";
    private static volatile bool _isUtc = false;

    // Public so DI can activate the singleton registration; all state is
    // static, the instance is just a token.
    public LogTimeZone() { }

    public static void Initialize(string? zone)
    {
        var trimmed = zone?.Trim() ?? "";
        _raw = trimmed;
        _isUtc = trimmed.Equals("UTC", StringComparison.OrdinalIgnoreCase);
    }

    public static LogTimeZone Reload(string newZone)
    {
        var trimmed = newZone?.Trim() ?? "";
        _raw = trimmed;
        _isUtc = trimmed.Equals("UTC", StringComparison.OrdinalIgnoreCase);
        return new LogTimeZone();
    }

    public string? Raw => _raw;
    public bool IsUtc => _isUtc;

    public static string Format(DateTimeOffset utc)
    {
        var zone = Resolve();
        if (zone is null)
            return utc.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz");
        return TimeZoneInfo.ConvertTime(utc, zone).ToString("yyyy-MM-ddTHH:mm:ss.fffzzz");
    }

    private static TimeZoneInfo? Resolve()
    {
        if (_isUtc) return null;
        if (string.IsNullOrWhiteSpace(_raw)) return null;
        try { return TimeZoneInfo.FindSystemTimeZoneById(_raw.Trim()); }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }
    }
}


