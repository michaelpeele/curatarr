using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Curatarr.Engine.Data;

public enum MediaKind { Movie, Show, Anime }
public enum LinkStatus { Ok, Broken }

/// <summary>
/// Broad area an ActivityEvent belongs to. Used by the Activity page's
/// category filter; kept as an enum (persisted as a string) so new categories
/// can be added without reinterpreting old rows.
/// </summary>
public enum ActivityCategory { Scheduling, User, Plex, Scan, Source, Path }

/// <summary>
/// One of potentially many top-level folders holding media (movies or
/// shows). Supports an arbitrary, growing number of roots -- e.g. as a
/// NAS expansion array is added, another SourceRoot row is inserted, no
/// schema or engine changes required.
/// </summary>
public class SourceRoot
{
    [Key] public string Id { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Path { get; set; } = default!;      // absolute path on disk
    public MediaKind MediaType { get; set; }

    public List<SourceItem> Items { get; set; } = new();
}

/// <summary>
/// A piece of real media discovered under a SourceRoot, enriched with
/// TMDB metadata by the scanner at discovery time. For movies, this is
/// one row per film. For shows, this is one row per episode -- so a show
/// with 3 approved seasons is 3 seasons' worth of SourceItem rows, each
/// individually approvable via VirtualLibraryItem.
/// </summary>
public class SourceItem
{
    [Key] public string Id { get; set; } = default!;
    public string SourceRootId { get; set; } = default!;
    [ForeignKey(nameof(SourceRootId))] public SourceRoot SourceRoot { get; set; } = default!;

    public string RelativePath { get; set; } = default!;   // relative to SourceRoot.Path
    public MediaKind MediaType { get; set; }

    // movie fields
    public string? Title { get; set; }
    public int? Year { get; set; }

    // show/episode fields
    public string? ShowTitle { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }

    public int? TmdbId { get; set; }
    public string? PosterUrl { get; set; }
    public string? Overview { get; set; }

    /// <summary>
    /// Set by the admin Match tooling. A manual match wins over re-scans: the
    /// scan merge freezes all metadata for these rows (path/episode fields are
    /// still refreshed).
    /// </summary>
    public bool ManuallyMatched { get; set; }
    
    // Genre IDs from TMDB (comma-separated for SQLite compatibility)
    // Used for client-side genre filtering in the UI
    public string? GenreIds { get; set; }

    // Catalog filter details, backfilled from TMDB (see DetailsBackfill).
    // Sentinel semantics: null = not fetched yet (will retry), "" = fetched
    // but no value found (never retried). Rating is the US certification
    // ("PG-13" for movies, "TV-14" for shows); ProductionCompanies (movies)
    // and Networks (shows/anime) are "|" -separated names. Director (movies)
    // is a single name, Creators (shows/anime) and Cast (both, top 30 in
    // billing order) are "|" -separated -- all for the free-text search.
    public string? Rating { get; set; }
    public string? ProductionCompanies { get; set; }
    public string? Networks { get; set; }
    public string? Director { get; set; }
    public string? Creators { get; set; }
    public string? Cast { get; set; }

    // Calendar dates, parsed from the cached TmdbRawDetails.Json (no live
    // TMDB calls). Null = parsed but no date/metadata in the cached payload;
    // DateDetailsParsedAt records that the parse was attempted so cached rows
    // are not retried forever. Show/anime episode rows share their show's
    // next_episode_to_air values, so Calendar de-duplicates on tmdbId + date.
    public DateTime? ReleaseDate { get; set; }
    public DateTime? AirDate { get; set; }
    public int? AirSeasonNumber { get; set; }
    public int? AirEpisodeNumber { get; set; }
    public bool AirIsSeasonPremiere { get; set; }
    public DateTime? DateDetailsParsedAt { get; set; }
    
    [NotMapped]
    public List<int> GenreIdList => 
        string.IsNullOrEmpty(GenreIds) 
            ? new List<int>() 
            : GenreIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                      .Select(s => int.TryParse(s, out var id) ? (int?)id : null)
                      .Where(id => id.HasValue)
                      .Select(id => id!.Value)
                      .ToList();

    [NotMapped]
    // null-safe: returns null instead of throwing if SourceRoot wasn't
    // eagerly loaded (e.g. via .Include()) -- guards against surprise
    // NullReferenceExceptions in any endpoint that serializes a SourceItem
    // without having included its SourceRoot navigation
    public string? SourcePath => SourceRoot is null ? null : System.IO.Path.Combine(SourceRoot.Path, RelativePath);
}

/// <summary>
/// Raw TMDB details response, stashed during the catalog backfill so a
/// future field expansion (budget, tagline, runtime, writers...) can be
/// parsed locally instead of re-fetching 60k+ rows from TMDB. One row per
/// unique (mediaType, tmdbId) -- NOT per SourceItem row, so all episodes
/// of a show share a single cached response. Key is "{mediaType}:{tmdbId}"
/// (movie 123 and tv 123 are different titles).
///
/// RequestedWith records the append_to_response set the row was fetched
/// with (e.g. "release_dates,credits") -- a future expansion that needs an
/// appended sub-object we didn't request knows exactly which keys to
/// refetch rather than invalidating the whole table.
/// </summary>
public class TmdbRawDetails
{
    [Key] public string Key { get; set; } = default!; // "{mediaType}:{tmdbId}"
    public string Json { get; set; } = default!;       // full raw response as TMDB returned it
    public string RequestedWith { get; set; } = default!;
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A curated, per-audience library (e.g. "Kids Movies", "Kids Shows").
/// OutputPath is the virtual directory tree that gets pointed at as its
/// own separate Plex library section.
/// </summary>
public class VirtualLibrary
{
    [Key] public string Id { get; set; } = default!;
    public string Name { get; set; } = default!;
    public MediaKind MediaType { get; set; }
    public string OutputPath { get; set; } = default!;

    /// <summary>
    /// When the symlink engine last completed a reconcile pass for this
    /// library. The UI compares this to VirtualLibraryItem.ApprovedAt to tell
    /// "marked but not yet synced" (yellow) from "marked and synced" (green).
    /// Null until the first successful reconcile.
    /// </summary>
    public DateTime? LastReconciledAt { get; set; }

    public List<VirtualLibraryItem> Items { get; set; } = new();
    public List<VirtualLibraryMembership> Memberships { get; set; } = new();
    public List<LibrarySchedule> Schedules { get; set; } = new();
    public List<HiddenItem> HiddenItems { get; set; } = new();
    public List<LibraryRule> Rules { get; set; } = new();
    public List<LibraryRuleRun> RuleRuns { get; set; } = new();
}

/// <summary>
/// One configurable availability window for a VirtualLibrary. A library can
/// have any number of enabled rules; the library is active when ANY rule
/// matches the current time (union). No enabled rules = Always On.
///
/// Times are local wall-clock in the configured schedule timezone
/// (Schedules:TimeZone), never UTC. End &lt;= Start means the window crosses
/// midnight; End == Start means all day. DaysOfWeek is optional (null/empty =
/// every day) and applies to the day the window STARTS, so an overnight
/// Friday window keeps running into Saturday morning.
///
/// Seasonal rules are optional month/day ranges (no year) and may wrap the
/// end of the year (e.g. Dec 15 - Jan 5).
/// </summary>
public class LibrarySchedule
{
    [Key] public string Id { get; set; } = default!;

    public string VirtualLibraryId { get; set; } = default!;
    [ForeignKey(nameof(VirtualLibraryId))] public VirtualLibrary VirtualLibrary { get; set; } = default!;

    /// <summary>Optional user-facing label ("Saturday morning cartoons").</summary>
    public string? Name { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Comma-separated ISO day numbers (0=Sunday .. 6=Saturday). Null/empty = every day.</summary>
    public string? DaysOfWeek { get; set; }

    /// <summary>Local start time of the window ("HH:mm").</summary>
    public TimeOnly StartTime { get; set; } = new(0, 0);

    /// <summary>Local end time ("HH:mm"). End &lt;= Start crosses midnight; equal = all day.</summary>
    public TimeOnly EndTime { get; set; } = new(23, 59);

    // Optional seasonal range (month 1-12 / day 1-31). Both endpoints set or neither.
    public int? StartMonth { get; set; }
    public int? StartDay { get; set; }
    public int? EndMonth { get; set; }
    public int? EndDay { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool HasSeason => StartMonth is not null && StartDay is not null
                             && EndMonth is not null && EndDay is not null;

    /// <summary>Parsed day set; empty = every day.</summary>
    public IReadOnlySet<DayOfWeek> Days =>
        (DaysOfWeek ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var d) && d is >= 0 and <= 6 ? (DayOfWeek?)d : null)
            .Where(d => d is not null)
            .Select(d => d!.Value)
            .ToHashSet();
}

/// <summary>
/// The approval join row: this SourceItem is allowed to appear in this
/// VirtualLibrary. For shows, approving "whole show" vs. "seasons 1-3
/// only" is just approving a different subset of that show's per-episode
/// SourceItem rows -- no special-casing needed in the engine.
/// </summary>
public class VirtualLibraryItem
{
    [Key] public string Id { get; set; } = default!;
    public string VirtualLibraryId { get; set; } = default!;
    [ForeignKey(nameof(VirtualLibraryId))] public VirtualLibrary VirtualLibrary { get; set; } = default!;

    public string SourceItemId { get; set; } = default!;
    [ForeignKey(nameof(SourceItemId))] public SourceItem SourceItem { get; set; } = default!;

    public DateTime ApprovedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// True when an automatic library rule (Sprint 6 #1) created this row.
    /// Rules only ever rewrite their own rows; manual approvals written by
    /// /approve stay false and therefore always win over rule outcomes.
    /// </summary>
    public bool SetByRule { get; set; }
}

/// <summary>
/// Per-library "hidden from this library" marker. Independent of approval:
/// hiding an item only removes it from the library grid views, it does not
/// revoke an existing approval. Stored per (library, source item) so hiding
/// a title from "Kids Movies" never affects another library.
///
/// Shows are stored as their episode rows (the UI writes every episode id of
/// the show group), so a hidden show stays hidden even if the representative
/// poster episode changes after a re-scan.
/// </summary>
public class HiddenItem
{
    [Key] public string Id { get; set; } = default!;
    public string VirtualLibraryId { get; set; } = default!;
    [ForeignKey(nameof(VirtualLibraryId))] public VirtualLibrary VirtualLibrary { get; set; } = default!;

    public string SourceItemId { get; set; } = default!;
    [ForeignKey(nameof(SourceItemId))] public SourceItem SourceItem { get; set; } = default!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Set when the user asks to unhide this title. The row stays until the next
    /// successful reconcile, so the title remains hidden in the grid until Sync;
    /// the reconcile then creates the link again and clears the row.
    /// </summary>
    public DateTime? UnhideRequestedAt { get; set; }

    /// <summary>True when an automatic rule created this hide; manual hides stay false.</summary>
    public bool SetByRule { get; set; }
}

/// <summary>
/// A reusable automatic decision rule for one VirtualLibrary (Sprint 6 #1).
/// Field/Operator/Action are lower-case registry strings (see RuleFields.cs)
/// rather than enums so adding a new grid filter only requires one registry
/// entry -- no database schema change.
/// </summary>
public class LibraryRule
{
    [Key] public string Id { get; set; } = default!;
    public string VirtualLibraryId { get; set; } = default!;
    [ForeignKey(nameof(VirtualLibraryId))] public VirtualLibrary VirtualLibrary { get; set; } = default!;

    /// <summary>One of RuleFields (genre, director, cast, studio, network, rating, year, mediaType).</summary>
    public string Field { get; set; } = default!;

    /// <summary>One of the operators advertised by the field's registry entry.</summary>
    public string Operator { get; set; } = default!;

    /// <summary>Comma/pipe-separated values, using the same delimiters as grid filters.</summary>
    public string Value { get; set; } = default!;

    /// <summary>"approve" or "hide".</summary>
    public string Action { get; set; } = default!;

    /// <summary>Lower runs first; a later matching rule overrides an earlier outcome.</summary>
    public int Priority { get; set; }

    public bool Enabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Audit row written once per Apply. Counts describe only the changes made by
/// that run; a second Apply with unchanged rules reports all zeroes.
/// </summary>
public class LibraryRuleRun
{
    [Key] public string Id { get; set; } = default!;
    public string VirtualLibraryId { get; set; } = default!;
    [ForeignKey(nameof(VirtualLibraryId))] public VirtualLibrary VirtualLibrary { get; set; } = default!;

    public DateTime RanAt { get; set; } = DateTime.UtcNow;
    public int RuleCount { get; set; }
    public int Approved { get; set; }
    public int Hidden { get; set; }
    public int Unapproved { get; set; }
    public int Unhidden { get; set; }
}

/// <summary>
/// Persisted ledger row -- this is what makes reconciliation survive a
/// restart. Previously an in-memory Dictionary; now a real SQLite table
/// via CuratarrDbContext.
/// </summary>
public class SymlinkRecord
{
    [Key] public string Id { get; set; } = default!;
    public string VirtualLibraryItemId { get; set; } = default!;
    public string SourcePath { get; set; } = default!;
    public string LinkPath { get; set; } = default!;
    public string VirtualRootPath { get; set; } = default!;
    public LinkStatus Status { get; set; } = LinkStatus.Ok;
}

public enum MembershipRole { Owner, Curator, Viewer }

/// <summary>
/// Grants a specific user curation/viewing rights on a specific
/// VirtualLibrary -- e.g. "wife is a Curator on Kids Movies". This is
/// deliberately separate from the user's global Identity role: being a
/// global "Curator" doesn't grant access to every library, only the ones
/// they're a member of.
/// </summary>
public class VirtualLibraryMembership
{
    [Key] public string Id { get; set; } = default!;
    public string VirtualLibraryId { get; set; } = default!;
    [ForeignKey(nameof(VirtualLibraryId))] public VirtualLibrary VirtualLibrary { get; set; } = default!;

    public string UserId { get; set; } = default!;
    [ForeignKey(nameof(UserId))] public ApplicationUser User { get; set; } = default!;

    public MembershipRole Role { get; set; }
}

/// <summary>
/// Translates a path prefix Curatarr sees (e.g. "/mnt/data" inside a
/// Docker container) into the prefix Plex sees for the same physical
/// storage (e.g. "/Volumes/data" on the Plex host). Applied only to the
/// symlink TARGET (the real source file) when a link is created --
/// Curatarr's own filesystem checks always use its own, untranslated
/// view. Longest-prefix-match wins when multiple mappings could apply.
/// </summary>
public class PathMapping
{
    [Key] public string Id { get; set; } = default!;
    public string FromPrefix { get; set; } = default!; // Curatarr's view, e.g. /mnt/data
    public string ToPrefix { get; set; } = default!;    // Plex's view, e.g. /Volumes/data
}

/// <summary>
/// Single-row configuration for the optional Plex integration. The Plex
/// token is never stored in plain text: TokenCipher holds the value
/// protected with IDataProtector. ClientId is generated once (on first
/// link) and stays stable so Plex recognizes this install across restarts.
///
/// ServerUrl is the base URL Curatarr uses to talk to the Plex server
/// (usually the best connection URI discovered via /api/v2/resources). It
/// can be overridden by the admin from Settings -> Plex.
/// </summary>
public class PlexSettings
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Stable X-Plex-Client-Identifier (GUID string).</summary>
    public string? ClientId { get; set; }

    /// <summary>Data-Protection-protected Plex auth token.</summary>
    public string? TokenCipher { get; set; }

    public string? AccountName { get; set; }
    public string? ServerName { get; set; }
    public string? ServerUrl { get; set; }
    public string? MachineId { get; set; }

    /// <summary>Refresh Plex library sections after Curatarr changes links.</summary>
    public bool RefreshOnChange { get; set; } = true;

    public DateTimeOffset? LastRefreshAt { get; set; }
    public string? LastRefreshStatus { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class ReconcileReport
{
    public List<string> Created { get; } = new();
    public List<string> Removed { get; } = new();
    public List<string> Broken { get; } = new();
    public List<string> Unchanged { get; } = new();
}

/// <summary>
/// One entry in the Activity feed (Sprint 5 #1). A standalone audit row: it has
/// no foreign key to a library or user, so deleting either never erases history.
/// Stored fields are deliberately redacted and bounded -- never tokens,
/// passwords, invite/reset links, or full request bodies.
/// </summary>
public class ActivityEvent
{
    [Key] public string Id { get; set; } = default!;
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public ActivityCategory Category { get; set; }
    /// <summary>Stable machine name, e.g. "ScanFinished".</summary>
    public string Type { get; set; } = default!;
    /// <summary>User email, or "Scheduler" / "Plex" / "System".</summary>
    public string Actor { get; set; } = default!;
    /// <summary>Human-readable, already redacted, truncated to ~400 chars.</summary>
    public string Detail { get; set; } = default!;
    /// <summary>Optional compact JSON (counts, ids, status).</summary>
    public string? Metadata { get; set; }
}
