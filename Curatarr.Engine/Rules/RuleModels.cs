using Curatarr.Engine.Data;

namespace Curatarr.Engine.Rules;

/// <summary>Validation-friendly shape for a rule, independent of EF plumbing.</summary>
public sealed record RuleDraft(
    string? Field,
    string? Operator,
    string? Value,
    string? Action,
    int Priority = 0,
    bool Enabled = true,
    string? Id = null)
{
    public static RuleDraft From(LibraryRule rule) => new(
        rule.Field, rule.Operator, rule.Value, rule.Action, rule.Priority, rule.Enabled, rule.Id);
}

/// <summary>A validated rule ready for repeated evaluation over many rows.</summary>
public sealed class RuleSpec
{
    public required string Id { get; init; }
    public required RuleFieldDefinition Definition { get; init; }
    public required string Field { get; init; }
    public required string Operator { get; init; }
    public required string Action { get; init; }
    public required string Value { get; init; }
    public required IReadOnlyList<string> Values { get; init; }
    public required IReadOnlyList<int> Numbers { get; init; }
    public int Priority { get; init; }
    public bool Enabled { get; init; }
}

/// <summary>
/// Current approval/hide state split by origin. Manual rows (written by the
/// existing /approve and /hidden-items endpoints) are never touched by rules;
/// only rule-owned rows are eligible for cleanup on a later Apply.
/// </summary>
public sealed class RuleAnnotations
{
    public static RuleAnnotations Empty { get; } = new(
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal));

    public RuleAnnotations(
        IReadOnlySet<string> manualApproved,
        IReadOnlySet<string> manualHidden,
        IReadOnlySet<string> ruleApproved,
        IReadOnlySet<string> ruleHidden)
    {
        ManualApproved = manualApproved;
        ManualHidden = manualHidden;
        RuleApproved = ruleApproved;
        RuleHidden = ruleHidden;
    }

    public IReadOnlySet<string> ManualApproved { get; }
    public IReadOnlySet<string> ManualHidden { get; }
    public IReadOnlySet<string> RuleApproved { get; }
    public IReadOnlySet<string> RuleHidden { get; }

    public bool IsManuallyDecided(string id) => ManualApproved.Contains(id) || ManualHidden.Contains(id);
}

/// <summary>The complete diff computed by one evaluation pass.</summary>
public sealed class RuleEvaluation
{
    public IReadOnlyList<string> Approve { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Hide { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Unapprove { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Unhide { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Samples { get; init; } = Array.Empty<string>();

    public RulePreviewDto Preview() => new(Approve.Count, Hide.Count, Samples);

    public RuleCountsDto Counts() => new(Approve.Count, Hide.Count, Unapprove.Count, Unhide.Count);
}

public sealed record RulePreviewDto(int ToApprove, int ToHide, IReadOnlyList<string> Samples);

public sealed record RuleCountsDto(int Approved, int Hidden, int Unapproved, int Unhidden);

public sealed record RuleApplyDto(int Approved, int Hidden, int Unapproved, int Unhidden, string LogId)
{
    /// <summary>Total row changes; zero on an idempotent re-run.</summary>
    public int Total => Approved + Hidden + Unapproved + Unhidden;
}

/// <summary>Metadata DTO served by /api/library-fields for the rule builder.</summary>
public sealed record RuleFieldDto(
    string Value,
    string Label,
    string Kind,
    IReadOnlyList<string> Operators,
    IReadOnlyList<string> MediaTypes,
    string? Hint);
