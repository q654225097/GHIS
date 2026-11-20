using System;
using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Workflow.Models;

public class WfDefinition : BaseEntity
{
    public string DefKey { get; set; } = string.Empty;
    public string DefName { get; set; } = string.Empty;
    public string BoType { get; set; } = string.Empty;
    public int VersionNum { get; set; } = 1;
    public string Status { get; set; } = "DRAFT"; // DRAFT, PUBLISHED, ARCHIVED
    public string? BoFieldsSchemaJson { get; set; }
    public string? GraphJson { get; set; }
    public string? FallbackApproverId { get; set; }
}

public class WfInstance : BaseEntity
{
    public string InstanceNo { get; set; } = string.Empty;
    public string DefinitionId { get; set; } = string.Empty;
    public string DefKey { get; set; } = string.Empty;
    public int DefinitionVersion { get; set; } = 1;
    public string BoType { get; set; } = string.Empty;
    public string BusinessKey { get; set; } = string.Empty;
    public string InitiatorUserId { get; set; } = string.Empty;
    public string? InitiatorDeptId { get; set; }
    public string? CurrentNodeIdsJson { get; set; }
    public string? BoVariablesJson { get; set; }
    public string Status { get; set; } = "RUNNING"; // RUNNING, APPROVED, REJECTED, WITHDRAWN, TERMINATED
    public long StartedAtMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public long? FinishedAtMs { get; set; }
}

public class WfTask : BaseEntity
{
    public string InstanceId { get; set; } = string.Empty;
    public string NodeId { get; set; } = string.Empty;
    public string NodeName { get; set; } = string.Empty;
    public string AssigneeUserId { get; set; } = string.Empty;
    public string? AssigneeName { get; set; }
    public string ApprovalMode { get; set; } = "SINGLE"; // SINGLE, AND_SIGN, OR_SIGN
    public string Status { get; set; } = "PENDING"; // PENDING, APPROVED, REJECTED, RETURNED, TRANSFERRED, CANCELLED
    public string? Comment { get; set; }
    public long? ActedAtMs { get; set; }
    public string? DelegatedFromUserId { get; set; }
}

public class WfHistoryTrack : BaseEntity
{
    public string InstanceId { get; set; } = string.Empty;
    public string? TaskId { get; set; }
    public string NodeId { get; set; } = string.Empty;
    public string NodeName { get; set; } = string.Empty;
    public string OperatorUserId { get; set; } = string.Empty;
    public string? OperatorName { get; set; }
    public string ActionType { get; set; } = string.Empty; // START, APPROVE, REJECT, RETURN_PREV, RETURN_START, WITHDRAW, TRANSFER, CC, AUTO_ROUTE, FINISH
    public string? Comment { get; set; }
    public string? SnapshotJson { get; set; }
    public long OccurredAtMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public class WfBusinessBinding : BaseEntity
{
    public string BoType { get; set; } = string.Empty;
    public string BusinessKey { get; set; } = string.Empty;
    public string InstanceId { get; set; } = string.Empty;
    public string WorkflowStatus { get; set; } = "RUNNING";
    public string CallbackStatus { get; set; } = "SUCCESS";
}

public class WfNotification : BaseEntity
{
    public string InstanceId { get; set; } = string.Empty;
    public string ReceiverUserId { get; set; } = string.Empty;
    public string NotifyType { get; set; } = "TASK_TODO"; // TASK_TODO, CC, RESULT
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public short IsRead { get; set; } = 0;
}
