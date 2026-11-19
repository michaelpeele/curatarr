using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Curatarr.Api.Hubs;

/// <summary>
/// Push-only hub -- the server broadcasts "ScanProgress" events as the
/// LibraryScanner processes each item; clients don't send anything back.
/// Requires the same cookie auth as the rest of the API.
/// </summary>
[Authorize]
public class ScanHub : Hub
{
}
