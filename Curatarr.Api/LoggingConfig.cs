using System.Text.Json;
using Serilog.Core;
using Serilog.Events;

namespace Curatarr.Api;

/// <summary>
/// Persisted log settings. Stored in logs-config.json in the content root
/// (the mounted data volume), so changes survive container recreation.
/// </summary>
public sealed class LoggingConfig
{
    public string Level { get; set; } = "Information";
    public int RetentionDays { get; set; } = 7;
    public int FileSizeLimitMB { get; set; } = 10;
    public bool ArchiveEnabled { get; set; } = true;

    public static readonly string[] ValidLevels =
        ["Trace", "Debug", "Information", "Warning", "Error", "Critical"];

    public static readonly int[] ValidRetentions = [7, 14, 21, 30, 60, 90];

    /// <summary>Canonicalises case and validates a level name.</summary>
    public static bool IsValidLevel(string? value, out string canonical, out string error)
    {
        canonical = "";
        error = "";
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Level is required.";
            return false;
        }
        var match = ValidLevels.FirstOrDefault(l => string.Equals(l, value.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            error = $"Invalid level. Use one of: {string.Join(", ", ValidLevels)}.";
            return false;
        }
        canonical = match;
        return true;
    }

    public static LogEventLevel ToSerilogLevel(string canonical) => canonical switch
    {
        "Trace" => LogEventLevel.Verbose,
        "Debug" => LogEventLevel.Debug,
        "Information" => LogEventLevel.Information,
        "Warning" => LogEventLevel.Warning,
        "Error" => LogEventLevel.Error,
        "Critical" => LogEventLevel.Fatal,
        _ => LogEventLevel.Information,
    };

    public bool IsValid(out string error)
    {
        if (!IsValidLevel(Level, out _, out error)) return false;
        if (!ValidRetentions.Contains(RetentionDays))
        {
            error = $"Invalid retention. Use one of: {string.Join(", ", ValidRetentions)} days.";
            return false;
        }
        if (FileSizeLimitMB < 1 || FileSizeLimitMB > 100)
        {
            error = "File size limit must be between 1 and 100 MB.";
            return false;
        }
        return true;
    }
}

/// <summary>
/// Loads and atomically persists logs-config.json. Instances are safe to share;
/// all access to the in-memory copy is locked.
/// </summary>
public sealed class LogsConfigService
{
    private readonly string _path;
    private readonly object _lock = new();
    private LoggingConfig _current;

    public string ConfigPath => _path;

    /// <summary>Set when the on-disk config was malformed and defaults were used.</summary>
    public string? LoadError { get; }

    public LogsConfigService(string contentRootPath)
    {
        _path = Path.Combine(contentRootPath, "logs-config.json");
        (_current, LoadError) = LoadOrDefault();
    }

    public LoggingConfig Current
    {
        get { lock (_lock) return Clone(_current); }
    }

    public bool TryUpdateLevel(string level, out string error)
    {
        if (!LoggingConfig.IsValidLevel(level, out var canonical, out error))
            return false;
        lock (_lock)
        {
            _current.Level = canonical;
            return Persist(out error);
        }
    }

    public bool TryUpdateConfig(int retentionDays, int fileSizeLimitMB, bool archiveEnabled, out string error)
    {
        if (!LoggingConfig.ValidRetentions.Contains(retentionDays))
        {
            error = $"Invalid retention. Use one of: {string.Join(", ", LoggingConfig.ValidRetentions)} days.";
            return false;
        }
        if (fileSizeLimitMB < 1 || fileSizeLimitMB > 100)
        {
            error = "File size limit must be between 1 and 100 MB.";
            return false;
        }
        lock (_lock)
        {
            _current.RetentionDays = retentionDays;
            _current.FileSizeLimitMB = fileSizeLimitMB;
            _current.ArchiveEnabled = archiveEnabled;
            return Persist(out error);
        }
    }

    private (LoggingConfig, string?) LoadOrDefault()
    {
        var defaults = new LoggingConfig();
        if (!File.Exists(_path)) return (defaults, null);
        try
        {
            var json = File.ReadAllText(_path);
            var cfg = JsonSerializer.Deserialize<LoggingConfig>(json) ?? defaults;
            if (!cfg.IsValid(out var error))
                return (defaults, $"logs-config.json invalid ({error}); using defaults.");
            return (cfg, null);
        }
        catch (Exception ex)
        {
            return (defaults, $"Failed to load logs-config.json ({ex.Message}); using defaults.");
        }
    }

    private bool Persist(out string error)
    {
        error = "";
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_current, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, _path, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to save log settings: {ex.Message}";
            return false;
        }
    }

    private static LoggingConfig Clone(LoggingConfig c) => new()
    {
        Level = c.Level,
        RetentionDays = c.RetentionDays,
        FileSizeLimitMB = c.FileSizeLimitMB,
        ArchiveEnabled = c.ArchiveEnabled,
    };
}
