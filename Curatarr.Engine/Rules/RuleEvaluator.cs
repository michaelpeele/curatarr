using Curatarr.Engine.Data;
using Curatarr.Engine.LibraryGrid;

namespace Curatarr.Engine.Rules;

/// <summary>
/// Pure rule validation + evaluation. Deliberately DbContext-free so every
/// field/operator/precedence rule can be unit-tested, and so preview and apply
/// share exactly the same code path (preview never writes).
/// </summary>
public static class RuleEvaluator
{
    /// <summary>Maximum number of example titles returned by a preview.</summary>
    public const int SampleLimit = 5;

    /// <summary>Splits rule values on both grid delimiters (comma and pipe).</summary>
    public static IReadOnlyList<string> SplitValues(string? value) =>
        (value ?? "")
            .Split([',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool TryPrepare(RuleDraft draft, out RuleSpec? spec, out string? error)
    {
        spec = null;
        error = null;

        var field = RuleFields.Find(draft.Field);
        if (field is null)
        {
            error = $"Unknown field '{draft.Field}'. Valid fields: {string.Join(", ", RuleFields.All.Select(f => f.Value))}";
            return false;
        }

        var op = (draft.Operator ?? "").Trim().ToLowerInvariant();
        if (!field.Operators.Any(o => string.Equals(o, op, StringComparison.OrdinalIgnoreCase)))
        {
            error = $"Operator '{draft.Operator}' is not valid for field '{field.Value}'. Allowed: {string.Join(", ", field.Operators)}";
            return false;
        }

        var action = (draft.Action ?? "").Trim().ToLowerInvariant();
        if (!RuleFields.IsValidAction(action))
        {
            error = $"Unknown action '{draft.Action}'. Valid actions: {string.Join(", ", RuleFields.Actions)}";
            return false;
        }

        var rawValue = (draft.Value ?? "").Trim();
        if (rawValue.Length == 0)
        {
            error = "Value is required";
            return false;
        }

        var values = SplitValues(rawValue);
        if (values.Count == 0)
        {
            error = "Value must contain at least one non-empty token";
            return false;
        }

        var numbers = new List<int>();
        if (field.Kind is "genre" or "number")
        {
            foreach (var token in values)
            {
                if (!int.TryParse(token, out var number))
                {
                    error = field.Kind == "genre"
                        ? $"Field 'genre' requires numeric TMDB genre ids; '{token}' is not a number"
                        : $"Field 'year' requires numeric values; '{token}' is not a number";
                    return false;
                }
                numbers.Add(number);
            }
            // Numeric comparisons take one value; 'in' and genre set-equality
            // ('equals') legitimately take several.
            if (field.Kind == "number" && op != RuleFields.OpIn && values.Count != 1)
            {
                error = $"Operator '{op}' on field '{field.Value}' requires exactly one value";
                return false;
            }
        }

        if (field.Value == "mediaType")
        {
            foreach (var token in values)
            {
                if (!TryParseMediaKind(token, out _))
                {
                    error = $"Field 'mediaType' requires Movie, Show or Anime; '{token}' is not valid";
                    return false;
                }
            }
        }

        spec = new RuleSpec
        {
            Id = draft.Id ?? "",
            Definition = field,
            Field = field.Value,
            Operator = op,
            Action = action,
            Value = rawValue,
            Values = values,
            Numbers = numbers,
            Priority = draft.Priority,
            Enabled = draft.Enabled,
        };
        return true;
    }

    public static string? Validate(RuleDraft draft) =>
        TryPrepare(draft, out _, out var error) ? null : error;

    /// <summary>Prepares a persisted rule set; throws on invalid rows (callers validate on write).</summary>
    public static IReadOnlyList<RuleSpec> Prepare(IEnumerable<LibraryRule> rules)
    {
        var result = new List<RuleSpec>();
        foreach (var rule in rules)
        {
            if (!TryPrepare(RuleDraft.From(rule), out var spec, out var error))
                throw new ArgumentException($"Invalid rule {rule.Id}: {error}", nameof(rules));
            result.Add(spec!);
        }
        return Order(result);
    }

    /// <summary>Lower Priority first; Id breaks ties so re-runs are deterministic.</summary>
    public static IReadOnlyList<RuleSpec> Order(IEnumerable<RuleSpec> rules) =>
        rules.OrderBy(r => r.Priority).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();

    public static bool Matches(GridRow row, RuleSpec rule)
    {
        switch (rule.Field)
        {
            case "genre":
                return MatchesGenre(row, rule);

            case "year":
                return MatchesYear(row, rule);

            case "mediaType":
                return rule.Values.Any(v => GridFilters.InMediaType(row, v));

            default:
                var haystack = FieldValue(row, rule.Field);
                return rule.Operator switch
                {
                    RuleFields.OpContains => GridFilters.ContainsToken(haystack, rule.Value),
                    RuleFields.OpEquals => EqualsValue(haystack, rule),
                    RuleFields.OpIn => rule.Values.Any(v => EqualsValue(haystack, rule.Field, v)),
                    _ => false,
                };
        }
    }

    /// <summary>
    /// Evaluates every enabled rule against the catalog rows for one library.
    /// Manual decisions are skipped entirely; rule-owned rows are rewound when
    /// their rule no longer matches (or a manual decision now wins).
    /// </summary>
    public static RuleEvaluation Evaluate(
        IReadOnlyList<GridRow> rows,
        IReadOnlyList<RuleSpec> rules,
        string libraryMediaType,
        RuleAnnotations annotations)
    {
        // Media-type scoping: a Movie library never evaluates Show rows, and a
        // later rule simply overwrites the previous outcome (Priority order).
        var ordered = Order(rules.Where(r => r.Enabled));
        var approve = new List<string>();
        var hide = new List<string>();
        var unapprove = new List<string>();
        var unhide = new List<string>();
        var samples = new List<string>();
        var seenSamples = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (!GridFilters.InMediaType(row, libraryMediaType)) continue;
            if (annotations.IsManuallyDecided(row.Id)) continue;

            string? outcome = null;
            foreach (var rule in ordered)
            {
                if (Matches(row, rule))
                    outcome = rule.Action; // later match wins (Rules run in Priority order)
            }

            var wasApproved = annotations.RuleApproved.Contains(row.Id);
            var wasHidden = annotations.RuleHidden.Contains(row.Id);

            if (outcome == RuleFields.Approve)
            {
                if (!wasApproved) approve.Add(row.Id);
                if (wasHidden) unhide.Add(row.Id);
                AddSample(samples, seenSamples, row);
            }
            else if (outcome == RuleFields.Hide)
            {
                if (!wasHidden) hide.Add(row.Id);
                if (wasApproved) unapprove.Add(row.Id);
                AddSample(samples, seenSamples, row);
            }
            else
            {
                if (wasApproved) unapprove.Add(row.Id);
                if (wasHidden) unhide.Add(row.Id);
            }
        }

        // A manual mark/hide added after a rule run must win. Drop any
        // rule-owned rows still attached to that title on the next Apply.
        foreach (var id in annotations.RuleApproved.Where(annotations.IsManuallyDecided))
            if (!unapprove.Contains(id)) unapprove.Add(id);
        foreach (var id in annotations.RuleHidden.Where(annotations.IsManuallyDecided))
            if (!unhide.Contains(id)) unhide.Add(id);

        return new RuleEvaluation
        {
            Approve = approve,
            Hide = hide,
            Unapprove = unapprove,
            Unhide = unhide,
            Samples = samples,
        };
    }

    private static void AddSample(List<string> samples, HashSet<string> seen, GridRow row)
    {
        if (samples.Count >= SampleLimit) return;
        var label = row.ShowTitle ?? row.Title ?? row.RelativePath ?? row.Id;
        if (string.IsNullOrWhiteSpace(label)) label = row.Id;
        if (seen.Add(label)) samples.Add(label);
    }

    private static bool TryParseMediaKind(string? value, out string kind)
    {
        kind = (value ?? "").Trim();
        return kind.Equals("Movie", StringComparison.OrdinalIgnoreCase)
               || kind.Equals("Show", StringComparison.OrdinalIgnoreCase)
               || kind.Equals("Anime", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FieldValue(GridRow row, string field) => field switch
    {
        "director" => row.Director ?? row.Creators, // Shows store their director as "creators"
        "cast" => row.Cast,
        "studio" => row.ProductionCompanies,
        "network" => row.Networks,
        "rating" => row.Rating,
        _ => null,
    };

    private static bool MatchesGenre(GridRow row, RuleSpec rule)
    {
        if (rule.Operator == RuleFields.OpEquals)
        {
            // Delimiter-aware exact set equality, using the grid's own splitter.
            var rowIds = GridFilters.SplitGenres(row.GenreIds)
                .Select(g => int.TryParse(g, out var id) ? (int?)id : null)
                .Where(id => id.HasValue).Select(id => id!.Value).Distinct().OrderBy(id => id);
            var expected = rule.Numbers.Distinct().OrderBy(id => id);
            return rowIds.SequenceEqual(expected);
        }
        return rule.Numbers.Any(id => GridFilters.MatchesGenre(row.GenreIds, id));
    }

    private static bool MatchesYear(GridRow row, RuleSpec rule)
    {
        if (row.Year is null) return false;
        return rule.Operator switch
        {
            RuleFields.OpEquals => row.Year == rule.Numbers[0],
            RuleFields.OpGte => row.Year >= rule.Numbers[0],
            RuleFields.OpLte => row.Year <= rule.Numbers[0],
            _ => rule.Numbers.Contains(row.Year.Value),
        };
    }

    private static bool EqualsValue(string? haystack, RuleSpec rule) =>
        EqualsValue(haystack, rule.Field, rule.Value);

    private static bool EqualsValue(string? haystack, string field, string token) => field switch
    {
        // Grid parity: rating is an exact string; studio/network are exact
        // pipe-delimited tokens (case-sensitive, as in GridFilters).
        "rating" => GridFilters.MatchesRating(haystack, token),
        "studio" => GridFilters.MatchesStudio(haystack, token),
        "network" => GridFilters.MatchesNetwork(haystack, token),
        _ => GridFilters.SplitPipe(haystack)
            .Any(name => string.Equals(name, token, StringComparison.OrdinalIgnoreCase)),
    };
}
