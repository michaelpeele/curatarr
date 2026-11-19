namespace Curatarr.Engine.LibraryGrid;

public static class GridPaging
{
    public const int DefaultLimit = 60;
    public const int MaxLimit = 200;

    /// <summary>Clamp to 1..200; a missing limit defaults to 60.</summary>
    public static int ClampLimit(int? limit)
    {
        if (limit is null) return DefaultLimit;
        return limit.Value < 1 ? 1 : limit.Value > MaxLimit ? MaxLimit : limit.Value;
    }

    /// <summary>Negative offsets are clamped to 0 (rather than a 400).</summary>
    public static int NormalizeOffset(int? offset) => offset is null || offset.Value < 0 ? 0 : offset.Value;

    /// <summary>True while at least one matching entry lies beyond this page.</summary>
    public static bool HasMore(int offset, int returned, int total) => (long)offset + returned < total;

    public static IReadOnlyList<T> Page<T>(IReadOnlyList<T> source, int offset, int limit)
    {
        if (offset >= source.Count) return Array.Empty<T>();
        return source.Skip(offset).Take(limit).ToList();
    }
}
