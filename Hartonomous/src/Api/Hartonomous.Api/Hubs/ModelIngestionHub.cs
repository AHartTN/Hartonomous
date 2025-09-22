using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;

namespace Hartonomous.Api.Hubs;

/// <summary>
/// SignalR hub for real-time model ingestion progress updates
/// </summary>
[Authorize]
public class ModelIngestionHub : Hub
{
    public async Task JoinIngestionGroup(string modelId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"ingestion_{modelId}");
    }

    public async Task LeaveIngestionGroup(string modelId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"ingestion_{modelId}");
    }

    public async Task JoinUserGroup()
    {
        var userId = Context.User?.Identity?.Name;
        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}