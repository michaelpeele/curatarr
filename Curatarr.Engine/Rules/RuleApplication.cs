using Curatarr.Engine.Data;
using Curatarr.Engine.LibraryGrid;
using Microsoft.EntityFrameworkCore;

namespace Curatarr.Engine.Rules;

/// <summary>
/// DbContext-backed preview/apply for Sprint 6 #1. Preview is read-only;
/// Apply writes the same VirtualLibraryItem / HiddenItem rows the manual
/// endpoints use and appends one LibraryRuleRun audit row. Neither one
/// reconciles symlinks nor talks to Plex -- Sync remains a separate step.
/// </summary>
public static class RuleApplication
{
    public static async Task<RulePreviewDto> PreviewAsync(
        CuratarrDbContext db, string libraryId, IReadOnlyList<string>? ruleIds = null)
    {
        var (library, specs, rows, annotations) = await LoadAsync(db, libraryId, ruleIds);
        return RuleEvaluator.Evaluate(rows, specs, library.MediaType.ToString(), annotations).Preview();
    }

    public static async Task<RuleApplyDto> ApplyAsync(
        CuratarrDbContext db, string libraryId, IReadOnlyList<string>? ruleIds = null)
    {
        var (library, specs, rows, annotations) = await LoadAsync(db, libraryId, ruleIds);
        var evaluation = RuleEvaluator.Evaluate(rows, specs, library.MediaType.ToString(), annotations);
        var counts = evaluation.Counts();

        foreach (var id in evaluation.Approve)
            db.VirtualLibraryItems.Add(new VirtualLibraryItem
            {
                Id = Guid.NewGuid().ToString(),
                VirtualLibraryId = libraryId,
                SourceItemId = id,
                SetByRule = true,
            });

        foreach (var id in evaluation.Hide)
            db.HiddenItems.Add(new HiddenItem
            {
                Id = Guid.NewGuid().ToString(),
                VirtualLibraryId = libraryId,
                SourceItemId = id,
                SetByRule = true,
            });

        if (evaluation.Unapprove.Count > 0)
        {
            var ids = evaluation.Unapprove.ToList();
            var toRemove = await db.VirtualLibraryItems
                .Where(v => v.VirtualLibraryId == libraryId && v.SetByRule && ids.Contains(v.SourceItemId))
                .ToListAsync();
            db.VirtualLibraryItems.RemoveRange(toRemove);
        }

        if (evaluation.Unhide.Count > 0)
        {
            var ids = evaluation.Unhide.ToList();
            var toRemove = await db.HiddenItems
                .Where(h => h.VirtualLibraryId == libraryId && h.SetByRule && ids.Contains(h.SourceItemId))
                .ToListAsync();
            db.HiddenItems.RemoveRange(toRemove);
        }

        var log = new LibraryRuleRun
        {
            Id = Guid.NewGuid().ToString(),
            VirtualLibraryId = libraryId,
            RanAt = DateTime.UtcNow,
            RuleCount = specs.Count,
            Approved = counts.Approved,
            Hidden = counts.Hidden,
            Unapproved = counts.Unapproved,
            Unhidden = counts.Unhidden,
        };
        db.LibraryRuleRuns.Add(log);
        await db.SaveChangesAsync();

        return new RuleApplyDto(counts.Approved, counts.Hidden, counts.Unapproved, counts.Unhidden, log.Id);
    }

    private static async Task<(VirtualLibrary Library, IReadOnlyList<RuleSpec> Specs,
        IReadOnlyList<GridRow> Rows, RuleAnnotations Annotations)> LoadAsync(
        CuratarrDbContext db, string libraryId, IReadOnlyList<string>? ruleIds)
    {
        var library = await db.VirtualLibraries.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == libraryId)
            ?? throw new KeyNotFoundException($"Library '{libraryId}' does not exist.");

        var query = db.LibraryRules.Where(r => r.VirtualLibraryId == libraryId && r.Enabled);
        if (ruleIds is not null && ruleIds.Count > 0)
        {
            var ids = ruleIds.ToList();
            query = query.Where(r => ids.Contains(r.Id));
        }
        var specs = RuleEvaluator.Prepare(await query.ToListAsync());

        // Same 18-field projection and media-type semantics as the grid, so a
        // preview matches what /api/library-entries shows for the same filter.
        var raw = await db.SourceItems.AsNoTracking().Select(i => new
        {
            i.Id, i.MediaType, i.Title, i.Year, i.ShowTitle, i.SeasonNumber, i.EpisodeNumber,
            i.GenreIds, i.Rating, i.ProductionCompanies, i.Networks, i.Director, i.Creators, i.Cast,
        }).ToListAsync();

        var rows = raw.Select(i => new GridRow
        {
            Id = i.Id,
            MediaType = i.MediaType.ToString(),
            Title = i.Title,
            Year = i.Year,
            ShowTitle = i.ShowTitle,
            SeasonNumber = i.SeasonNumber,
            EpisodeNumber = i.EpisodeNumber,
            GenreIds = i.GenreIds,
            Rating = i.Rating,
            ProductionCompanies = i.ProductionCompanies,
            Networks = i.Networks,
            Director = i.Director,
            Creators = i.Creators,
            Cast = i.Cast,
        }).ToList();

        var approvals = await db.VirtualLibraryItems
            .Where(v => v.VirtualLibraryId == libraryId)
            .Select(v => new { v.SourceItemId, v.SetByRule })
            .ToListAsync();
        var hides = await db.HiddenItems
            .Where(h => h.VirtualLibraryId == libraryId)
            .Select(h => new { h.SourceItemId, h.SetByRule })
            .ToListAsync();

        var annotations = new RuleAnnotations(
            approvals.Where(a => !a.SetByRule).Select(a => a.SourceItemId).ToHashSet(StringComparer.Ordinal),
            hides.Where(h => !h.SetByRule).Select(h => h.SourceItemId).ToHashSet(StringComparer.Ordinal),
            approvals.Where(a => a.SetByRule).Select(a => a.SourceItemId).ToHashSet(StringComparer.Ordinal),
            hides.Where(h => h.SetByRule).Select(h => h.SourceItemId).ToHashSet(StringComparer.Ordinal));

        return (library, specs, rows, annotations);
    }
}
