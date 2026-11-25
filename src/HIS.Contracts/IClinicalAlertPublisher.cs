using System.Threading.Tasks;

namespace HIS.Contracts;

public interface IClinicalAlertPublisher
{
    Task PublishCriticalValueAsync(string encounterId, string patientName, string item, string value, string deptOrWardId);
    Task PublishReportCriticalValueAsync(string alertId, string? deptId, string? wardId);
    Task PublishOrderChangedAsync(string encounterId, string wardId, string orderId, string changeType);
    Task PublishTimelinessAlertAsync(string encounterId, string doctorId, string docType, string message);
    Task PublishMonitoringAlarmAsync(string encounterId, string bedCode, string alarmType, string severity, string message);
    Task PublishPrescriptionCommentAsync(string rxId, string doctorId, string message);
    Task PublishCarePlanReminderAsync(string encounterId, string wardId, string planTitle);
    Task PublishWorkflowTodoAsync(string receiverUserId, string instanceId, string title, string message);
    Task PublishOpdQueueCallAsync(string deptId, string clinicRoomName, string doctorName, int queueNo, string patientName, string ticketAction);
}
