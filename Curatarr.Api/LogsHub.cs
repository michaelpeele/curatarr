using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Curatarr.Api.Hubs;

/// <summary>Push-only hub for live log lines. Admin-only.</summary>
[Authorize(Roles = "Admin")]
public class LogsHub : Hub
{
}
