using System;
using System.Threading.Tasks;
using HIS.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace HIS.Server.Host.Hubs;

public class ClinicalAlertPublisher : IClinicalAlertPublisher
{
    private readonly IHubContext<ClinicalAlertHub, IClinicalAlertClient> _hub;

    public ClinicalAlertPublisher(IHubContext<ClinicalAlertHub, IClinicalAlertClient> hub)
    {
        _hub = hub;
    }

    public async Task PublishCriticalValueAsync(string encounterId, string patientName, string item, string value, string deptOrWardId)
    {
        var payload = new
        {
            EncounterId = encounterId,
            PatientName = patientName,
            Item = item,
            Value = value,
            DeptOrWardId = deptOrWardId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        if (!string.IsNullOrWhiteSpace(deptOrWardId))
        {
            await _hub.Clients.Groups($"ward_{deptOrWardId}", $"dept_{deptOrWardId}").CriticalValuePublished(payload);
        }
        else
        {
            await _hub.Clients.All.CriticalValuePublished(payload);
        }
    }

    public async Task PublishOrderChangedAsync(string encounterId, string wardId, string orderId, string changeType)
    {
        var payload = new
        {
            EncounterId = encounterId,
            WardId = wardId,
            OrderId = orderId,
            ChangeType = changeType,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        if (!string.IsNullOrWhiteSpace(wardId))
        {
            await _hub.Clients.Group($"ward_{wardId}").MedicalOrderChanged(payload);
        }
        else
        {
            await _hub.Clients.All.MedicalOrderChanged(payload);
        }
    }

    public async Task PublishTimelinessAlertAsync(string encounterId, string doctorId, string docType, string message)
    {
        var payload = new
        {
            EncounterId = encounterId,
            DoctorId = doctorId,
            DocType = docType,
            Message = message,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        if (!string.IsNullOrWhiteSpace(doctorId))
        {
            await _hub.Clients.Group($"user_{doctorId}").EmrTimelinessAlert(payload);
        }
        else
        {
            await _hub.Clients.All.EmrTimelinessAlert(payload);
        }
    }

    public async Task PublishMonitoringAlarmAsync(string encounterId, string bedCode, string alarmType, string severity, string message)
    {
        var payload = new
        {
            EncounterId = encounterId,
            BedCode = bedCode,
            AlarmType = alarmType,
            Severity = severity,
            Message = message,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await _hub.Clients.All.MonitoringAlarm(payload);
    }

    public async Task PublishPrescriptionCommentAsync(string rxId, string doctorId, string message)
    {
        var payload = new
        {
            RxId = rxId,
            DoctorId = doctorId,
            Message = message,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        if (!string.IsNullOrWhiteSpace(doctorId))
        {
            await _hub.Clients.Group($"user_{doctorId}").PrescriptionCommentReceived(payload);
        }
        else
        {
            await _hub.Clients.All.PrescriptionCommentReceived(payload);
        }
    }

    public async Task PublishCarePlanReminderAsync(string encounterId, string wardId, string planTitle)
    {
        var payload = new
        {
            EncounterId = encounterId,
            WardId = wardId,
            PlanTitle = planTitle,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        if (!string.IsNullOrWhiteSpace(wardId))
        {
            await _hub.Clients.Group($"ward_{wardId}").CarePlanReminder(payload);
        }
        else
        {
            await _hub.Clients.All.CarePlanReminder(payload);
        }
    }

    public async Task PublishWorkflowTodoAsync(string receiverUserId, string instanceId, string title, string message)
    {
        var payload = new
        {
            ReceiverUserId = receiverUserId,
            InstanceId = instanceId,
            Title = title,
            Message = message,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        if (!string.IsNullOrWhiteSpace(receiverUserId))
        {
            await _hub.Clients.Group($"user_{receiverUserId}").WorkflowTodoReceived(payload);
        }
        else
        {
            await _hub.Clients.All.WorkflowTodoReceived(payload);
        }
    }
}
