using Curatarr.Engine.Data;
using Curatarr.Engine.LibraryGrid;
using Curatarr.Engine.Rules;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Curatarr.Tests;

public class LibraryRuleTests
{
    private static RuleSpec Spec(string field, string op, string value, string action = "approve", int priority = 0)
    {
        var ok = RuleEvaluator.TryPrepare(
            new RuleDraft(field, op, value, action, priority), out var spec, out var error);
        Assert.True(ok, error);
        return spec!;
    }

    private static RuleAnnotations Manual(
        string[]? approved = null, string[]? hidden = null,
        string[]? ruleApproved = null, string[]? ruleHidden = null) => new(
        (approved ?? []).ToHashSet(StringComparer.Ordinal),
        (hidden ?? []).ToHashSet(StringComparer.Ordinal),
        (ruleApproved ?? []).ToHashSet(StringComparer.Ordinal),
        (ruleHidden ?? []).ToHashSet(StringComparer.Ordinal));

    private static GridRow Row(string id, string mediaType = "Movie") => new()
    {
        Id = id,
        MediaType = mediaType,
        Title = "Title " + id,
        Year = 2015,
        GenreIds = "35,10751",
        Rating = "PG",
        ProductionCompanies = "Pixar|Disney",
        Networks = "PBS|NBC",
        Director = "Jane",
        Creators = "Jane",
        Cast = "Alice|Bob",
        SeasonNumber = mediaType == "Movie" ? null : 1,
        EpisodeNumber = mediaType == "Movie" ? null : 1,
    };

    [Fact]
    public void Registry_exposes_every_filter_field_with_its_operators()
    {
        Assert.Equal(
            new[] { "genre", "director", "cast", "studio", "network", "rating", "year", "mediaType" },
            RuleFields.All.Select(f => f.Value));
        Assert.Equal(new[] { "in", "equals" }, RuleFields.Find("genre")!.Operators);
        Assert.Contains("gte", RuleFields.Find("year")!.Operators);
        Assert.Contains("contains", RuleFields.Find("cast")!.Operators);
        Assert.DoesNotContain("gte", RuleFields.Find("rating")!.Operators);
        Assert.Equal(new[] { "approve", "hide" }, RuleFields.Actions);
        Assert.False(RuleFields.SupportsMediaType(RuleFields.Find("network")!, "Movie"));
    }

    [Fact]
    public void Validation_rejects_unknown_field_operator_action_and_empty_value()
    {
        Assert.Contains("Unknown field", RuleEvaluator.Validate(new RuleDraft("nope", "in", "x", "approve"))!);
        Assert.Contains("not valid for field", RuleEvaluator.Validate(new RuleDraft("year", "contains", "2000", "approve"))!);
        Assert.Contains("Unknown action", RuleEvaluator.Validate(new RuleDraft("rating", "equals", "G", "delete"))!);
        Assert.Equal("Value is required", RuleEvaluator.Validate(new RuleDraft("rating", "equals", "  ", "approve")));
    }

    [Fact]
    public void Validation_rejects_bad_numeric_genre_and_media_type_values()
    {
        Assert.Contains("numeric", RuleEvaluator.Validate(new RuleDraft("year", "gte", "abc", "approve"))!);
        Assert.Contains("exactly one", RuleEvaluator.Validate(new RuleDraft("year", "gte", "2000,2010", "approve"))!);
        Assert.Contains("numeric TMDB genre ids", RuleEvaluator.Validate(new RuleDraft("genre", "in", "35,abc", "approve"))!);
        Assert.Contains("Movie, Show or Anime", RuleEvaluator.Validate(new RuleDraft("mediaType", "equals", "Book", "approve"))!);
    }

    [Fact]
    public void Evaluation_matches_grid_filter_predicates()
    {
        var row = Row("m1");
        Assert.True(RuleEvaluator.Matches(row, Spec("genre", "in", "35,10751")));
        Assert.False(RuleEvaluator.Matches(row, Spec("genre", "in", "3,5")));
        Assert.True(RuleEvaluator.Matches(row, Spec("genre", "equals", "35,10751")));
        Assert.False(RuleEvaluator.Matches(row, Spec("genre", "equals", "35")));
        Assert.True(RuleEvaluator.Matches(row, Spec("studio", "equals", "Pixar")));
        Assert.False(RuleEvaluator.Matches(row, Spec("studio", "equals", "Pi")));
        Assert.True(RuleEvaluator.Matches(row, Spec("network", "in", "CBS,NBC")));
        Assert.True(RuleEvaluator.Matches(row, Spec("rating", "equals", "PG")));
        Assert.False(RuleEvaluator.Matches(row, Spec("rating", "equals", "PG-13")));
        Assert.True(RuleEvaluator.Matches(row, Spec("cast", "contains", "BOB")));
        Assert.True(RuleEvaluator.Matches(row, Spec("director", "equals", "jane")));
        Assert.True(RuleEvaluator.Matches(row, Spec("year", "gte", "2010")));
        Assert.True(RuleEvaluator.Matches(row, Spec("year", "lte", "2020")));
        Assert.True(RuleEvaluator.Matches(row, Spec("year", "in", "1999,2015")));
        Assert.True(RuleEvaluator.Matches(row, Spec("mediaType", "equals", "Movie")));
        Assert.False(RuleEvaluator.Matches(row, Spec("mediaType", "equals", "Show")));
    }

    [Fact]
    public void Later_rules_override_earlier_ones()
    {
        var row = Row("m1");
        var approve = Spec("genre", "in", "10751", "approve", priority: 0);
        var hide = Spec("rating", "equals", "PG", "hide", priority: 10);

        var result = RuleEvaluator.Evaluate([row], [approve, hide], "Movie", Manual());
        Assert.Empty(result.Approve);
        Assert.Equal(new[] { "m1" }, result.Hide);

        var reversed = RuleEvaluator.Evaluate([row],
            [Spec("rating", "equals", "PG", "hide", priority: 0),
             Spec("genre", "in", "10751", "approve", priority: 10)],
            "Movie", Manual());
        Assert.Equal(new[] { "m1" }, reversed.Approve);
        Assert.Empty(reversed.Hide);
    }

    [Fact]
    public void Manual_decisions_win_and_rule_owned_rows_are_rewound()
    {
        var row = Row("m1");
        var hide = Spec("rating", "equals", "PG", "hide");

        // Manual approval blocks the hide rule completely.
        var manualApproved = RuleEvaluator.Evaluate([row], [hide], "Movie", Manual(approved: ["m1"]));
        Assert.Empty(manualApproved.Hide);
        Assert.Empty(manualApproved.Unapprove);

        // Manual hide blocks an approve rule, and a stale rule-owned approval
        // on that title is cleaned up so the manual decision is unambiguous.
        var manualHidden = RuleEvaluator.Evaluate(
            [row], [Spec("rating", "equals", "PG")], "Movie",
            Manual(hidden: ["m1"], ruleApproved: ["m1"]));
        Assert.Empty(manualHidden.Approve);
        Assert.Equal(new[] { "m1" }, manualHidden.Unapprove);
    }

    [Fact]
    public void Rules_only_evaluate_the_library_media_type()
    {
        var movie = Row("m1", "Movie");
        var episode = Row("e1", "Show");
        var rule = Spec("genre", "in", "10751");

        var movieLibrary = RuleEvaluator.Evaluate([movie, episode], [rule], "Movie", Manual());
        Assert.Equal(new[] { "m1" }, movieLibrary.Approve);

        var showLibrary = RuleEvaluator.Evaluate([movie, episode], [rule], "Show", Manual());
        Assert.Equal(new[] { "e1" }, showLibrary.Approve);
    }

    [Fact]
    public async Task Preview_never_mutates_and_apply_is_idempotent_and_audited()
    {
        using var connection = OpenDatabase(out var db);
        var library = Seed(db);
        db.LibraryRules.Add(new LibraryRule
        {
            Id = "r1", VirtualLibraryId = library.Id, Field = "genre", Operator = "in",
            Value = "10751", Action = "approve", Priority = 0, Enabled = true,
        });
        db.SaveChanges();

        var preview = await RuleApplication.PreviewAsync(db, library.Id);
        Assert.Equal(1, preview.ToApprove);
        Assert.Equal(0, preview.ToHide);
        Assert.Contains("Family Fun", preview.Samples);
        Assert.Equal(0, db.VirtualLibraryItems.Count());
        Assert.Empty(db.LibraryRuleRuns);

        var first = await RuleApplication.ApplyAsync(db, library.Id);
        Assert.Equal(1, first.Approved);
        Assert.Equal(0, first.Hidden);
        Assert.Single(db.VirtualLibraryItems);
        Assert.True(db.VirtualLibraryItems.Single().SetByRule);
        var log = db.LibraryRuleRuns.Single();
        Assert.Equal(first.LogId, log.Id);
        Assert.Equal(1, log.Approved);
        Assert.Equal(1, log.RuleCount);

        var second = await RuleApplication.ApplyAsync(db, library.Id);
        Assert.Equal(0, second.Total);
        Assert.Single(db.VirtualLibraryItems);
        Assert.Equal(2, db.LibraryRuleRuns.Count());
        Assert.Empty(db.SymlinkRecords); // no reconcile / Plex call from Apply
    }

    [Fact]
    public async Task Disabling_a_rule_rewinds_only_its_rule_owned_rows()
    {
        using var connection = OpenDatabase(out var db);
        var library = Seed(db);
        db.LibraryRules.AddRange(
            new LibraryRule
            {
                Id = "r1", VirtualLibraryId = library.Id, Field = "genre", Operator = "in",
                Value = "10751", Action = "approve", Priority = 0, Enabled = true,
            },
            new LibraryRule
            {
                Id = "r2", VirtualLibraryId = library.Id, Field = "rating", Operator = "equals",
                Value = "R", Action = "hide", Priority = 1, Enabled = true,
            });
        db.SaveChanges();

        // A manual approval must survive the whole run untouched.
        db.VirtualLibraryItems.Add(new VirtualLibraryItem
        {
            Id = "manual", VirtualLibraryId = library.Id, SourceItemId = "m3", SetByRule = false,
        });
        db.SaveChanges();

        var first = await RuleApplication.ApplyAsync(db, library.Id);
        Assert.Equal(1, first.Approved);
        Assert.Equal(1, first.Hidden);

        var rule = db.LibraryRules.Single(r => r.Id == "r1");
        rule.Enabled = false;
        db.SaveChanges();

        var second = await RuleApplication.ApplyAsync(db, library.Id);
        Assert.Equal(0, second.Approved);
        Assert.Equal(0, second.Hidden);
        Assert.Equal(1, second.Unapproved);
        Assert.Equal(0, second.Unhidden);
        Assert.Contains(db.VirtualLibraryItems, v => v.Id == "manual");
        Assert.DoesNotContain(db.VirtualLibraryItems, v => v.SetByRule);
    }

    [Fact]
    public void Migration_applies_cleanly_to_throwaway_sqlite()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<CuratarrDbContext>().UseSqlite(connection).Options;
        using var db = new CuratarrDbContext(options);

        db.Database.Migrate();

        var tables = db.Database.SqlQueryRaw<string>(
            "SELECT name FROM sqlite_master WHERE type='table'").ToList();
        Assert.Contains("LibraryRules", tables);
        Assert.Contains("LibraryRuleRuns", tables);
        Assert.Contains("__EFMigrationsHistory", tables);

        var approvalColumns = db.Database.SqlQueryRaw<string>(
            "SELECT name FROM pragma_table_info('VirtualLibraryItems')").ToList();
        var hiddenColumns = db.Database.SqlQueryRaw<string>(
            "SELECT name FROM pragma_table_info('HiddenItems')").ToList();
        Assert.Contains("SetByRule", approvalColumns);
        Assert.Contains("SetByRule", hiddenColumns);
        Assert.Contains("UnhideRequestedAt", hiddenColumns);
        Assert.Contains("AddActivityEvents", db.Database.GetAppliedMigrations().Last());
    }

    private static SqliteConnection OpenDatabase(out CuratarrDbContext db)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<CuratarrDbContext>().UseSqlite(connection).Options;
        db = new CuratarrDbContext(options);
        db.Database.EnsureCreated();
        return connection;
    }

    private static VirtualLibrary Seed(CuratarrDbContext db)
    {
        var root = new SourceRoot { Id = "root1", Name = "Root", Path = "/media", MediaType = MediaKind.Movie };
        var library = new VirtualLibrary
        {
            Id = "lib1", Name = "Kids", MediaType = MediaKind.Movie, OutputPath = "/output/kids",
        };
        db.SourceRoots.Add(root);
        db.VirtualLibraries.Add(library);
        db.SourceItems.AddRange(
            new SourceItem
            {
                Id = "m1", SourceRootId = root.Id, MediaType = MediaKind.Movie,
                RelativePath = "Family Fun (2015).mkv",
                Title = "Family Fun", Year = 2015, GenreIds = "10751", Rating = "G",
                ProductionCompanies = "Pixar", Director = "Jane", Cast = "Alice|Bob",
            },
            new SourceItem
            {
                Id = "m2", SourceRootId = root.Id, MediaType = MediaKind.Movie,
                RelativePath = "Scary (2018).mkv",
                Title = "Scary", Year = 2018, GenreIds = "27", Rating = "R",
                ProductionCompanies = "Studio", Director = "John", Cast = "Carol",
            },
            new SourceItem
            {
                Id = "m3", SourceRootId = root.Id, MediaType = MediaKind.Movie,
                RelativePath = "Manual (2019).mkv",
                Title = "Manual", Year = 2019, GenreIds = "35", Rating = "PG",
                ProductionCompanies = "Other", Director = "Ann", Cast = "Dave",
            });
        db.SaveChanges();
        return library;
    }
}
