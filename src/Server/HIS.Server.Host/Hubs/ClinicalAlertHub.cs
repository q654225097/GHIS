using System;
using System.Linq;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Security;
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
    Task OpdQueueCalled(object payload);
}

public class ClinicalAlertHub : Hub<IClinicalAlertClient>
{
    public override async Task OnConnectedAsync()
    {
        if (Context.GetHttpContext()?.User is not { Identity.IsAuthenticated: true } user)
        {
            Context.Abort();
            return;
        }
        var session = UserSession.FromClaims(user);
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{session.UserId}");
        if (!string.IsNullOrWhiteSpace(session.ActiveDeptId))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"dept_{session.ActiveDeptId}");
        foreach (string wardId in session.LinkedWardIds.Append(session.ActiveWardId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
            await Groups.AddToGroupAsync(Context.ConnectionId, $"ward_{wardId}");

        await base.OnConnectedAsync();
    }
}
