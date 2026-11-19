using System.Text.Json;
using Curatarr.Engine.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Curatarr.Api;

/// <summary>
/// Optional Plex integration: encrypts/decrypts the stored token and asks
/// Plex to refresh the library sections that contain a Curatarr virtual
/// library after its symlinks change.
///
/// Registered as a singleton. The refresh trigger always runs on its own
/// service scope (and its own DbContext) so callers can fire-and-forget
/// without waiting for, or racing, the request's DbContext. The "linked"
/// check is cached in memory: an install with no Plex link pays one tiny
/// query on first use and zero work afterwards.
/// </summary>
public sealed class PlexService
{
    /// <summary>
    /// Fallback X-Plex-Client-Identifier used only when the persisted row
    /// has no ClientId yet. Must remain stable across restarts.
    /// </summary>
    public const string FallbackClientId = "f2c1a6d4-7b3e-4e8a-9c5d-1a7b6e2f9d30";

    public const string ProtectorPurpose = "Curatarr.Plex.Token";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly IDataProtector _protector;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PlexService> _logger;

    private readonly SemaphoreSlim _configuredGate = new(1, 1);
    private volatile bool _configuredKnown;
    private volatile bool _configured;

    public PlexService(
        IDataProtectionProvider protections,
        IServiceScopeFactory scopes,
        ILogger<PlexService> logger)
    {
        _protector = protections.CreateProtector(ProtectorPurpose);
        _scopes = scopes;
        _logger = logger;
    }

    /// <summary>The persisted ClientId, or the fixed app-level fallback.</summary>
    public static string ResolveClientId(PlexSettings? settings) =>
        string.IsNullOrWhiteSpace(settings?.ClientId) ? FallbackClientId : settings!.ClientId!;

    public string ProtectToken(string token) => _protector.Protect(token);

    public string? UnprotectToken(string? cipher)
    {
        if (string.IsNullOrWhiteSpace(cipher)) return null;
        try
        {
            return _protector.Unprotect(cipher);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stored Plex token could not be decrypted; treating the integration as unlinked.");
            return null;
        }
    }

    /// <summary>Cheap check used by hooks: false means do no upstream work.</summary>
    public async Task<bool> IsConfiguredAsync(CuratarrDbContext db, CancellationToken ct = default)
    {
        if (_configuredKnown) return _configured;

        await _configuredGate.WaitAsync(ct);
        try
        {
            if (_configuredKnown) return _configured;

            var linked = await db.PlexSettings.AsNoTracking()
                .AnyAsync(s => s.TokenCipher != null && s.TokenCipher != "", ct);
            _configured = linked;
            _configuredKnown = true;
            return linked;
        }
        finally
        {
            _configuredGate.Release();
        }
    }

    public void SetConfigured(bool value)
    {
        _configured = value;
        _configuredKnown = true;
    }

    /// <summary>True once we know this install has a usable Plex link.</summary>
    public bool IsConfigured => _configuredKnown && _configured;

    /// <summary>
    /// Fire-and-forget wrapper for request handlers and the schedule worker.
    /// Creates its own scope/DbContext; never throws.
    /// </summary>
    /// <param name="changedPaths">
    /// Optional container-space folders (e.g. the item directories created or
    /// pruned by a Sync). When present Plex is asked for a path-scoped scan of
    /// just those folders, which surfaces new titles in clients immediately,
    /// instead of queueing a full section rescan behind anything Plex is
    /// already doing.
    /// </param>
    public void TriggerInBackground(
        IEnumerable<string>? libraryIds,
        IReadOnlyCollection<string>? changedPaths = null,
        IReadOnlyCollection<string>? removedPaths = null)
    {
        // Unlinked installs: no task, no scope, no query.
        if (_configuredKnown && !_configured) return;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CuratarrDbContext>();
                if (!await IsConfiguredAsync(db, CancellationToken.None)) return;
                await TriggerLibraryRefreshAsync(db, libraryIds, _logger, CancellationToken.None, changedPaths, removedPaths);
            }
            catch (Exception ex)
            {
                // TriggerLibraryRefreshAsync swallows its own failures; this is
                // just belt-and-braces (e.g. DI/scope construction problems).
                _logger.LogWarning(ex, "Plex refresh trigger failed.");
            }
        });
    }

    /// <summary>
    /// Refresh the Plex section(s) that contain the given virtual libraries
    /// (or every library when <paramref name="libraryIds"/> is null).
    /// Swallows all failures and records a short status on PlexSettings.
    /// </summary>
    public async Task TriggerLibraryRefreshAsync(
        CuratarrDbContext db,
        IEnumerable<string>? libraryIds,
        ILogger logger,
        CancellationToken ct,
        IReadOnlyCollection<string>? changedPaths = null,
        IReadOnlyCollection<string>? removedPaths = null)
    {
        try
        {
            var settings = await db.PlexSettings.FirstOrDefaultAsync(ct);
            if (settings is null) return;
            if (!settings.RefreshOnChange) return;
            if (string.IsNullOrWhiteSpace(settings.ServerUrl)) return;

            var token = UnprotectToken(settings.TokenCipher);
            if (string.IsNullOrWhiteSpace(token)) return;

            var baseUrl = settings.ServerUrl.Trim().TrimEnd('/');
            var outputPaths = await ResolveOutputPathsAsync(db, libraryIds, ct);
            if (outputPaths.Count == 0) return;

            var mappings = await db.PathMappings.AsNoTracking().ToListAsync(ct);
            var translated = outputPaths
                .Select(p => TranslatePath(p, mappings))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Folders that actually changed during this Sync, in Plex's path space.
            var translatedChanged = (changedPaths ?? [])
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => TranslatePath(p, mappings))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var translatedRemoved = (removedPaths ?? [])
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => TranslatePath(p, mappings))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            using var listRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"{baseUrl}/library/sections?X-Plex-Token={Uri.EscapeDataString(token)}");
            listRequest.Headers.Accept.ParseAdd("application/json");

            using var listResponse = await Http.SendAsync(listRequest, ct);
            if (!listResponse.IsSuccessStatusCode)
            {
                await RecordStatusAsync(db, logger,
                    $"Plex returned HTTP {(int)listResponse.StatusCode} listing sections", ct);
                return;
            }

            var sectionsJson = await listResponse.Content.ReadAsStringAsync(ct);
            var sectionKeys = new List<string>();
            var failures = new List<string>();

            using (var document = JsonDocument.Parse(sectionsJson))
            {
                if (!document.RootElement.TryGetProperty("MediaContainer", out var container) ||
                    !container.TryGetProperty("Directory", out var directories))
                {
                    await RecordStatusAsync(db, logger, "Plex returned no library sections", ct);
                    return;
                }

                // Diagnostic: when nothing matches, the operator needs to see what
                // Plex actually reports so the PathMapping rows can be corrected.
                logger.LogInformation(
                    "Plex refresh: looking for translated path(s): {Paths}",
                    string.Join(" | ", translated));

                var sectionElements = AsArray(directories).ToList();

                // Prefer a path-scoped scan, which is what Sonarr/Radarr do: a
                // full-section rescan queues behind whatever Plex is already
                // working on, so new titles can take minutes to appear in clients.
                // Additions are scanned folder-by-folder (instant). Removals force
                // a full section scan because Plex ignores a path that no longer
                // exists -- without it, deleted titles linger until a manual
                // refresh. force=1 makes Plex re-evaluate instead of trusting
                // cached timestamps, which is what pushes the change to clients.
                if (translatedChanged.Count > 0 || translatedRemoved.Count > 0)
                {
                    var pathRefreshes = 0;
                    var fullSections = new List<string>();

                    foreach (var directory in sectionElements)
                    {
                        var key = GetString(directory, "key");
                        if (string.IsNullOrWhiteSpace(key)) continue;

                        foreach (var changed in translatedChanged)
                        {
                            var target = ResolveSectionPath(directory, changed);
                            if (target is null) continue;
                            if (await RefreshPathAsync(key, target, token, baseUrl, failures, ct))
                                pathRefreshes++;
                        }

                        if (translatedRemoved.Any(removed => ResolveSectionPath(directory, removed) is not null) &&
                            !fullSections.Contains(key, StringComparer.Ordinal))
                        {
                            fullSections.Add(key);
                        }
                    }

                    var sectionRefreshes = 0;
                    foreach (var key in fullSections)
                    {
                        if (await RefreshSectionAsync(key, token, baseUrl, failures, ct))
                            sectionRefreshes++;
                    }

                    if (pathRefreshes > 0 || sectionRefreshes > 0)
                    {
                        var scanStatus = $"Refreshed {pathRefreshes} path(s), {sectionRefreshes} section(s)";
                        if (failures.Count > 0)
                            scanStatus += $"; {failures.Count} failed ({string.Join("; ", failures)})";

                        await RecordStatusAsync(db, logger, scanStatus, ct);
                        logger.LogInformation(
                            "Plex refresh: {Status} (path-scoped) for {Added} added / {Removed} removed folder(s).",
                            scanStatus, translatedChanged.Count, translatedRemoved.Count);
                        return;
                    }

                    // Nothing matched a section -- fall through to the full-section
                    // refresh below so a Sync never silently skips Plex.
                    logger.LogInformation(
                        "Plex refresh: no section covers the changed folder(s); falling back to a full section refresh.");
                }

                foreach (var directory in sectionElements)
                {
                    var sectionTitle = GetString(directory, "title");
                    var sectionLocations = new List<string>();
                    if (directory.TryGetProperty("Location", out var locationEls))
                    {
                        foreach (var locationEl in AsArray(locationEls))
                        {
                            var locPath = GetString(locationEl, "path");
                            if (!string.IsNullOrWhiteSpace(locPath)) sectionLocations.Add(locPath);
                        }
                    }
                    logger.LogInformation(
                        "Plex refresh: section '{Title}' locations: {Locations}",
                        sectionTitle, string.Join(" | ", sectionLocations));

                    if (!SectionContains(directory, translated)) continue;

                    var key = GetString(directory, "key");
                    if (string.IsNullOrWhiteSpace(key) || sectionKeys.Contains(key)) continue;

                    using var refreshRequest = new HttpRequestMessage(
                        HttpMethod.Post,
                        $"{baseUrl}/library/sections/{Uri.EscapeDataString(key)}/refresh?X-Plex-Token={Uri.EscapeDataString(token)}");
                    refreshRequest.Headers.Accept.ParseAdd("application/json");

                    try
                    {
                        using var refreshResponse = await Http.SendAsync(refreshRequest, ct);
                        if (refreshResponse.IsSuccessStatusCode)
                            sectionKeys.Add(key);
                        else
                            failures.Add($"section {key}: HTTP {(int)refreshResponse.StatusCode}");
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    {
                        failures.Add($"section {key}: {ex.Message}");
                    }
                }
            }

            var status = sectionKeys.Count > 0
                ? $"Refreshed {sectionKeys.Count} section(s)"
                : "No matching Plex section";
            if (failures.Count > 0)
                status += $"; {failures.Count} failed ({string.Join("; ", failures)})";

            await RecordStatusAsync(db, logger, status, ct);
            logger.LogInformation("Plex refresh: {Status} for {PathCount} path(s).", status, translated.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutting down -- nothing useful to record.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Plex library refresh failed.");
            try
            {
                await RecordStatusAsync(db, logger, $"Refresh failed: {ex.Message}", CancellationToken.None);
            }
            catch
            {
                // Never let bookkeeping failures escape the trigger.
            }
        }
    }

    private static async Task<List<string>> ResolveOutputPathsAsync(
        CuratarrDbContext db, IEnumerable<string>? libraryIds, CancellationToken ct)
    {
        var query = db.VirtualLibraries.AsNoTracking();

        if (libraryIds is not null)
        {
            var ids = libraryIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (ids.Count == 0) return [];
            query = query.Where(l => ids.Contains(l.Id));
        }

        return await query
            .Where(l => l.OutputPath != null && l.OutputPath != "")
            .Select(l => l.OutputPath)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Translate a Curatarr path into Plex's view using PathMapping rows.
    /// Longest matching FromPrefix wins; separators are normalized to '/'
    /// and comparisons are case-insensitive. Unmapped paths pass through.
    /// </summary>
    internal static string TranslatePath(string path, IReadOnlyList<PathMapping> mappings)
    {
        var normalized = NormalizePath(path);
        PathMapping? best = null;
        var bestFrom = "";
        var bestLength = -1;

        foreach (var mapping in mappings)
        {
            var from = NormalizePath(mapping.FromPrefix);
            if (string.IsNullOrEmpty(from)) continue;

            var matches = normalized.Equals(from, StringComparison.OrdinalIgnoreCase) ||
                          normalized.StartsWith(from + "/", StringComparison.OrdinalIgnoreCase);
            if (!matches || from.Length <= bestLength) continue;

            best = mapping;
            bestFrom = from;
            bestLength = from.Length;
        }

        if (best is null) return path;

        var to = NormalizePath(best.ToPrefix);
        var remainder = normalized.Length > bestFrom.Length ? normalized[bestFrom.Length..] : "";
        return to + remainder;
    }

    /// <summary>
    /// Asks Plex to scan a single folder. <c>force=1</c> makes Plex re-read the
    /// folder instead of trusting cached timestamps, which is what reliably
    /// propagates the change to clients.
    /// </summary>
    private async Task<bool> RefreshPathAsync(
        string sectionKey,
        string path,
        string token,
        string baseUrl,
        List<string> failures,
        CancellationToken ct)
    {
        var url = $"{baseUrl}/library/sections/{Uri.EscapeDataString(sectionKey)}/refresh" +
                  $"?path={Uri.EscapeDataString(path)}&force=1&X-Plex-Token={Uri.EscapeDataString(token)}";
        return await SendRefreshAsync(url, $"path {path}", failures, ct);
    }

    /// <summary>
    /// Full section rescan. Used when a folder was removed: Plex ignores a
    /// <c>path</c> that no longer exists, so the section has to be re-read to
    /// drop the missing item.
    /// </summary>
    private async Task<bool> RefreshSectionAsync(
        string sectionKey,
        string token,
        string baseUrl,
        List<string> failures,
        CancellationToken ct)
    {
        var url = $"{baseUrl}/library/sections/{Uri.EscapeDataString(sectionKey)}/refresh" +
                  $"?force=1&X-Plex-Token={Uri.EscapeDataString(token)}";
        return await SendRefreshAsync(url, $"section {sectionKey}", failures, ct);
    }

    private async Task<bool> SendRefreshAsync(
        string url, string label, List<string> failures, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/json");

        try
        {
            using var response = await Http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode) return true;
            failures.Add($"{label}: HTTP {(int)response.StatusCode}");
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            failures.Add($"{label}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Returns the absolute path to hand to Plex's path-scoped scan for this
    /// section, or null when the changed folder is not inside the section.
    /// Plex expects the full path (section location + relative item folder),
    /// exactly like Sonarr/Radarr send it.
    /// </summary>
    private static string? ResolveSectionPath(JsonElement directory, string absolutePath)
    {
        if (!directory.TryGetProperty("Location", out var locations)) return null;

        foreach (var location in AsArray(locations))
        {
            var sectionPath = GetString(location, "path");
            if (string.IsNullOrWhiteSpace(sectionPath)) continue;

            var normalizedSection = NormalizePath(sectionPath);
            if (normalizedSection.Length == 0) continue;

            if (absolutePath.Equals(normalizedSection, StringComparison.OrdinalIgnoreCase)) return absolutePath;
            if (absolutePath.StartsWith(normalizedSection + "/", StringComparison.OrdinalIgnoreCase)) return absolutePath;
        }

        return null;
    }

    private static bool SectionContains(JsonElement directory, IReadOnlyList<string> translatedPaths)
    {
        if (!directory.TryGetProperty("Location", out var locations)) return false;

        foreach (var location in AsArray(locations))
        {
            var sectionPath = GetString(location, "path");
            if (string.IsNullOrWhiteSpace(sectionPath)) continue;

            var normalizedSection = NormalizePath(sectionPath);
            foreach (var target in translatedPaths)
            {
                if (normalizedSection.Equals(target, StringComparison.OrdinalIgnoreCase)) return true;
                if (normalizedSection == "/" && target.StartsWith('/')) return true;
                if (target.StartsWith(normalizedSection + "/", StringComparison.OrdinalIgnoreCase)) return true;

                // Plex can report the same folder under a different volume root, e.g.
                // "/Volumes/Macintosh HD/Volumes/data/VirtualLibraries/x" for the tree
                // Curatarr writes as "/Volumes/data/VirtualLibraries/x". Both sides are
                // absolute (leading '/'), so a plain suffix match is already
                // path-boundary safe.
                if (normalizedSection.Length > target.Length &&
                    normalizedSection.EndsWith(target, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static async Task RecordStatusAsync(
        CuratarrDbContext db, ILogger logger, string status, CancellationToken ct)
    {
        try
        {
            var settings = await db.PlexSettings.FirstOrDefaultAsync(ct);
            if (settings is null) return;

            var now = DateTimeOffset.UtcNow;
            settings.LastRefreshAt = now;
            settings.LastRefreshStatus = Truncate(status, 400);
            settings.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not record Plex refresh status.");
        }
    }

    private static string NormalizePath(string? path)
    {
        var value = (path ?? "").Trim().Replace('\\', '/');
        while (value.Length > 1 && value.EndsWith('/'))
            value = value[..^1];
        return value;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static IEnumerable<JsonElement> AsArray(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                yield return item;
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            yield return element;
        }
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }
}
