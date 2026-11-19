using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Curatarr.Engine;
using Curatarr.Engine.Data;
using Curatarr.Api;
using Curatarr.Api.Hubs;
// Serilog also defines ILogger; keep existing DI-injected loggers unambiguous.
using ILogger = Microsoft.Extensions.Logging.ILogger;

var builder = WebApplication.CreateBuilder(args);

// Logs settings (Sprint 5 #2): console + rolling file + in-memory ring.
// Loaded before Serilog so the file sink can use the persisted size/retention.
var contentRoot = builder.Environment.ContentRootPath;
var logsConfig = new LogsConfigService(contentRoot);
if (logsConfig.LoadError is not null) LogStartup($"Warning: {logsConfig.LoadError}");
var currentLogConfig = logsConfig.Current;
var levelSwitch = new LoggingLevelSwitch(LoggingConfig.ToSerilogLevel(currentLogConfig.Level));
var logBuffer = new LogBufferSink();

const string logTemplate = "{Timestamp:yyyy-MM-ddTHH:mm:ss.fffzzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";
builder.Host.UseSerilog((ctx, cfg) => cfg
    .MinimumLevel.ControlledBy(levelSwitch)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: logTemplate)
    .WriteTo.File(
        Path.Combine(contentRoot, "logs", "curatarr-.log"),
        rollingInterval: RollingInterval.Day,
        fileSizeLimitBytes: (long)currentLogConfig.FileSizeLimitMB * 1024 * 1024,
        rollOnFileSizeLimit: true,
        retainedFileTimeLimit: TimeSpan.FromDays(currentLogConfig.RetentionDays),
        encoding: System.Text.Encoding.UTF8,
        outputTemplate: logTemplate)
    .WriteTo.Sink(logBuffer));

// Seed the LogTimeZone singleton used by the Settings → Server endpoint.
var bootZone = builder.Configuration["Schedules:TimeZone"];
LogTimeZone.Initialize(bootZone);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // lets request/response bodies use "Movie"/"Admin"/etc instead of
    // raw integers for MediaKind, MembershipRole, and any other enum
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var dbPath = Path.Combine(builder.Environment.ContentRootPath, "curatarr.db");
builder.Services.AddDbContext<CuratarrDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));

// Load the persisted schedule timezone from the data volume (written by the
// Settings → Server save endpoint) so it takes effect across container
// restarts without touching docker-compose.yml.
var scheduleConfigPath = Path.Combine(builder.Environment.ContentRootPath, "schedules-config.json");
if (File.Exists(scheduleConfigPath))
{
    try
    {
        var doc = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(scheduleConfigPath));
        if (doc.RootElement.TryGetProperty("Schedules", out var s) &&
            s.TryGetProperty("TimeZone", out var tz))
        {
            builder.Configuration["Schedules:TimeZone"] = tz.GetString() ?? "";
            LogStartup($"Schedules: TimeZone loaded from {scheduleConfigPath}");
        }
    }
    catch (Exception ex)
    {
        LogStartup($"Warning: failed to load {scheduleConfigPath} ({ex.Message}); using appsettings.json value.");
    }
}

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        // relaxed for a self-hosted family app; tighten if you want
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
    })
    .AddEntityFrameworkStores<CuratarrDbContext>()
    .AddDefaultTokenProviders();

// Persist Data Protection keys inside the mounted data volume. The default
// key-ring lives in the container filesystem, so every rebuild/recreate
// rotated it and silently invalidated all existing auth cookies -- which is
// what surfaced as "random" 401s after a deploy or long idle period.
var keysPath = Path.Combine(builder.Environment.ContentRootPath, "data-protection-keys");
Directory.CreateDirectory(keysPath);
builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
    .SetApplicationName("Curatarr");

// return 401/403 as plain status codes instead of redirecting to a login
// page -- this is an API, there's no login page (yet) to redirect to
builder.Services.ConfigureApplicationCookie(options =>
{
    // Login uses isPersistent: true, so the auth cookie survives browser
    // restarts; sliding expiration renews it on activity. The name is
    // configurable so a sandbox instance on the same host (cookies are
    // port-agnostic) doesn't clobber the production session.
    options.Cookie.Name = builder.Configuration["Cookie:Name"] ?? "curatarr.auth";
    options.Cookie.HttpOnly = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
});

builder.Services.AddAuthorization();

// TMDB is optional -- set it via `dotnet user-secrets set Tmdb:ApiKey "..."` or
// the Tmdb__ApiKey environment variable. Without a key, scanning still works,
// items just get their title/year from folder-name parsing only, no posters.
var tmdbApiKey = builder.Configuration["Tmdb:ApiKey"];
if (!string.IsNullOrWhiteSpace(tmdbApiKey))
{
    // masked diagnostic so a truncated/mis-copied key is visible at a
    // glance in the startup log, instead of only surfacing as mystery 401s
    var masked = tmdbApiKey.Length > 6 ? tmdbApiKey[..6] + new string('*', tmdbApiKey.Length - 6) : tmdbApiKey;
    LogStartup($"TMDB key configured: {masked} (length {tmdbApiKey.Length}, expected 32)");
}
else
{
    LogStartup("No TMDB key configured -- scanning will use folder-name parsing only, no posters.");
}
builder.Services.AddSingleton<IMetadataProvider>(_ =>
    string.IsNullOrWhiteSpace(tmdbApiKey)
        ? new FakeMetadataProvider(new Dictionary<string, MovieMetadata>(), new Dictionary<string, ShowMetadata>())
        : new TmdbMetadataProvider(tmdbApiKey));

builder.Services.AddScoped<LibraryScanner>();
builder.Services.AddScoped<SymlinkEngine>();
builder.Services.AddSingleton<ReconcileGate>();
builder.Services.AddSingleton<PlexService>();
builder.Services.AddSingleton<ScheduleTimeZone>();
builder.Services.AddSingleton<LogTimeZone>();
builder.Services.AddSignalR();
builder.Services.AddSingleton<ScanControlService>();

// Sprint 5 #1 — Activity audit feed. A bounded channel decouples UI actions
// from persistence so recording never stalls a request; the hosted service
// drains it in batches and broadcasts new rows on the Admin-only hub.
builder.Services.AddSingleton<ActivityRecorder>();
builder.Services.AddSingleton<IActivityRecorder>(sp => sp.GetRequiredService<ActivityRecorder>());
builder.Services.AddHostedService<ActivityIngestService>();

// Sprint 5 #2 — Logs: persisted config, live level switch, ring buffer,
// and the hourly archive/retention maintainer.
builder.Services.AddSingleton(logsConfig);
builder.Services.AddSingleton(levelSwitch);
builder.Services.AddSingleton(logBuffer);
builder.Services.AddHostedService<LogArchiveService>();

// Email: real SMTP when Smtp__Host is configured (Gmail app password or
// any standard relay -- see EmailSender.cs), otherwise the console
// fallback that logs links to `docker logs` for dev-mode testing.
var smtpHost = builder.Configuration["Smtp:Host"];
if (!string.IsNullOrWhiteSpace(smtpHost))
{
    builder.Services.AddSingleton<IAppEmailSender, SmtpEmailSender>();
    LogStartup($"Email: SMTP mode via {smtpHost}:{builder.Configuration["Smtp:Port"] ?? "587"} (from {builder.Configuration["Smtp:From"] ?? builder.Configuration["Smtp:User"]})");
}
else
{
    builder.Services.AddSingleton<IAppEmailSender, ConsoleEmailSender>();
    LogStartup("Email: console fallback (dev mode) -- Smtp__Host not set; links are logged, not sent.");
}

// Sprint 5 Bug #1 -- sandbox-only invite token sink for the opt-in
// accept-invite integration test. Never enabled in prod (no env var there),
// so no one-time tokens are retained in a normal deployment.
var inviteTokenSinkEnabled = string.Equals(builder.Configuration["Testing:InviteSink"], "1", StringComparison.Ordinal);
builder.Services.AddSingleton(new InviteTokenSink(inviteTokenSinkEnabled));
if (inviteTokenSinkEnabled) LogStartup("Testing: invite token sink enabled (sandbox only).");

// Where the frontend lives, for building clickable links in emails
// (invite / password reset). Configure via Frontend:BaseUrl if the
// frontend isn't on the default Vite dev port.
var frontendBaseUrl = builder.Configuration["Frontend:BaseUrl"] ?? "http://localhost:5173";

// Register the background genre backfill service
builder.Services.AddHostedService<DetailsBackfillService>();

// Parses calendar dates from raw TMDB payloads already cached by earlier
// detail backfills (no live TMDB calls).
builder.Services.AddHostedService<CalendarDateBackfillService>();

// Evaluates library schedules every minute and flips symlinks on window
// boundaries (Sprint 3).
builder.Services.AddHostedService<LibraryScheduleService>();

var app = builder.Build();

// Attach live log broadcasting after build (hub context is unavailable during
// early Serilog setup). REST polling in the Logs page remains a fallback.
var logBroadcaster = new LogBroadcaster(logBuffer, app.Services.GetRequiredService<IHubContext<LogsHub>>());
app.Lifetime.ApplicationStopping.Register(logBroadcaster.Dispose);

await SeedAsync(app.Services);

app.UseAuthentication();
app.UseAuthorization();

app.MapHub<ScanHub>("/hubs/scan");
app.MapHub<ActivityHub>("/hubs/activity");
app.MapHub<LogsHub>("/hubs/logs");

// Admin-only test hook, mapped only in the sandbox. test-api.sh reads the
// captured one-time token from here instead of scraping console logs.
if (inviteTokenSinkEnabled)
{
    app.MapGet("/api/test/invites", (InviteTokenSink sink) => Results.Ok(sink.Snapshot()))
       .RequireAuthorization(p => p.RequireRole("Admin"));
}

// =========================================================================
// Bootstrap wizard (anonymous — only usable when DB has no users)
// =========================================================================
app.MapBootstrappedEndpoint();
app.MapBootstrapWizardEndpoints();
app.MapLibraryScheduleEndpoints();
app.MapPlexEndpoints();
app.MapLibraryGridEndpoints();
app.MapCalendarEndpoints();     // Calendar from metadata (read-only slice)
app.MapLibraryRuleEndpoints(); // Sprint 6 #1: per-library automatic rules
app.MapActivityEndpoints(); // Sprint 5 #1: Activity feed + scan status
app.MapLogEndpoints();       // Sprint 5 #2: Logs ring + config

// =========================================================================
// Auth
// =========================================================================

// First registered user becomes global Admin (you); everyone else defaults
// to Viewer until an Admin grants VirtualLibraryMembership or a higher role.
app.MapPost("/api/auth/register", async (RegisterRequest req, UserManager<ApplicationUser> userManager) =>
{
    var user = new ApplicationUser { UserName = req.Email, Email = req.Email, DisplayName = req.DisplayName };
    var result = await userManager.CreateAsync(user, req.Password);
    if (!result.Succeeded) return Results.BadRequest(result.Errors);

    var isFirstUser = await userManager.Users.CountAsync() == 1;
    var role = isFirstUser ? "Admin" : "Viewer";
    await userManager.AddToRoleAsync(user, role);

    return Results.Ok(new { user.Id, user.Email, Role = role });
});

app.MapPost("/api/auth/login", async (LoginRequest req, SignInManager<ApplicationUser> signInManager, IActivityRecorder activity) =>
{
    var result = await signInManager.PasswordSignInAsync(req.Email, req.Password, isPersistent: true, lockoutOnFailure: true);
    if (!result.Succeeded) return Results.Unauthorized();
    activity.Record(ActivityCategory.User, "UserLoggedIn", req.Email, $"{req.Email} signed in");
    return Results.Ok(new { message = "Logged in" });
});

app.MapPost("/api/auth/logout", async (SignInManager<ApplicationUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.Ok();
}).RequireAuthorization();

// Any signed-in user: change their own password (requires the current one)
app.MapPost("/api/auth/change-password", async (
    ChangePasswordRequest req, ClaimsPrincipal principal, UserManager<ApplicationUser> userManager) =>
{
    var user = await userManager.GetUserAsync(principal);
    if (user is null) return Results.Unauthorized();
    var result = await userManager.ChangePasswordAsync(user, req.CurrentPassword, req.NewPassword);
    return result.Succeeded ? Results.Ok() : Results.BadRequest(result.Errors);
}).RequireAuthorization();

// Anyone: request a password-reset email. Always returns 200 regardless of
// whether the email is registered, so this can't be used to probe which
// emails have accounts. An SMTP failure is logged but still returns 200
// for the same reason (the caller can't act on the difference anyway).
app.MapPost("/api/auth/forgot-password", async (
    ForgotPasswordRequest req, UserManager<ApplicationUser> userManager, IAppEmailSender email,
    ILogger<Program> logger) =>
{
    var user = await userManager.FindByEmailAsync(req.Email);
    if (user is not null)
    {
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var link = $"{frontendBaseUrl}/reset-password?email={Uri.EscapeDataString(req.Email)}&token={Uri.EscapeDataString(token)}";
        try
        {
            await email.SendAsync(req.Email, "Reset your Curatarr password",
                $"Click the link below to set a new password. This link is single-use.\n\n{link}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ForgotPassword] Email send failed for {Email}", req.Email);
        }
    }
    return Results.Ok();
});

// Anyone with a valid token: actually reset the password
app.MapPost("/api/auth/reset-password", async (
    ResetPasswordRequest req, UserManager<ApplicationUser> userManager) =>
{
    var user = await userManager.FindByEmailAsync(req.Email);
    if (user is null) return Results.BadRequest(new[] { new { description = "Invalid or expired link." } });
    var result = await userManager.ResetPasswordAsync(user, req.Token, req.NewPassword);
    return result.Succeeded ? Results.Ok() : Results.BadRequest(result.Errors);
});

// Anyone with a valid invite token: set their password, display name, and
// optionally switch to a different email than the one they were invited
// with -- if they do, a SEPARATE confirmation email goes to the new
// address, and login stays on the original email until that's confirmed.
app.MapPost("/api/auth/accept-invite", async (
    AcceptInviteRequest req, UserManager<ApplicationUser> userManager, IAppEmailSender email) =>
{
    var user = await userManager.FindByIdAsync(req.UserId);
    if (user is null) return Results.BadRequest(new[] { new { description = "Invalid invite." } });

    var confirmResult = await userManager.ConfirmEmailAsync(user, req.Token);
    if (!confirmResult.Succeeded) return Results.BadRequest(confirmResult.Errors);

    user.DisplayName = req.DisplayName;
    var addPasswordResult = await userManager.AddPasswordAsync(user, req.Password);
    if (!addPasswordResult.Succeeded) return Results.BadRequest(addPasswordResult.Errors);

    await userManager.UpdateAsync(user);

    if (!string.IsNullOrWhiteSpace(req.AlternateEmail) &&
        !string.Equals(req.AlternateEmail, user.Email, StringComparison.OrdinalIgnoreCase))
    {
        var changeToken = await userManager.GenerateChangeEmailTokenAsync(user, req.AlternateEmail);
        var link = $"{frontendBaseUrl}/confirm-email-change?userId={user.Id}&newEmail={Uri.EscapeDataString(req.AlternateEmail)}&token={Uri.EscapeDataString(changeToken)}";
        await email.SendAsync(req.AlternateEmail, "Confirm your new Curatarr email",
            $"You asked to use this address for your Curatarr account instead. Confirm below -- your login stays as {user.Email} until you do.\n\n{link}");
        return Results.Ok(new
        {
            message = $"Account set up. Check {req.AlternateEmail} to confirm your new login email -- until then, sign in with {user.Email}.",
            email = user.Email
        });
    }

    return Results.Ok(new { message = "Account set up. You can sign in now.", email = user.Email });
});

// Anyone with a valid change-email token: finalize the email/login switch
app.MapPost("/api/auth/confirm-email-change", async (
    ConfirmEmailChangeRequest req, UserManager<ApplicationUser> userManager) =>
{
    var user = await userManager.FindByIdAsync(req.UserId);
    if (user is null) return Results.BadRequest(new[] { new { description = "Invalid link." } });

    var result = await userManager.ChangeEmailAsync(user, req.NewEmail, req.Token);
    if (!result.Succeeded) return Results.BadRequest(result.Errors);

    await userManager.SetUserNameAsync(user, req.NewEmail);
    return Results.Ok();
});

app.MapGet("/api/me", (ClaimsPrincipal user) =>
    Results.Ok(new
    {
        Email = user.Identity?.Name,
        Roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value)
    })
).RequireAuthorization();

// =========================================================================
// Virtual libraries + membership  (architecture doc section 6)
// =========================================================================

// Admin-only: list all libraries (needed to look up an id for granting membership)
app.MapGet("/api/virtual-libraries", async (CuratarrDbContext db, ScheduleTimeZone tz) =>
{
    var libraries = await db.VirtualLibraries.AsNoTracking().ToListAsync();
    var schedules = await db.LibrarySchedules.AsNoTracking().ToListAsync();
    var now = DateTimeOffset.UtcNow;

    return Results.Ok(libraries.Select(v =>
    {
        var rules = schedules.Where(s => s.VirtualLibraryId == v.Id).ToList();
        return new
        {
            v.Id,
            v.Name,
            v.MediaType,
            v.OutputPath,
            v.LastReconciledAt,
            scheduleCount = rules.Count,
            isActive = ScheduleEngine.IsActive(rules, now, tz.Zone),
            isAlwaysOn = ScheduleEngine.IsAlwaysOn(rules)
        };
    }));
}).RequireAuthorization(p => p.RequireRole("Admin"));

// Admin-only: list all users (needed to look up an id for granting membership).
// Global Identity roles ride along so the Users page can show/set them.
app.MapGet("/api/users", async (UserManager<ApplicationUser> userManager) =>
{
    var result = new List<object>();
    foreach (var u in userManager.Users.ToList())
    {
        var roles = await userManager.GetRolesAsync(u);
        result.Add(new { u.Id, u.Email, u.DisplayName, Roles = roles.ToArray() });
    }
    return Results.Ok(result);
}).RequireAuthorization(p => p.RequireRole("Admin"));

// Admin-only: replace a user's global Identity roles. Guards keep at least one
// Admin in the system and stop anyone (including the original admin) from
// dropping their own Admin role.
app.MapPut("/api/users/{id}/roles", async (
    string id, SetUserRolesRequest req, ClaimsPrincipal principal,
    UserManager<ApplicationUser> userManager, IActivityRecorder activity) =>
{
    var user = await userManager.FindByIdAsync(id);
    if (user is null) return Results.NotFound();

    var allowed = new[] { "Admin", "Viewer" };
    var raw = (req.Roles ?? Array.Empty<string>())
        .Select(r => (r ?? "").Trim())
        .Where(r => r.Length > 0)
        .ToArray();
    if (raw.Length == 0 || raw.Any(r => !allowed.Contains(r, StringComparer.OrdinalIgnoreCase)))
        return Results.BadRequest(new[] { new { description = "roles must contain Admin and/or Viewer" } });

    var roles = allowed.Where(a => raw.Any(r => r.Equals(a, StringComparison.OrdinalIgnoreCase))).ToList();
    var current = (await userManager.GetRolesAsync(user)).ToList();
    var wasAdmin = current.Contains("Admin", StringComparer.OrdinalIgnoreCase);
    var willBeAdmin = roles.Contains("Admin");

    var actingUser = await userManager.GetUserAsync(principal);
    if (actingUser?.Id == user.Id && wasAdmin && !willBeAdmin)
        return Results.BadRequest(new[] { new { description = "You can't remove your own Admin role." } });

    if (wasAdmin && !willBeAdmin)
    {
        var admins = await userManager.GetUsersInRoleAsync("Admin");
        if (admins.Count <= 1)
            return Results.BadRequest(new[] { new { description = "Can't remove the last remaining Admin." } });
    }

    var toRemove = current.Where(r => !roles.Contains(r, StringComparer.OrdinalIgnoreCase)).ToArray();
    var toAdd = roles.Where(r => !current.Contains(r, StringComparer.OrdinalIgnoreCase)).ToArray();
    if (toRemove.Length > 0)
    {
        var removeResult = await userManager.RemoveFromRolesAsync(user, toRemove);
        if (!removeResult.Succeeded) return Results.BadRequest(removeResult.Errors);
    }
    if (toAdd.Length > 0)
    {
        var addResult = await userManager.AddToRolesAsync(user, toAdd);
        if (!addResult.Succeeded) return Results.BadRequest(addResult.Errors);
    }

    activity.Record(ActivityCategory.User, "UserRolesChanged", ActivityRecorder.ActorFrom(principal),
        $"Roles for {user.Email} set to {string.Join(", ", roles)}");
    return Results.Ok(new { id = user.Id, roles });
}).RequireAuthorization(p => p.RequireRole("Admin"));

// Admin-only: rename a user's display name (email/login is left alone --
// changing that safely requires re-normalizing Identity's own fields, a
// separate, riskier operation not needed for "fix a typo")
// Admin-only: edit a user's display name and/or email/login. Email changes
// go through SetEmailAsync + SetUserNameAsync (keeping UserName in sync
// with Email, matching how registration sets them) rather than assigning
// the properties directly, since Identity also needs to update its
// internal Normalized* columns for lookups/uniqueness checks to keep working.
app.MapPut("/api/users/{id}", async (string id, UpdateUserRequest req, UserManager<ApplicationUser> userManager, IActivityRecorder activity) =>
{
    var user = await userManager.FindByIdAsync(id);
    if (user is null) return Results.NotFound();

    user.DisplayName = req.DisplayName;

    if (!string.Equals(user.Email, req.Email, StringComparison.OrdinalIgnoreCase))
    {
        var emailResult = await userManager.SetEmailAsync(user, req.Email);
        if (!emailResult.Succeeded) return Results.BadRequest(emailResult.Errors);

        var usernameResult = await userManager.SetUserNameAsync(user, req.Email);
        if (!usernameResult.Succeeded) return Results.BadRequest(usernameResult.Errors);
    }

    var updateResult = await userManager.UpdateAsync(user);
    if (!updateResult.Succeeded) return Results.BadRequest(updateResult.Errors);
    activity.Record(ActivityCategory.User, "UserUpdated", "System", $"Updated user {req.Email}");
    return Results.Ok(new { user.Id, user.Email, user.DisplayName });
}).RequireAuthorization(p => p.RequireRole("Admin"));

// Admin-only: set another user's password directly (no current password
// needed -- this is an admin override, distinct from /api/auth/change-password)
app.MapPut("/api/users/{id}/password", async (string id, AdminSetPasswordRequest req, UserManager<ApplicationUser> userManager, IActivityRecorder activity) =>
{
    var user = await userManager.FindByIdAsync(id);
    if (user is null) return Results.NotFound();

    if (await userManager.HasPasswordAsync(user))
    {
        var removeResult = await userManager.RemovePasswordAsync(user);
        if (!removeResult.Succeeded) return Results.BadRequest(removeResult.Errors);
    }
    var addResult = await userManager.AddPasswordAsync(user, req.NewPassword);
    if (!addResult.Succeeded) return Results.BadRequest(addResult.Errors);
    activity.Record(ActivityCategory.User, "UserPasswordChanged", "System", $"Password changed for user {user.Email}");
    return Results.Ok();
}).RequireAuthorization(p => p.RequireRole("Admin"));

// Admin-only: invite a user by email rather than setting their password
// directly. Creates the account with no usable password and EmailConfirmed
// = false, then emails a one-time link to /api/auth/accept-invite.
//
// Re-invite handling: a previously invited but never-accepted account
// (EmailConfirmed = false, no password) is an ORPHAN from the admin's
// perspective -- re-inviting should resend the link, not 400 on the
// duplicate row. Active accounts still get a friendly 400. If anything
// fails after CreateAsync, the just-created row is rolled back so it
// can't become a new orphan.
app.MapPost("/api/users/invite", async (
    InviteUserRequest req, UserManager<ApplicationUser> userManager, IAppEmailSender email,
    CuratarrDbContext db, ILogger<Program> logger, IActivityRecorder activity, InviteTokenSink inviteSink) =>
{
    var existing = await userManager.FindByEmailAsync(req.Email);
    if (existing is not null)
    {
        var isPendingInvite = !existing.EmailConfirmed && existing.PasswordHash is null;
        if (isPendingInvite)
        {
            // Resend a fresh one-time link for the existing pending row.
            var resendToken = await userManager.GenerateEmailConfirmationTokenAsync(existing);
            var resendLink = $"{frontendBaseUrl}/accept-invite?userId={existing.Id}&token={Uri.EscapeDataString(resendToken)}";
            await email.SendAsync(req.Email, "You're invited to Curatarr",
                $"Click below to set up your account.\n\n{resendLink}");
            inviteSink.Capture(req.Email, existing.Id, resendToken, resendLink);
            logger.LogInformation("[Invite] Re-sent pending invite to {Email} (userId {UserId})",
                req.Email, existing.Id);
            return Results.Ok(new { existing.Id, existing.Email, Message = $"Invite re-sent to {req.Email}." });
        }

        logger.LogInformation("[Invite] Rejected invite for {Email}: account already exists (active)", req.Email);
        return Results.BadRequest(new[]
        {
            new { description = $"An account with {req.Email} already exists. Use password reset instead." }
        });
    }

    // No tracked entities expected here, but Identity shares this DbContext
    // with app code -- clear defensively so CreateAsync can't trip over
    // stale change-tracker state from earlier in the request scope.
    db.ChangeTracker.Clear();

    var user = new ApplicationUser { UserName = req.Email, Email = req.Email, EmailConfirmed = false };
    var createResult = await userManager.CreateAsync(user); // no password -- set during accept-invite
    if (!createResult.Succeeded)
    {
        logger.LogWarning("[Invite] Failed to create user for {Email}: {Errors}",
            req.Email, string.Join("; ", createResult.Errors.Select(e => $"{e.Code}: {e.Description}")));
        return Results.BadRequest(createResult.Errors);
    }

    // Roll the new row back if role assignment or the email send fails,
    // so a failed invite never leaves an orphaned Identity row behind.
    var roleResult = await userManager.AddToRoleAsync(user, "Viewer");
    if (!roleResult.Succeeded)
    {
        logger.LogError("[Invite] AddToRoleAsync(Viewer) failed for {Email}: {Errors}; rolling back user {UserId}",
            req.Email, string.Join("; ", roleResult.Errors.Select(e => e.Description)), user.Id);
        await userManager.DeleteAsync(user);
        db.ChangeTracker.Clear();
        return Results.BadRequest(roleResult.Errors);
    }

    try
    {
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var link = $"{frontendBaseUrl}/accept-invite?userId={user.Id}&token={Uri.EscapeDataString(token)}";
        await email.SendAsync(req.Email, "You're invited to Curatarr",
            $"Click below to set up your account.\n\n{link}");
        inviteSink.Capture(req.Email, user.Id, token, link);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[Invite] Email send failed for {Email}; rolling back user {UserId}",
            req.Email, user.Id);
        await userManager.DeleteAsync(user);
        db.ChangeTracker.Clear();
        return Results.BadRequest(new[] { new { description = "Invite email failed to send. Please try again." } });
    }

    logger.LogInformation("[Invite] Invited {Email} (userId {UserId})", req.Email, user.Id);
    activity.Record(ActivityCategory.User, "UserInvited", "System", $"Invited {req.Email}");
    return Results.Ok(new { user.Id, user.Email, Message = $"Invite sent to {req.Email}." });
}).RequireAuthorization(p => p.RequireRole("Admin"));

// Admin-only: delete a user account entirely. Guards against deleting your
// own account (avoid accidental self-lockout) and against deleting the
// last remaining Admin (avoid locking everyone out of admin functions).
app.MapDelete("/api/users/{id}", async (
    string id, ClaimsPrincipal principal, UserManager<ApplicationUser> userManager, CuratarrDbContext db,
    IActivityRecorder activity) =>
{
    var user = await userManager.FindByIdAsync(id);
    if (user is null) return Results.NotFound();

    var actingUser = await userManager.GetUserAsync(principal);
    if (actingUser?.Id == user.Id)
        return Results.BadRequest(new[] { new { description = "You can't delete your own account here." } });

    if (await userManager.IsInRoleAsync(user, "Admin"))
    {
        var admins = await userManager.GetUsersInRoleAsync("Admin");
        if (admins.Count <= 1)
            return Results.BadRequest(new[] { new { description = "Can't delete the last remaining Admin." } });
    }

    // remove memberships first -- SQLite enforces the foreign key
    var memberships = await db.VirtualLibraryMemberships.Where(m => m.UserId == id).ToListAsync();
    db.VirtualLibraryMemberships.RemoveRange(memberships);
    await db.SaveChangesAsync();

    var result = await userManager.DeleteAsync(user);
    if (!result.Succeeded) return Results.BadRequest(result.Errors);
    activity.Record(ActivityCategory.User, "UserDeleted", ActivityRecorder.ActorFrom(principal), $"Deleted user {user.Email}");
    return Results.Ok();
}).RequireAuthorization(p => p.RequireRole("Admin"));

// Admin-only: list subdirectories of a path ON THE API SERVER -- for a
// server-side folder browser/autocomplete when registering source roots
// or library output paths (not the browser's own machine, which has no
// access to the actual media storage). Once containerized, this browses
// whatever the container has mounted, same as Sonarr/Radarr's own
// "manual add" folder picker does inside their containers.
app.MapGet("/api/filesystem/browse", (string? path) =>
{
    var target = string.IsNullOrWhiteSpace(path) ? "/" : path;
    try
    {
        var dir = new DirectoryInfo(target);
        if (!dir.Exists) return Results.NotFound(new { message = $"Path does not exist: {target}" });

        var entries = dir.GetDirectories()
            .Where(d => (d.Attributes & FileAttributes.Hidden) == 0 && !d.Name.StartsWith('.'))
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(d => new { name = d.Name, path = d.FullName })
            .ToList();

        return Results.Ok(new { path = dir.FullName, parent = dir.Parent?.FullName, entries });
    }
    catch (UnauthorizedAccessException)
    {
        return Results.BadRequest(new { message = "Permission denied for this path." });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
}).RequireAuthorization(p => p.RequireRole("Admin"));

// Admin-only: path mappings for translating symlink targets between
// Curatarr's own filesystem view and Plex's (e.g. Docker's /mnt/data vs
// the Plex host's /Volumes/data for the same physical storage)
app.MapGet("/api/path-mappings", async (CuratarrDbContext db) =>
    Results.Ok(await db.PathMappings.ToListAsync())
).RequireAuthorization(p => p.RequireRole("Admin"));

app.MapPost("/api/path-mappings", async (CreatePathMappingRequest req, CuratarrDbContext db, IActivityRecorder activity) =>
{
    var mapping = new PathMapping { Id = Guid.NewGuid().ToString(), FromPrefix = req.FromPrefix, ToPrefix = req.ToPrefix };
    db.PathMappings.Add(mapping);
    await db.SaveChangesAsync();
    activity.Record(ActivityCategory.Path, "PathMappingAdded", "System", $"Added path mapping {req.FromPrefix} -> {req.ToPrefix}");
    return Results.Ok(mapping);
}).RequireAuthorization(p => p.RequireRole("Admin"));

app.MapDelete("/api/path-mappings/{id}", async (string id, CuratarrDbContext db, IActivityRecorder activity) =>
{
    var mapping = await db.PathMappings.FindAsync(id);
    if (mapping is null) return Results.NotFound();
    db.PathMappings.Remove(mapping);
    await db.SaveChangesAsync();
    activity.Record(ActivityCategory.Path, "PathMappingRemoved", "System", $"Removed path mapping {id}");
    return Results.Ok();
}).RequireAuthorization(p => p.RequireRole("Admin"));

// Admin-only: list registered source roots
app.MapGet("/api/source-roots", async (CuratarrDbContext db) =>
    Results.Ok(await db.SourceRoots
        .Select(r => new { r.Id, r.Name, r.Path, r.MediaType })
        .ToListAsync())
).RequireAuthorization(p => p.RequireRole("Admin"));

// Any signed-in user: browse the full scanned catalog (needed so a Curator
// can see what's available to approve into their library -- approval
// itself is still gated per-library by membership, see /approve below).
//
// Optional ?libraryId= annotates each item with that library's local state:
// approved (and ApprovedAt, for the green "synced since marked" check) and
// hidden. Without it the flags default to false/null, so existing callers
// that just want the catalog are unaffected.
app.MapGet("/api/source-items", async (string? libraryId, CuratarrDbContext db) =>
{
    var items = await db.SourceItems
        .Select(i => new
        {
            i.Id, i.MediaType, i.Title, i.Year,
            i.ShowTitle, i.SeasonNumber, i.EpisodeNumber,
            i.TmdbId, i.PosterUrl, i.Overview, i.RelativePath,
            i.GenreIds, i.Rating, i.ProductionCompanies, i.Networks,
            i.Director, i.Creators, i.Cast
        })
        .ToListAsync();

    if (string.IsNullOrWhiteSpace(libraryId))
    {
        return Results.Ok(items.Select(i => new
        {
            i.Id, i.MediaType, i.Title, i.Year,
            i.ShowTitle, i.SeasonNumber, i.EpisodeNumber,
            i.TmdbId, i.PosterUrl, i.Overview, i.RelativePath,
            i.GenreIds, i.Rating, i.ProductionCompanies, i.Networks,
            i.Director, i.Creators, i.Cast,
            Approved = false,
            ApprovedAt = (DateTime?)null,
            Hidden = false,
            HiddenAt = (DateTime?)null
        }));
    }

    var approvals = await db.VirtualLibraryItems
        .Where(v => v.VirtualLibraryId == libraryId)
        .Select(v => new { v.SourceItemId, v.ApprovedAt })
        .ToListAsync();
    var approvedAtByItem = approvals.ToDictionary(a => a.SourceItemId, a => a.ApprovedAt);
    // Hides are two-phase: the row is a request until Sync applies it. A card
    // is only absent from All/Library once a reconcile has completed after the
    // hide; an unhide request keeps it in Hidden until the next reconcile.
    // HiddenAt carries the timestamp of the current request (hide or unhide)
    // so the eye can show yellow until that Sync, then green/gray.
    var lastReconciledAt = await db.VirtualLibraries
        .Where(l => l.Id == libraryId)
        .Select(l => (DateTime?)l.LastReconciledAt)
        .FirstOrDefaultAsync();
    var hiddenRows = await db.HiddenItems
        .Where(h => h.VirtualLibraryId == libraryId)
        .Select(h => new { h.SourceItemId, h.CreatedAt, h.UnhideRequestedAt })
        .ToListAsync();
    var hiddenAtByItem = hiddenRows.ToDictionary(
        h => h.SourceItemId,
        h => HiddenItemState.StateStamp(h.CreatedAt, h.UnhideRequestedAt));
    var hiddenIds = hiddenRows
        .Where(h => HiddenItemState.IsEffectiveHidden(h.CreatedAt, h.UnhideRequestedAt, lastReconciledAt))
        .Select(h => h.SourceItemId)
        .ToHashSet();

    return Results.Ok(items.Select(i => new
    {
        i.Id, i.MediaType, i.Title, i.Year,
        i.ShowTitle, i.SeasonNumber, i.EpisodeNumber,
        i.TmdbId, i.PosterUrl, i.Overview, i.RelativePath,
        i.GenreIds, i.Rating, i.ProductionCompanies, i.Networks,
        i.Director, i.Creators, i.Cast,
        Approved = approvedAtByItem.ContainsKey(i.Id),
        ApprovedAt = approvedAtByItem.TryGetValue(i.Id, out var approvedAt)
            ? approvedAt
            : (DateTime?)null,
        Hidden = hiddenIds.Contains(i.Id),
        HiddenAt = hiddenAtByItem.TryGetValue(i.Id, out var hiddenAt)
            ? hiddenAt
            : (DateTime?)null
    }));
}).RequireAuthorization();

// Admin-only: backfill catalog filter details (genre ids, US rating,
// studios/networks, director/creators, top-30 cast) for items missing them.
// Fetches in batches to avoid overwhelming TMDB API. `pending` lets callers
// show remaining work.
app.MapPost("/api/backfill-genres", async (CuratarrDbContext db, IMetadataProvider metadata, ILogger<Program> logger) =>
{
    var pending = await DetailsBackfill.Pending(db).CountAsync();
    if (pending == 0)
        return Results.Ok(new { processed = 0, updated = 0, failed = 0, pending = 0, message = "No items need details backfill" });

    var (processed, updated, failed) = await DetailsBackfill.RunBatchAsync(
        db, metadata, logger, DetailsBackfill.EndpointBatchSize);

    pending = await DetailsBackfill.Pending(db).CountAsync();
    return Results.Ok(new { processed, updated, failed, pending });
}).RequireAuthorization(p => p.RequireRole("Admin"));

// Any signed-in user: fetch rich details (synopsis, director/creators, cast)
// for one item, on demand -- not during the bulk scan, since that would
// double TMDB call volume for large libraries. "id" is a query param, not
// a path segment, because SourceItem ids contain "/" (rootId:relativePath).
app.MapGet("/api/source-item-details", async (string id, CuratarrDbContext db, IMetadataProvider metadata) =>
{
    var item = await db.SourceItems.FindAsync(id);
    if (item is null) return Results.NotFound();
    if (item.TmdbId is null) return Results.Ok(new { available = false });

    var details = item.MediaType == MediaKind.Movie
        ? await metadata.GetMovieDetailsAsync(item.TmdbId.Value)
        : await metadata.GetShowDetailsAsync(item.TmdbId.Value);

    if (details is null) return Results.Ok(new { available = false });

    return Results.Ok(new
    {
        available = true,
        details.Title,
        details.Overview,
        details.PosterUrl,
        details.Director,
        details.Creators,
        details.Cast
    });
}).RequireAuthorization();

// Admin-only: TMDB candidate search for the Match modal. A no-key (Fake)
// provider reports available=false so the UI can explain why search is off.
app.MapGet("/api/metadata/search", async (
    string query, int? year, string? mediaType, IMetadataProvider metadata, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(query)) return Results.BadRequest(new { error = "query is required" });

    string? wanted = null;
    if (!string.IsNullOrWhiteSpace(mediaType))
    {
        var text = mediaType.Trim();
        if (text.Equals("Movie", StringComparison.OrdinalIgnoreCase)) wanted = "Movie";
        else if (text.Equals("Show", StringComparison.OrdinalIgnoreCase) ||
                 text.Equals("Anime", StringComparison.OrdinalIgnoreCase)) wanted = "Show";
        else return Results.BadRequest(new { error = "mediaType must be Movie, Show or Anime" });
    }

    if (metadata is not TmdbMetadataProvider)
        return Results.Ok(new { available = false, results = Array.Empty<TitleSearchResult>() });

    var results = await metadata.SearchTitlesAsync(query.Trim(), year, ct);
    if (wanted is not null) results = results.Where(r => r.MediaType == wanted).ToList();
    return Results.Ok(new { available = true, results });
}).RequireAuthorization(p => p.RequireRole("Admin"));

// Admin-only: apply one TMDB candidate to a row, or to every episode of a show.
// Clearing only the backfill columns lets DetailsBackfill re-run while the
// applied match (id/poster/overview/title) survives until the next manual match.
app.MapPost("/api/metadata/match", async (
    MatchMetadataRequest req, CuratarrDbContext db, IActivityRecorder activity, ClaimsPrincipal principal) =>
{
    if (req.TmdbId <= 0) return Results.BadRequest(new { error = "tmdbId must be positive" });
    if (string.IsNullOrWhiteSpace(req.MediaType) ||
        !Enum.TryParse<MediaKind>(req.MediaType.Trim(), true, out _))
        return Results.BadRequest(new { error = "mediaType must be Movie, Show or Anime" });
    if (string.IsNullOrWhiteSpace(req.SourceItemId) && string.IsNullOrWhiteSpace(req.RepresentativeId))
        return Results.BadRequest(new { error = "sourceItemId or representativeId is required" });

    List<SourceItem> targets;
    if (!string.IsNullOrWhiteSpace(req.SourceItemId))
    {
        var single = await db.SourceItems.FindAsync(req.SourceItemId);
        targets = single is null ? new List<SourceItem>() : new List<SourceItem> { single };
    }
    else
    {
        var rep = await db.SourceItems.FindAsync(req.RepresentativeId);
        if (rep is null) return Results.NotFound();
        if (string.IsNullOrWhiteSpace(rep.ShowTitle))
        {
            // Without a shared show title there is no safe group key -- match
            // just the representative row rather than every untitled episode.
            targets = new List<SourceItem> { rep };
        }
        else
        {
            targets = await db.SourceItems
                .Where(i => i.SourceRootId == rep.SourceRootId &&
                            i.MediaType == rep.MediaType &&
                            i.ShowTitle == rep.ShowTitle)
                .ToListAsync();
        }
    }
    if (targets.Count == 0) return Results.NotFound();

    var matchTitle = req.Title?.Trim();
    if (string.IsNullOrEmpty(matchTitle)) return Results.BadRequest(new { error = "title is required" });

    foreach (var item in targets)
    {
        item.TmdbId = req.TmdbId;
        item.PosterUrl = req.PosterUrl;
        item.Overview = req.Overview;
        item.Title = matchTitle;
        item.ShowTitle = matchTitle;
        item.Year = req.Year;
        item.GenreIds = null;
        item.Rating = null;
        item.ProductionCompanies = null;
        item.Networks = null;
        item.Director = null;
        item.Creators = null;
        item.Cast = null;
        item.ManuallyMatched = true;
    }
    await db.SaveChangesAsync();

    activity.Record(ActivityCategory.Source, "MetadataMatched", ActivityRecorder.ActorFrom(principal),
        $"Matched {targets.Count} row(s) to '{matchTitle}' (TMDB {req.TmdbId})");
    return Results.Ok(new { affected = targets.Count });
}).RequireAuthorization(p => p.RequireRole("Admin"));

// Admin-only: walk every registered SourceRoot and upsert SourceItems.
// Safe to call repeatedly -- matches by the scanner's stable id
// (rootId:relativePath), so re-scanning updates existing rows (in case
// metadata changed) rather than duplicating them.
app.MapPost("/api/scan", async (CuratarrDbContext db, LibraryScanner scanner, IHubContext<ScanHub> hub, ScanControlService scanControl, IActivityRecorder activity, ScanRequest? req) =>
{
    // Optional library scope: scan only roots whose MediaType matches the
    // selected library's type. No body / no id keeps the legacy all-roots scan.
    var allRoots = await db.SourceRoots.ToListAsync();
    string scopeDetail = "all source roots";
    MediaKind? scopeType = null;
    if (!string.IsNullOrWhiteSpace(req?.LibraryId))
    {
        var library = await db.VirtualLibraries.FindAsync(req.LibraryId);
        if (library is null) return Results.NotFound(new { error = "Library not found" });
        scopeType = library.MediaType;
        scopeDetail = $"{library.MediaType} roots for library '{library.Name}'";
    }
    var roots = ScanScope.FilterRoots(allRoots, scopeType);

    var token = scanControl.BeginNewScan();
    activity.Record(ActivityCategory.Scan, "ScanStarted", "System", $"Library scan started ({scopeDetail})");
    int scannedCount = 0, createdCount = 0, updatedCount = 0;
    try
    {
        var scanned = await scanner.ScanAsync(
            roots,
            onProgress: async evt =>
            {
                scanControl.ReportProgress(evt.Completed, evt.Total);
                await hub.Clients.All.SendAsync("ScanProgress", evt);
            },
            waitIfPaused: scanControl.WaitWhilePausedAsync,
            ct: token);

        int created = 0, updated = 0;
        foreach (var item in scanned)
        {
            var existing = await db.SourceItems.FindAsync(item.Id);
            if (existing is null)
            {
                db.SourceItems.Add(item);
                created++;
            }
            else
            {
                // Non-metadata fields are structural: a re-scan always refreshes
                // them, even for a manually matched row.
                existing.SeasonNumber = item.SeasonNumber;
                existing.EpisodeNumber = item.EpisodeNumber;
                existing.MediaType = item.MediaType;
                existing.RelativePath = item.RelativePath;

                // A manual match wins over TMDB results on every re-scan.
                if (!existing.ManuallyMatched)
                {
                    existing.Title = item.Title;
                    existing.Year = item.Year;
                    existing.ShowTitle = item.ShowTitle;
                    existing.TmdbId = item.TmdbId;
                    existing.PosterUrl = item.PosterUrl;
                    existing.Overview = item.Overview;
                }
                updated++;
            }
        }
        scannedCount = scanned.Count;
        createdCount = created;
        updatedCount = updated;
        await db.SaveChangesAsync();
        return Results.Ok(new { scanned = scanned.Count, created, updated, stopped = token.IsCancellationRequested });
    }
    finally
    {
        scanControl.Finish();
        activity.Record(ActivityCategory.Scan, "ScanFinished", "System",
            $"Scan finished ({scopeDetail}): {scannedCount} scanned, {createdCount} created, {updatedCount} updated");
    }
}).RequireAuthorization(p => p.RequireRole("Admin"));

// Admin-only: pause the currently running scan. Items already in flight
// finish normally; no new items start until Resume is called.
app.MapPost("/api/scan/pause", (ScanControlService scanControl, IActivityRecorder activity) =>
{
    scanControl.Pause();
    activity.Record(ActivityCategory.Scan, "ScanPaused", "System", "Scan paused");
    return Results.Ok();
}).RequireAuthorization(p => p.RequireRole("Admin"));

app.MapPost("/api/scan/resume", (ScanControlService scanControl, IActivityRecorder activity) =>
{
    scanControl.Resume();
    activity.Record(ActivityCategory.Scan, "ScanResumed", "System", "Scan resumed");
    return Results.Ok();
}).RequireAuthorization(p => p.RequireRole("Admin"));

// Admin-only: stop the scan. Whatever items already finished are kept
// and upserted -- nothing scanned so far is thrown away.
app.MapPost("/api/scan/stop", (ScanControlService scanControl, IActivityRecorder activity) =>
{
    scanControl.Stop();
    activity.Record(ActivityCategory.Scan, "ScanStopped", "System", "Scan stopped");
    return Results.Ok();
}).RequireAuthorization(p => p.RequireRole("Admin"));

// Admin-only: run the symlink reconciliation engine against current approvals.
// Schedule-aware: an inactive library's links are pruned rather than rebuilt,
// so a manual Sync can't bypass an availability window.
app.MapPost("/api/reconcile", async (SymlinkEngine engine, CuratarrDbContext db, ScheduleTimeZone tz, PlexService plex, IActivityRecorder activity) =>
{
    var schedules = await db.LibrarySchedules.AsNoTracking().ToListAsync();
    var now = DateTimeOffset.UtcNow;
    var report = await engine.ReconcileAsync(library =>
        ScheduleEngine.IsActive(
            schedules.Where(s => s.VirtualLibraryId == library.Id).ToList(), now, tz.Zone));

    // Additions are scanned folder-by-folder (instant, like Sonarr/Radarr), and
    // removals force a full section scan because Plex ignores a path that no
    // longer exists -- otherwise deleted titles linger until a manual refresh.
    static List<string> Folders(IEnumerable<string> paths) => paths
        .Select(Path.GetDirectoryName)
        .Where(folder => !string.IsNullOrWhiteSpace(folder))
        .Select(folder => folder!)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    // Fire-and-forget: a full reconcile can touch every library.
    plex.TriggerInBackground(null, Folders(report.Created), Folders(report.Removed));
    activity.Record(ActivityCategory.Path, "SyncCompleted", "System",
        $"Sync completed: {report.Created.Count} created, {report.Removed.Count} removed, {report.Broken.Count} broken");
    return Results.Ok(report);
}).RequireAuthorization(p => p.RequireRole("Admin"));

// Admin-only: register a source root manually (stopgap until the scanner
// is wired into the API -- see below for the matching SourceItem endpoint)
app.MapPost("/api/source-roots", async (CreateSourceRootRequest req, CuratarrDbContext db, IActivityRecorder activity) =>
{
    var name = (req.Name ?? "").Trim();
    var path = (req.Path ?? "").Trim();
    var pathKey = NormalizePathKey(path);
    if (name.Length == 0) return Results.BadRequest(new { error = "Source name is required." });
    if (pathKey.Length == 0) return Results.BadRequest(new { error = "Source path is required." });

    var existing = await db.SourceRoots.AsNoTracking().ToListAsync();
    if (existing.Any(r => string.Equals((r.Name ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase)))
        return Results.Conflict(new { error = "A source with that name already exists — choose a different name." });
    if (existing.Any(r => string.Equals(NormalizePathKey(r.Path), pathKey, StringComparison.OrdinalIgnoreCase)))
        return Results.Conflict(new { error = "A source with that path already exists." });

    var root = new SourceRoot { Id = Guid.NewGuid().ToString(), Name = name, Path = path, MediaType = req.MediaType };
    db.SourceRoots.Add(root);
    await db.SaveChangesAsync();
    activity.Record(ActivityCategory.Source, "SourceRootAdded", "System", $"Added source root {name}");
    return Results.Ok(root);
}).RequireAuthorization(p => p.RequireRole("Admin"));

// Admin-only: rename/edit a source root
app.MapPut("/api/source-roots/{id}", async (string id, CreateSourceRootRequest req, CuratarrDbContext db, IActivityRecorder activity) =>
{
    var root = await db.SourceRoots.FindAsync(id);
    if (root is null) return Results.NotFound();

    var name = (req.Name ?? "").Trim();
    var path = (req.Path ?? "").Trim();
    var pathKey = NormalizePathKey(path);
    if (name.Length == 0) return Results.BadRequest(new { error = "Source name is required." });
    if (pathKey.Length == 0) return Results.BadRequest(new { error = "Source path is required." });

    var others = await db.SourceRoots.AsNoTracking().Where(r => r.Id != id).ToListAsync();
    if (others.Any(r => string.Equals((r.Name ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase)))
        return Results.Conflict(new { error = "A source with that name already exists — choose a different name." });
    if (others.Any(r => string.Equals(NormalizePathKey(r.Path), pathKey, StringComparison.OrdinalIgnoreCase)))
        return Results.Conflict(new { error = "A source with that path already exists." });

    root.Name = name;
    root.Path = path;
    root.MediaType = req.MediaType;
    await db.SaveChangesAsync();
    activity.Record(ActivityCategory.Source, "SourceRootUpdated", "System", $"Updated source root {name}");
    return Results.Ok(root);
}).RequireAuthorization(p => p.RequireRole("Admin"));

// Admin-only: unregister a source root. Does NOT delete the already-scanned
// SourceItems that came from it (those just become orphaned from future
// scans) or touch any files on disk -- purely stops it being scanned again.
app.MapDelete("/api/source-roots/{id}", async (string id, CuratarrDbContext db, IActivityRecorder activity) =>
{
    var root = await db.SourceRoots.FindAsync(id);
    if (root is null) return Results.NotFound();
    db.SourceRoots.Remove(root);
    await db.SaveChangesAsync();
    activity.Record(ActivityCategory.Source, "SourceRootRemoved", "System", $"Removed source root {root.Name}");
    return Results.Ok();
}).RequireAuthorization(p => p.RequireRole("Admin"));

// Admin-only: delete a virtual library entirely (its approvals go with it).
// Does not touch existing symlinks on disk -- run Sync after deleting to
// actually remove them via the normal reconcile "orphan" cleanup path.
app.MapDelete("/api/virtual-libraries/{id}", async (string id, CuratarrDbContext db) =>
{
    var library = await db.VirtualLibraries.FindAsync(id);
    if (library is null) return Results.NotFound();

    var items = await db.VirtualLibraryItems.Where(v => v.VirtualLibraryId == id).ToListAsync();
    var hiddenItems = await db.HiddenItems.Where(h => h.VirtualLibraryId == id).ToListAsync();
    var memberships = await db.VirtualLibraryMemberships.Where(m => m.VirtualLibraryId == id).ToListAsync();
    var schedules = await db.LibrarySchedules.Where(s => s.VirtualLibraryId == id).ToListAsync();
    db.VirtualLibraryItems.RemoveRange(items);
    db.HiddenItems.RemoveRange(hiddenItems);
    db.VirtualLibraryMemberships.RemoveRange(memberships);
    db.LibrarySchedules.RemoveRange(schedules);
    db.VirtualLibraries.Remove(library);
    await db.SaveChangesAsync();
    return Results.Ok();
}).RequireAuthorization(p => p.RequireRole("Admin"));

// Admin-only: revoke a user's membership on a library (both ids are plain
// GUIDs, safe as path segments -- unlike SourceItem ids, which aren't)
app.MapDelete("/api/virtual-libraries/{libraryId}/members/{userId}", async (string libraryId, string userId, CuratarrDbContext db, IActivityRecorder activity) =>
{
    var membership = await db.VirtualLibraryMemberships
        .FirstOrDefaultAsync(m => m.VirtualLibraryId == libraryId && m.UserId == userId);
    if (membership is null) return Results.NotFound();
    db.VirtualLibraryMemberships.Remove(membership);
    await db.SaveChangesAsync();
    activity.Record(ActivityCategory.User, "LibraryMembershipChanged", "System",
        $"Removed user {userId} from library {libraryId}");
    return Results.Ok();
}).RequireAuthorization(p => p.RequireRole("Admin"));

// Admin-only: list a library's current members (for the Users management page)
app.MapGet("/api/virtual-libraries/{libraryId}/members", async (string libraryId, CuratarrDbContext db) =>
    Results.Ok(await db.VirtualLibraryMemberships
        .Where(m => m.VirtualLibraryId == libraryId)
        .Include(m => m.User)
        .Select(m => new { m.UserId, Email = m.User.Email, m.Role })
        .ToListAsync())
).RequireAuthorization(p => p.RequireRole("Admin"));

// Admin-only: register a source item manually (real usage will be the
// LibraryScanner populating these; this exists so approve/reconcile can
// be tested against a real, valid SourceItemId in the meantime)
app.MapPost("/api/source-items", async (CreateSourceItemRequest req, CuratarrDbContext db) =>
{
    var item = new SourceItem
    {
        Id = Guid.NewGuid().ToString(),
        SourceRootId = req.SourceRootId,
        RelativePath = req.RelativePath,
        MediaType = req.MediaType,
        Title = req.Title
    };
    db.SourceItems.Add(item);
    await db.SaveChangesAsync();
    // return a plain DTO, not the entity -- SourceItem.SourcePath is computed
    // from the SourceRoot navigation, which isn't loaded here, so serializing
    // the raw entity throws a NullReferenceException
    return Results.Ok(new { item.Id, item.SourceRootId, item.RelativePath, item.MediaType, item.Title });
}).RequireAuthorization(p => p.RequireRole("Admin"));

// Admin-only: create a new curated library (e.g. "Kids Movies")
app.MapPost("/api/virtual-libraries", async (CreateVirtualLibraryRequest req, CuratarrDbContext db) =>
{
    var name = (req.Name ?? "").Trim();
    var outputPath = (req.OutputPath ?? "").Trim();
    var outputKey = NormalizePathKey(outputPath);
    if (name.Length == 0) return Results.BadRequest(new { error = "Library name is required." });
    if (outputKey.Length == 0) return Results.BadRequest(new { error = "Library output path is required." });

    var outputs = await db.VirtualLibraries.AsNoTracking().Select(l => l.OutputPath).ToListAsync();
    if (outputs.Any(p => string.Equals(NormalizePathKey(p), outputKey, StringComparison.OrdinalIgnoreCase)))
        return Results.Conflict(new { error = "Another library already uses that output path." });

    var vl = new VirtualLibrary
    {
        Id = Guid.NewGuid().ToString(),
        Name = name,
        MediaType = req.MediaType,
        OutputPath = outputPath
    };
    db.VirtualLibraries.Add(vl);
    await db.SaveChangesAsync();
    return Results.Ok(vl);
}).RequireAuthorization(p => p.RequireRole("Admin"));

// Admin-only: rename/edit a library. If the OutputPath changed, the on-disk
// symlink tree under the old path is relocated to the new one via
// ReconcileAsync (idempotent no-op when the path didn't actually change).
// The frontend gates this with a confirm dialog before the request and an
// alert after it advising that Plex must be pointed at the new location.
app.MapPut("/api/virtual-libraries/{id}", async (string id, CreateVirtualLibraryRequest req, CuratarrDbContext db, SymlinkEngine symlinkEngine, ScheduleTimeZone tz, PlexService plex, IActivityRecorder activity) =>
{
    var lib = await db.VirtualLibraries.FindAsync(id);
    if (lib is null) return Results.NotFound();

    var name = (req.Name ?? "").Trim();
    var outputPath = (req.OutputPath ?? "").Trim();
    var outputKey = NormalizePathKey(outputPath);
    if (name.Length == 0) return Results.BadRequest(new { error = "Library name is required." });
    if (outputKey.Length == 0) return Results.BadRequest(new { error = "Library output path is required." });

    var otherOutputs = await db.VirtualLibraries.AsNoTracking()
        .Where(l => l.Id != id).Select(l => l.OutputPath).ToListAsync();
    if (otherOutputs.Any(p => string.Equals(NormalizePathKey(p), outputKey, StringComparison.OrdinalIgnoreCase)))
        return Results.Conflict(new { error = "Another library already uses that output path." });

    var outputPathChanged = !string.Equals(lib.OutputPath, outputPath, StringComparison.Ordinal);
    lib.Name = name;
    lib.MediaType = req.MediaType;
    lib.OutputPath = outputPath;
    await db.SaveChangesAsync();

    // Respect schedules: editing an inactive library must not recreate its links.
    var schedules = await db.LibrarySchedules.AsNoTracking().ToListAsync();
    var now = DateTimeOffset.UtcNow;
    await symlinkEngine.ReconcileAsync(library =>
        ScheduleEngine.IsActive(
            schedules.Where(s => s.VirtualLibraryId == library.Id).ToList(), now, tz.Zone));

    if (outputPathChanged)
    {
        activity.Record(ActivityCategory.Path, "LibraryOutputPathChanged", "System",
            $"Library {lib.Name} output path changed to {outputPath}");
        plex.TriggerInBackground([id]);
    }

    return Results.Ok(lib);
}).RequireAuthorization(p => p.RequireRole("Admin"));

// Admin-only: grant a user curation/viewing rights on a specific library
// e.g. "wife is a Curator on Kids Movies"
app.MapPost("/api/virtual-libraries/{libraryId}/members", async (
    string libraryId, AddMemberRequest req, CuratarrDbContext db, IActivityRecorder activity) =>
{
    var membership = new VirtualLibraryMembership
    {
        Id = Guid.NewGuid().ToString(),
        VirtualLibraryId = libraryId,
        UserId = req.UserId,
        Role = req.Role
    };
    db.VirtualLibraryMemberships.Add(membership);
    await db.SaveChangesAsync();
    activity.Record(ActivityCategory.User, "LibraryMembershipChanged", "System",
        $"Added user {req.UserId} to library {libraryId} as {req.Role}");
    return Results.Ok(membership);
}).RequireAuthorization(p => p.RequireRole("Admin"));

// Admin-only: change an existing member's role on a library, without
// needing to remove and re-add them
app.MapPut("/api/virtual-libraries/{libraryId}/members/{userId}", async (
    string libraryId, string userId, AddMemberRequest req, CuratarrDbContext db, IActivityRecorder activity) =>
{
    var membership = await db.VirtualLibraryMemberships
        .FirstOrDefaultAsync(m => m.VirtualLibraryId == libraryId && m.UserId == userId);
    if (membership is null) return Results.NotFound();
    membership.Role = req.Role;
    await db.SaveChangesAsync();
    activity.Record(ActivityCategory.User, "LibraryMembershipChanged", "System",
        $"Changed user {userId} role on library {libraryId} to {req.Role}");
    return Results.Ok(membership);
}).RequireAuthorization(p => p.RequireRole("Admin"));

// Any signed-in user: list the libraries THEY are a member of, with their role on each
app.MapGet("/api/virtual-libraries/mine", async (
    ClaimsPrincipal principal, UserManager<ApplicationUser> userManager, CuratarrDbContext db,
    ScheduleTimeZone tz) =>
{
    var user = await userManager.GetUserAsync(principal);
    if (user is null) return Results.Unauthorized();

    var memberships = await db.VirtualLibraryMemberships
        .Where(m => m.UserId == user.Id)
        .Include(m => m.VirtualLibrary)
        .ToListAsync();
    var libraryIds = memberships.Select(m => m.VirtualLibraryId).ToList();
    var schedules = await db.LibrarySchedules
        .Where(s => libraryIds.Contains(s.VirtualLibraryId))
        .AsNoTracking()
        .ToListAsync();
    var now = DateTimeOffset.UtcNow;

    return Results.Ok(memberships.Select(m =>
    {
        var rules = schedules.Where(s => s.VirtualLibraryId == m.VirtualLibraryId).ToList();
        return new
        {
            m.VirtualLibrary.Id,
            m.VirtualLibrary.Name,
            m.VirtualLibrary.MediaType,
            m.VirtualLibrary.LastReconciledAt,
            MyRole = m.Role,
            scheduleCount = rules.Count,
            isActive = ScheduleEngine.IsActive(rules, now, tz.Zone)
        };
    }));
}).RequireAuthorization();

// Approve a SourceItem into a library -- requires Owner or Curator
// membership on THAT library specifically; a Curator on "Kids Movies"
// cannot approve into "Kids Shows" unless also a member there.
// Which SourceItems are already approved into this library -- powers the
// "Approved" badge in the UI. Same access rule as approve: Admins see any
// library, everyone else needs a matching membership.
app.MapGet("/api/virtual-libraries/{libraryId}/approved-item-ids", async (
    string libraryId, ClaimsPrincipal principal, UserManager<ApplicationUser> userManager, CuratarrDbContext db) =>
{
    var user = await userManager.GetUserAsync(principal);
    if (user is null) return Results.Unauthorized();

    if (!principal.IsInRole("Admin"))
    {
        var membership = await db.VirtualLibraryMemberships
            .FirstOrDefaultAsync(m => m.VirtualLibraryId == libraryId && m.UserId == user.Id);
        if (membership is null) return Results.Forbid();
    }

    var ids = await db.VirtualLibraryItems
        .Where(v => v.VirtualLibraryId == libraryId)
        .Select(v => v.SourceItemId)
        .ToListAsync();
    return Results.Ok(ids);
}).RequireAuthorization();

// Batch semantics: approving only records the intent. The symlink tree and
// the Plex refresh are deferred to the next Sync (/api/reconcile) so marking
// a whole viewing session of titles stays a single reconcile + single Plex
// refresh instead of one per click.
app.MapPost("/api/virtual-libraries/{libraryId}/approve", async (
    string libraryId, ApproveRequest req, ClaimsPrincipal principal,
    UserManager<ApplicationUser> userManager, CuratarrDbContext db) =>
{
    var user = await userManager.GetUserAsync(principal);
    if (user is null) return Results.Unauthorized();

    // global Admins manage every library, not just ones they're an
    // explicit member of (architecture doc section 6); Curators/Owners
    // still need a matching VirtualLibraryMembership row
    var isAdmin = principal.IsInRole("Admin");
    if (!isAdmin)
    {
        var membership = await db.VirtualLibraryMemberships
            .FirstOrDefaultAsync(m => m.VirtualLibraryId == libraryId && m.UserId == user.Id);

        if (membership is null || membership.Role == MembershipRole.Viewer)
            return Results.Forbid();
    }

    // Idempotent: skip silently if already in the library. A duplicate
    // insert would hit the IX_VirtualLibraryItems unique index and throw.
    // Returning ApprovedAt lets the grid tell "marked but not synced yet"
    // (yellow) from "marked and already synced" (green) without a second
    // round-trip.
    var existing = await db.VirtualLibraryItems
        .FirstOrDefaultAsync(v => v.VirtualLibraryId == libraryId && v.SourceItemId == req.SourceItemId);
    if (existing is not null)
        return Results.Ok(new { existing.ApprovedAt });

    var item = new VirtualLibraryItem
    {
        Id = Guid.NewGuid().ToString(),
        VirtualLibraryId = libraryId,
        SourceItemId = req.SourceItemId
    };
    db.VirtualLibraryItems.Add(item);
    await db.SaveChangesAsync();

    // Deliberately no reconcile / Plex refresh here -- see the note above the
    // route. Sync (/api/reconcile) materializes symlinks and then triggers the
    // Plex refresh for everything marked so far.
    return Results.Ok(new { item.ApprovedAt });
}).RequireAuthorization();

// Revoke a previously-approved item. Same auth rule as approve, and the same
// batching rule: the symlink on disk is pruned by the next Sync, not here.
// POST-with-body (not DELETE-with-path-segment) deliberately: SourceItem
// ids are "rootId:relativePath" and contain "/", which breaks routing if
// placed directly in a URL path segment.
app.MapPost("/api/virtual-libraries/{libraryId}/unapprove", async (
    string libraryId, ApproveRequest req, ClaimsPrincipal principal,
    UserManager<ApplicationUser> userManager, CuratarrDbContext db) =>
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

    var existing = await db.VirtualLibraryItems
        .FirstOrDefaultAsync(v => v.VirtualLibraryId == libraryId && v.SourceItemId == req.SourceItemId);
    if (existing is null) return Results.NotFound();

    db.VirtualLibraryItems.Remove(existing);
    await db.SaveChangesAsync();

    // Symlink pruning + Plex refresh both happen on the next Sync, matching
    // the approve path.
    return Results.Ok();
}).RequireAuthorization();

// =========================================================================
// Per-library hidden items (Sprint 4 #1)
//
// Hiding is an overlay on the catalog, not an approval change: a hidden item
// keeps any VirtualLibraryItem row it already has, it just disappears from
// that library's grid views. Per-library rows mean hiding a title from "Kids
// Movies" never affects the same title in another library.
//
// Writes use the same gate as approve/unapprove (Admin bypass, Owner/Curator
// membership on THAT library, Viewer rejected). The grid reads the flags via
// GET /api/source-items?libraryId=..., which is available to every signed-in
// user so Viewers still get correct hidden filtering.
// =========================================================================

app.MapPost("/api/virtual-libraries/{libraryId}/hidden-items", async (
    string libraryId, HiddenItemsRequest req, ClaimsPrincipal principal,
    UserManager<ApplicationUser> userManager, CuratarrDbContext db) =>
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

    var requested = (req.SourceItemIds ?? Array.Empty<string>())
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Distinct()
        .ToList();

    // Ignore ids that don't exist in the catalog rather than tripping the FK.
    var validIds = await db.SourceItems
        .Where(i => requested.Contains(i.Id))
        .Select(i => i.Id)
        .ToListAsync();
    var existingRows = await db.HiddenItems
        .Where(h => h.VirtualLibraryId == libraryId && validIds.Contains(h.SourceItemId))
        .ToListAsync();
    var byItem = existingRows.ToDictionary(h => h.SourceItemId);

    foreach (var sourceItemId in validIds)
    {
        if (byItem.TryGetValue(sourceItemId, out var row))
        {
            // Re-hiding before Sync cancels a pending unhide. Keep the original
            // hide stamp: the title was already hidden on disk, so it should
            // stay green rather than restart as pending.
            row.UnhideRequestedAt = null;
        }
        else
        {
            db.HiddenItems.Add(new HiddenItem
            {
                Id = Guid.NewGuid().ToString(),
                VirtualLibraryId = libraryId,
                SourceItemId = sourceItemId
            });
        }
    }
    await db.SaveChangesAsync();

    // The response counts hide requests, which is what callers use to show a
    // pending indicator. The grid derives *effective* hidden state from these
    // rows plus LastReconciledAt.
    var hidden = await db.HiddenItems.CountAsync(h => h.VirtualLibraryId == libraryId);
    return Results.Ok(new { hidden });
}).RequireAuthorization();

// DELETE carries the same { sourceItemIds: [...] } body. POST .../unhide is
// provided as an alias for clients/proxies that don't forward DELETE bodies.
app.MapDelete("/api/virtual-libraries/{libraryId}/hidden-items", RemoveHiddenItemsAsync)
    .RequireAuthorization();
app.MapPost("/api/virtual-libraries/{libraryId}/unhide", RemoveHiddenItemsAsync)
    .RequireAuthorization();

app.MapGet("/api/virtual-libraries/{libraryId}/hidden-items", async (
    string libraryId, ClaimsPrincipal principal,
    UserManager<ApplicationUser> userManager, CuratarrDbContext db) =>
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

    var ids = await db.HiddenItems
        .Where(h => h.VirtualLibraryId == libraryId)
        .Select(h => h.SourceItemId)
        .ToListAsync();
    return Results.Ok(ids);
}).RequireAuthorization();

static async Task<IResult> RemoveHiddenItemsAsync(
    string libraryId, [Microsoft.AspNetCore.Mvc.FromBody] HiddenItemsRequest req,
    ClaimsPrincipal principal, UserManager<ApplicationUser> userManager, CuratarrDbContext db)
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

    var requested = (req.SourceItemIds ?? Array.Empty<string>())
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Distinct()
        .ToList();

    // An unhide is also two-phase. If the hide has not been applied by Sync
    // yet, simply cancel the request. Otherwise mark when the unhide was asked
    // for: the card stays in Hidden (yellow eye) until the next Sync clears it
    // and recreates the symlink.
    var lastReconciledAt = await db.VirtualLibraries
        .Where(l => l.Id == libraryId)
        .Select(l => (DateTime?)l.LastReconciledAt)
        .FirstOrDefaultAsync();
    var rows = await db.HiddenItems
        .Where(h => h.VirtualLibraryId == libraryId && requested.Contains(h.SourceItemId))
        .ToListAsync();
    var now = DateTime.UtcNow;
    var touched = 0;
    foreach (var row in rows)
    {
        if (row.UnhideRequestedAt is not null) continue; // already pending
        if (lastReconciledAt is null || row.CreatedAt > lastReconciledAt.Value)
            db.HiddenItems.Remove(row); // cancel a hide that Sync never applied
        else
            row.UnhideRequestedAt = now;
        touched++;
    }
    await db.SaveChangesAsync();

    var hidden = await db.HiddenItems.CountAsync(h => h.VirtualLibraryId == libraryId);
    return Results.Ok(new { removed = touched, hidden });
}

// Sprint 4 #1: uniqueness guards. Names are compared case-insensitively after
// trimming; paths are compared case-insensitively with separators normalized to
// '/' and a trailing separator removed, so "/media/Kids/" and "\media\kids"
// collide.
static string NormalizePathKey(string? path)
{
    var p = (path ?? "").Trim().Replace('\\', '/');
    while (p.Length > 1 && p[^1] == '/') p = p[..^1];
    return p;
}

app.Run();

// =========================================================================
// Startup diagnostics (pre-host): same ISO 8601 shape as the console logger
// so `docker logs` lines can be sorted/correlated with request logs.
// =========================================================================
static void LogStartup(string message) =>
    Console.WriteLine($"{DateTimeOffset.Now:yyyy-MM-ddTHH:mm:ss.fffzzz} [Curatarr] {message}");

// =========================================================================
// Startup seeding: global roles + DB schema
// =========================================================================
static async Task SeedAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();

    // schema must exist before RoleManager/UserManager can query their tables
    var db = scope.ServiceProvider.GetRequiredService<CuratarrDbContext>();

    // Real EF Core migrations (Curatarr.Engine/Data/Migrations). Fresh
    // databases get every table from InitialCreate; databases that predate
    // migrations (EnsureCreated + raw CREATE TABLE patches) are baselined
    // first so the initial migration isn't replayed against existing tables.
    await EnsureMigrationBaselineAsync(db);
    await db.Database.MigrateAsync();

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in new[] { "Admin", "Curator", "Viewer" })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }
}

// A pre-migrations database has all the tables but no "__EFMigrationsHistory"
// row, so MigrateAsync would try to CREATE them again and fail. Detect that
// shape and mark InitialCreate as applied without running it. No-ops on fresh
// DBs (no SourceItems table yet) and on already-baselined DBs.
static async Task EnsureMigrationBaselineAsync(CuratarrDbContext db)
{
    const string initialMigration = "20261115123142_InitialCreate";
    const string productVersion = "10.0.11";

    var connection = db.Database.GetDbConnection();
    await connection.OpenAsync();

    static async Task<long> ScalarAsync(System.Data.Common.DbConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? 0 : Convert.ToInt64(value);
    }

    var hasTables = await ScalarAsync(connection,
        "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='SourceItems'") > 0;
    if (!hasTables) return; // fresh DB -- MigrateAsync builds the whole schema

    await using (var create = connection.CreateCommand())
    {
        create.CommandText = """
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                "ProductVersion" TEXT NOT NULL
            );
            """;
        await create.ExecuteNonQueryAsync();
    }

    var baselined = await ScalarAsync(connection,
        $"SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '{initialMigration}'") > 0;
    if (baselined) return;

    await using var insert = connection.CreateCommand();
    insert.CommandText =
        $"INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") " +
        $"VALUES ('{initialMigration}', '{productVersion}')";
    await insert.ExecuteNonQueryAsync();
}

record RegisterRequest(string Email, string Password, string? DisplayName);
record LoginRequest(string Email, string Password);
record CreateVirtualLibraryRequest(string Name, MediaKind MediaType, string OutputPath);
record AddMemberRequest(string UserId, MembershipRole Role);
record ApproveRequest(string SourceItemId);
record HiddenItemsRequest(string[] SourceItemIds);
// Optional library scope for /api/scan. Null/absent means scan every root.
record ScanRequest(string? LibraryId);
record CreateSourceRootRequest(string Name, string Path, MediaKind MediaType);
record CreateSourceItemRequest(string SourceRootId, string RelativePath, MediaKind MediaType, string? Title);
record UpdateUserRequest(string DisplayName, string Email);
record CreatePathMappingRequest(string FromPrefix, string ToPrefix);
record ChangePasswordRequest(string CurrentPassword, string NewPassword);
record ForgotPasswordRequest(string Email);
record ResetPasswordRequest(string Email, string Token, string NewPassword);
record AcceptInviteRequest(string UserId, string Token, string Password, string DisplayName, string? AlternateEmail);
record ConfirmEmailChangeRequest(string UserId, string NewEmail, string Token);
record AdminSetPasswordRequest(string NewPassword);
record InviteUserRequest(string Email);
record SetUserRolesRequest(string[] Roles);
record MatchMetadataRequest(
    string? SourceItemId, string? RepresentativeId, int TmdbId,
    string MediaType, string? Title, int? Year, string? PosterUrl, string? Overview);

// Shared logic for filling in SourceItem catalog details (genre ids, US
// rating, studios/networks) that the scan skips to keep TMDB call volume
// down. Used by both the manual /api/backfill-genres endpoint and the
// hourly DetailsBackfillService.
//
// Sentinel semantics: NULL = never fetched (retry), "" / "[]" = fetched but
// no value (never retried). A failed fetch writes nothing, so the row stays
// pending for the next pass.
public static class DetailsBackfill
{
    // Kept small: each item is a sequential TMDB round-trip, and this
    // endpoint is called through nginx (default proxy_read_timeout = 60s).
    // The caller loops within its own time budget instead.
    public const int EndpointBatchSize = 50;
    public const int ServiceBatchSize = 500; // in-process, no HTTP timeout

    /// <summary>Items still missing at least one detail column.</summary>
    public static IQueryable<SourceItem> Pending(CuratarrDbContext db) =>
        db.SourceItems.Where(i => i.TmdbId != null &&
            (i.GenreIds == null || i.GenreIds == "" ||
             i.Rating == null ||
             i.Cast == null ||
             (i.MediaType == MediaKind.Movie
                ? i.ProductionCompanies == null || i.Director == null
                : i.Networks == null || i.Creators == null)));

    /// <summary>
    /// Fetch + store one batch. Returns (rows examined, rows changed,
    /// fetches failed). Failed rows keep their NULL columns so they retry.
    /// </summary>
    public static async Task<(int Processed, int Updated, int Failed)> RunBatchAsync(
        CuratarrDbContext db,
        IMetadataProvider metadata,
        ILogger logger,
        int batchSize,
        CancellationToken ct = default)
    {
        var items = await Pending(db).Take(batchSize).ToListAsync(ct);
        if (items.Count == 0) return (0, 0, 0);

        var updated = 0;
        var failed = 0;

        foreach (var item in items)
        {
            try
            {
                var details = await metadata.GetCatalogDetailsAsync(item.TmdbId!.Value, item.MediaType, ct);
                if (details is null)
                {
                    failed++; // columns stay NULL -> retried next pass
                    continue;
                }

                // Stash the raw response for future field expansions (one row
                // per unique mediaType+tmdbId; episodes share their show's).
                // Atomic upsert instead of FindAsync+Add: the hourly service
                // and the endpoint can run concurrently against the same key,
                // and a tracked Add/Add race trips UNIQUE on SaveChanges.
                // Runs as plain SQL (outside the EF transaction) -- the stash
                // committing even if the column update later fails is fine,
                // the next pass just re-stashes the same idempotent payload.
                var rawKey = $"{item.MediaType}:{item.TmdbId.Value}";
                await db.Database.ExecuteSqlAsync($@"
INSERT INTO TmdbRawDetails (""Key"", ""Json"", ""RequestedWith"", ""FetchedAt"")
VALUES ({rawKey}, {details.RawJson}, {details.RequestedWith}, {DateTime.UtcNow})
ON CONFLICT(""Key"") DO UPDATE SET
    ""Json"" = excluded.""Json"",
    ""RequestedWith"" = excluded.""RequestedWith"",
    ""FetchedAt"" = excluded.""FetchedAt""");

                // Parse the raw payload we just cached. This is local-only;
                // cached rows from earlier runs are handled by the sweep service.
                CalendarDateParser.ApplyRawDates(item, details.RawJson, DateTime.UtcNow);

                if (string.IsNullOrEmpty(item.GenreIds))
                    item.GenreIds = details.GenreIds.Count > 0 ? string.Join(',', details.GenreIds) : "[]";
                if (item.Rating is null)
                    item.Rating = details.Rating ?? ""; // fetched, none found -> stop retrying
                if (item.MediaType == MediaKind.Movie && item.ProductionCompanies is null)
                    item.ProductionCompanies = details.ProductionCompanies is { Count: > 0 }
                        ? string.Join('|', details.ProductionCompanies) : "";
                if (item.MediaType != MediaKind.Movie && item.Networks is null)
                    item.Networks = details.Networks is { Count: > 0 }
                        ? string.Join('|', details.Networks) : "";
                if (item.MediaType == MediaKind.Movie && item.Director is null)
                    item.Director = details.Director ?? ""; // fetched, none found -> stop retrying
                if (item.MediaType != MediaKind.Movie && item.Creators is null)
                    item.Creators = details.Creators is { Count: > 0 }
                        ? string.Join('|', details.Creators) : "";
                if (item.Cast is null)
                    item.Cast = details.Cast is { Count: > 0 }
                        ? string.Join('|', details.Cast) : "";

                updated++;
            }
            catch (Exception ex)
            {
                // Fetch failed: write nothing, so the row's NULL columns keep
                // it pending for the next pass (never marked "fetched, empty").
                logger.LogWarning(ex, "Failed to fetch catalog details for item {Id}", item.Id);
                failed++;
            }
        }

        await db.SaveChangesAsync(ct);
        return (items.Count, updated, failed);
    }
}

// Background details backfill - runs hourly to keep genre/rating/studio/
// director/cast data fresh and to retry rows the foreground pass couldn't fetch
public class DetailsBackfillService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMetadataProvider _metadata;
    private readonly ILogger<DetailsBackfillService> _logger;
    private static bool _isRunning = false;

    public DetailsBackfillService(
        IServiceScopeFactory scopeFactory,
        IMetadataProvider metadata,
        ILogger<DetailsBackfillService> logger)
    {
        _scopeFactory = scopeFactory;
        _metadata = metadata;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait 30 seconds after startup before first run
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await BackfillRunAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Details backfill run failed");
            }

            // Wait 1 hour between runs
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task BackfillRunAsync(CancellationToken ct)
    {
        if (_isRunning)
        {
            _logger.LogDebug("Details backfill already running, skipping");
            return;
        }

        _isRunning = true;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CuratarrDbContext>();

            var totalProcessed = 0;
            var totalUpdated = 0;

            while (true)
            {
                var (processed, updated, failed) = await DetailsBackfill.RunBatchAsync(
                    db, _metadata, _logger, DetailsBackfill.ServiceBatchSize, ct);

                if (processed == 0) break;

                totalProcessed += processed;
                totalUpdated += updated;
                _logger.LogInformation(
                    "Details backfill: {Processed} items ({Updated} updated, {Failed} failed; run totals {TotalProcessed}/{TotalUpdated})",
                    processed, updated, failed, totalProcessed, totalUpdated);

                // Progress guard: a full batch that changed nothing means the
                // pending rows are stuck (e.g. TMDB outage) -- break instead
                // of re-fetching the same rows forever within this run.
                if (updated == 0)
                {
                    _logger.LogWarning(
                        "Details backfill: no progress this batch ({Failed} failures) -- stopping run, will retry next hour",
                        failed);
                    break;
                }

                // Small delay between batches to respect TMDB rate limits
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }

            _logger.LogInformation("Details backfill run complete: processed {Total}, updated {Updated}", totalProcessed, totalUpdated);
        }
        finally
        {
            _isRunning = false;
        }
    }
}
