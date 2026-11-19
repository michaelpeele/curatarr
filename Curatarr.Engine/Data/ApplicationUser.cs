using Microsoft.AspNetCore.Identity;

namespace Curatarr.Engine.Data;

/// <summary>
/// Curatarr's user. Global capability (Admin/Curator/Viewer) is handled
/// via ASP.NET Core Identity's built-in role system ("Admin" etc, seeded
/// at startup); per-library curation rights are separate, via
/// VirtualLibraryMembership -- a Curator role globally doesn't imply
/// membership on any specific library, matching architecture doc section 6.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }
}
