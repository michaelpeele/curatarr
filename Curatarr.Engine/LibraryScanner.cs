using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Curatarr.Engine.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Curatarr.Engine;

/// <summary>
/// One item's worth of live scan progress, pushed to the UI via SignalR
/// as each title is processed. "Status" is "Found" (metadata matched),
/// "PartialMatch" (kept the item but the TMDB lookup failed), or "Skipped"
/// (no video file in the folder).
/// </summary>
public record ScanProgressEvent(string Id, string Title, string? PosterUrl, int? Year, string MediaType, int Completed, int Total, string Status);

/// <summary>
/// Walks each configured SourceRoot. Movie roots: one folder per movie
/// ("Title (Year)/Title (Year).mkv"). Show roots: "ShowName/Season NN/
/// ShowName - SxxEyy - Episode Title.mkv" -- the standard Sonarr/Plex
/// season-folder layout. Handles any number of roots, so adding a new
/// NAS expansion array later is just registering another SourceRoot.
///
/// Metadata lookups run with bounded concurrency (8 at a time). Progress
/// is logged/reported periodically so a long scan doesn't look hung.
/// Supports cooperative pause (via waitIfPaused) and stop (via the
/// CancellationToken) -- on stop, whatever items already finished are
/// kept rather than the whole scan being thrown away.
/// </summary>
public partial class LibraryScanner
{
    private readonly IMetadataProvider _metadata;
    private readonly ILogger<LibraryScanner> _logger;
    private static readonly string[] VideoExtensions = [".mkv", ".mp4", ".avi", ".m4v"];
    private const int MaxConcurrency = 8;
    private const int LogEveryN = 50;

    public LibraryScanner(IMetadataProvider metadata, ILogger<LibraryScanner>? logger = null)
    {
        _metadata = metadata;
        _logger = logger ?? NullLogger<LibraryScanner>.Instance;
    }

    public async Task<List<SourceItem>> ScanAsync(
        IEnumerable<SourceRoot> roots,
        Func<ScanProgressEvent, Task>? onProgress = null,
        Func<CancellationToken, Task>? waitIfPaused = null,
        CancellationToken ct = default)
    {
        var rootList = roots.ToList();
        _logger.LogInformation("Starting scan of {Count} source root(s)", rootList.Count);
        var sw = Stopwatch.StartNew();

        var items = new List<SourceItem>();
        foreach (var root in rootList)
        {
            if (ct.IsCancellationRequested)
            {
                _logger.LogInformation("Scan stopped before root '{Root}'", root.Name);
                break;
            }

            var rootDir = new DirectoryInfo(root.Path);
            if (!rootDir.Exists)
            {
                _logger.LogWarning("Source root '{Root}' path does not exist, skipping: {Path}", root.Name, root.Path);
                continue;
            }

            items.AddRange(root.MediaType == MediaKind.Movie
                ? await ScanMoviesAsync(root, rootDir, onProgress, waitIfPaused, ct)
                : await ScanShowsAsync(root, rootDir, root.MediaType, onProgress, waitIfPaused, ct));
        }

        _logger.LogInformation("Scan finished in {Elapsed}: {Count} total items", sw.Elapsed, items.Count);
        return items;
    }

    private async Task<List<SourceItem>> ScanMoviesAsync(
        SourceRoot root, DirectoryInfo rootDir,
        Func<ScanProgressEvent, Task>? onProgress, Func<CancellationToken, Task>? waitIfPaused, CancellationToken ct)
    {
        var movieDirs = rootDir.GetDirectories();
        _logger.LogInformation(
            "Root '{Root}': found {Count} folders, looking up metadata ({Concurrency} at a time)...",
            root.Name, movieDirs.Length, MaxConcurrency);

        using var semaphore = new SemaphoreSlim(MaxConcurrency);
        var completed = 0;
        // accumulate into a thread-safe bag rather than Task.WhenAll's return
        // value, so a Stop mid-scan still keeps whatever finished so far
        var results = new ConcurrentBag<SourceItem>();

        var tasks = movieDirs.Select(async movieDir =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                if (waitIfPaused is not null) await waitIfPaused(ct);
                ct.ThrowIfCancellationRequested();

                var videoFile = FindVideoFile(movieDir);
                if (videoFile is null) return; // folder with no video yet -- still downloading, etc.

                var (title, year) = ParseTitleYear(movieDir.Name);
                var relativePath = Path.GetRelativePath(rootDir.FullName, videoFile.FullName);

                MovieMetadata? meta = null;
                var status = "Found";
                try
                {
                    meta = await _metadata.SearchMovieAsync(title, year, ct);
                    if (meta is null) status = "PartialMatch";
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    status = "PartialMatch";
                    _logger.LogWarning(ex, "Metadata lookup failed for '{Folder}', using folder name only", movieDir.Name);
                }

                var done = Interlocked.Increment(ref completed);
                if (done % LogEveryN == 0 || done == movieDirs.Length)
                    _logger.LogInformation("Root '{Root}': {Done}/{Total} scanned", root.Name, done, movieDirs.Length);

                var displayTitle = meta?.Title ?? title;
                var itemId = StableId(root.Id, relativePath);
                var displayYear = meta?.Year ?? year;

                if (onProgress is not null)
                    await onProgress(new ScanProgressEvent(
                        itemId, displayTitle, meta?.PosterUrl, displayYear, "Movie", done, movieDirs.Length, status));

                results.Add(new SourceItem
                {
                    Id = itemId,
                    SourceRootId = root.Id,
                    SourceRoot = root,
                    RelativePath = relativePath,
                    MediaType = MediaKind.Movie,
                    Title = displayTitle,
                    Year = displayYear,
                    TmdbId = meta?.TmdbId,
                    PosterUrl = meta?.PosterUrl,
                    Overview = meta?.Overview,
                    GenreIds = meta?.GenreIds != null && meta.GenreIds.Count > 0
                        ? string.Join(',', meta.GenreIds)
                        : null
                });
            }
            catch (OperationCanceledException)
            {
                // expected on Stop -- swallow here, results bag keeps whatever finished
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to scan '{Folder}' in root '{Root}' -- skipping it", movieDir.Name, root.Name);
            }
            finally
            {
                semaphore.Release();
            }
        });

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Root '{Root}': scan stopped early, keeping {Count} completed items", root.Name, results.Count);
        }

        return results.ToList();
    }

    private async Task<List<SourceItem>> ScanShowsAsync(
        SourceRoot root, DirectoryInfo rootDir, MediaKind mediaKind,
        Func<ScanProgressEvent, Task>? onProgress, Func<CancellationToken, Task>? waitIfPaused, CancellationToken ct)
    {
        var showDirs = new List<DirectoryInfo>();
        CollectShowCandidates(rootDir, showDirs, depth: 0, maxDepth: 5);
        _logger.LogInformation("Root '{Root}': found {Count} show folders (recursive)", root.Name, showDirs.Count);

        using var semaphore = new SemaphoreSlim(MaxConcurrency);
        var completed = 0;
        var results = new ConcurrentBag<SourceItem>();

        var perShowTasks = showDirs.Select(async showDir =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                if (waitIfPaused is not null) await waitIfPaused(ct);
                ct.ThrowIfCancellationRequested();

                var (showTitle, _) = ParseTitleYear(showDir.Name);
                ShowMetadata? showMeta = null;
                try
                {
                    showMeta = await _metadata.SearchShowAsync(showTitle, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Metadata lookup failed for show '{Folder}', using folder name only", showDir.Name);
                }

                var episodeItems = new List<SourceItem>();
                foreach (var seasonDir in showDir.GetDirectories())
                {
                    var seasonNum = ParseSeasonNumber(seasonDir.Name);
                    if (seasonNum is null) continue;

                    foreach (var videoFile in seasonDir.GetFiles()
                                 .Where(f => VideoExtensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase)))
                    {
                        var episodeNum = ParseEpisodeNumber(videoFile.Name);
                        var relativePath = Path.GetRelativePath(rootDir.FullName, videoFile.FullName);
                        var itemId = StableId(root.Id, relativePath);

                        var episodeItem = new SourceItem
                        {
                            Id = itemId,
                            SourceRootId = root.Id,
                            SourceRoot = root,
                            RelativePath = relativePath,
                            MediaType = mediaKind,
                            ShowTitle = showMeta?.Title ?? showTitle,
                            SeasonNumber = seasonNum,
                            EpisodeNumber = episodeNum,
                            TmdbId = showMeta?.TmdbId,
                            PosterUrl = showMeta?.PosterUrl,
                            Overview = showMeta?.Overview,
                            GenreIds = showMeta?.GenreIds != null && showMeta.GenreIds.Count > 0
                                ? string.Join(',', showMeta.GenreIds)
                                : null
                        };
                        episodeItems.Add(episodeItem);
                        results.Add(episodeItem);
                    }
                }

                var done = Interlocked.Increment(ref completed);
                var showStatus = showMeta is not null ? "Found" : "PartialMatch";
                if (onProgress is not null)
                {
                    if (episodeItems.Count == 0)
                    {
                        // show folder had no recognized episodes -- still report it was looked at
                        await onProgress(new ScanProgressEvent(
                        StableId(root.Id, showDir.Name), showMeta?.Title ?? showTitle, showMeta?.PosterUrl,
                        null, mediaKind.ToString(), done, showDirs.Count, showStatus));
                    }
                    else
                    {
                        foreach (var ep in episodeItems)
                            await onProgress(new ScanProgressEvent(
                                ep.Id, ep.ShowTitle ?? showTitle, ep.PosterUrl, null, mediaKind.ToString(), done, showDirs.Count, showStatus));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // expected on Stop
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to scan/enrich show '{Folder}' in root '{Root}' -- skipping it", showDir.Name, root.Name);
            }
            finally
            {
                semaphore.Release();
            }
        });

        try
        {
            await Task.WhenAll(perShowTasks);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Root '{Root}': scan stopped early, keeping {Count} completed items", root.Name, results.Count);
        }

        return results.ToList();
    }

    private static FileInfo? FindVideoFile(DirectoryInfo dir) =>
        dir.GetFiles().FirstOrDefault(f => VideoExtensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase));

    private static (string Title, int? Year) ParseTitleYear(string folderName) =>
        TitleQueryParser.Parse(folderName);

    private static int? ParseSeasonNumber(string folderName)
    {
        var m = SeasonRegex().Match(folderName);
        if (m.Success) return int.Parse(m.Groups["num"].Value);
        return folderName.Equals("Specials", StringComparison.OrdinalIgnoreCase) ? 0 : null;
    }

    private static int? ParseEpisodeNumber(string fileName)
    {
        var m = EpisodeRegex().Match(fileName);
        return m.Success ? int.Parse(m.Groups["ep"].Value) : null;
    }

    private static string StableId(string rootId, string relativePath) => $"{rootId}:{relativePath}";

    // Descend into a directory tree until we find show dirs (dirs whose children
    // include Season-N folders). Stops at known-noise directories so letter-group
    // layouts like "/media/tvshows/M thru Z/GLOW/Season 1/..." are handled.
    private static void CollectShowCandidates(
        DirectoryInfo dir, List<DirectoryInfo> into, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;
        foreach (var child in dir.GetDirectories()
                     .Where(d => !IsGarbageFolder(d))
                     .OrderBy(d => d.Name))
        {
            if (child.GetDirectories().Any(d => ParseSeasonNumber(d.Name) is not null))
                into.Add(child); // this IS a show dir
            else
                CollectShowCandidates(child, into, depth + 1, maxDepth);
        }
    }

    private static bool IsGarbageFolder(DirectoryInfo d) =>
        d.Name.StartsWith(".") ||
        d.Name.Equals("#recycle", StringComparison.OrdinalIgnoreCase) ||
        d.Name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"Season\s*(?<num>\d{1,2})", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonRegex();

    [GeneratedRegex(@"[Ss]\d{1,2}[Ee](?<ep>\d{1,3})")]
    private static partial Regex EpisodeRegex();
}
