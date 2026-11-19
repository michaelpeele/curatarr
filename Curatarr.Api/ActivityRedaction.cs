using System.Text.RegularExpressions;

namespace Curatarr.Api;

/// <summary>
/// Scrubs secrets from free-text activity details before they are persisted.
/// Activity rows are readable by every Admin, so a token that appears in a
/// request detail must never make it into the database.
/// </summary>
public static partial class ActivityRedaction
{
    public const int MaxDetailLength = 400;

    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var s = SecretAssignmentRegex().Replace(value, "$1=[redacted]");
        s = SensitiveUrlRegex().Replace(s, "[link redacted]");
        s = BearerRegex().Replace(s, "Bearer [redacted]");
        if (s.Length > MaxDetailLength) s = s[..MaxDetailLength] + "…";
        return s;
    }

    [GeneratedRegex(@"(?i)\b(password|passwd|pwd|token|secret|api[-_]?key|client[-_]?secret|authorization)\s*[=:]\s*[^\s,;]+", RegexOptions.Compiled)]
    private static partial Regex SecretAssignmentRegex();

    [GeneratedRegex(@"(?i)https?://\S*(reset-password|invite|token=)\S*", RegexOptions.Compiled)]
    private static partial Regex SensitiveUrlRegex();

    [GeneratedRegex(@"(?i)\bBearer\s+[A-Za-z0-9._\-]+", RegexOptions.Compiled)]
    private static partial Regex BearerRegex();
}
