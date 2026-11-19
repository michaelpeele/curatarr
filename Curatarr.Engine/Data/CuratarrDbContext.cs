using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Curatarr.Engine.Data;

/// <summary>
/// SQLite-backed persistence, now also the Identity store (Users/Roles
/// tables come from IdentityDbContext&lt;ApplicationUser&gt;). Uses the
/// standard DbContextOptions constructor pattern so it plugs into ASP.NET
/// Core DI via AddDbContext -- callers configure the SQLite connection
/// string at registration time (see Curatarr.Api/Program.cs) rather than
/// this class hardcoding a path.
///
/// <para>
/// Schema is managed by EF Core migrations (see <c>Data/Migrations</c>, applied
/// via <c>Database.MigrateAsync()</c> in the API's startup seeding). Databases
/// created before migrations existed are baselined there; do not call
/// EnsureCreated here -- it bypasses the migration history.
/// </para>
/// </summary>
public class CuratarrDbContext(DbContextOptions<CuratarrDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<SourceRoot> SourceRoots => Set<SourceRoot>();
    public DbSet<SourceItem> SourceItems => Set<SourceItem>();
    public DbSet<VirtualLibrary> VirtualLibraries => Set<VirtualLibrary>();
    public DbSet<VirtualLibraryItem> VirtualLibraryItems => Set<VirtualLibraryItem>();
    public DbSet<HiddenItem> HiddenItems => Set<HiddenItem>();
    public DbSet<VirtualLibraryMembership> VirtualLibraryMemberships => Set<VirtualLibraryMembership>();
    public DbSet<SymlinkRecord> SymlinkRecords => Set<SymlinkRecord>();
    public DbSet<PathMapping> PathMappings => Set<PathMapping>();
    public DbSet<TmdbRawDetails> TmdbRawDetails => Set<TmdbRawDetails>();
    public DbSet<LibrarySchedule> LibrarySchedules => Set<LibrarySchedule>();
    public DbSet<PlexSettings> PlexSettings => Set<PlexSettings>();
    public DbSet<LibraryRule> LibraryRules => Set<LibraryRule>();
    public DbSet<LibraryRuleRun> LibraryRuleRuns => Set<LibraryRuleRun>();
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // required first -- sets up Identity's own tables

        modelBuilder.Entity<SourceRoot>().Property(e => e.MediaType).HasConversion<string>();
        modelBuilder.Entity<SourceItem>().Property(e => e.MediaType).HasConversion<string>();
        modelBuilder.Entity<VirtualLibrary>().Property(e => e.MediaType).HasConversion<string>();
        modelBuilder.Entity<VirtualLibraryMembership>().Property(e => e.Role).HasConversion<string>();
        modelBuilder.Entity<SymlinkRecord>().Property(e => e.Status).HasConversion<string>();
        modelBuilder.Entity<ActivityEvent>().Property(e => e.Category).HasConversion<string>();

        // Activity is an append-only audit log. Newest-first paging uses this
        // composite index; Id is the deterministic tie-break for equal stamps.
        modelBuilder.Entity<ActivityEvent>()
            .HasIndex(e => new { e.OccurredAt, e.Id });

        // These two unique indexes were originally created by raw SQL in
        // SeedAsync. Declaring them here (with the same explicit names) makes
        // the EF model the single source of truth and keeps fresh
        // migration-built databases identical to the already-migrated prod DB.
        modelBuilder.Entity<SymlinkRecord>()
            .HasIndex(e => new { e.VirtualLibraryItemId, e.LinkPath })
            .IsUnique()
            .HasDatabaseName("IX_SymlinkRecords_UniqueLinkPerLibrary");

        modelBuilder.Entity<VirtualLibraryItem>()
            .HasIndex(e => new { e.VirtualLibraryId, e.SourceItemId })
            .IsUnique()
            .HasDatabaseName("IX_VirtualLibraryItems_OnePerLibraryAndItem");

        // Hiding is a per-library overlay on the catalog; the same source item
        // may be hidden from one library and visible in another, but a library
        // can only hide it once.
        modelBuilder.Entity<HiddenItem>()
            .HasIndex(e => new { e.VirtualLibraryId, e.SourceItemId })
            .IsUnique()
            .HasDatabaseName("IX_HiddenItems_OnePerLibraryAndItem");

        // Sprint 6 #1: rules and their audit runs belong to exactly one
        // library and disappear with it (matching schedules/hidden items).
        // Id is the PK, so no extra uniqueness constraint is needed.
        modelBuilder.Entity<LibraryRule>()
            .HasOne(e => e.VirtualLibrary)
            .WithMany(l => l.Rules)
            .HasForeignKey(e => e.VirtualLibraryId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LibraryRuleRun>()
            .HasOne(e => e.VirtualLibrary)
            .WithMany(l => l.RuleRuns)
            .HasForeignKey(e => e.VirtualLibraryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
