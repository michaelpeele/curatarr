using System.Security.Claims;
using Curatarr.Engine.Data;
using Curatarr.Engine.Rules;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Curatarr.Api;

/// <summary>
/// Sprint 6 #1 — per-library automatic approve/hide rules. Gating mirrors the
/// existing approve / hidden-items endpoints exactly: Admin bypasses, Owner
/// and Curator need membership on THAT library, Viewer is rejected. Evaluating
/// (preview) and applying never reconcile symlinks or call Plex; Sync still
/// materializes the actual library changes.
/// </summary>
public static class LibraryRuleEndpoints
{
    public static void MapLibraryRuleEndpoints(this WebApplication app)
    {
        // Shared registry: the frontend builds its rule builder from this, so
        // new grid filters automatically become rule fields.
        app.MapGet("/api/library-fields", () => Results.Ok(RuleFields.All.Select(f =>
            new RuleFieldDto(f.Value, f.Label, f.Kind, f.Operators, f.MediaTypes, f.Hint))))
            .RequireAuthorization();

        app.MapGet("/api/virtual-libraries/{libraryId}/rules", async (
            string libraryId, ClaimsPrincipal principal, UserManager<ApplicationUser> userManager,
            CuratarrDbContext db) =>
        {
            var denied = await AuthorizeAsync(libraryId, principal, userManager, db);
            if (denied is not null) return denied;

            var rules = await db.LibraryRules
                .Where(r => r.VirtualLibraryId == libraryId)
                .OrderBy(r => r.Priority).ThenBy(r => r.Id)
                .Select(r => new LibraryRuleDto(r.Id, r.Field, r.Operator, r.Value, r.Action,
                    r.Priority, r.Enabled, r.CreatedAt, r.UpdatedAt))
                .ToListAsync();
            return Results.Ok(rules);
        }).RequireAuthorization();

        app.MapPost("/api/virtual-libraries/{libraryId}/rules", async (
            string libraryId, [Microsoft.AspNetCore.Mvc.FromBody] LibraryRuleRequest req, ClaimsPrincipal principal,
            UserManager<ApplicationUser> userManager, CuratarrDbContext db) =>
        {
            var denied = await AuthorizeAsync(libraryId, principal, userManager, db);
            if (denied is not null) return denied;

            var draft = req.ToDraft();
            var error = RuleEvaluator.Validate(draft);
            if (error is not null) return Results.BadRequest(new { error });

            var now = DateTime.UtcNow;
            var rule = new LibraryRule
            {
                Id = Guid.NewGuid().ToString(),
                VirtualLibraryId = libraryId,
                Field = draft.Field!.Trim().ToLowerInvariant(),
                Operator = draft.Operator!.Trim().ToLowerInvariant(),
                Value = draft.Value!.Trim(),
                Action = draft.Action!.Trim().ToLowerInvariant(),
                Priority = draft.Priority,
                Enabled = draft.Enabled,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.LibraryRules.Add(rule);
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(rule));
        }).RequireAuthorization();

        app.MapPut("/api/virtual-libraries/{libraryId}/rules", async (
            string libraryId, [Microsoft.AspNetCore.Mvc.FromBody] LibraryRuleRequest req, ClaimsPrincipal principal,
            UserManager<ApplicationUser> userManager, CuratarrDbContext db) =>
        {
            var denied = await AuthorizeAsync(libraryId, principal, userManager, db);
            if (denied is not null) return denied;

            var rule = await db.LibraryRules
                .FirstOrDefaultAsync(r => r.VirtualLibraryId == libraryId && r.Id == req.Id);
            if (rule is null) return Results.NotFound();

            var draft = req.ToDraft();
            var error = RuleEvaluator.Validate(draft);
            if (error is not null) return Results.BadRequest(new { error });

            rule.Field = draft.Field!.Trim().ToLowerInvariant();
            rule.Operator = draft.Operator!.Trim().ToLowerInvariant();
            rule.Value = draft.Value!.Trim();
            rule.Action = draft.Action!.Trim().ToLowerInvariant();
            rule.Priority = draft.Priority;
            rule.Enabled = draft.Enabled;
            rule.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(rule));
        }).RequireAuthorization();

        app.MapDelete("/api/virtual-libraries/{libraryId}/rules", async (
            string libraryId, [Microsoft.AspNetCore.Mvc.FromBody] DeleteLibraryRuleRequest req, ClaimsPrincipal principal,
            UserManager<ApplicationUser> userManager, CuratarrDbContext db) =>
        {
            var denied = await AuthorizeAsync(libraryId, principal, userManager, db);
            if (denied is not null) return denied;

            var rule = await db.LibraryRules
                .FirstOrDefaultAsync(r => r.VirtualLibraryId == libraryId && r.Id == req.Id);
            if (rule is null) return Results.NotFound();

            db.LibraryRules.Remove(rule);
            await db.SaveChangesAsync();
            return Results.Ok(new { removed = 1 });
        }).RequireAuthorization();

        // Dry run: identical evaluation path to Apply, but no writes and no audit row.
        app.MapPost("/api/virtual-libraries/{libraryId}/rules/preview", async (
            string libraryId, [Microsoft.AspNetCore.Mvc.FromBody] LibraryRuleRunRequest req, ClaimsPrincipal principal,
            UserManager<ApplicationUser> userManager, CuratarrDbContext db) =>
        {
            var denied = await AuthorizeAsync(libraryId, principal, userManager, db);
            if (denied is not null) return denied;

            try
            {
                return Results.Ok(await RuleApplication.PreviewAsync(db, libraryId, req?.RuleIds));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).RequireAuthorization();

        // Apply: writes VirtualLibraryItem / HiddenItem rows plus one audit row.
        // Deliberately no reconcile / Plex call here (the user still presses Sync).
        app.MapPost("/api/virtual-libraries/{libraryId}/rules/apply", async (
            string libraryId, [Microsoft.AspNetCore.Mvc.FromBody] LibraryRuleRunRequest req, ClaimsPrincipal principal,
            UserManager<ApplicationUser> userManager, CuratarrDbContext db) =>
        {
            var denied = await AuthorizeAsync(libraryId, principal, userManager, db);
            if (denied is not null) return denied;

            try
            {
                return Results.Ok(await RuleApplication.ApplyAsync(db, libraryId, req?.RuleIds));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).RequireAuthorization();
    }

    private static async Task<IResult?> AuthorizeAsync(
        string libraryId, ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager, CuratarrDbContext db)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null) return Results.Unauthorized();

        if (!principal.IsInRole("Admin"))
        {
            var membership = await db.VirtualLibraryMemberships
                .FirstOrDefaultAsync(m => m.VirtualLibraryId == libraryId && m.UserId == user.Id);
            if (membership is null || membership.Role == MembershipRole.Viewer)
                return Results.Forbid();
        }

        if (!await db.VirtualLibraries.AnyAsync(l => l.Id == libraryId))
            return Results.NotFound();

        return null;
    }

    private static LibraryRuleDto ToDto(LibraryRule rule) => new(
        rule.Id, rule.Field, rule.Operator, rule.Value, rule.Action,
        rule.Priority, rule.Enabled, rule.CreatedAt, rule.UpdatedAt);
}

public sealed record LibraryRuleRequest(
    string Field, string Operator, string Value, string Action,
    int Priority = 0, bool Enabled = true, string? Id = null)
{
    public RuleDraft ToDraft() => new(Field, Operator, Value, Action, Priority, Enabled, Id);
}

public sealed record DeleteLibraryRuleRequest(string Id);

public sealed record LibraryRuleRunRequest(string[]? RuleIds = null);

public sealed record LibraryRuleDto(
    string Id, string Field, string Operator, string Value, string Action,
    int Priority, bool Enabled, DateTime CreatedAt, DateTime UpdatedAt);
