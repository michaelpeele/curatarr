using Curatarr.Engine.Data;
using Microsoft.EntityFrameworkCore;

namespace Curatarr.Api;

/// <summary>
/// One-time-ish sweep that parses calendar dates out of TmdbRawDetails rows
/// cached by earlier detail backfills. Rows fetched from now on are parsed
/// inline by DetailsBackfill, so this only needs to catch up the existing
/// library; it never calls TMDB itself.
/// </summary>
public class CalendarDateBackfillService : BackgroundService
{
    private const int BatchSize = 500;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CalendarDateBackfillService> _logger;
    private static bool _isRunning;

    public CalendarDateBackfillService(
        IServiceScopeFactory scopeFactory,
        ILogger<CalendarDateBackfillService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give the initial migration/SeedAsync a moment, then parse cached rows.
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Calendar date backfill sweep failed");
            }

            // Cheap local-only pass; repeat in case details backfill stashed new
            // raw rows for items whose detail columns were already filled.
            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        if (_isRunning)
        {
            _logger.LogDebug("Calendar date backfill already running, skipping");
            return;
        }

        _isRunning = true;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CuratarrDbContext>();

            // Items whose raw row hasn't arrived yet stay unparsed for a later
            // pass; this set only prevents re-reading them within one sweep.
            var attemptedWithoutRaw = new HashSet<string>(StringComparer.Ordinal);
            var totalParsed = 0;
            var totalSkipped = 0;

            while (true)
            {
                var items = await db.SourceItems
                    .Where(i => i.TmdbId != null &&
                                i.DateDetailsParsedAt == null &&
                                !attemptedWithoutRaw.Contains(i.Id))
                    .Take(BatchSize)
                    .ToListAsync(ct);
                if (items.Count == 0) break;

                var keys = items
                    .Select(i => $"{i.MediaType}:{i.TmdbId!.Value}")
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                var rawByKey = await db.TmdbRawDetails
                    .Where(r => keys.Contains(r.Key))
                    .ToDictionaryAsync(r => r.Key, r => r.Json, ct);

                var parsedAt = DateTime.UtcNow;
                var parsed = 0;
                foreach (var item in items)
                {
                    var key = $"{item.MediaType}:{item.TmdbId!.Value}";
                    if (!rawByKey.TryGetValue(key, out var json))
                    {
                        attemptedWithoutRaw.Add(item.Id);
                        continue;
                    }

                    CalendarDateParser.ApplyRawDates(item, json, parsedAt);
                    parsed++;
                }

                await db.SaveChangesAsync(ct);
                totalParsed += parsed;
                totalSkipped += items.Count - parsed;

                _logger.LogInformation(
                    "Calendar date backfill: parsed {Parsed} of {Processed} cached rows ({Skipped} awaiting raw details)",
                    parsed, items.Count, items.Count - parsed);
            }

            if (totalParsed > 0 || totalSkipped > 0)
                _logger.LogInformation(
                    "Calendar date backfill sweep complete: {TotalParsed} parsed, {TotalSkipped} awaiting raw details",
                    totalParsed, totalSkipped);
        }
        finally
        {
            _isRunning = false;
        }
    }
}
