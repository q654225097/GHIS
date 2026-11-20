using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace HIS.Server.Host.Hubs;

public interface IClinicalAlertClient
{
    Task CriticalValuePublished(object payload);
    Task MedicalOrderChanged(object payload);
    Task EmrTimelinessAlert(object payload);
    Task MonitoringAlarm(object payload);
    Task PrescriptionCommentReceived(object payload);
    Task CarePlanReminder(object payload);
    Task WorkflowTodoReceived(object payload);
}

public class ClinicalAlertHub : Hub<IClinicalAlertClient>
{
    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        var user = httpContext?.User;

        string? userId = user?.FindFirst("sub")?.Value;
        string? deptId = user?.FindFirst("dept_id")?.Value;
        string? wardId = user?.FindFirst("ward_id")?.Value;

        if (!string.IsNullOrWhiteSpace(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
        }
        if (!string.IsNullOrWhiteSpace(deptId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"dept_{deptId}");
        }
        if (!string.IsNullOrWhiteSpace(wardId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"ward_{wardId}");
        }

        await base.OnConnectedAsync();
    }

    public async Task JoinDepartmentGroup(string deptId)
    {
        if (!string.IsNullOrWhiteSpace(deptId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"dept_{deptId}");
        }
    }

    public async Task JoinWardGroup(string wardId)
    {
        if (!string.IsNullOrWhiteSpace(wardId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"ward_{wardId}");
        }
    }
}
