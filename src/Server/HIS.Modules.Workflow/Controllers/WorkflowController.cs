using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.Workflow.Models;
using HIS.Modules.Workflow.Services;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Workflow.Controllers;

[ApiController]
[Route("api/v4/workflow")]
public class WorkflowController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly WorkflowRuntimeService _runtimeService;
    private readonly IUserSession? _userSession;
    private readonly IAuditTrailContract? _auditTrail;
    private readonly IClinicalProcessSignatureContract? _signatureService;

    public WorkflowController(
        IDbConnectionFactory connFactory,
        WorkflowRuntimeService runtimeService,
        IUserSession? userSession = null,
        IAuditTrailContract? auditTrail = null,
        IClinicalProcessSignatureContract? signatureService = null)
    {
        _connFactory = connFactory;
        _runtimeService = runtimeService;
        _userSession = userSession;
        _auditTrail = auditTrail;
        _signatureService = signatureService;
    }

    private string CurrentUserId => _userSession?.UserId?.Trim() ?? string.Empty;
    private bool IsAuthenticated => !string.IsNullOrWhiteSpace(CurrentUserId);
    private string CurrentUserName => _userSession?.DisplayName ?? _userSession?.UserName ?? CurrentUserId;
    private string? CurrentDeptId => _userSession?.ActiveDeptId ?? "DEPT-ADMIN";

    #region BO Types
    [HttpGet("bo-types")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WorkflowBoTypeDto>>>> GetBoTypes(CancellationToken ct)
    {
        var boTypes = await _runtimeService.GetSupportedBoTypesAsync(ct);
        return Ok(ApiResponse<IReadOnlyList<WorkflowBoTypeDto>>.Success(boTypes, "获取业务单据类型成功"));
    }
    #endregion

    #region Definitions CRUD & Publish
    [HttpGet("definitions")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WfDefinition>>>> GetDefinitions(
        [FromQuery] string? boType = null,
        [FromQuery] string? status = null)
    {
        using var conn = _connFactory.CreateConnection();
        string sql = "SELECT * FROM wf_definitions WHERE 1=1";
        var dp = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(boType))
        {
            sql += " AND bo_type = @BoType";
            dp.Add("BoType", boType);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            sql += " AND status = @Status";
            dp.Add("Status", status);
        }

        sql += " ORDER BY def_key ASC, version_num DESC";
        var list = (await conn.QueryAsync<WfDefinition>(sql, dp)).ToList();
        return Ok(ApiResponse<IReadOnlyList<WfDefinition>>.Success(list));
    }

    [HttpGet("definitions/{id}")]
    public async Task<ActionResult<ApiResponse<WfDefinition>>> GetDefinitionById(string id)
    {
        using var conn = _connFactory.CreateConnection();
        var def = await conn.QueryFirstOrDefaultAsync<WfDefinition>(
            "SELECT * FROM wf_definitions WHERE id = @Id", new { Id = id });

        if (def == null)
        {
            return NotFound(ApiResponse<WfDefinition>.Fail(404, "流程定义不存在"));
        }

        return Ok(ApiResponse<WfDefinition>.Success(def));
    }

    public record SaveDefinitionDto(
        string? Id,
        string DefKey,
        string DefName,
        string BoType,
        string? FallbackApproverId,
        string? BoFieldsSchemaJson,
        string? GraphJson
    );

    [RequireRole("ADMIN", "ROLE-ADMIN")]
    [HttpPost("definitions")]
    public async Task<ActionResult<ApiResponse<WfDefinition>>> SaveDefinition(
        [FromBody] SaveDefinitionDto dto,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.DefKey))
        {
            return BadRequest(ApiResponse<WfDefinition>.Fail(400, "流程唯一标识 (defKey) 不能为空"));
        }
        if (string.IsNullOrWhiteSpace(dto.DefName))
        {
            return BadRequest(ApiResponse<WfDefinition>.Fail(400, "流程名称不能为空"));
        }
        if (string.IsNullOrWhiteSpace(dto.BoType))
        {
            return BadRequest(ApiResponse<WfDefinition>.Fail(400, "业务单据类型 (boType) 不能为空"));
        }

        using var conn = _connFactory.CreateConnection();
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        WfDefinition? def = null;
        if (!string.IsNullOrWhiteSpace(dto.Id))
        {
            def = await conn.QueryFirstOrDefaultAsync<WfDefinition>(
                "SELECT * FROM wf_definitions WHERE id = @Id", new { Id = dto.Id });
        }

        if (def != null)
        {
            // If already published, creating a new draft version
            if (def.Status == "PUBLISHED")
            {
                def = new WfDefinition
                {
                    Id = Guid.NewGuid().ToString("D"),
                    DefKey = dto.DefKey,
                    DefName = dto.DefName,
                    BoType = dto.BoType,
                    VersionNum = def.VersionNum + 1,
                    Status = "DRAFT",
                    FallbackApproverId = dto.FallbackApproverId,
                    BoFieldsSchemaJson = dto.BoFieldsSchemaJson,
                    GraphJson = dto.GraphJson,
                    CreatedAtMs = now,
                    CreatedBy = CurrentUserId,
                    UpdatedAtMs = now,
                    UpdatedBy = CurrentUserId,
                    VersionNo = 1
                };

                await conn.ExecuteAsync(@"
                    INSERT INTO wf_definitions (id, def_key, def_name, bo_type, version_num, status, bo_fields_schema_json, graph_json, fallback_approver_id, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                    VALUES (@Id, @DefKey, @DefName, @BoType, @VersionNum, @Status, @BoFieldsSchemaJson, @GraphJson, @FallbackApproverId, 1, @CreatedAtMs, @CreatedBy, @UpdatedAtMs, @UpdatedBy, 1)",
                    def);
            }
            else
            {
                // Update draft
                def.DefName = dto.DefName;
                def.FallbackApproverId = dto.FallbackApproverId;
                def.BoFieldsSchemaJson = dto.BoFieldsSchemaJson;
                def.GraphJson = dto.GraphJson;
                def.UpdatedAtMs = now;
                def.UpdatedBy = CurrentUserId;

                await conn.ExecuteAsync(@"
                    UPDATE wf_definitions 
                    SET def_name = @DefName, fallback_approver_id = @FallbackApproverId, bo_fields_schema_json = @BoFieldsSchemaJson, graph_json = @GraphJson, updated_at_ms = @UpdatedAtMs, updated_by = @UpdatedBy, version_no = version_no + 1
                    WHERE id = @Id",
                    def);
            }
        }
        else
        {
            def = new WfDefinition
            {
                Id = Guid.NewGuid().ToString("D"),
                DefKey = dto.DefKey,
                DefName = dto.DefName,
                BoType = dto.BoType,
                VersionNum = 1,
                Status = "DRAFT",
                FallbackApproverId = dto.FallbackApproverId,
                BoFieldsSchemaJson = dto.BoFieldsSchemaJson,
                GraphJson = dto.GraphJson,
                CreatedAtMs = now,
                CreatedBy = CurrentUserId,
                UpdatedAtMs = now,
                UpdatedBy = CurrentUserId,
                VersionNo = 1
            };

            await conn.ExecuteAsync(@"
                INSERT INTO wf_definitions (id, def_key, def_name, bo_type, version_num, status, bo_fields_schema_json, graph_json, fallback_approver_id, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES (@Id, @DefKey, @DefName, @BoType, @VersionNum, @Status, @BoFieldsSchemaJson, @GraphJson, @FallbackApproverId, 1, @CreatedAtMs, @CreatedBy, @UpdatedAtMs, @UpdatedBy, 1)",
                def);
        }

        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync(
                "WORKFLOW_DEF_SAVE",
                "wf_definitions",
                def.Id,
                $"保存流程设计草稿: {def.DefName} (v{def.VersionNum})",
                CurrentUserId,
                CurrentUserName,
                CurrentDeptId,
                ct);
        }

        return Ok(ApiResponse<WfDefinition>.Success(def, "流程设计已保存为草稿"));
    }

    [RequireRole("ADMIN", "ROLE-ADMIN")]
    [HttpPost("definitions/{id}/publish")]
    public async Task<ActionResult<ApiResponse<WfDefinition>>> PublishDefinition(string id, CancellationToken ct)
    {
        try
        {
            var published = await _runtimeService.PublishDefinitionAsync(id, CurrentUserId, ct);

            if (_auditTrail != null)
            {
                await _auditTrail.RecordAuditAsync(
                    "WORKFLOW_DEF_PUBLISH",
                    "wf_definitions",
                    published.Id,
                    $"发布流程新版本: {published.DefName} (v{published.VersionNum})",
                    CurrentUserId,
                    CurrentUserName,
                    CurrentDeptId,
                    ct);
            }

            return Ok(ApiResponse<WfDefinition>.Success(published, $"流程 [{published.DefName}] 第 {published.VersionNum} 版发布成功！"));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<WfDefinition>.Fail(400, ex.Message));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<WfDefinition>.Fail(500, ex.Message));
        }
    }

    [HttpGet("definitions/{defKey}/versions")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WfDefinition>>>> GetVersions(string defKey)
    {
        using var conn = _connFactory.CreateConnection();
        var versions = (await conn.QueryAsync<WfDefinition>(
            "SELECT * FROM wf_definitions WHERE def_key = @DefKey ORDER BY version_num DESC",
            new { DefKey = defKey })).ToList();

        return Ok(ApiResponse<IReadOnlyList<WfDefinition>>.Success(versions));
    }
    #endregion

    #region Instances & Execution
    public record StartWorkflowRequestDto(
        string BoType,
        string BusinessKey,
        Dictionary<string, object?>? BoVariables
    );

    [HttpPost("instances/start")]
    public async Task<ActionResult<ApiResponse<WorkflowStartResultDto>>> StartInstance(
        [FromBody] StartWorkflowRequestDto req,
        CancellationToken ct)
    {
        if (!IsAuthenticated) return Unauthorized(ApiResponse<WorkflowStartResultDto>.Fail(401, "请先登录后发起审批流"));
        var vars = req.BoVariables ?? new Dictionary<string, object?>();
        var result = await _runtimeService.StartWorkflowAsync(
            req.BoType, req.BusinessKey, CurrentUserId, CurrentDeptId, vars, ct);

        if (_auditTrail != null && result.InstanceId != null)
        {
            await _auditTrail.RecordAuditAsync(
                "WORKFLOW_START",
                "wf_instances",
                result.InstanceId,
                $"发起审批流 [{req.BoType}] 单据号: {req.BusinessKey}",
                CurrentUserId,
                CurrentUserName,
                CurrentDeptId,
                ct);
        }

        return Ok(ApiResponse<WorkflowStartResultDto>.Success(result));
    }

    [HttpGet("instances/{id}")]
    public async Task<ActionResult<ApiResponse<WorkflowInstanceDetailDto>>> GetInstanceDetail(string id, CancellationToken ct)
    {
        var detail = await _runtimeService.GetInstanceByIdAsync(id, ct);
        if (detail == null)
        {
            return NotFound(ApiResponse<WorkflowInstanceDetailDto>.Fail(404, "流程实例不存在"));
        }
        return Ok(ApiResponse<WorkflowInstanceDetailDto>.Success(detail));
    }

    [HttpGet("instances/{id}/tracks")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WorkflowTrackItemDto>>>> GetInstanceTracks(string id)
    {
        using var conn = _connFactory.CreateConnection();
        var tracks = (await conn.QueryAsync<WfHistoryTrack>(
            "SELECT * FROM wf_history_tracks WHERE instance_id = @Id ORDER BY occurred_at_ms ASC", new { Id = id })).ToList();

        var dtos = tracks.Select(tr => new WorkflowTrackItemDto(
            tr.Id, tr.InstanceId, tr.TaskId, tr.NodeId, tr.NodeName,
            tr.OperatorUserId, tr.OperatorName, tr.ActionType, tr.Comment, tr.OccurredAtMs
        )).ToList();

        return Ok(ApiResponse<IReadOnlyList<WorkflowTrackItemDto>>.Success(dtos));
    }

    [HttpGet("instances/by-business/{boType}/{businessKey}")]
    public async Task<ActionResult<ApiResponse<WorkflowInstanceDetailDto>>> GetInstanceByBusiness(
        string boType, string businessKey, CancellationToken ct)
    {
        var detail = await _runtimeService.GetInstanceByBusinessAsync(boType, businessKey, ct);
        if (detail == null)
        {
            return NotFound(ApiResponse<WorkflowInstanceDetailDto>.Fail(404, "该单据暂无关联的审批流实例"));
        }
        return Ok(ApiResponse<WorkflowInstanceDetailDto>.Success(detail));
    }


    [HttpGet("instances/my-initiated")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WorkflowTaskItemDto>>>> GetMyInitiatedInstances(CancellationToken ct)
    {
        if (!IsAuthenticated) return Unauthorized(ApiResponse<IReadOnlyList<WorkflowTaskItemDto>>.Fail(401, "请先登录"));
        using var conn = _connFactory.CreateConnection();
        string sql = @"
            SELECT i.id AS TaskId, i.id AS InstanceId, i.instance_no AS InstanceNo,
                   i.def_key AS DefKey, i.bo_type AS BoType, i.business_key AS BusinessKey,
                   COALESCE(i.current_node_ids_json, '') AS NodeId,
                   CASE i.status WHEN 'RUNNING' THEN '审批流转中' WHEN 'APPROVED' THEN '已通过' WHEN 'REJECTED' THEN '已驳回' WHEN 'WITHDRAWN' THEN '已撤回' ELSE i.status END AS NodeName,
                   i.initiator_user_id AS AssigneeUserId, COALESCE(u.display_name, i.initiator_user_id) AS AssigneeName,
                   'SINGLE' AS ApprovalMode, i.status AS Status,
                   NULL AS Comment, i.finished_at_ms AS ActedAtMs, i.initiator_user_id AS InitiatorUserId,
                   COALESCE(u.display_name, i.initiator_user_id) AS InitiatorName, i.initiator_dept_id AS InitiatorDeptId,
                   d.dept_name AS InitiatorDeptName, i.bo_variables_json AS BoVariablesJson,
                   i.created_at_ms AS CreatedAtMs
            FROM wf_instances i
            LEFT JOIN sys_users u ON u.id = i.initiator_user_id
            LEFT JOIN sys_departments d ON d.id = i.initiator_dept_id
            WHERE i.initiator_user_id = @UserId
            ORDER BY i.created_at_ms DESC LIMIT 100";
        var list = (await conn.QueryAsync<WorkflowTaskItemDto>(sql, new { UserId = CurrentUserId })).ToList();
        return Ok(ApiResponse<IReadOnlyList<WorkflowTaskItemDto>>.Success(list));
    }

    [HttpPost("instances/{id}/withdraw")]
    public async Task<ActionResult<ApiResponse<WorkflowActionResultDto>>> WithdrawInstance(
        string id,
        [FromBody] WorkflowActionRequestDto? req,
        CancellationToken ct)
    {
        if (!IsAuthenticated) return Unauthorized(ApiResponse<WorkflowActionResultDto>.Fail(401, "请先登录后撤回审批流"));
        var result = await _runtimeService.WithdrawInstanceAsync(id, CurrentUserId, req?.Comment, ct);

        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync(
                "WORKFLOW_WITHDRAW",
                "wf_instances",
                id,
                $"发起人撤回流程: {req?.Comment}",
                CurrentUserId,
                CurrentUserName,
                CurrentDeptId,
                ct);
        }

        return Ok(ApiResponse<WorkflowActionResultDto>.Success(result));
    }
    #endregion

    #region Tasks & Notifications
    [HttpGet("tasks/my-todo")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WorkflowTaskItemDto>>>> GetMyTodoTasks(CancellationToken ct)
    {
        using var conn = _connFactory.CreateConnection();
        string sql = @"
            SELECT t.id AS TaskId, t.instance_id AS InstanceId, i.instance_no AS InstanceNo,
                   i.def_key AS DefKey, i.bo_type AS BoType, i.business_key AS BusinessKey,
                   t.node_id AS NodeId, t.node_name AS NodeName, t.assignee_user_id AS AssigneeUserId,
                   t.assignee_name AS AssigneeName, t.approval_mode AS ApprovalMode, t.status AS Status,
                   t.comment AS Comment, t.acted_at_ms AS ActedAtMs, i.initiator_user_id AS InitiatorUserId,
                   u.display_name AS InitiatorName, i.initiator_dept_id AS InitiatorDeptId,
                   d.dept_name AS InitiatorDeptName, i.bo_variables_json AS BoVariablesJson,
                   t.created_at_ms AS CreatedAtMs
            FROM wf_tasks t
            INNER JOIN wf_instances i ON i.id = t.instance_id
            LEFT JOIN sys_users u ON u.id = i.initiator_user_id
            LEFT JOIN sys_departments d ON d.id = i.initiator_dept_id
            WHERE t.assignee_user_id = @UserId AND t.status = 'PENDING'
            ORDER BY t.created_at_ms DESC";

        var list = (await conn.QueryAsync<WorkflowTaskItemDto>(sql, new { UserId = CurrentUserId })).ToList();
        return Ok(ApiResponse<IReadOnlyList<WorkflowTaskItemDto>>.Success(list));
    }

    [HttpGet("tasks/my-done")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WorkflowTaskItemDto>>>> GetMyDoneTasks(CancellationToken ct)
    {
        using var conn = _connFactory.CreateConnection();
        string sql = @"
            SELECT t.id AS TaskId, t.instance_id AS InstanceId, i.instance_no AS InstanceNo,
                   i.def_key AS DefKey, i.bo_type AS BoType, i.business_key AS BusinessKey,
                   t.node_id AS NodeId, t.node_name AS NodeName, t.assignee_user_id AS AssigneeUserId,
                   t.assignee_name AS AssigneeName, t.approval_mode AS ApprovalMode, t.status AS Status,
                   t.comment AS Comment, t.acted_at_ms AS ActedAtMs, i.initiator_user_id AS InitiatorUserId,
                   u.display_name AS InitiatorName, i.initiator_dept_id AS InitiatorDeptId,
                   d.dept_name AS InitiatorDeptName, i.bo_variables_json AS BoVariablesJson,
                   t.created_at_ms AS CreatedAtMs
            FROM wf_tasks t
            INNER JOIN wf_instances i ON i.id = t.instance_id
            LEFT JOIN sys_users u ON u.id = i.initiator_user_id
            LEFT JOIN sys_departments d ON d.id = i.initiator_dept_id
            WHERE t.assignee_user_id = @UserId AND t.status IN ('APPROVED', 'REJECTED', 'RETURNED', 'TRANSFERRED')
            ORDER BY t.acted_at_ms DESC LIMIT 100";

        var list = (await conn.QueryAsync<WorkflowTaskItemDto>(sql, new { UserId = CurrentUserId })).ToList();
        return Ok(ApiResponse<IReadOnlyList<WorkflowTaskItemDto>>.Success(list));
    }

    [HttpGet("notifications/my-cc")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WfNotification>>>> GetMyCcNotifications(CancellationToken ct)
    {
        using var conn = _connFactory.CreateConnection();
        var list = (await conn.QueryAsync<WfNotification>(@"
            SELECT * FROM wf_notifications 
            WHERE receiver_user_id = @UserId AND notify_type = 'CC'
            ORDER BY created_at_ms DESC LIMIT 100",
            new { UserId = CurrentUserId })).ToList();

        return Ok(ApiResponse<IReadOnlyList<WfNotification>>.Success(list));
    }

    public record WorkflowActionRequestDto(
        string ActionType, // APPROVE, REJECT, RETURN_PREV, RETURN_START, TRANSFER
        string? Comment,
        string? DelegateToUserId
    );

    [HttpPost("tasks/{taskId}/action")]
    public async Task<ActionResult<ApiResponse<WorkflowActionResultDto>>> ExecuteTaskAction(
        string taskId,
        [FromBody] WorkflowActionRequestDto req,
        CancellationToken ct)
    {
        if (!IsAuthenticated) return Unauthorized(ApiResponse<WorkflowActionResultDto>.Fail(401, "请先登录后执行审批操作"));
        var result = await _runtimeService.ExecuteTaskActionAsync(
            taskId, CurrentUserId, req.ActionType, req.Comment, req.DelegateToUserId, ct);

        // Sign action with digital signature if available
        if (result.Success && _signatureService != null && _userSession != null && (req.ActionType == "APPROVE" || req.ActionType == "REJECT"))
        {
            try
            {
                await _signatureService.SignClinicalRecordAsync(
                    "WorkflowTask",
                    taskId,
                    req.ActionType,
                    new { TaskId = taskId, Action = req.ActionType, Comment = req.Comment, Operator = CurrentUserId },
                    _userSession,
                    ct);
            }
            catch (Exception sigEx)
            {
                if (_auditTrail != null)
                {
                    await _auditTrail.RecordAuditAsync("WORKFLOW_SIGN_ERROR", "wf_tasks", taskId, $"审批电子签章失败: {sigEx.Message}", CurrentUserId, CurrentUserName, CurrentDeptId, ct);
                }
            }
        }

        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync(
                $"WORKFLOW_{req.ActionType}",
                "wf_tasks",
                taskId,
                $"审批任务操作 [{req.ActionType}]: {req.Comment}",
                CurrentUserId,
                CurrentUserName,
                CurrentDeptId,
                ct);
        }

        return Ok(ApiResponse<WorkflowActionResultDto>.Success(result));
    }
    #endregion
}
