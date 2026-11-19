using System.Text.Json;
using Curatarr.Engine.Data;
using Microsoft.EntityFrameworkCore;

namespace Curatarr.Api;

/// <summary>
/// Optional Plex account linking (PIN-based OAuth) plus the admin settings
/// surface. Tokens are stored encrypted by <see cref="PlexService"/> and are
/// never returned to clients.
/// </summary>
public static class PlexEndpoints
{
    private const string PlexApi = "https://plex.tv/api/v2";

    public static void MapPlexEndpoints(this WebApplication app)
    {
        // ------------------------------------------------------------------
        // Settings -> Plex: current status (never includes the token)
        // ------------------------------------------------------------------
        app.MapGet("/api/plex/status", async (CuratarrDbContext db, PlexService plex) =>
        {
            var settings = await LoadAsync(db, create: true);
            plex.SetConfigured(!string.IsNullOrWhiteSpace(settings?.TokenCipher));
            return Results.Ok(ToStatus(settings));
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        // ------------------------------------------------------------------
        // Begin OAuth: create a Plex PIN and build the app.plex.tv auth URL
        // ------------------------------------------------------------------
        app.MapPost("/api/plex/oauth/start", async (CuratarrDbContext db, PlexService plex) =>
        {
            try
            {
                var settings = await LoadAsync(db, create: true)
                    ?? throw new InvalidOperationException("Could not initialize Plex settings.");
                var clientId = PlexService.ResolveClientId(settings);
                if (settings.ClientId != clientId)
                {
                    settings.ClientId = clientId;
                    settings.UpdatedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync();
                }

                using var request = PlexRequest(
                    HttpMethod.Post, $"{PlexApi}/pins?strong=true", clientId);
                using var response = await Http.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    throw new UpstreamException($"Plex returned HTTP {(int)response.StatusCode} creating a PIN.");

                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (!root.TryGetProperty("id", out var idElement) ||
                    !TryGetLong(idElement, out var pinId) ||
                    !root.TryGetProperty("code", out var codeElement) ||
                    codeElement.ValueKind != JsonValueKind.String)
                {
                    throw new UpstreamException("Plex returned an unexpected PIN response.");
                }

                var code = codeElement.GetString() ?? "";
                var authUrl =
                    $"https://app.plex.tv/auth#?clientID={Uri.EscapeDataString(clientId)}" +
                    $"&code={Uri.EscapeDataString(code)}" +
                    "&context%5Bdevice%5D%5Bproduct%5D=Curatarr";

                return Results.Ok(new { pinId, authUrl });
            }
            catch (Exception ex) when (IsUpstreamFailure(ex))
            {
                return UpstreamError(ex);
            }
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        // ------------------------------------------------------------------
        // Poll a PIN. When Plex has issued a token, persist the link.
        // ------------------------------------------------------------------
        app.MapGet("/api/plex/oauth/poll/{pinId:long}", async (
            long pinId, CuratarrDbContext db, PlexService plex, IActivityRecorder activity) =>
        {
            try
            {
                var settings = await LoadAsync(db, create: true)
                    ?? throw new InvalidOperationException("Could not initialize Plex settings.");
                var clientId = PlexService.ResolveClientId(settings);

                using var pinRequest = PlexRequest(
                    HttpMethod.Get, $"{PlexApi}/pins/{pinId}", clientId);
                using var pinResponse = await Http.SendAsync(pinRequest);
                var pinBody = await pinResponse.Content.ReadAsStringAsync();
                if (!pinResponse.IsSuccessStatusCode)
                    throw new UpstreamException($"Plex returned HTTP {(int)pinResponse.StatusCode} polling the PIN.");

                string? authToken = null;
                using (var pinDocument = JsonDocument.Parse(pinBody))
                {
                    authToken = GetString(pinDocument.RootElement, "authToken");
                }

                if (string.IsNullOrWhiteSpace(authToken))
                    return Results.Ok(new { status = "pending" });

                // Account identity (best-effort: a missing user response must
                // not prevent us from persisting a working token).
                string? accountName = null;
                try
                {
                    using var userRequest = PlexRequest(HttpMethod.Get, $"{PlexApi}/user", clientId);
                    userRequest.Headers.TryAddWithoutValidation("X-Plex-Token", authToken);
                    using var userResponse = await Http.SendAsync(userRequest);
                    if (userResponse.IsSuccessStatusCode)
                    {
                        var userBody = await userResponse.Content.ReadAsStringAsync();
                        using var userDocument = JsonDocument.Parse(userBody);
                        var user = userDocument.RootElement;
                        accountName = FirstNonEmpty(
                            GetString(user, "username"),
                            GetString(user, "title"),
                            GetString(user, "email"));
                    }
                }
                catch (Exception ex) when (IsUpstreamFailure(ex))
                {
                    // Token is still usable; keep going with no account name.
                }

                // Discover the Plex Media Server this account can see.
                string? serverName = null;
                string? serverUrl = null;
                string? machineId = null;
                try
                {
                    using var resourcesRequest = PlexRequest(
                        HttpMethod.Get, $"{PlexApi}/resources?includeHttps=1", clientId);
                    resourcesRequest.Headers.TryAddWithoutValidation("X-Plex-Token", authToken);
                    using var resourcesResponse = await Http.SendAsync(resourcesRequest);
                    if (!resourcesResponse.IsSuccessStatusCode)
                        throw new UpstreamException(
                            $"Plex returned HTTP {(int)resourcesResponse.StatusCode} listing resources.");

                    var resourcesBody = await resourcesResponse.Content.ReadAsStringAsync();
                    using var resourcesDocument = JsonDocument.Parse(resourcesBody);
                    if (resourcesDocument.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var resource in resourcesDocument.RootElement.EnumerateArray())
                        {
                            if (!ProvidesServer(resource)) continue;

                            serverName = GetString(resource, "name");
                            machineId = GetString(resource, "clientIdentifier");
                            serverUrl = BestConnectionUrl(resource);
                            break;
                        }
                    }
                }
                catch (Exception ex) when (IsUpstreamFailure(ex))
                {
                    // Link the account anyway; an admin can set the URL manually.
                }

                settings.ClientId = clientId;
                settings.TokenCipher = plex.ProtectToken(authToken);
                settings.AccountName = accountName;
                settings.ServerName = serverName;
                settings.ServerUrl = string.IsNullOrWhiteSpace(serverUrl) ? settings.ServerUrl : serverUrl;
                settings.MachineId = machineId;
                settings.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();

                plex.SetConfigured(true);
                activity.Record(ActivityCategory.Plex, "PlexLinked", "Plex",
                    $"Linked Plex account {accountName ?? "unknown"}");
                return Results.Ok(new
                {
                    status = "linked",
                    accountName,
                    serverName,
                    serverUrl = settings.ServerUrl
                });
            }
            catch (Exception ex) when (IsUpstreamFailure(ex))
            {
                return UpstreamError(ex);
            }
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        // ------------------------------------------------------------------
        // Update the server URL override / refresh-on-change toggle
        // ------------------------------------------------------------------
        app.MapPut("/api/plex/settings", async (
            UpdatePlexSettingsRequest req, CuratarrDbContext db, PlexService plex, IActivityRecorder activity) =>
        {
            var settings = await LoadAsync(db, create: true);
            if (settings is null) return Results.Problem("Could not initialize Plex settings.");

            if (req.ServerUrl is not null)
            {
                settings.ServerUrl = string.IsNullOrWhiteSpace(req.ServerUrl)
                    ? null
                    : req.ServerUrl.Trim().TrimEnd('/');
            }
            if (req.RefreshOnChange is { } refreshOnChange)
                settings.RefreshOnChange = refreshOnChange;

            settings.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            plex.SetConfigured(!string.IsNullOrWhiteSpace(settings.TokenCipher));
            activity.Record(ActivityCategory.Plex, "PlexSettingsUpdated", "Plex", "Plex settings updated");
            return Results.Ok(ToStatus(settings));
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        // ------------------------------------------------------------------
        // Unlink the account. Keeps ClientId/RefreshOnChange for next time.
        // ------------------------------------------------------------------
        app.MapDelete("/api/plex/link", async (CuratarrDbContext db, PlexService plex, IActivityRecorder activity) =>
        {
            var settings = await LoadAsync(db, create: false);
            if (settings is not null)
            {
                settings.TokenCipher = null;
                settings.AccountName = null;
                settings.ServerName = null;
                settings.ServerUrl = null;
                settings.MachineId = null;
                settings.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }

            plex.SetConfigured(false);
            activity.Record(ActivityCategory.Plex, "PlexUnlinked", "Plex", "Plex account unlinked");
            return Results.Ok(ToStatus(settings));
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        // ------------------------------------------------------------------
        // Admin diagnostics: what Plex actually reports. Lets "the scan ran but
        // nothing appeared" be debugged without needing the Plex UI, and shows
        // the exact file paths Plex matched (or failed to match).
        // ------------------------------------------------------------------
        app.MapGet("/api/plex/sections", async (CuratarrDbContext db, PlexService plex) =>
        {
            var settings = await LoadAsync(db, create: false);
            var token = plex.UnprotectToken(settings?.TokenCipher);
            if (settings is null || string.IsNullOrWhiteSpace(settings.ServerUrl) || string.IsNullOrWhiteSpace(token))
                return Results.BadRequest(new { error = "Plex is not linked." });

            var baseUrl = settings.ServerUrl.Trim().TrimEnd('/');
            using var request = PlexRequest(HttpMethod.Get,
                $"{baseUrl}/library/sections?X-Plex-Token={Uri.EscapeDataString(token)}", settings.ClientId ?? "");
            request.Headers.Accept.ParseAdd("application/json");

            using var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return Results.Problem($"Plex returned HTTP {(int)response.StatusCode} listing sections.");

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!document.RootElement.TryGetProperty("MediaContainer", out var container) ||
                !container.TryGetProperty("Directory", out var directories))
                return Results.Ok(Array.Empty<object>());

            var sections = new List<object>();
            foreach (var directory in directories.EnumerateArray())
            {
                var locations = new List<string>();
                if (directory.TryGetProperty("Location", out var locationEls))
                {
                    foreach (var locationEl in locationEls.EnumerateArray())
                    {
                        if (locationEl.TryGetProperty("path", out var pathEl) &&
                            pathEl.GetString() is { Length: > 0 } location)
                        {
                            locations.Add(location);
                        }
                    }
                }

                sections.Add(new
                {
                    key = directory.TryGetProperty("key", out var keyEl) ? keyEl.GetString() : null,
                    title = directory.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null,
                    type = directory.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null,
                    locations
                });
            }

            return Results.Ok(sections);
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        // Server preferences relevant to how fast changes reach clients.
        app.MapGet("/api/plex/prefs", async (CuratarrDbContext db, PlexService plex) =>
        {
            var settings = await LoadAsync(db, create: false);
            var token = plex.UnprotectToken(settings?.TokenCipher);
            if (settings is null || string.IsNullOrWhiteSpace(settings.ServerUrl) || string.IsNullOrWhiteSpace(token))
                return Results.BadRequest(new { error = "Plex is not linked." });

            var baseUrl = settings.ServerUrl.Trim().TrimEnd('/');
            using var request = PlexRequest(HttpMethod.Get,
                $"{baseUrl}/:/prefs?X-Plex-Token={Uri.EscapeDataString(token)}", settings.ClientId ?? "");
            request.Headers.Accept.ParseAdd("application/json");

            using var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return Results.Problem($"Plex returned HTTP {(int)response.StatusCode} reading preferences.");

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!document.RootElement.TryGetProperty("MediaContainer", out var container) ||
                !container.TryGetProperty("Setting", out var settingEls))
                return Results.Ok(Array.Empty<object>());

            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "FSEventLibraryUpdatesEnabled",
                "FSEventLibraryPartialScanEnabled",
                "WatchMonitorLibraries",
                "ScheduledLibraryUpdatesEnabled",
                "autoEmptyTrash",
                "AutoEmptyTrash",
                "GenerateChapterThumbs"
            };

            var prefs = new List<object>();
            foreach (var setting in settingEls.EnumerateArray())
            {
                var id = setting.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (id is null || !wanted.Contains(id)) continue;
                prefs.Add(new
                {
                    id,
                    value = setting.TryGetProperty("value", out var valueEl) ? valueEl.ToString() : null
                });
            }

            return Results.Ok(prefs);
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        // Update one or more Plex server preferences (Admin only). Body is a flat
        // string map, e.g. { "FSEventLibraryUpdatesEnabled": "1" }.
        app.MapPut("/api/plex/prefs", async (
            Dictionary<string, string> values, CuratarrDbContext db, PlexService plex) =>
        {
            if (values.Count == 0) return Results.BadRequest(new { error = "No preferences supplied." });

            var settings = await LoadAsync(db, create: false);
            var token = plex.UnprotectToken(settings?.TokenCipher);
            if (settings is null || string.IsNullOrWhiteSpace(settings.ServerUrl) || string.IsNullOrWhiteSpace(token))
                return Results.BadRequest(new { error = "Plex is not linked." });

            var baseUrl = settings.ServerUrl.Trim().TrimEnd('/');
            var query = string.Join("&", values.Select(kv =>
                $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

            using var request = PlexRequest(HttpMethod.Put,
                $"{baseUrl}/:/prefs?{query}&X-Plex-Token={Uri.EscapeDataString(token)}", settings.ClientId ?? "");
            request.Headers.Accept.ParseAdd("application/json");

            using var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return Results.Problem($"Plex returned HTTP {(int)response.StatusCode} updating preferences.");

            return Results.Ok(new { updated = values.Keys });
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        // Most recently added items in a section, newest first, including the
        // file path Plex stored. `limit` defaults to 15.
        app.MapGet("/api/plex/sections/{key}/recent", async (
            string key, int? limit, CuratarrDbContext db, PlexService plex) =>
        {
            var settings = await LoadAsync(db, create: false);
            var token = plex.UnprotectToken(settings?.TokenCipher);
            if (settings is null || string.IsNullOrWhiteSpace(settings.ServerUrl) || string.IsNullOrWhiteSpace(token))
                return Results.BadRequest(new { error = "Plex is not linked." });

            var take = Math.Clamp(limit ?? 15, 1, 100);
            var baseUrl = settings.ServerUrl.Trim().TrimEnd('/');
            var url = $"{baseUrl}/library/sections/{Uri.EscapeDataString(key)}/all" +
                      $"?X-Plex-Token={Uri.EscapeDataString(token)}" +
                      $"&sort=addedAt:desc&X-Plex-Container-Start=0&X-Plex-Container-Size={take}";

            using var request = PlexRequest(HttpMethod.Get, url, settings.ClientId ?? "");
            request.Headers.Accept.ParseAdd("application/json");

            using var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return Results.Problem($"Plex returned HTTP {(int)response.StatusCode} listing section items.");

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!document.RootElement.TryGetProperty("MediaContainer", out var container) ||
                !container.TryGetProperty("Metadata", out var metadata))
                return Results.Ok(Array.Empty<object>());

            var items = new List<object>();
            foreach (var item in metadata.EnumerateArray())
            {
                var files = new List<string>();
                if (item.TryGetProperty("Media", out var mediaEls))
                {
                    foreach (var media in mediaEls.EnumerateArray())
                    {
                        if (!media.TryGetProperty("Part", out var partEls)) continue;
                        foreach (var part in partEls.EnumerateArray())
                        {
                            if (part.TryGetProperty("file", out var fileEl) &&
                                fileEl.GetString() is { Length: > 0 } file)
                            {
                                files.Add(file);
                            }
                        }
                    }
                }

                items.Add(new
                {
                    ratingKey = item.TryGetProperty("ratingKey", out var rk) ? rk.GetString() : null,
                    title = item.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null,
                    year = item.TryGetProperty("year", out var yearEl) ? yearEl.GetInt32() : (int?)null,
                    addedAt = item.TryGetProperty("addedAt", out var addedEl) ? addedEl.GetInt64() : (long?)null,
                    files
                });
            }

            return Results.Ok(items);
        }).RequireAuthorization(p => p.RequireRole("Admin"));
    }

    private static object ToStatus(PlexSettings? settings) => new
    {
        linked = !string.IsNullOrWhiteSpace(settings?.TokenCipher),
        accountName = settings?.AccountName,
        serverName = settings?.ServerName,
        serverUrl = settings?.ServerUrl,
        refreshOnChange = settings?.RefreshOnChange ?? true,
        lastRefreshAt = settings?.LastRefreshAt,
        lastRefreshStatus = settings?.LastRefreshStatus
    };

    private static async Task<PlexSettings?> LoadAsync(CuratarrDbContext db, bool create)
    {
        var settings = await db.PlexSettings.OrderBy(s => s.Id).FirstOrDefaultAsync();
        if (settings is null && create)
        {
            // Generated once per install and kept forever, so Plex treats
            // this Curatarr instance as the same device across restarts.
            settings = new PlexSettings { ClientId = Guid.NewGuid().ToString() };
            db.PlexSettings.Add(settings);
            await db.SaveChangesAsync();
        }
        else if (settings is not null && string.IsNullOrWhiteSpace(settings.ClientId))
        {
            // Only fill a gap on rows created before this field was populated;
            // never rotate an identifier Plex already knows.
            settings.ClientId = Guid.NewGuid().ToString();
            await db.SaveChangesAsync();
        }

        return settings;
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private static HttpRequestMessage PlexRequest(HttpMethod method, string url, string clientId)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.TryAddWithoutValidation("X-Plex-Client-Identifier", clientId);
        request.Headers.TryAddWithoutValidation("X-Plex-Product", "Curatarr");
        request.Headers.TryAddWithoutValidation("X-Plex-Version", "1.0");
        return request;
    }

    private static bool ProvidesServer(JsonElement resource)
    {
        if (!resource.TryGetProperty("provides", out var provides)) return false;

        if (provides.ValueKind == JsonValueKind.String)
            return (provides.GetString() ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(p => p.Equals("server", StringComparison.OrdinalIgnoreCase));

        if (provides.ValueKind == JsonValueKind.Array)
            return provides.EnumerateArray().Any(p =>
                p.ValueKind == JsonValueKind.String &&
                string.Equals(p.GetString(), "server", StringComparison.OrdinalIgnoreCase));

        return false;
    }

    private static string? BestConnectionUrl(JsonElement resource)
    {
        if (!resource.TryGetProperty("connections", out var connections) ||
            connections.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        JsonElement? fallback = null;
        foreach (var connection in connections.EnumerateArray())
        {
            var uri = GetString(connection, "uri");
            if (string.IsNullOrWhiteSpace(uri)) continue;

            fallback ??= connection;
            if (GetBool(connection, "local") == true && GetBool(connection, "https") == true)
                return uri;
        }

        return fallback is { } first ? GetString(first, "uri") : null;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static bool TryGetLong(JsonElement element, out long value)
    {
        if (element.ValueKind == JsonValueKind.Number) return element.TryGetInt64(out value);
        if (element.ValueKind == JsonValueKind.String)
            return long.TryParse(element.GetString(), out value);
        value = 0;
        return false;
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static bool? GetBool(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static bool IsUpstreamFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or JsonException
            or UpstreamException or InvalidOperationException;

    private static IResult UpstreamError(Exception ex) =>
        Results.Json(new { error = $"Plex request failed: {ex.Message}" }, statusCode: 502);

    private sealed class UpstreamException(string message) : Exception(message);
}

public record UpdatePlexSettingsRequest(string? ServerUrl, bool? RefreshOnChange);
