using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HIS.Contracts;

public record WorkflowBoFieldDto(string FieldKey, string FieldName, string DataType, string? UnitText = null);

public record WorkflowBoTypeDto(
    string BoType,
    string BoTypeName,
    string BusinessKeyField,
    string PrimaryTable,
    IReadOnlyList<WorkflowBoFieldDto> AvailableFields
);

public record WorkflowStartResultDto(
    bool IsWorkflowTriggered,
    string? InstanceId,
    string Status, // PENDING_APPROVAL / APPROVED / BYPASSED
    string? Message
);

public record WorkflowActionResultDto(
    bool Success,
    string NewStatus, // RUNNING / APPROVED / REJECTED / WITHDRAWN / RETURNED
    string Message
);

public record WorkflowTaskItemDto(
    string TaskId,
    string InstanceId,
    string InstanceNo,
    string DefKey,
    string BoType,
    string BusinessKey,
    string NodeId,
    string NodeName,
    string AssigneeUserId,
    string? AssigneeName,
    string ApprovalMode,
    string Status,
    string? Comment,
    long? ActedAtMs,
    string? InitiatorUserId,
    string? InitiatorName,
    string? InitiatorDeptId,
    string? InitiatorDeptName,
    string? BoVariablesJson,
    long CreatedAtMs
);

public record WorkflowTrackItemDto(
    string Id,
    string InstanceId,
    string? TaskId,
    string NodeId,
    string NodeName,
    string OperatorUserId,
    string? OperatorName,
    string ActionType,
    string? Comment,
    long OccurredAtMs
);

public record WorkflowInstanceDetailDto(
    string Id,
    string InstanceNo,
    string DefKey,
    string DefName,
    int DefinitionVersion,
    string BoType,
    string BusinessKey,
    string InitiatorUserId,
    string? InitiatorName,
    string? InitiatorDeptId,
    string? InitiatorDeptName,
    string Status,
    long StartedAtMs,
    long? FinishedAtMs,
    IReadOnlyList<string> CurrentNodeIds,
    string? BoVariablesJson,
    IReadOnlyList<WorkflowTaskItemDto> Tasks,
    IReadOnlyList<WorkflowTrackItemDto> Tracks
);

public interface IWorkflowEngineContract
{
    Task<WorkflowStartResultDto> StartWorkflowAsync(
        string boType,
        string businessKey,
        string initiatorUserId,
        string? initiatorDeptId,
        Dictionary<string, object?> boVariables,
        CancellationToken ct = default);

    Task<WorkflowActionResultDto> ExecuteTaskActionAsync(
        string taskId,
        string operatorUserId,
        string actionType,
        string? comment,
        string? delegateToUserId = null,
        CancellationToken ct = default);

    Task<WorkflowActionResultDto> WithdrawInstanceAsync(
        string instanceId,
        string initiatorUserId,
        string? comment,
        CancellationToken ct = default);

    Task<WorkflowInstanceDetailDto?> GetInstanceByBusinessAsync(
        string boType,
        string businessKey,
        CancellationToken ct = default);

    Task<WorkflowInstanceDetailDto?> GetInstanceByIdAsync(
        string instanceId,
        CancellationToken ct = default);

    Task<IReadOnlyList<WorkflowBoTypeDto>> GetSupportedBoTypesAsync(
        CancellationToken ct = default);
}

public interface IWorkflowBusinessCallback
{
    Task OnWorkflowCompletedAsync(
        string boType,
        string businessKey,
        string finalStatus,
        string? approverUserId,
        long approvedAtMs,
        CancellationToken ct = default);
}

public class WorkflowDefinitionSummaryDto
{
    public string Id { get; set; } = string.Empty;
    public string DefKey { get; set; } = string.Empty;
    public string DefName { get; set; } = string.Empty;
    public string BoType { get; set; } = string.Empty;
    public int VersionNum { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? FallbackApproverId { get; set; }
}

public class WorkflowDefinitionDetailDto
{
    public string Id { get; set; } = string.Empty;
    public string DefKey { get; set; } = string.Empty;
    public string DefName { get; set; } = string.Empty;
    public string BoType { get; set; } = string.Empty;
    public int VersionNum { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? FallbackApproverId { get; set; }
    public string? BoFieldsSchemaJson { get; set; }
    public string? GraphJson { get; set; }
}

public class WorkflowNotificationDto
{
    public string Id { get; set; } = string.Empty;
    public string InstanceId { get; set; } = string.Empty;
    public string NotifyType { get; set; } = "CC";
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public long CreatedAtMs { get; set; }
}

public class WorkflowGraphDto
{
    public List<WorkflowNodeDto> Nodes { get; set; } = new();
    public List<WorkflowEdgeDto> Edges { get; set; } = new();
}

public class WorkflowNodeDto
{
    public string Id { get; set; } = string.Empty;
    public string NodeId { get => Id; set => Id = value; }
    public string Name { get; set; } = string.Empty;
    public string NodeName { get => Name; set => Name = value; }
    public string NodeType { get; set; } = "APPROVAL";
    public double X { get; set; }
    public double Y { get; set; }
    public AssigneeConfigDto? AssigneeConfig { get; set; }
    public string? AssigneeStrategy { get => AssigneeConfig?.Strategy; set { AssigneeConfig ??= new(); AssigneeConfig.Strategy = value ?? "FIXED_USER"; } }
    public string? ApprovalMode { get; set; } = "SINGLE";
    public string? FallbackApproverId { get; set; }
    public int? EscalateLevels { get => AssigneeConfig?.EscalateLevels; set { AssigneeConfig ??= new(); AssigneeConfig.EscalateLevels = value; } }
    public List<string>? CcReceiverUserIds { get; set; }
}

public class AssigneeConfigDto
{
    public string Strategy { get; set; } = "FIXED_USER";
    public List<string>? FixedUserIds { get; set; }
    public string? RoleCode { get; set; }
    public bool? DeptScoped { get; set; }
    public int? EscalateLevels { get; set; } = 1;
    public string? BoExpression { get; set; }
}

public class WorkflowEdgeDto
{
    public string Id { get; set; } = string.Empty;
    public string EdgeId { get => Id; set => Id = value; }
    public string SourceNodeId { get; set; } = string.Empty;
    public string TargetNodeId { get; set; } = string.Empty;
    public string? Label { get; set; }
    public bool IsDefault { get; set; }
    public int Priority { get; set; }
    public WorkflowConditionGroupDto? ConditionGroup { get; set; }
    public WorkflowConditionGroupDto? Condition { get => ConditionGroup; set => ConditionGroup = value; }
}

public class WorkflowConditionRuleDto
{
    public string Field { get; set; } = string.Empty;
    public string Operator { get; set; } = "==";
    public string Value { get; set; } = string.Empty;
    public string? LogicalRelation { get; set; } = "AND";
}

public class WorkflowConditionGroupDto
{
    public string LogicalOp { get; set; } = "AND";
    public string LogicalOperator { get => LogicalOp; set => LogicalOp = value; }
    public List<WorkflowConditionRuleDto> Rules { get; set; } = new();
    public List<WorkflowConditionRuleDto> Conditions { get => Rules; set => Rules = value; }
}

// Aliases for unified server/client graph serialization
public class WorkflowGraphNodeDto : WorkflowNodeDto { }
public class WorkflowGraphEdgeDto : WorkflowEdgeDto { }
public class WorkflowConditionItemDto : WorkflowConditionRuleDto { }
