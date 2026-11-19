using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Curatarr.Engine.Data;

namespace Curatarr.Api;

/// <summary>
/// Endpoints consumed by the first-boot wizard. Only usable when no users
/// exist yet (the wizard gates access itself, so no [Authorize] here).
/// </summary>
public static class BootstrapWizardEndpoints
{
    public record BootstrapAllRequest(
        string AdminUsername,
        string AdminPassword,
        string AdminDisplayName,
        List<SourceRootDto> SourceRoots,
        List<PathMappingDto> PathMappings,
        string LibraryName,
        string LibraryOutputPath,
        string? MediaType,          // Sprint 4 #1: the one media type for the whole wizard
        string? LibraryMediaType    // legacy field, honoured only when MediaType is absent
    );

    public record SourceRootDto(string Name, string Path, string? MediaType);
    public record PathMappingDto(string FromPrefix, string ToPrefix);

    /// <summary>
    /// Sprint 4 #1 resolves one media type for the whole wizard. The new
    /// top-level <paramref name="primary"/> wins; legacy clients that still
    /// send only <paramref name="legacy"/> are honoured. Invalid values are
    /// rejected (no silent Movie fallback).
    /// </summary>
    public static bool TryResolveMediaType(string? primary, string? legacy, out MediaKind mediaKind)
    {
        var requested = !string.IsNullOrWhiteSpace(primary) ? primary : legacy;
        return Enum.TryParse<MediaKind>(requested, ignoreCase: true, out mediaKind);
    }

    public static WebApplication MapBootstrapWizardEndpoints(this WebApplication app)
    {
        // One-shot: creates admin, source roots, path mappings, and a default
        // library in a single call. Intentionally anonymous — usable only
        // when the DB is empty (no users exist yet).
        app.MapPost("/api/bootstrap", async (
            BootstrapAllRequest req,
            UserManager<ApplicationUser> userManager,
            CuratarrDbContext db) =>
        {
            // Guard: don't let someone call this if the DB already has users
            if (await db.Users.AnyAsync())
                return Results.BadRequest(new { error = "Admin user already exists. Use the normal login flow." });

            // ── 0. Resolve the single media type (Sprint 4 #1) ─────────
            // The wizard now sends one top-level MediaType that applies to every
            // source root and the first library. Legacy clients may still send
            // LibraryMediaType instead; accept it for backwards compatibility.
            if (!TryResolveMediaType(req.MediaType, req.LibraryMediaType, out var mediaKind))
                return Results.BadRequest(new { error = "Invalid media type. Expected Movie, Show, or Anime." });

            // ── 1. Create admin user ──────────────────────────────────
            var user = new ApplicationUser
            {
                UserName = req.AdminUsername,
                Email = req.AdminUsername,
                DisplayName = req.AdminDisplayName,
                EmailConfirmed = true  // self-hosted; no email verification
            };

            var createResult = await userManager.CreateAsync(user, req.AdminPassword);
            if (!createResult.Succeeded)
                return Results.BadRequest(new { error = "Failed to create admin user", errors = createResult.Errors.Select(e => e.Description) });

            await userManager.AddToRoleAsync(user, "Admin");

            // ── 2. Create source roots ────────────────────────────────
            var sourceRoots = new List<SourceRoot>();
            foreach (var sr in req.SourceRoots ?? new())
            {
                sourceRoots.Add(new SourceRoot
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = sr.Name,
                    Path = sr.Path,
                    MediaType = mediaKind
                });
            }
            db.SourceRoots.AddRange(sourceRoots);

            // ── 3. Create path mappings ────────────────────────────────
            var pathMappings = new List<PathMapping>();
            foreach (var pm in req.PathMappings ?? new())
            {
                pathMappings.Add(new PathMapping
                {
                    Id = Guid.NewGuid().ToString("N"),
                    FromPrefix = pm.FromPrefix,
                    ToPrefix = pm.ToPrefix
                });
            }
            db.PathMappings.AddRange(pathMappings);

            // ── 4. Create default virtual library ──────────────────────
            var library = new VirtualLibrary
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = req.LibraryName,
                MediaType = mediaKind,
                OutputPath = req.LibraryOutputPath
            };

            db.VirtualLibraries.Add(library);

            // ── 5. Grant admin membership to the library ───────────────
            db.VirtualLibraryMemberships.Add(new VirtualLibraryMembership
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = user.Id,
                VirtualLibraryId = library.Id,
                Role = MembershipRole.Owner
            });

            // ── 6. Persist everything in one transaction ───────────────
            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                message = "Bootstrap complete",
                adminUser = new { user.Id, user.UserName, user.DisplayName },
                sourceRoots = sourceRoots.Count,
                pathMappings = pathMappings.Count,
                library = new { library.Id, library.Name, library.OutputPath }
            });
        }).AllowAnonymous();

        return app;
    }
}