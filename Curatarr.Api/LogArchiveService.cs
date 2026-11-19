using System.IO.Compression;

namespace Curatarr.Api;

/// <summary>
/// Hourly maintenance for rolled logs: zips closed daily files into
/// logs/archive/yyyy-MM-dd.zip and deletes raw logs/archives past retention.
/// </summary>
public sealed class LogArchiveService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private readonly LogsConfigService _config;
    private readonly string _logsDir;
    private readonly ILogger<LogArchiveService> _logger;
    private DateTime _lastFailureLog = DateTime.MinValue;

    public LogArchiveService(LogsConfigService config, IWebHostEnvironment env, ILogger<LogArchiveService> logger)
    {
        _config = config;
        _logsDir = Path.Combine(env.ContentRootPath, "logs");
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run once shortly after boot, then hourly.
        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { RunOnce(); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                if (DateTime.UtcNow - _lastFailureLog > TimeSpan.FromMinutes(5))
                {
                    _lastFailureLog = DateTime.UtcNow;
                    _logger.LogWarning(ex, "Log archive maintenance failed; will retry later.");
                }
            }
            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void RunOnce()
    {
        if (!Directory.Exists(_logsDir)) return;
        var cfg = _config.Current;
        var now = DateTime.UtcNow;

        if (cfg.ArchiveEnabled)
        {
            var archiveDir = Path.Combine(_logsDir, "archive");
            Directory.CreateDirectory(archiveDir);
            var files = Directory.GetFiles(_logsDir, LogRetention.FilePrefix + "*.log");
            foreach (var group in LogRetention.ArchiveCandidates(files, now))
            {
                var zipPath = Path.Combine(archiveDir, group.Key.ToString("yyyy-MM-dd") + ".zip");
                try
                {
                    using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update);
                    foreach (var file in group)
                    {
                        var entryName = Path.GetFileName(file);
                        if (zip.GetEntry(entryName) is null) zip.CreateEntryFromFile(file, entryName);
                    }
                    foreach (var file in group) File.Delete(file);
                }
                catch (IOException)
                {
                    // A file may still be open by the sink; retry next hour.
                }
            }
        }

        // Expire old archives and any stray raw logs.
        var archivePath = Path.Combine(_logsDir, "archive");
        if (Directory.Exists(archivePath))
        {
            foreach (var zip in Directory.GetFiles(archivePath, "*.zip"))
            {
                var name = Path.GetFileNameWithoutExtension(zip);
                if (DateTime.TryParseExact(name, "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var d) &&
                    LogRetention.IsExpired(d, now, cfg.RetentionDays))
                    File.Delete(zip);
            }
        }
        foreach (var file in Directory.GetFiles(_logsDir, LogRetention.FilePrefix + "*.log"))
        {
            if (LogRetention.TryParseDate(file, out var d) && LogRetention.IsExpired(d, now, cfg.RetentionDays))
                File.Delete(file);
        }
    }
}
