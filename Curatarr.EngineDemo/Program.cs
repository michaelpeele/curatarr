using Microsoft.EntityFrameworkCore;
using Curatarr.Engine;
using Curatarr.Engine.Data;

var baseDir = Path.Combine(Path.GetTempPath(), "curatarr_sim_cs");
if (Directory.Exists(baseDir)) Directory.Delete(baseDir, recursive: true);
var dbPath = Path.Combine(baseDir, "curatarr.db");

var root4k = Path.Combine(baseDir, "media", "movies-4k");
var rootStandard = Path.Combine(baseDir, "media", "movies-standard");
var rootShows = Path.Combine(baseDir, "media", "shows");
var kidsMoviesOut = Path.Combine(baseDir, "media-curated", "kids-movies");
var kidsShowsOut = Path.Combine(baseDir, "media-curated", "kids-shows");
Directory.CreateDirectory(root4k);
Directory.CreateDirectory(rootStandard);
Directory.CreateDirectory(rootShows);

MakeFakeMovie(root4k, "Inside Out (2015)");
MakeFakeMovie(rootStandard, "Coco (2017)");
MakeFakeMovie(rootStandard, "The Matrix (1999)");
MakeFakeEpisode(rootShows, "Bluey", "Season 01", "Bluey - S01E01 - Magic Xylophone.mkv");
MakeFakeEpisode(rootShows, "Bluey", "Season 01", "Bluey - S01E02 - Hospital.mkv");
MakeFakeEpisode(rootShows, "Bluey", "Season 02", "Bluey - S02E01 - Cricket.mkv");
MakeFakeEpisode(rootShows, "Breaking Bad", "Season 01", "Breaking Bad - S01E01 - Pilot.mkv"); // will NOT be approved

// --- offline metadata stand-in; swap for TmdbMetadataProvider(apiKey) for real lookups ---
var movieCatalog = new Dictionary<string, MovieMetadata>
{
    ["Inside Out"] = new(150540, "Inside Out", 2015, "https://image.tmdb.org/t/p/w500/2H1TmgdfNtsKlU9jKdeNyYL5y8T.jpg", "Growing up can be a bumpy road..."),
    ["Coco"] = new(354912, "Coco", 2017, "https://image.tmdb.org/t/p/w500/gGEsBPAijhVUFoiNpgZXqRVWJt2.jpg", "Despite his family's generations-old ban on music..."),
    ["The Matrix"] = new(603, "The Matrix", 1999, "https://image.tmdb.org/t/p/w500/f89U3ADr1oiB1s9GkdPOEpXUk5H.jpg", "Set in the 22nd century..."),
};
var showCatalog = new Dictionary<string, ShowMetadata>
{
    ["Bluey"] = new(82728, "Bluey", "https://image.tmdb.org/t/p/w500/rjBwfHFY7iP0xNwrX9EDrGSFXka.jpg", "Bluey is an inexhaustible six-year-old Blue Heeler dog..."),
    ["Breaking Bad"] = new(1396, "Breaking Bad", "https://image.tmdb.org/t/p/w500/ggFHVNu6YYI5L9pCfOacjizRGt.jpg", "A high school chemistry teacher turned meth cook..."),
};
IMetadataProvider metadataProvider = new FakeMetadataProvider(movieCatalog, showCatalog);

// =========================================================================
// PROCESS 1: initial scan + first-time approvals + first reconcile
// =========================================================================
Section("PROCESS 1 -- initial scan, approvals, and first reconcile");

await using (var db = OpenDb(dbPath))
{
    await db.Database.EnsureCreatedAsync();

    var sourceRoots = new List<SourceRoot>
    {
        new() { Id = "root-4k", Name = "4K Movies", Path = root4k, MediaType = MediaKind.Movie },
        new() { Id = "root-standard", Name = "Standard Movies", Path = rootStandard, MediaType = MediaKind.Movie },
        new() { Id = "root-shows", Name = "TV Shows", Path = rootShows, MediaType = MediaKind.Show },
    };
    db.SourceRoots.AddRange(sourceRoots);
    await db.SaveChangesAsync();

    var scanner = new LibraryScanner(metadataProvider);
    var scanned = await scanner.ScanAsync(sourceRoots);
    db.SourceItems.AddRange(scanned);
    await db.SaveChangesAsync();

    Console.WriteLine($"  Scanned {scanned.Count} source items:");
    foreach (var item in scanned.OrderBy(i => i.RelativePath))
    {
        var label = item.MediaType == MediaKind.Movie
            ? $"{item.Title} ({item.Year})"
            : $"{item.ShowTitle} S{item.SeasonNumber:D2}E{item.EpisodeNumber:D2}";
        Console.WriteLine($"    [{item.MediaType}] {label}  tmdbId={item.TmdbId}");
    }

    var kidsMovies = new VirtualLibrary { Id = "vl-kids-movies", Name = "Kids Movies", MediaType = MediaKind.Movie, OutputPath = kidsMoviesOut };
    var kidsShows = new VirtualLibrary { Id = "vl-kids-shows", Name = "Kids Shows", MediaType = MediaKind.Show, OutputPath = kidsShowsOut };
    db.VirtualLibraries.AddRange(kidsMovies, kidsShows);

    // approve: Inside Out + Coco (not the Matrix), and ALL of Bluey (not Breaking Bad)
    var insideOut = scanned.Single(i => i.Title == "Inside Out");
    var coco = scanned.Single(i => i.Title == "Coco");
    var blueyEpisodes = scanned.Where(i => i.ShowTitle == "Bluey").ToList();

    db.VirtualLibraryItems.Add(new VirtualLibraryItem { Id = Guid.NewGuid().ToString(), VirtualLibraryId = kidsMovies.Id, SourceItemId = insideOut.Id });
    db.VirtualLibraryItems.Add(new VirtualLibraryItem { Id = Guid.NewGuid().ToString(), VirtualLibraryId = kidsMovies.Id, SourceItemId = coco.Id });
    foreach (var ep in blueyEpisodes)
        db.VirtualLibraryItems.Add(new VirtualLibraryItem { Id = Guid.NewGuid().ToString(), VirtualLibraryId = kidsShows.Id, SourceItemId = ep.Id });

    await db.SaveChangesAsync();

    var engine = new SymlinkEngine(db);
    PrintReport(await engine.ReconcileAsync());
}

PrintTree(kidsMoviesOut, "kids-movies after process 1");
PrintTree(kidsShowsOut, "kids-shows after process 1");

// =========================================================================
// PROCESS 2: brand-new DbContext, same file -- simulates an app restart.
// Re-running reconcile should be a pure no-op (everything "Unchanged"),
// proving the ledger actually persisted rather than living in memory.
// =========================================================================
Section("PROCESS 2 -- new DbContext instance, same DB file (simulated restart)");

await using (var db = OpenDb(dbPath))
{
    var engine = new SymlinkEngine(db);
    PrintReport(await engine.ReconcileAsync());
}

// =========================================================================
// PROCESS 3: revoke Bluey Season 2 (parent decides one season is enough),
// again via a fresh DbContext, proving unapproval + cleanup also persists
// correctly across restarts.
// =========================================================================
Section("PROCESS 3 -- new DbContext, revoke Bluey Season 2");

await using (var db = OpenDb(dbPath))
{
    var season2Items = await db.VirtualLibraryItems
        .Include(v => v.SourceItem)
        .Where(v => v.SourceItem.ShowTitle == "Bluey" && v.SourceItem.SeasonNumber == 2)
        .ToListAsync();

    db.VirtualLibraryItems.RemoveRange(season2Items);
    await db.SaveChangesAsync();

    var engine = new SymlinkEngine(db);
    PrintReport(await engine.ReconcileAsync());
}

PrintTree(kidsShowsOut, "kids-shows after revoking Season 2");

return;

static CuratarrDbContext OpenDb(string path)
{
    var options = new DbContextOptionsBuilder<CuratarrDbContext>().UseSqlite($"Data Source={path}").Options;
    return new CuratarrDbContext(options);
}

static void MakeFakeMovie(string root, string folderName)
{
    var dir = Directory.CreateDirectory(Path.Combine(root, folderName));
    File.WriteAllText(Path.Combine(dir.FullName, $"{folderName}.mkv"), "fake video bytes");
}

static void MakeFakeEpisode(string root, string showName, string seasonFolder, string fileName)
{
    var dir = Directory.CreateDirectory(Path.Combine(root, showName, seasonFolder));
    File.WriteAllText(Path.Combine(dir.FullName, fileName), "fake video bytes");
}

static void Section(string title)
{
    Console.WriteLine(new string('=', 60));
    Console.WriteLine(title);
}

static void PrintReport(ReconcileReport r)
{
    Console.WriteLine($"  Created:   {Fmt(r.Created)}");
    Console.WriteLine($"  Removed:   {Fmt(r.Removed)}");
    Console.WriteLine($"  Broken:    {Fmt(r.Broken)}");
    Console.WriteLine($"  Unchanged: {Fmt(r.Unchanged)}");
}

static string Fmt(List<string> items) => items.Count == 0 ? "(none)" : string.Join(", ", items);

static void PrintTree(string root, string label)
{
    Console.WriteLine($"\n--- {label}: {root} ---");
    if (!Directory.Exists(root)) { Console.WriteLine("  (missing)"); return; }

    foreach (var entry in new DirectoryInfo(root).GetFileSystemInfos("*", SearchOption.AllDirectories).OrderBy(e => e.FullName))
    {
        var rel = Path.GetRelativePath(root, entry.FullName);
        var target = entry is FileInfo fi ? fi.LinkTarget : null;
        Console.WriteLine($"  {rel}{(target is not null ? $" -> {target}" : "")}");
    }
}
