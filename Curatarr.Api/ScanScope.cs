using Curatarr.Engine.Data;

namespace Curatarr.Api;

/// <summary>
/// Sprint 5 #3: a scan can be scoped to one library by keeping only source
/// roots whose MediaType matches the library's. A null type keeps every root
/// (the legacy all-roots behaviour used by no-body callers).
/// </summary>
public static class ScanScope
{
    public static List<SourceRoot> FilterRoots(IEnumerable<SourceRoot> roots, MediaKind? mediaType) =>
        mediaType is null
            ? roots.ToList()
            : roots.Where(r => r.MediaType == mediaType.Value).ToList();
}
