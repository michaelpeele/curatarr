namespace Curatarr.Engine.Rules;

/// <summary>
/// One filter field that automatic rules can use. This registry is the single
/// source of truth for the backend validator and the frontend rule builder:
/// the API serves it from /api/library-fields, so adding a new grid filter
/// only requires one extra entry here (plus its predicate in RuleEvaluator).
/// </summary>
public sealed record RuleFieldDefinition(
    string Value,
    string Label,
    string Kind,
    IReadOnlyList<string> Operators,
    IReadOnlyList<string> MediaTypes,
    string? Hint = null);

public static class RuleFields
{
    public const string OpEquals = "equals";
    public const string OpContains = "contains";
    public const string OpIn = "in";
    public const string OpGte = "gte";
    public const string OpLte = "lte";

    public const string Approve = "approve";
    public const string Hide = "hide";

    private static readonly string[] AllMedia = ["Movie", "Show", "Anime"];

    /// <summary>Registry order is also the order offered in the UI.</summary>
    public static readonly IReadOnlyList<RuleFieldDefinition> All =
    [
        new("genre", "Genre", "genre", [OpIn, OpEquals], AllMedia, "TMDB genre id, e.g. 10751 (Family)"),
        new("director", "Director / Creator", "text", [OpEquals, OpContains, OpIn], AllMedia,
            "Movie director; show creator (pipe-separated for 'in')"),
        new("cast", "Cast", "text", [OpContains, OpEquals, OpIn], AllMedia,
            "Pipe-separated names for 'equals'/'in'"),
        new("studio", "Studio", "text", [OpEquals, OpContains, OpIn], ["Movie"],
            "Production company, pipe-separated for 'in'"),
        new("network", "Network", "text", [OpEquals, OpContains, OpIn], ["Show", "Anime"],
            "Network name, pipe-separated for 'in'"),
        new("rating", "Rating", "text", [OpEquals, OpContains, OpIn], AllMedia,
            "US certification, e.g. G, PG-13, TV-MA"),
        new("year", "Year", "number", [OpEquals, OpGte, OpLte, OpIn], AllMedia, "Release year"),
        new("mediaType", "Media type", "enum", [OpEquals, OpIn], AllMedia, "Movie, Show or Anime"),
    ];

    public static readonly IReadOnlyList<string> Actions = [Approve, Hide];

    public static RuleFieldDefinition? Find(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : All.FirstOrDefault(f => string.Equals(f.Value, value.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool IsValidAction(string? action) =>
        Actions.Any(a => string.Equals(a, action?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool SupportsMediaType(RuleFieldDefinition field, string mediaType) =>
        field.MediaTypes.Any(m => string.Equals(m, mediaType, StringComparison.OrdinalIgnoreCase));
}
