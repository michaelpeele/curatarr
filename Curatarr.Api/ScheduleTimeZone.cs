namespace Curatarr.Api;

/// <summary>
/// The timezone used for schedule evaluation and (later) log timestamps.
/// Configured with <c>Schedules:TimeZone</c> in appsettings.json, overridable
/// with the <c>Schedules__TimeZone</c> environment variable. IANA ids such as
/// <c>America/New_York</c> work on Linux containers. Empty or unknown values
/// fall back to the server's local zone.
/// </summary>
public sealed class ScheduleTimeZone
{
    public TimeZoneInfo Zone { get; }
    public string Id { get; }
    public bool IsConfigured { get; }

    public ScheduleTimeZone(IConfiguration configuration, ILogger<ScheduleTimeZone> logger)
    {
        var configured = configuration["Schedules:TimeZone"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            Zone = TimeZoneInfo.Local;
            Id = Zone.Id;
            IsConfigured = false;
            return;
        }

        try
        {
            Zone = TimeZoneInfo.FindSystemTimeZoneById(configured.Trim());
            Id = configured.Trim();
            IsConfigured = true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger.LogWarning(
                "Schedules:TimeZone '{Configured}' is not valid on this host; falling back to {Local}.",
                configured, TimeZoneInfo.Local.Id);
            Zone = TimeZoneInfo.Local;
            Id = Zone.Id;
            IsConfigured = false;
        }
    }
}
