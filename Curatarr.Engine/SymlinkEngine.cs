using Microsoft.EntityFrameworkCore;
using Curatarr.Engine.Data;

namespace Curatarr.Engine;

/// <summary>
/// Core reconciliation engine (architecture doc, section 5) -- SQLite-backed
/// via CuratarrDbContext, so the ledger survives process restarts. Diffs
/// the desired state (approved VirtualLibraryItems) against the persisted
/// SymlinkRecord ledger, and does the minimum filesystem work to converge.
/// Idempotent: running Reconcile twice in a row is a no-op the second time.
///
/// Path mapping: the symlink TARGET written to disk is translated through
/// any configured PathMapping rows (e.g. "/mnt/data" -> "/Volumes/data")
/// so a symlink created inside a Docker container still resolves correctly
/// when Plex, running on the host or a different container, reads it.
/// Curatarr's own File.Exists checks always use the untranslated path,
/// since that's the filesystem Curatarr itself actually has mounted.
/// </summary>
public class SymlinkEngine
{
    private readonly CuratarrDbContext _db;
    private readonly ReconcileGate? _gate;

    public SymlinkEngine(CuratarrDbContext db, ReconcileGate? gate = null)
    {
        _db = db;
        _gate = gate;
    }

    /// <summary>
    /// Reconciles every library (the manual/admin Sync path). Pass
    /// <paramref name="isLibraryActive"/> to respect schedules: inactive
    /// libraries are pruned instead of being re-linked.
    /// </summary>
    public Task<ReconcileReport> ReconcileAsync(
        Func<VirtualLibrary, bool>? isLibraryActive = null,
        CancellationToken ct = default) =>
        ReconcileScopedAsync(null, null, isLibraryActive, ct);

    /// <summary>
    /// Reconciles one library only. Used by schedule activation/deactivation so
    /// a library's window can't remove or recreate another library's links.
    /// <paramref name="libraryActive"/> false prunes every link for the library.
    /// </summary>
    public Task<ReconcileReport> ReconcileLibraryAsync(
        string libraryId,
        bool libraryActive = true,
        CancellationToken ct = default) =>
        ReconcileScopedAsync(libraryId, libraryActive, null, ct);

    private async Task<ReconcileReport> ReconcileScopedAsync(
        string? libraryId,
        bool? libraryActive,
        Func<VirtualLibrary, bool>? isLibraryActive,
        CancellationToken ct)
    {
        if (_gate is null)
            return await ReconcileCoreAsync(libraryId, libraryActive, isLibraryActive, ct);
        using var lease = await _gate.AcquireAsync(ct);
        return await ReconcileCoreAsync(libraryId, libraryActive, isLibraryActive, ct);
    }

    private async Task<ReconcileReport> ReconcileCoreAsync(
        string? libraryId,
        bool? libraryActive,
        Func<VirtualLibrary, bool>? isLibraryActive,
        CancellationToken ct)
    {
        var report = new ReconcileReport();

        var approvedQuery = _db.VirtualLibraryItems
            .Include(v => v.SourceItem).ThenInclude(si => si.SourceRoot)
            .Include(v => v.VirtualLibrary)
            .AsQueryable();
        if (libraryId is not null)
            approvedQuery = approvedQuery.Where(v => v.VirtualLibraryId == libraryId);
        var approvedItems = await approvedQuery.ToListAsync(ct);

        // Hide requests are applied by Sync, not at click time. A row without an
        // unhide request is hidden for this pass (including one just requested);
        // an unhide request is honoured now and its row is cleared below. This
        // keeps hide/unhide a single-Sync operation in both directions.
        var hiddenByLibrary = (await _db.HiddenItems
            .Where(h => h.UnhideRequestedAt == null)
            .Select(h => new { h.VirtualLibraryId, h.SourceItemId })
            .ToListAsync(ct))
            .GroupBy(h => h.VirtualLibraryId)
            .ToDictionary(g => g.Key, g => g.Select(h => h.SourceItemId).ToHashSet(StringComparer.Ordinal));

        bool HiddenForReconcile(VirtualLibraryItem v) =>
            hiddenByLibrary.TryGetValue(v.VirtualLibraryId, out var ids) && ids.Contains(v.SourceItemId);

        // Restrict stale-record cleanup to the library's own rows; null scope
        // means a full reconcile, where every record is fair game. Records whose
        // VirtualLibraryItem has just been unapproved no longer have a matching
        // id, so we scope by the library's output root as well -- otherwise
        // unapproving an item would leave its symlink behind until a full Sync.
        HashSet<string>? scopeVliIds = null;
        string? scopeRoot = null;
        if (libraryId is not null)
        {
            scopeVliIds = (await _db.VirtualLibraryItems
                .Where(v => v.VirtualLibraryId == libraryId)
                .Select(v => v.Id)
                .ToListAsync(ct)).ToHashSet();

            scopeRoot = await _db.VirtualLibraries
                .Where(l => l.Id == libraryId)
                .Select(l => l.OutputPath)
                .FirstOrDefaultAsync(ct);
        }

        var existingRecords = (await _db.SymlinkRecords.ToListAsync(ct))
            .Where(r => scopeVliIds is null
                        || scopeVliIds.Contains(r.VirtualLibraryItemId)
                        || (scopeRoot is not null &&
                            string.Equals(r.VirtualRootPath, scopeRoot, StringComparison.Ordinal)))
            .ToList();
        var mappings = await _db.PathMappings.ToListAsync(ct);

        bool Active(VirtualLibrary lib) => libraryActive ?? isLibraryActive?.Invoke(lib) ?? true;

        // Only approved items in currently-active libraries are desired. An
        // inactive library's approvals stay in the DB, but their links are
        // removed (the window has ended).
        var desiredIds = approvedItems
            .Where(a => Active(a.VirtualLibrary) && !HiddenForReconcile(a))
            .Select(a => a.Id)
            .ToHashSet();
        var existingByVliId = existingRecords.ToDictionary(r => r.VirtualLibraryItemId);

        // 1) remove anything no longer approved (or in an inactive library)
        foreach (var staleId in existingByVliId.Keys.Except(desiredIds).ToList())
        {
            var rec = existingByVliId[staleId];
            DeleteLink(rec.LinkPath);
            PruneEmptyDirs(Path.GetDirectoryName(rec.LinkPath)!, rec.VirtualRootPath);
            _db.SymlinkRecords.Remove(rec);
            report.Removed.Add(rec.LinkPath);
        }

        // 2) create missing links / verify existing ones
        foreach (var vli in approvedItems)
        {
            // Inactive library: its links were pruned in pass 1 above.
            // Hidden title: pass 1 removes its link; don't recreate it here.
            if (!Active(vli.VirtualLibrary) || HiddenForReconcile(vli)) continue;

            var srcItem = vli.SourceItem;
            // Shows/anime link the whole top-level show folder in one go;
            // movies link each source file individually. See ResolveLinkPaths.
            var (linkPath, sourceTarget) = ResolveLinkPaths(vli, srcItem);

            var existingRec = existingByVliId.TryGetValue(vli.Id, out var rec) ? rec : null;

            // If the library's OutputPath (or a PathMapping target) changed
            // since this link was first created, the persisted LinkPath no
            // longer matches where the link should live now. Remove the stale
            // link and let the create branch below place a fresh one at the
            // new location -- this is what makes editing a library's output
            // path actually relocate its on-disk symlink tree.
            if (existingRec is not null &&
                (!string.Equals(existingRec.LinkPath, linkPath, StringComparison.Ordinal) ||
                 !string.Equals(existingRec.VirtualRootPath, vli.VirtualLibrary.OutputPath, StringComparison.Ordinal)))
            {
                if (File.Exists(existingRec.LinkPath) || IsSymlink(existingRec.LinkPath))
                {
                    File.Delete(existingRec.LinkPath);
                    PruneEmptyDirs(Path.GetDirectoryName(existingRec.LinkPath)!, existingRec.VirtualRootPath);
                }
                _db.SymlinkRecords.Remove(existingRec);
                report.Removed.Add(existingRec.LinkPath);
                existingRec = null;
            }

            if (existingRec is not null)
            {
                if (!SourcePathExists(sourceTarget))
                {
                    existingRec.Status = LinkStatus.Broken;
                    report.Broken.Add(linkPath);
                }
                else
                {
                    report.Unchanged.Add(linkPath);
                }
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);

            if (!SourcePathExists(sourceTarget))
            {
                _db.SymlinkRecords.Add(new SymlinkRecord
                {
                    Id = Guid.NewGuid().ToString(),
                    VirtualLibraryItemId = vli.Id,
                    SourcePath = ApplyPathMapping(sourceTarget, mappings),
                    LinkPath = linkPath,
                    VirtualRootPath = vli.VirtualLibrary.OutputPath,
                    Status = LinkStatus.Broken
                });
                report.Broken.Add(linkPath);
                continue;
            }

            var symlinkTarget = ApplyPathMapping(sourceTarget, mappings);

            // A link may already exist at linkPath — two VLI rows for the
            // same show both resolve to the same folder-level link. If it
            // points to the correct target, just record this VLI against it.
            if (IsSymlink(linkPath))
            {
                string? existingTarget;
                if (Directory.Exists(linkPath))
                    existingTarget = new DirectoryInfo(linkPath).LinkTarget;
                else
                    existingTarget = new FileInfo(linkPath).LinkTarget;

                if (string.Equals(existingTarget, symlinkTarget, StringComparison.Ordinal))
                {
                    // Correct link already in place — just record this VLI
                    _db.SymlinkRecords.Add(new SymlinkRecord
                    {
                        Id = Guid.NewGuid().ToString(),
                        VirtualLibraryItemId = vli.Id,
                        SourcePath = symlinkTarget,
                        LinkPath = linkPath,
                        VirtualRootPath = vli.VirtualLibrary.OutputPath,
                        Status = LinkStatus.Ok
                    });
                    report.Created.Add(linkPath);
                    continue;
                }
                DeleteLink(linkPath);
            }
            else if (File.Exists(linkPath))
            {
                DeleteLink(linkPath);
            }

            File.CreateSymbolicLink(linkPath, symlinkTarget);

            _db.SymlinkRecords.Add(new SymlinkRecord
            {
                Id = Guid.NewGuid().ToString(),
                VirtualLibraryItemId = vli.Id,
                SourcePath = symlinkTarget,
                LinkPath = linkPath,
                VirtualRootPath = vli.VirtualLibrary.OutputPath,
                Status = LinkStatus.Ok
            });
            report.Created.Add(linkPath);
        }

        // Record the successful reconcile pass. The UI uses this against each
        // approval's ApprovedAt to show green ("marked and synced") versus
        // yellow ("marked, waiting for the next Sync"). Scoped reconciles only
        // touch their own library; a full reconcile touches every library.
        var reconciledAt = DateTime.UtcNow;
        if (libraryId is not null)
        {
            var library = await _db.VirtualLibraries.FindAsync(new object[] { libraryId }, ct);
            if (library is not null) library.LastReconciledAt = reconciledAt;
        }
        else
        {
            foreach (var library in await _db.VirtualLibraries.ToListAsync(ct))
                library.LastReconciledAt = reconciledAt;
        }

        // Any unhide requested before this pass has now been applied (the link
        // was recreated above), so retire the row. Newer requests stay pending
        // for the next Sync.
        var completedUnhides = await _db.HiddenItems
            .Where(h => h.UnhideRequestedAt != null && h.UnhideRequestedAt <= reconciledAt)
            .ToListAsync(ct);
        _db.HiddenItems.RemoveRange(completedUnhides);

        await _db.SaveChangesAsync(ct);
        return report;
    }

    internal static (string linkPath, string sourceTarget) ResolveLinkPaths(
        VirtualLibraryItem vli, SourceItem item)
    {
        var rootPath = item.SourceRoot!.Path;
        var rel = item.RelativePath;
        var outputPath = vli.VirtualLibrary.OutputPath;

        // Shows and anime: link the entire show folder in one symlink.
        // The link is FLATTENED to the library root -- letter-group
        // containers in the source tree ("M thru Z/<Show>/...") are dropped
        // from the output path so Plex sees shows directly under the
        // library. Movies: link each file individually (unchanged).
        if (item.MediaType == MediaKind.Show || item.MediaType == MediaKind.Anime)
        {
            var showRel = ShowDirectoryRelativePath(rel, item.ShowTitle);
            var showName = showRel[(showRel.LastIndexOf('/') + 1)..];
            return (Path.Combine(outputPath, showName), Path.Combine(rootPath, showRel));
        }

        return (Path.Combine(outputPath, rel), Path.Combine(rootPath, rel));
    }

    /// <summary>
    /// "M thru Z/Show Name/Season 1/ep.mkv" -> "M thru Z/Show Name".
    /// Strips the file name and any trailing season folder. When ShowTitle
    /// is known, the path is truncated at the matching segment so unexpected
    /// intermediate folders can't be mistaken for the show root.
    /// </summary>
    internal static string ShowDirectoryRelativePath(string rel, string? showTitle)
    {
        var segments = rel.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length <= 1) return rel; // file sits at the root -- nothing to strip

        // Drop the file name, then drop a trailing season folder if present.
        var dirCount = segments.Length - 1;
        if (dirCount >= 1 && IsSeasonFolder(segments[dirCount - 1])) dirCount--;
        if (dirCount == 0) return rel;

        // Prefer the ShowTitle segment as the authoritative show root.
        if (!string.IsNullOrWhiteSpace(showTitle))
        {
            for (var i = 0; i < dirCount; i++)
            {
                if (string.Equals(segments[i], showTitle, StringComparison.OrdinalIgnoreCase))
                    return string.Join('/', segments.Take(i + 1));
            }
        }

        return string.Join('/', segments.Take(dirCount));
    }

    private static bool IsSeasonFolder(string name)
    {
        if (name.Equals("Specials", StringComparison.OrdinalIgnoreCase)) return true;
        return name.Length > 7
               && name.StartsWith("Season ", StringComparison.OrdinalIgnoreCase)
               && int.TryParse(name[7..], out _);
    }

    private static bool SourcePathExists(string path) =>
        File.Exists(path) || Directory.Exists(path);

    private static void DeleteLink(string path)
    {
        // File symlink or regular file
        if (File.Exists(path) || IsSymlink(path))
        {
            File.Delete(path);
            return;
        }
        // Directory symlink (File.CreateSymbolicLink on Linux for a directory)
        if (Directory.Exists(path))
        {
            var di = new DirectoryInfo(path);
            if (di.LinkTarget is not null)
            {
                Directory.Delete(path, false);
            }
            // Real (non-symlink) directories are left for PruneEmptyDirs.
        }
    }

    internal static string ApplyPathMapping(string path, IReadOnlyList<PathMapping> mappings)
    {
        var match = mappings
            .Where(m => path.StartsWith(m.FromPrefix, StringComparison.Ordinal))
            .OrderByDescending(m => m.FromPrefix.Length)
            .FirstOrDefault();

        return match is null ? path : match.ToPrefix + path[match.FromPrefix.Length..];
    }

    private static bool IsSymlink(string path)
    {
        // Linux: a directory symlink is not detected by FileInfo — must also
        // check DirectoryInfo to cover both file and directory symlinks.
        if (new FileInfo(path).LinkTarget is not null) return true;
        return new DirectoryInfo(path).LinkTarget is not null;
    }

    private static void PruneEmptyDirs(string dir, string stopAtRoot)
    {
        var current = dir;
        var stop = Path.GetFullPath(stopAtRoot);
        while (Directory.Exists(current)
               && !Directory.EnumerateFileSystemEntries(current).Any()
               && !string.Equals(Path.GetFullPath(current), stop, StringComparison.Ordinal))
        {
            var parent = Directory.GetParent(current)?.FullName;
            Directory.Delete(current);
            if (parent is null) break;
            current = parent;
        }
    }
}
