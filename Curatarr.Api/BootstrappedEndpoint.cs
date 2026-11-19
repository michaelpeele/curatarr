using Microsoft.EntityFrameworkCore;
using Curatarr.Engine.Data;

namespace Curatarr.Api;

/// <summary>
/// Lightweight service that checks whether the app has been bootstrapped
/// (i.e. has at least one user). If not, the frontend shows the first-boot
/// wizard instead of the login screen.
/// </summary>
public static class BootstrappedEndpoint
{
    public record BootstrappedResponse(bool IsBootstrapped);

    public static WebApplication MapBootstrappedEndpoint(this WebApplication app)
    {
        app.MapGet("/api/bootstrapped", async (CuratarrDbContext db) =>
        {
            var hasUsers = await db.Users.AnyAsync();
            return Results.Ok(new BootstrappedResponse(IsBootstrapped: hasUsers));
        }).AllowAnonymous();

        return app;
    }
}