using Curatarr.Engine.Data;

namespace Curatarr.Engine;

/// <summary>
/// Pure availability evaluation for <see cref="LibrarySchedule"/> rules.
///
/// Semantics:
/// - Union: a library is active when ANY enabled rule matches.
/// - Fail-open: zero enabled rules = Always On (a library never disappears
///   because its last rule was disabled or never created).
/// - All comparisons are local wall-clock in the configured schedule timezone
///   (see Schedules:TimeZone), never UTC, so rules behave the same across DST.
/// - A window belongs to the day it STARTS: an overnight Friday window keeps
///   running into Saturday morning, and the day/season filter applies to
///   Friday.
/// - End &lt; Start crosses midnight; End == Start means the whole day.
/// </summary>
public static class ScheduleEngine
{
    /// <summary>True when the library should currently be visible.</summary>
    public static bool IsActive(
        IReadOnlyCollection<LibrarySchedule> schedules,
        DateTimeOffset nowUtc,
        TimeZoneInfo timeZone)
    {
        var enabled = schedules.Where(s => s.Enabled).ToList();
        if (enabled.Count == 0) return true; // fail-open / Always On

        var localNow = TimeZoneInfo.ConvertTime(nowUtc, timeZone).DateTime;
        return enabled.Any(s => Matches(s, localNow));
    }

    /// <summary>No enabled rules — the library is 24/7 and has no transitions.</summary>
    public static bool IsAlwaysOn(IReadOnlyCollection<LibrarySchedule> schedules) =>
        !schedules.Any(s => s.Enabled);

    /// <summary>
    /// First instant after <paramref name="nowUtc"/> at which the active state
    /// flips. Null for Always On or when nothing flips within the search
    /// window (366 days). Candidate instants are rule boundaries and local
    /// midnights, so this stays cheap even with several rules.
    /// </summary>
    public static DateTimeOffset? NextTransition(
        IReadOnlyCollection<LibrarySchedule> schedules,
        DateTimeOffset nowUtc,
        TimeZoneInfo timeZone)
    {
        var enabled = schedules.Where(s => s.Enabled).ToList();
        if (enabled.Count == 0) return null;

        var localNow = TimeZoneInfo.ConvertTime(nowUtc, timeZone).DateTime;
        var currentlyActive = enabled.Any(s => Matches(s, localNow));

        var candidates = new SortedSet<DateTime>();
        for (var offset = -1; offset <= 366; offset++)
        {
            var date = localNow.Date.AddDays(offset);
            candidates.Add(date); // season/day boundaries
            foreach (var rule in enabled)
            {
                var start = date + rule.StartTime.ToTimeSpan();
                candidates.Add(start);

                DateTime end;
                if (rule.StartTime == rule.EndTime)
                    end = start.AddDays(1);
                else if (rule.EndTime < rule.StartTime)
                    end = date.AddDays(1) + rule.EndTime.ToTimeSpan();
                else
                    end = date + rule.EndTime.ToTimeSpan();
                candidates.Add(end);
            }
        }

        foreach (var candidate in candidates)
        {
            if (candidate <= localNow) continue;
            var active = enabled.Any(s => Matches(s, candidate));
            if (active == currentlyActive) continue;

            // Wall-clock candidate -> UTC instant in the schedule timezone.
            var unspecified = DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified);
            return new DateTimeOffset(unspecified, timeZone.GetUtcOffset(unspecified));
        }

        return null;
    }

    /// <summary>True when the rule's window covers <paramref name="localNow"/>.</summary>
    public static bool Matches(LibrarySchedule rule, DateTime localNow)
    {
        if (!rule.Enabled) return false;

        var days = rule.Days;

        // The window can start today, or yesterday when it crosses midnight.
        for (var dayOffset = 0; dayOffset >= -1; dayOffset--)
        {
            var crossingMidnight = rule.EndTime < rule.StartTime;
            if (dayOffset == -1 && !crossingMidnight) continue;

            var startDate = localNow.Date.AddDays(dayOffset);
            if (!InSeason(rule, startDate)) continue;
            if (days.Count > 0 && !days.Contains(startDate.DayOfWeek)) continue;

            var start = startDate + rule.StartTime.ToTimeSpan();
            DateTime end;
            if (rule.StartTime == rule.EndTime)
                end = start.AddDays(1); // all day
            else if (crossingMidnight)
                end = startDate.AddDays(1) + rule.EndTime.ToTimeSpan();
            else
                end = startDate + rule.EndTime.ToTimeSpan();

            if (localNow >= start && localNow < end) return true;
        }

        return false;
    }

    private static bool InSeason(LibrarySchedule rule, DateTime date)
    {
        if (!rule.HasSeason) return true;

        var start = rule.StartMonth!.Value * 100 + rule.StartDay!.Value;
        var end = rule.EndMonth!.Value * 100 + rule.EndDay!.Value;
        var current = date.Month * 100 + date.Day;

        // Ranges may wrap the year end (Dec 15 - Jan 5).
        return start <= end
            ? current >= start && current <= end
            : current >= start || current <= end;
    }

    /// <summary>
    /// Validates and normalizes a rule's day set / date range. Returns an error
    /// message for the API, or null when the values are usable.
    /// </summary>
    public static string? Validate(
        IEnumerable<int>? daysOfWeek,
        int? startMonth, int? startDay,
        int? endMonth, int? endDay)
    {
        if (daysOfWeek is not null && daysOfWeek.Any(d => d is < 0 or > 6))
            return "Days of week must be 0 (Sunday) through 6 (Saturday).";

        var seasonParts = new[] { startMonth, startDay, endMonth, endDay };
        var seasonCount = seasonParts.Count(p => p is not null);
        if (seasonCount is not (0 or 4))
            return "Seasonal ranges need both a start and an end date.";

        if (seasonCount == 4)
        {
            if (!IsValidMonthDay(startMonth!.Value, startDay!.Value) ||
                !IsValidMonthDay(endMonth!.Value, endDay!.Value))
                return "Seasonal dates must be valid month/day pairs (e.g. 10/1 - 10/31).";
        }

        return null;
    }

    /// <summary>Day set -> the CSV shape stored on the entity (sorted, deduped).</summary>
    public static string? NormalizeDays(IEnumerable<int>? daysOfWeek)
    {
        if (daysOfWeek is null) return null;
        var days = daysOfWeek.Distinct().OrderBy(d => d).ToList();
        return days.Count == 0 ? null : string.Join(",", days);
    }

    private static bool IsValidMonthDay(int month, int day)
    {
        if (month is < 1 or > 12 || day < 1) return false;
        // 2000 is a leap year, so 2/29 is accepted.
        return day <= DateTime.DaysInMonth(2000, month);
    }
}
