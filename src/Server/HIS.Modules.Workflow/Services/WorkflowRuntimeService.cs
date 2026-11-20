using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Workflow.Models;

namespace HIS.Modules.Workflow.Services;

public class WorkflowRuntimeService : IWorkflowEngineContract
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly WorkflowAssigneeResolver _assigneeResolver;
    private readonly IWorkflowBusinessCallback? _businessCallback;
    private readonly IClinicalAlertPublisher? _alertPublisher;
    private readonly IAuditTrailContract? _auditTrail;

    public WorkflowRuntimeService(
        IDbConnectionFactory connFactory,
        WorkflowAssigneeResolver assigneeResolver,
        IWorkflowBusinessCallback? businessCallback = null,
        IClinicalAlertPublisher? alertPublisher = null,
        IAuditTrailContract? auditTrail = null)
    {
        _connFactory = connFactory;
        _assigneeResolver = assigneeResolver;
        _businessCallback = businessCallback;
        _alertPublisher = alertPublisher;
        _auditTrail = auditTrail;
    }

    public static readonly IReadOnlyList<WorkflowBoTypeDto> SupportedBoTypes = new List<WorkflowBoTypeDto>
    {
        new("CSM_REQUISITION", "耗材请领审批流", "id", "csm_requisitions", new List<WorkflowBoFieldDto>
        {
            new("requisitionNo", "请领单号", "string"),
            new("deptId", "请领科室", "string"),
            new("totalAmount", "请领总金额", "decimal", "元"),
            new("urgency", "加急程度", "string"),
            new("isSterile", "包含灭菌包", "boolean")
        }),
        new("INV_TRANSFER", "药品调拨审批流", "id", "inv_transfer_orders", new List<WorkflowBoFieldDto>
        {
            new("transferNo", "调拨单号", "string"),
            new("sourceWarehouseId", "调出库房", "string"),
            new("targetWarehouseId", "调入库房", "string"),
            new("totalRetailAmount", "调拨总金额", "decimal", "元"),
            new("drugCategory", "药品类别", "string")
        }),
        new("INV_STOCK_TAKE", "药品盘点审批流", "id", "inv_stock_takes", new List<WorkflowBoFieldDto>
        {
            new("takeNo", "盘点单号", "string"),
            new("warehouseId", "盘点库房", "string"),
            new("totalDiffAmount", "盘点差异金额", "decimal", "元"),
            new("itemMismatchCount", "差异品规数", "number", "项")
        }),
        new("BLOOD_REQUEST", "临床用血审批流", "id", "mtc_blood_requests", new List<WorkflowBoFieldDto>
        {
            new("bloodVolumeMl", "用血量", "number", "ml"),
            new("bloodProductCode", "血液品种", "string"),
            new("aboType", "ABO血型", "string"),
            new("isEmergency", "是否急诊用血", "boolean"),
            new("surgeryLevel", "手术等级", "number")
        }),
        new("SURGERY_SCHEDULE", "手术排程审批流", "id", "mte_surgery_schedules", new List<WorkflowBoFieldDto>
        {
            new("surgeryCode", "手术编码", "string"),
            new("surgeryName", "手术名称", "string"),
            new("surgeryLevel", "手术级别", "number", "级"),
            new("isUrgent", "是否急症手术", "boolean")
        }),
        new("EMR_ACCESS_GRANT", "病历跨科外院调阅授权审批流", "id", "sec_emr_access_grants", new List<WorkflowBoFieldDto>
        {
            new("grantType", "授权类型", "string"),
            new("targetDeptId", "申请调阅科室", "string"),
            new("durationHours", "申请时限", "number", "小时"),
            new("isExternalOrg", "是否跨院共享", "boolean")
        }),
        new("ORD_SPECIAL_DRUG", "特殊级抗菌药与越权医嘱审批流", "id", "ord_medical_orders", new List<WorkflowBoFieldDto>
        {
            new("orderNo", "医嘱编号", "string"),
            new("itemCode", "药品编码", "string"),
            new("itemName", "药品名称", "string"),
            new("dosage", "单次剂量", "decimal"),
            new("antibioticLevel", "抗菌药物级别", "number", "级")
        }),
        new("IPD_DISCHARGE_REQUEST", "欠费与特殊出院审批流", "id", "ipd_discharge_requests", new List<WorkflowBoFieldDto>
        {
            new("admissionNo", "住院号", "string"),
            new("debtAmount", "欠费金额", "decimal", "元"),
            new("isForced", "是否强行出院", "boolean")
        })
    };

    public Task<IReadOnlyList<WorkflowBoTypeDto>> GetSupportedBoTypesAsync(CancellationToken ct = default)
    {
        return Task.FromResult(SupportedBoTypes);
    }

    #region Graph Validation
    public static bool ValidateGraph(WorkflowGraphDto? graph, out List<string> errors)
    {
        errors = new List<string>();
        if (graph == null || graph.Nodes == null || graph.Nodes.Count == 0)
        {
            errors.Add("流程画布不能为空，必须包含节点。");
            return false;
        }

        var startNodes = graph.Nodes.Where(n => string.Equals(n.NodeType, "START", StringComparison.OrdinalIgnoreCase)).ToList();
        if (startNodes.Count != 1)
        {
            errors.Add($"流程必须有且仅有 1 个【开始节点】(当前有 {startNodes.Count} 个)。");
        }

        var endNodes = graph.Nodes.Where(n => string.Equals(n.NodeType, "END", StringComparison.OrdinalIgnoreCase)).ToList();
        if (endNodes.Count < 1)
        {
            errors.Add("流程必须包含至少 1 个【结束节点】。");
        }

        // Check node IDs uniqueness
        var duplicateIds = graph.Nodes.GroupBy(n => n.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicateIds.Count > 0)
        {
            errors.Add($"存在重复的节点编号: {string.Join(", ", duplicateIds)}。");
        }

        // Check Exclusive Gateways default outgoing transition
        var gateways = graph.Nodes.Where(n => string.Equals(n.NodeType, "EXCLUSIVE_GATEWAY", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var gw in gateways)
        {
            var outgoing = graph.Edges.Where(e => e.SourceNodeId == gw.Id).ToList();
            if (outgoing.Count == 0)
            {
                errors.Add($"条件排他网关 [{gw.Name ?? gw.Id}] 未配置任何连线出口。");
            }
            else
            {
                int defaultCount = outgoing.Count(e => e.IsDefault);
                if (defaultCount != 1)
                {
                    errors.Add($"条件排他网关 [{gw.Name ?? gw.Id}] 必须有且仅有 1 条默认兜底分支(isDefault=true)，当前配置了 {defaultCount} 条。");
                }
            }
        }

        // Check Approval Nodes assignee config
        var approvalNodes = graph.Nodes.Where(n => string.Equals(n.NodeType, "APPROVAL", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var appNode in approvalNodes)
        {
            if (appNode.AssigneeConfig == null || string.IsNullOrWhiteSpace(appNode.AssigneeConfig.Strategy))
            {
                errors.Add($"审批节点 [{appNode.Name ?? appNode.Id}] 必须配置审批人指派策略。");
            }
        }

        // Reachability analysis from START node
        if (startNodes.Count == 1)
        {
            var startNode = startNodes[0];
            var visited = new HashSet<string>();
            var queue = new Queue<string>();
            queue.Enqueue(startNode.Id);
            visited.Add(startNode.Id);

            var adjacency = graph.Edges.GroupBy(e => e.SourceNodeId).ToDictionary(g => g.Key, g => g.Select(e => e.TargetNodeId).ToList());

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (adjacency.TryGetValue(cur, out var targets))
                {
                    foreach (var t in targets)
                    {
                        if (visited.Add(t))
                        {
                            queue.Enqueue(t);
                        }
                    }
                }
            }

            var unreachableNodes = graph.Nodes.Where(n => !visited.Contains(n.Id)).Select(n => n.Name ?? n.Id).ToList();
            if (unreachableNodes.Count > 0)
            {
                errors.Add($"存在从开始节点不可达的孤立节点: {string.Join(", ", unreachableNodes)}。");
            }
        }

        // Cycle detection for automatic nodes (START/EXCLUSIVE_GATEWAY/CC) to prevent infinite recursion
        var autoNodeTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "START", "EXCLUSIVE_GATEWAY", "CC" };
        var autoNodes = graph.Nodes.Where(n => autoNodeTypes.Contains(n.NodeType ?? "")).Select(n => n.Id).ToHashSet();
        var autoAdj = graph.Edges.Where(e => autoNodes.Contains(e.SourceNodeId) && autoNodes.Contains(e.TargetNodeId))
            .GroupBy(e => e.SourceNodeId).ToDictionary(g => g.Key, g => g.Select(e => e.TargetNodeId).ToList());
        var color = new Dictionary<string, int>(); // 0=unvisited, 1=visiting, 2=visited
        bool HasAutoCycle(string u)
        {
            color[u] = 1;
            if (autoAdj.TryGetValue(u, out var nbrs))
            {
                foreach (var v in nbrs)
                {
                    int c = color.TryGetValue(v, out var cv) ? cv : 0;
                    if (c == 1 || (c == 0 && HasAutoCycle(v))) return true;
                }
            }
            color[u] = 2;
            return false;
        }
        foreach (var nid in autoNodes)
        {
            if ((color.TryGetValue(nid, out var c) ? c : 0) == 0 && HasAutoCycle(nid))
            {
                errors.Add("流程图存在无人工审批节点的自动闭环死循环（条件网关/抄送节点成环），禁止发布。");
                break;
            }
        }

        return errors.Count == 0;
    }
    #endregion

    #region Save Draft Definition
    public async Task<WfDefinition> SaveDefinitionDraftAsync(WfDefinition draft, string operatorUserId = "SYSTEM", CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        WfDefinition? existing = null;
        if (!string.IsNullOrWhiteSpace(draft.Id))
        {
            existing = await conn.QueryFirstOrDefaultAsync<WfDefinition>(
                "SELECT * FROM wf_definitions WHERE id = @Id", new { Id = draft.Id });
        }

        if (existing != null && existing.Status != "PUBLISHED")
        {
            existing.DefKey = draft.DefKey;
            existing.DefName = draft.DefName;
            existing.BoType = draft.BoType;
            existing.FallbackApproverId = draft.FallbackApproverId;
            existing.BoFieldsSchemaJson = draft.BoFieldsSchemaJson;
            existing.GraphJson = draft.GraphJson;
            existing.UpdatedAtMs = now;
            existing.UpdatedBy = operatorUserId;

            await conn.ExecuteAsync(@"
                UPDATE wf_definitions
                SET def_key = @DefKey, def_name = @DefName, bo_type = @BoType,
                    fallback_approver_id = @FallbackApproverId, bo_fields_schema_json = @BoFieldsSchemaJson,
                    graph_json = @GraphJson, updated_at_ms = @UpdatedAtMs, updated_by = @UpdatedBy,
                    version_no = version_no + 1
                WHERE id = @Id", existing);
            return existing;
        }

        int maxVer = await conn.ExecuteScalarAsync<int>(
            "SELECT COALESCE(MAX(version_num), 0) FROM wf_definitions WHERE def_key = @DefKey",
            new { DefKey = draft.DefKey });

        var newDef = new WfDefinition
        {
            Id = string.IsNullOrWhiteSpace(draft.Id) || (existing != null && existing.Status == "PUBLISHED") ? Guid.NewGuid().ToString("D") : draft.Id,
            DefKey = draft.DefKey,
            DefName = draft.DefName,
            BoType = draft.BoType,
            VersionNum = (existing != null && existing.Status == "PUBLISHED") ? existing.VersionNum + 1 : (maxVer > 0 ? maxVer : 1),
            Status = "DRAFT",
            FallbackApproverId = draft.FallbackApproverId,
            BoFieldsSchemaJson = draft.BoFieldsSchemaJson,
            GraphJson = draft.GraphJson,
            CreatedAtMs = now,
            CreatedBy = operatorUserId,
            UpdatedAtMs = now,
            UpdatedBy = operatorUserId,
            SchemaVersion = 1,
            VersionNo = 1
        };

        await conn.ExecuteAsync(@"
            INSERT INTO wf_definitions (id, def_key, def_name, bo_type, version_num, status, fallback_approver_id, bo_fields_schema_json, graph_json, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @DefKey, @DefName, @BoType, @VersionNum, @Status, @FallbackApproverId, @BoFieldsSchemaJson, @GraphJson, 1, @CreatedAtMs, @CreatedBy, @UpdatedAtMs, @UpdatedBy, 1)",
            newDef);

        return newDef;
    }
    #endregion

    #region Publish Definition
    public async Task<WfDefinition> PublishDefinitionAsync(string definitionId, string publisherUserId, CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();
        var def = await conn.QueryFirstOrDefaultAsync<WfDefinition>(
            "SELECT * FROM wf_definitions WHERE id = @Id", new { Id = definitionId });

        if (def == null)
        {
            throw new InvalidOperationException($"流程定义不存在: {definitionId}");
        }

        WorkflowGraphDto? graph = null;
        if (!string.IsNullOrWhiteSpace(def.GraphJson))
        {
            try
            {
                graph = JsonSerializer.Deserialize<WorkflowGraphDto>(def.GraphJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"流程图形配置 JSON 解析失败: {ex.Message}");
            }
        }

        if (!ValidateGraph(graph, out var errors))
        {
            throw new ArgumentException($"流程校验不通过，无法发布:\n{string.Join("\n", errors)}");
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Archive previous published definitions with same bo_type or def_key
        await conn.ExecuteAsync(@"
            UPDATE wf_definitions 
            SET status = 'ARCHIVED', updated_at_ms = @Now, updated_by = @Publisher 
            WHERE (bo_type = @BoType OR def_key = @DefKey) AND status = 'PUBLISHED' AND id <> @Id",
            new { BoType = def.BoType, DefKey = def.DefKey, Now = now, Publisher = publisherUserId, Id = def.Id });

        // Calculate max version_num
        int maxVer = await conn.ExecuteScalarAsync<int>(
            "SELECT COALESCE(MAX(version_num), 0) FROM wf_definitions WHERE def_key = @DefKey AND id <> @Id",
            new { DefKey = def.DefKey, Id = def.Id });

        def.VersionNum = maxVer + 1;
        def.Status = "PUBLISHED";
        def.UpdatedAtMs = now;
        def.UpdatedBy = publisherUserId;

        await conn.ExecuteAsync(@"
            UPDATE wf_definitions 
            SET version_num = @VersionNum, status = @Status, updated_at_ms = @UpdatedAtMs, updated_by = @UpdatedBy, version_no = version_no + 1
            WHERE id = @Id",
            new { def.VersionNum, def.Status, def.UpdatedAtMs, def.UpdatedBy, def.Id });

        return def;
    }
    #endregion

    #region Start Workflow
    public async Task<WorkflowStartResultDto> StartWorkflowAsync(
        string boType,
        string businessKey,
        string initiatorUserId,
        string? initiatorDeptId,
        Dictionary<string, object?> boVariables,
        CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();

        // 0. Prevent duplicate RUNNING workflow instance for same (boType, businessKey)
        var existingRunningId = await conn.ExecuteScalarAsync<string?>(
            "SELECT id FROM wf_instances WHERE bo_type = @BoType AND business_key = @BusinessKey AND status = 'RUNNING' LIMIT 1",
            new { BoType = boType, BusinessKey = businessKey });
        if (!string.IsNullOrWhiteSpace(existingRunningId))
        {
            return new WorkflowStartResultDto(true, existingRunningId, "RUNNING", "该单据已存在在途审批流程，请勿重复提交。");
        }

        // 1. Check if a published definition exists for bo_type
        var definition = await conn.QueryFirstOrDefaultAsync<WfDefinition>(@"
            SELECT * FROM wf_definitions 
            WHERE bo_type = @BoType AND status = 'PUBLISHED' 
            ORDER BY updated_at_ms DESC, version_num DESC LIMIT 1",
            new { BoType = boType });

        if (definition == null || string.IsNullOrWhiteSpace(definition.GraphJson))
        {
            // Direct pass-through if no workflow is configured
            return new WorkflowStartResultDto(false, null, "APPROVED", "未配置已发布审批流，单据直接自动审批通过。");
        }

        WorkflowGraphDto graph;
        try
        {
            graph = JsonSerializer.Deserialize<WorkflowGraphDto>(definition.GraphJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"流程图反序列化失败: {ex.Message}");
        }

        var startNode = graph.Nodes.FirstOrDefault(n => string.Equals(n.NodeType, "START", StringComparison.OrdinalIgnoreCase));
        if (startNode == null)
        {
            throw new InvalidOperationException("流程定义缺少开始节点。");
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string instanceId = Guid.NewGuid().ToString("D");
        string instanceNo = $"WF-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}";

        var instance = new WfInstance
        {
            Id = instanceId,
            InstanceNo = instanceNo,
            DefinitionId = definition.Id,
            DefKey = definition.DefKey,
            DefinitionVersion = definition.VersionNum,
            BoType = boType,
            BusinessKey = businessKey,
            InitiatorUserId = initiatorUserId,
            InitiatorDeptId = initiatorDeptId,
            BoVariablesJson = JsonSerializer.Serialize(boVariables),
            Status = "RUNNING",
            StartedAtMs = now,
            CreatedAtMs = now,
            CreatedBy = initiatorUserId,
            UpdatedAtMs = now,
            UpdatedBy = initiatorUserId,
            VersionNo = 1
        };

        // Insert instance & binding
        await conn.ExecuteAsync(@"
            INSERT INTO wf_instances (id, instance_no, definition_id, def_key, definition_version, bo_type, business_key, initiator_user_id, initiator_dept_id, current_node_ids_json, bo_variables_json, status, started_at_ms, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @InstanceNo, @DefinitionId, @DefKey, @DefinitionVersion, @BoType, @BusinessKey, @InitiatorUserId, @InitiatorDeptId, @CurrentNodeIdsJson, @BoVariablesJson, @Status, @StartedAtMs, 1, @CreatedAtMs, @CreatedBy, @UpdatedAtMs, @UpdatedBy, 1)",
            instance);

        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO wf_business_bindings (id, bo_type, business_key, instance_id, workflow_status, callback_status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @BoType, @BusinessKey, @InstanceId, 'RUNNING', 'SUCCESS', 1, @Now, @UserId, @Now, @UserId, 1)",
            new { Id = Guid.NewGuid().ToString("D"), BoType = boType, BusinessKey = businessKey, InstanceId = instanceId, Now = now, UserId = initiatorUserId });

        // Record START track
        await RecordTrackAsync(conn, instanceId, null, startNode.Id, startNode.Name ?? "开始", initiatorUserId, "发起人", "START", "提交单据并启动审批流程", instance.BoVariablesJson, now);

        // Drive flow from START
        await DriveWorkflowForwardAsync(conn, instance, definition, graph, new[] { startNode.Id }, boVariables, now, ct);

        // Re-read instance to see if it immediately finished
        var updatedStatus = await conn.ExecuteScalarAsync<string>("SELECT status FROM wf_instances WHERE id = @Id", new { Id = instanceId });

        return new WorkflowStartResultDto(true, instanceId, updatedStatus ?? "RUNNING", "审批流程已成功启动。");
    }
    #endregion

    #region Flow Driving Engine
    private async Task DriveWorkflowForwardAsync(
        DbConnection conn,
        WfInstance instance,
        WfDefinition definition,
        WorkflowGraphDto graph,
        IEnumerable<string> fromNodeIds,
        IReadOnlyDictionary<string, object?> boVariables,
        long now,
        CancellationToken ct,
        string? lastApproverUserId = null,
        int hopDepth = 0)
    {
        if (hopDepth > 50)
        {
            throw new InvalidOperationException("流程自动流转超过最大跳数限制(50)，疑似存在闭环死循环，已强制中止。");
        }
        var targetNodes = new List<WorkflowNodeDto>();

        foreach (var fromNodeId in fromNodeIds)
        {
            var nextNodes = ResolveNextNodes(graph, fromNodeId, boVariables, out var matchedEdges);

            foreach (var edge in matchedEdges)
            {
                if (!string.IsNullOrWhiteSpace(edge.Label))
                {
                    await RecordTrackAsync(conn, instance.Id, null, edge.SourceNodeId, "条件分支", "SYSTEM", "系统路由", "AUTO_ROUTE", $"按条件路由至分支: {edge.Label}", null, now);
                }
            }

            targetNodes.AddRange(nextNodes);
        }

        var newPendingNodeIds = new List<string>();

        foreach (var node in targetNodes)
        {
            string nodeType = node.NodeType?.ToUpperInvariant() ?? "APPROVAL";

            if (nodeType == "CC")
            {
                // Handle Carbon Copy node without blocking
                await HandleCcNodeAsync(conn, instance, definition, node, boVariables, now, ct);

                // CC continues to next nodes
                await DriveWorkflowForwardAsync(conn, instance, definition, graph, new[] { node.Id }, boVariables, now, ct, lastApproverUserId, hopDepth + 1);
            }
            else if (nodeType == "END")
            {
                // Complete Workflow with APPROVED
                await CompleteWorkflowInstanceAsync(conn, instance, "APPROVED", lastApproverUserId ?? instance.InitiatorUserId, now, ct);
                return;
            }
            else if (nodeType == "EXCLUSIVE_GATEWAY")
            {
                // Traverse gateway recursively
                await DriveWorkflowForwardAsync(conn, instance, definition, graph, new[] { node.Id }, boVariables, now, ct, lastApproverUserId, hopDepth + 1);
            }
            else if (nodeType == "APPROVAL")
            {
                // Create pending tasks for resolved assignees
                var assignees = await _assigneeResolver.ResolveAssigneesAsync(
                    node.AssigneeConfig, definition, node, instance.InitiatorUserId, instance.InitiatorDeptId, boVariables, ct);

                string mode = node.ApprovalMode?.ToUpperInvariant() ?? "SINGLE";

                foreach (var assignee in assignees)
                {
                    string taskId = Guid.NewGuid().ToString("D");
                    var task = new WfTask
                    {
                        Id = taskId,
                        InstanceId = instance.Id,
                        NodeId = node.Id,
                        NodeName = node.Name ?? "审批",
                        AssigneeUserId = assignee.UserId,
                        AssigneeName = assignee.DisplayName,
                        ApprovalMode = mode,
                        Status = "PENDING",
                        CreatedAtMs = now,
                        CreatedBy = "SYSTEM",
                        UpdatedAtMs = now,
                        UpdatedBy = "SYSTEM",
                        VersionNo = 1
                    };

                    await conn.ExecuteAsync(@"
                        INSERT INTO wf_tasks (id, instance_id, node_id, node_name, assignee_user_id, assignee_name, approval_mode, status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                        VALUES (@Id, @InstanceId, @NodeId, @NodeName, @AssigneeUserId, @AssigneeName, @ApprovalMode, @Status, 1, @CreatedAtMs, @CreatedBy, @UpdatedAtMs, @UpdatedBy, 1)",
                        task);

                    // Create Notification
                    string title = $"待审批: {definition.DefName} ({instance.InstanceNo})";
                    string content = $"您有一条来自 {instance.InitiatorUserId} 的 {definition.DefName} 等待审批。";
                    if (assignee.IsFallback)
                    {
                        content += $" (注: {assignee.FallbackReason})";
                    }

                    await conn.ExecuteAsync(@"
                        INSERT INTO wf_notifications (id, instance_id, receiver_user_id, notify_type, title, content, is_read, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                        VALUES (@Id, @InstanceId, @ReceiverUserId, 'TASK_TODO', @Title, @Content, 0, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1)",
                        new { Id = Guid.NewGuid().ToString("D"), InstanceId = instance.Id, ReceiverUserId = assignee.UserId, Title = title, Content = content, Now = now });

                    // Publish Alert via SignalR
                    if (_alertPublisher != null)
                    {
                        await _alertPublisher.PublishWorkflowTodoAsync(assignee.UserId, instance.Id, title, content);
                    }
                }

                newPendingNodeIds.Add(node.Id);
            }
        }

        if (newPendingNodeIds.Count > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE wf_instances 
                SET current_node_ids_json = @NodesJson, updated_at_ms = @Now 
                WHERE id = @Id",
                new { NodesJson = JsonSerializer.Serialize(newPendingNodeIds), Now = now, Id = instance.Id });
        }
    }

    private static List<WorkflowNodeDto> ResolveNextNodes(
        WorkflowGraphDto graph,
        string sourceNodeId,
        IReadOnlyDictionary<string, object?> boVariables,
        out List<WorkflowEdgeDto> matchedEdges)
    {
        matchedEdges = new List<WorkflowEdgeDto>();
        var sourceNode = graph.Nodes.FirstOrDefault(n => n.Id == sourceNodeId);
        if (sourceNode == null) return new List<WorkflowNodeDto>();

        var outgoingEdges = graph.Edges.Where(e => e.SourceNodeId == sourceNodeId).ToList();
        if (outgoingEdges.Count == 0) return new List<WorkflowNodeDto>();

        if (string.Equals(sourceNode.NodeType, "EXCLUSIVE_GATEWAY", StringComparison.OrdinalIgnoreCase))
        {
            // Evaluate in priority order
            var sortedEdges = outgoingEdges.OrderBy(e => e.Priority).ToList();
            WorkflowEdgeDto? selected = null;

            foreach (var edge in sortedEdges.Where(e => !e.IsDefault))
            {
                if (WorkflowConditionEvaluator.EvaluateGroup(edge.ConditionGroup, boVariables))
                {
                    selected = edge;
                    break;
                }
            }

            // Fallback to default branch if none matched
            selected ??= sortedEdges.FirstOrDefault(e => e.IsDefault) ?? sortedEdges.FirstOrDefault();

            if (selected != null)
            {
                matchedEdges.Add(selected);
                var target = graph.Nodes.FirstOrDefault(n => n.Id == selected.TargetNodeId);
                return target != null ? new List<WorkflowNodeDto> { target } : new List<WorkflowNodeDto>();
            }
            return new List<WorkflowNodeDto>();
        }
        else
        {
            // Normal transition
            foreach (var edge in outgoingEdges)
            {
                if (edge.ConditionGroup == null || WorkflowConditionEvaluator.EvaluateGroup(edge.ConditionGroup, boVariables))
                {
                    matchedEdges.Add(edge);
                }
            }

            return matchedEdges.Select(e => graph.Nodes.FirstOrDefault(n => n.Id == e.TargetNodeId))
                               .Where(n => n != null)
                               .ToList()!;
        }
    }

    private async Task HandleCcNodeAsync(DbConnection conn, WfInstance instance, WfDefinition definition, WorkflowNodeDto ccNode, IReadOnlyDictionary<string, object?> boVariables, long now, CancellationToken ct)
    {
        var receiverSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (ccNode.CcReceiverUserIds != null)
        {
            foreach (var uid in ccNode.CcReceiverUserIds.Where(x => !string.IsNullOrWhiteSpace(x)))
                receiverSet.Add(uid.Trim());
        }
        if (ccNode.AssigneeConfig != null && !string.IsNullOrWhiteSpace(ccNode.AssigneeConfig.Strategy))
        {
            var resolved = await _assigneeResolver.ResolveAssigneesAsync(ccNode.AssigneeConfig, definition, ccNode, instance.InitiatorUserId, instance.InitiatorDeptId, boVariables, ct);
            foreach (var r in resolved.Where(x => !string.IsNullOrWhiteSpace(x.UserId)))
                receiverSet.Add(r.UserId);
        }
        var receivers = receiverSet.ToList();
        foreach (var receiverId in receivers)
        {
            string title = $"[抄送] 流程进度通知: {instance.InstanceNo}";
            string content = $"单据 {instance.BoType} ({instance.BusinessKey}) 已流转至抄送节点: {ccNode.Name}。";

            await conn.ExecuteAsync(@"
                INSERT INTO wf_notifications (id, instance_id, receiver_user_id, notify_type, title, content, is_read, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES (@Id, @InstanceId, @ReceiverUserId, 'CC', @Title, @Content, 0, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1)",
                new { Id = Guid.NewGuid().ToString("D"), InstanceId = instance.Id, ReceiverUserId = receiverId, Title = title, Content = content, Now = now });

            if (_alertPublisher != null)
            {
                await _alertPublisher.PublishWorkflowTodoAsync(receiverId, instance.Id, title, content);
            }
        }

        await RecordTrackAsync(conn, instance.Id, null, ccNode.Id, ccNode.Name ?? "抄送", "SYSTEM", "系统抄送", "CC", $"抄送至 {receivers.Count} 位相关人员", null, now);
    }

    private async Task CompleteWorkflowInstanceAsync(DbConnection conn, WfInstance instance, string finalStatus, string? approverUserId, long now, CancellationToken ct)
    {
        instance.Status = finalStatus;
        instance.FinishedAtMs = now;
        instance.CurrentNodeIdsJson = "[]";

        await conn.ExecuteAsync(@"
            UPDATE wf_instances 
            SET status = @Status, finished_at_ms = @FinishedAtMs, current_node_ids_json = '[]', updated_at_ms = @Now, version_no = version_no + 1
            WHERE id = @Id",
            new { Status = finalStatus, FinishedAtMs = now, Now = now, Id = instance.Id });

        await conn.ExecuteAsync(@"
            UPDATE wf_business_bindings 
            SET workflow_status = @Status, updated_at_ms = @Now 
            WHERE instance_id = @Id",
            new { Status = finalStatus, Now = now, Id = instance.Id });

        await RecordTrackAsync(conn, instance.Id, null, "node_end", "流程结束", approverUserId ?? "SYSTEM", "终审人", "FINISH", $"流程终审完成，状态: {finalStatus}", null, now);

        // Notify initiator
        await conn.ExecuteAsync(@"
            INSERT INTO wf_notifications (id, instance_id, receiver_user_id, notify_type, title, content, is_read, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @InstanceId, @ReceiverUserId, 'RESULT', @Title, @Content, 0, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1)",
            new
            {
                Id = Guid.NewGuid().ToString("D"),
                InstanceId = instance.Id,
                ReceiverUserId = instance.InitiatorUserId,
                Title = $"流程结果通知: {instance.InstanceNo} [{finalStatus}]",
                Content = $"您的申请单已完成审批，最终结果为: {finalStatus}。",
                Now = now
            });

        // Trigger business callback
        if (_businessCallback != null)
        {
            await _businessCallback.OnWorkflowCompletedAsync(instance.BoType, instance.BusinessKey, finalStatus, approverUserId, now, ct);
        }
    }
    #endregion

    #region Task Actions (APPROVE, REJECT, RETURN_PREV, RETURN_START, WITHDRAW, TRANSFER)
    public async Task<WorkflowActionResultDto> ExecuteTaskActionAsync(
        string taskId,
        string operatorUserId,
        string actionType,
        string? comment,
        string? delegateToUserId = null,
        CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();
        var task = await conn.QueryFirstOrDefaultAsync<WfTask>(
            "SELECT * FROM wf_tasks WHERE id = @Id", new { Id = taskId });

        if (task == null)
        {
            return new WorkflowActionResultDto(false, "NOT_FOUND", "待办任务不存在。");
        }

        if (task.Status != "PENDING")
        {
            return new WorkflowActionResultDto(false, task.Status, $"该任务已被处理，当前状态为: {task.Status}。");
        }

        // Security check: only the assigned user may act on this pending task
        if (!string.Equals(task.AssigneeUserId, operatorUserId, StringComparison.OrdinalIgnoreCase))
        {
            return new WorkflowActionResultDto(false, "FORBIDDEN", "无权越权审批：当前登录人不是该待办任务的指派审批人。");
        }

        var instance = await conn.QueryFirstOrDefaultAsync<WfInstance>(
            "SELECT * FROM wf_instances WHERE id = @Id", new { Id = task.InstanceId });

        if (instance == null || instance.Status != "RUNNING")
        {
            return new WorkflowActionResultDto(false, instance?.Status ?? "INVALID", "流程实例已结束或不在流转状态。");
        }

        var definition = await conn.QueryFirstOrDefaultAsync<WfDefinition>(
            "SELECT * FROM wf_definitions WHERE id = @Id", new { Id = instance.DefinitionId });

        if (definition == null || string.IsNullOrWhiteSpace(definition.GraphJson))
        {
            return new WorkflowActionResultDto(false, "INVALID_DEF", "流程定义缺失。");
        }

        var graph = JsonSerializer.Deserialize<WorkflowGraphDto>(definition.GraphJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var currentNode = graph.Nodes.FirstOrDefault(n => n.Id == task.NodeId);

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string action = actionType.ToUpperInvariant();

        switch (action)
        {
            case "APPROVE":
                task.Status = "APPROVED";
                task.ActedAtMs = now;
                task.Comment = comment;
                int affectedApprove = await conn.ExecuteAsync(@"
                    UPDATE wf_tasks 
                    SET status = 'APPROVED', acted_at_ms = @Now, comment = @Comment, updated_at_ms = @Now, version_no = version_no + 1
                    WHERE id = @Id AND status = 'PENDING'",
                    new { Now = now, Comment = comment, Id = task.Id });
                if (affectedApprove != 1)
                {
                    return new WorkflowActionResultDto(false, "CONFLICT", "该审批任务已被并发处理，请刷新列表。");
                }

                await RecordTrackAsync(conn, instance.Id, task.Id, task.NodeId, task.NodeName, operatorUserId, task.AssigneeName ?? operatorUserId, "APPROVE", comment ?? "同意", null, now);

                // Check node mode: SINGLE, AND_SIGN, OR_SIGN
                string mode = task.ApprovalMode.ToUpperInvariant();
                bool shouldAdvance = false;

                if (mode == "OR_SIGN" || mode == "SINGLE")
                {
                    // Cancel other pending tasks on this node
                    await conn.ExecuteAsync(@"
                        UPDATE wf_tasks 
                        SET status = 'CANCELLED', updated_at_ms = @Now, version_no = version_no + 1
                        WHERE instance_id = @InstanceId AND node_id = @NodeId AND status = 'PENDING'",
                        new { Now = now, InstanceId = instance.Id, NodeId = task.NodeId });
                    shouldAdvance = true;
                }
                else if (mode == "AND_SIGN")
                {
                    int pendingCount = await conn.ExecuteScalarAsync<int>(@"
                        SELECT COUNT(1) FROM wf_tasks 
                        WHERE instance_id = @InstanceId AND node_id = @NodeId AND status = 'PENDING'",
                        new { InstanceId = instance.Id, NodeId = task.NodeId });

                    if (pendingCount == 0)
                    {
                        shouldAdvance = true;
                    }
                }

                if (shouldAdvance)
                {
                    var boVars = ParseVariables(instance.BoVariablesJson);
                    await DriveWorkflowForwardAsync(conn, instance, definition, graph, new[] { task.NodeId }, boVars, now, ct, operatorUserId, 0);
                }

                var finalStatus = await conn.ExecuteScalarAsync<string>("SELECT status FROM wf_instances WHERE id = @Id", new { Id = instance.Id });
                return new WorkflowActionResultDto(true, finalStatus ?? "RUNNING", "审批已同意。");

            case "REJECT":
                task.Status = "REJECTED";
                task.ActedAtMs = now;
                task.Comment = comment;

                await conn.ExecuteAsync(@"
                    UPDATE wf_tasks 
                    SET status = 'REJECTED', acted_at_ms = @Now, comment = @Comment, updated_at_ms = @Now, version_no = version_no + 1
                    WHERE id = @Id",
                    new { Now = now, Comment = comment, Id = task.Id });

                // Cancel all remaining pending tasks for this instance
                await conn.ExecuteAsync(@"
                    UPDATE wf_tasks 
                    SET status = 'CANCELLED', updated_at_ms = @Now, version_no = version_no + 1
                    WHERE instance_id = @InstanceId AND status = 'PENDING'",
                    new { Now = now, InstanceId = instance.Id });

                await RecordTrackAsync(conn, instance.Id, task.Id, task.NodeId, task.NodeName, operatorUserId, task.AssigneeName ?? operatorUserId, "REJECT", comment ?? "拒绝", null, now);

                await CompleteWorkflowInstanceAsync(conn, instance, "REJECTED", operatorUserId, now, ct);
                return new WorkflowActionResultDto(true, "REJECTED", "审批已拒绝，流程终止。");

            case "RETURN_PREV":
                // Find previous approval node from graph predecessor first, fallback to prior APPROVE track
                string? graphPrevApprovalId = null;
                var backQueue = new Queue<string>();
                var backVisited = new HashSet<string> { task.NodeId };
                backQueue.Enqueue(task.NodeId);
                while (backQueue.Count > 0 && graphPrevApprovalId == null)
                {
                    var curId = backQueue.Dequeue();
                    foreach (var inEdge in graph.Edges.Where(e => e.TargetNodeId == curId))
                    {
                        if (!backVisited.Add(inEdge.SourceNodeId)) continue;
                        var srcNode = graph.Nodes.FirstOrDefault(n => n.Id == inEdge.SourceNodeId);
                        if (srcNode == null) continue;
                        if (string.Equals(srcNode.NodeType, "APPROVAL", StringComparison.OrdinalIgnoreCase))
                        {
                            graphPrevApprovalId = srcNode.Id;
                            break;
                        }
                        if (!string.Equals(srcNode.NodeType, "START", StringComparison.OrdinalIgnoreCase))
                        {
                            backQueue.Enqueue(srcNode.Id);
                        }
                    }
                }
                string targetNodeId = graphPrevApprovalId ?? "START";

                // Cancel current pending tasks
                await conn.ExecuteAsync(@"
                    UPDATE wf_tasks 
                    SET status = 'RETURNED', acted_at_ms = @Now, comment = @Comment, updated_at_ms = @Now, version_no = version_no + 1
                    WHERE id = @Id",
                    new { Now = now, Comment = comment, Id = task.Id });

                await conn.ExecuteAsync(@"
                    UPDATE wf_tasks 
                    SET status = 'CANCELLED', updated_at_ms = @Now, version_no = version_no + 1
                    WHERE instance_id = @InstanceId AND status = 'PENDING'",
                    new { Now = now, InstanceId = instance.Id });

                await RecordTrackAsync(conn, instance.Id, task.Id, task.NodeId, task.NodeName, operatorUserId, task.AssigneeName ?? operatorUserId, "RETURN_PREV", comment ?? "驳回至上一节点", null, now);

                if (targetNodeId != "START")
                {
                    var targetNode = graph.Nodes.FirstOrDefault(n => n.Id == targetNodeId);
                    if (targetNode != null)
                    {
                        var boVars = ParseVariables(instance.BoVariablesJson);
                        var assignees = await _assigneeResolver.ResolveAssigneesAsync(targetNode.AssigneeConfig, definition, targetNode, instance.InitiatorUserId, instance.InitiatorDeptId, boVars, ct);

                        foreach (var assignee in assignees)
                        {
                            await conn.ExecuteAsync(@"
                                INSERT INTO wf_tasks (id, instance_id, node_id, node_name, assignee_user_id, assignee_name, approval_mode, status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                                VALUES (@Id, @InstanceId, @NodeId, @NodeName, @AssigneeUserId, @AssigneeName, @ApprovalMode, 'PENDING', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1)",
                                new { Id = Guid.NewGuid().ToString("D"), InstanceId = instance.Id, NodeId = targetNode.Id, NodeName = targetNode.Name, AssigneeUserId = assignee.UserId, AssigneeName = assignee.DisplayName, ApprovalMode = targetNode.ApprovalMode ?? "SINGLE", Now = now });
                        }

                        await conn.ExecuteAsync("UPDATE wf_instances SET current_node_ids_json = @NodesJson WHERE id = @Id",
                            new { NodesJson = JsonSerializer.Serialize(new[] { targetNode.Id }), Id = instance.Id });

                        return new WorkflowActionResultDto(true, "RETURNED", $"已驳回至上一节点 [{targetNode.Name}]。");
                    }
                }

                // If no prev node found, reject to initiator
                await CompleteWorkflowInstanceAsync(conn, instance, "REJECTED", operatorUserId, now, ct);
                return new WorkflowActionResultDto(true, "REJECTED", "无前置审批节点，已直接驳回至发起人并终止流程。");

            case "RETURN_START":
                await conn.ExecuteAsync(@"
                    UPDATE wf_tasks 
                    SET status = 'RETURNED', acted_at_ms = @Now, comment = @Comment, updated_at_ms = @Now, version_no = version_no + 1
                    WHERE id = @Id",
                    new { Now = now, Comment = comment, Id = task.Id });

                await conn.ExecuteAsync(@"
                    UPDATE wf_tasks 
                    SET status = 'CANCELLED', updated_at_ms = @Now, version_no = version_no + 1
                    WHERE instance_id = @InstanceId AND status = 'PENDING'",
                    new { Now = now, InstanceId = instance.Id });

                await RecordTrackAsync(conn, instance.Id, task.Id, task.NodeId, task.NodeName, operatorUserId, task.AssigneeName ?? operatorUserId, "RETURN_START", comment ?? "驳回至发起人", null, now);
                await CompleteWorkflowInstanceAsync(conn, instance, "REJECTED", operatorUserId, now, ct);
                return new WorkflowActionResultDto(true, "REJECTED", "已驳回至发起人，流程已终止。");

            case "TRANSFER":
                if (string.IsNullOrWhiteSpace(delegateToUserId))
                {
                    return new WorkflowActionResultDto(false, "MISSING_DELEGATE", "转办必须指定目标人员。");
                }

                var delegatee = await conn.QueryFirstOrDefaultAsync<(string Id, string DisplayName)>(
                    "SELECT id, display_name FROM sys_users WHERE id = @Id AND is_active = 1",
                    new { Id = delegateToUserId });

                if (delegatee.Id == null)
                {
                    return new WorkflowActionResultDto(false, "USER_NOT_FOUND", "转办目标人员不存在或未启用。");
                }

                // Mark current task TRANSFERRED
                await conn.ExecuteAsync(@"
                    UPDATE wf_tasks 
                    SET status = 'TRANSFERRED', acted_at_ms = @Now, comment = @Comment, updated_at_ms = @Now, version_no = version_no + 1
                    WHERE id = @Id",
                    new { Now = now, Comment = comment, Id = task.Id });

                // Create new delegated task
                string newTaskId = Guid.NewGuid().ToString("D");
                await conn.ExecuteAsync(@"
                    INSERT INTO wf_tasks (id, instance_id, node_id, node_name, assignee_user_id, assignee_name, approval_mode, status, delegated_from_user_id, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                    VALUES (@Id, @InstanceId, @NodeId, @NodeName, @AssigneeUserId, @AssigneeName, @ApprovalMode, 'PENDING', @DelegatedFrom, 1, @Now, @Operator, @Now, @Operator, 1)",
                    new
                    {
                        Id = newTaskId,
                        InstanceId = instance.Id,
                        NodeId = task.NodeId,
                        NodeName = task.NodeName,
                        AssigneeUserId = delegatee.Id,
                        AssigneeName = delegatee.DisplayName,
                        ApprovalMode = task.ApprovalMode,
                        DelegatedFrom = operatorUserId,
                        Now = now,
                        Operator = operatorUserId
                    });

                await RecordTrackAsync(conn, instance.Id, task.Id, task.NodeId, task.NodeName, operatorUserId, task.AssigneeName ?? operatorUserId, "TRANSFER", $"转办给 {delegatee.DisplayName}: {comment}", null, now);

                // Notify new assignee
                await conn.ExecuteAsync(@"
                    INSERT INTO wf_notifications (id, instance_id, receiver_user_id, notify_type, title, content, is_read, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                    VALUES (@Id, @InstanceId, @ReceiverUserId, 'TASK_TODO', @Title, @Content, 0, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1)",
                    new
                    {
                        Id = Guid.NewGuid().ToString("D"),
                        InstanceId = instance.Id,
                        ReceiverUserId = delegatee.Id,
                        Title = $"[转办代办] {definition.DefName} ({instance.InstanceNo})",
                        Content = $"{task.AssigneeName} 将任务转办给您: {comment}",
                        Now = now
                    });

                return new WorkflowActionResultDto(true, "TRANSFERRED", $"任务已成功转办给 {delegatee.DisplayName}。");

            default:
                return new WorkflowActionResultDto(false, "UNSUPPORTED_ACTION", $"不支持的操作类型: {actionType}");
        }
    }

    public async Task<WorkflowActionResultDto> WithdrawInstanceAsync(
        string instanceId,
        string initiatorUserId,
        string? comment,
        CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();
        var instance = await conn.QueryFirstOrDefaultAsync<WfInstance>(
            "SELECT * FROM wf_instances WHERE id = @Id", new { Id = instanceId });

        if (instance == null)
        {
            return new WorkflowActionResultDto(false, "NOT_FOUND", "流程实例不存在。");
        }

        if (instance.Status != "RUNNING")
        {
            return new WorkflowActionResultDto(false, instance.Status, $"流程已不在运行状态 (当前状态: {instance.Status})，无法撤回。");
        }

        if (!string.Equals(instance.InitiatorUserId, initiatorUserId, StringComparison.OrdinalIgnoreCase))
        {
            return new WorkflowActionResultDto(false, "FORBIDDEN", "只有流程发起人才能撤回流程申请。");
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Cancel all pending tasks
        await conn.ExecuteAsync(@"
            UPDATE wf_tasks 
            SET status = 'CANCELLED', updated_at_ms = @Now, version_no = version_no + 1
            WHERE instance_id = @InstanceId AND status = 'PENDING'",
            new { Now = now, InstanceId = instance.Id });

        await RecordTrackAsync(conn, instance.Id, null, "WITHDRAW", "发起人撤回", initiatorUserId, "发起人", "WITHDRAW", comment ?? "发起人撤回申请", null, now);

        await CompleteWorkflowInstanceAsync(conn, instance, "WITHDRAWN", initiatorUserId, now, ct);

        return new WorkflowActionResultDto(true, "WITHDRAWN", "流程已被发起人撤回。");
    }
    #endregion

    #region Query Instances, Tasks, Tracks
    public async Task<WorkflowInstanceDetailDto?> GetInstanceByBusinessAsync(string boType, string businessKey, CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();
        var instance = await conn.QueryFirstOrDefaultAsync<WfInstance>(@"
            SELECT * FROM wf_instances 
            WHERE bo_type = @BoType AND business_key = @BusinessKey 
            ORDER BY created_at_ms DESC LIMIT 1",
            new { BoType = boType, BusinessKey = businessKey });

        if (instance == null) return null;
        return await BuildInstanceDetailDtoAsync(conn, instance);
    }

    public async Task<WorkflowInstanceDetailDto?> GetInstanceByIdAsync(string instanceId, CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();
        var instance = await conn.QueryFirstOrDefaultAsync<WfInstance>(
            "SELECT * FROM wf_instances WHERE id = @Id", new { Id = instanceId });

        if (instance == null) return null;
        return await BuildInstanceDetailDtoAsync(conn, instance);
    }

    private static async Task<WorkflowInstanceDetailDto> BuildInstanceDetailDtoAsync(DbConnection conn, WfInstance inst)
    {
        var def = await conn.QueryFirstOrDefaultAsync<WfDefinition>(
            "SELECT * FROM wf_definitions WHERE id = @Id", new { Id = inst.DefinitionId });

        var initiator = await conn.QueryFirstOrDefaultAsync<(string Id, string DisplayName)>(
            "SELECT id, display_name FROM sys_users WHERE id = @Id", new { Id = inst.InitiatorUserId });

        string? deptName = null;
        if (!string.IsNullOrWhiteSpace(inst.InitiatorDeptId))
        {
            var deptRow = await conn.QueryFirstOrDefaultAsync<(string Id, string DeptName)>(
                "SELECT id, dept_name FROM sys_departments WHERE id = @Id", new { Id = inst.InitiatorDeptId });
            deptName = deptRow.DeptName;
        }

        var tasks = (await conn.QueryAsync<WfTask>(
            "SELECT * FROM wf_tasks WHERE instance_id = @Id ORDER BY created_at_ms ASC", new { Id = inst.Id })).ToList();

        var tracks = (await conn.QueryAsync<WfHistoryTrack>(
            "SELECT * FROM wf_history_tracks WHERE instance_id = @Id ORDER BY occurred_at_ms ASC", new { Id = inst.Id })).ToList();

        var currentNodes = string.IsNullOrWhiteSpace(inst.CurrentNodeIdsJson)
            ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(inst.CurrentNodeIdsJson) ?? new List<string>();

        var taskDtos = tasks.Select(t => new WorkflowTaskItemDto(
            t.Id, t.InstanceId, inst.InstanceNo, inst.DefKey, inst.BoType, inst.BusinessKey,
            t.NodeId, t.NodeName, t.AssigneeUserId, t.AssigneeName, t.ApprovalMode,
            t.Status, t.Comment, t.ActedAtMs, inst.InitiatorUserId, initiator.DisplayName,
            inst.InitiatorDeptId, deptName, inst.BoVariablesJson, t.CreatedAtMs
        )).ToList();

        var trackDtos = tracks.Select(tr => new WorkflowTrackItemDto(
            tr.Id, tr.InstanceId, tr.TaskId, tr.NodeId, tr.NodeName,
            tr.OperatorUserId, tr.OperatorName, tr.ActionType, tr.Comment, tr.OccurredAtMs
        )).ToList();

        return new WorkflowInstanceDetailDto(
            inst.Id, inst.InstanceNo, inst.DefKey, def?.DefName ?? inst.DefKey,
            inst.DefinitionVersion, inst.BoType, inst.BusinessKey,
            inst.InitiatorUserId, initiator.DisplayName, inst.InitiatorDeptId, deptName,
            inst.Status, inst.StartedAtMs, inst.FinishedAtMs,
            currentNodes, inst.BoVariablesJson, taskDtos, trackDtos
        );
    }
    #endregion

    #region Helper Methods
    private static async Task RecordTrackAsync(
        DbConnection conn,
        string instanceId,
        string? taskId,
        string nodeId,
        string nodeName,
        string operatorUserId,
        string? operatorName,
        string actionType,
        string? comment,
        string? snapshotJson,
        long now)
    {
        await conn.ExecuteAsync(@"
            INSERT INTO wf_history_tracks (id, instance_id, task_id, node_id, node_name, operator_user_id, operator_name, action_type, comment, snapshot_json, occurred_at_ms, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @InstanceId, @TaskId, @NodeId, @NodeName, @OperatorUserId, @OperatorName, @ActionType, @Comment, @SnapshotJson, @Now, 1, @Now, @OperatorUserId, @Now, @OperatorUserId, 1)",
            new
            {
                Id = Guid.NewGuid().ToString("D"),
                InstanceId = instanceId,
                TaskId = taskId,
                NodeId = nodeId,
                NodeName = nodeName,
                OperatorUserId = operatorUserId,
                OperatorName = operatorName ?? operatorUserId,
                ActionType = actionType,
                Comment = comment,
                SnapshotJson = snapshotJson,
                Now = now
            });
    }

    private static Dictionary<string, object?> ParseVariables(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, object?>();
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (dict == null) return new Dictionary<string, object?>();

            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in dict)
            {
                result[kv.Key] = kv.Value.ValueKind switch
                {
                    JsonValueKind.Number => kv.Value.GetDecimal(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.String => kv.Value.GetString(),
                    _ => kv.Value.ToString()
                };
            }
            return result;
        }
        catch
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }
    }
    #endregion
}
