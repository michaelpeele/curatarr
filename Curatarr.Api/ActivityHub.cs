using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Curatarr.Api.Hubs;

/// <summary>
/// Push-only, Admin-only hub. The ingest service broadcasts ActivityCreated
/// after each batch is persisted; clients never send anything back.
/// </summary>
[Authorize(Roles = "Admin")]
public class ActivityHub : Hub
{
}
