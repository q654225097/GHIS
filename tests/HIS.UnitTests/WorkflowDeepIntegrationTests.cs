using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Workflow.Controllers;
using HIS.Modules.Workflow.Models;
using HIS.Modules.Workflow.Services;
using Xunit;

namespace HIS.UnitTests;

public class WorkflowDeepIntegrationTests : IAsyncLifetime
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly WorkflowAssigneeResolver _assigneeResolver;
    private readonly WorkflowBusinessCallbackDispatcher _callbackDispatcher;
    private readonly WorkflowRuntimeService _runtimeService;

    public WorkflowDeepIntegrationTests()
    {
        _connFactory = new SqliteConnectionFactory();
        _assigneeResolver = new WorkflowAssigneeResolver(_connFactory);
        _callbackDispatcher = new WorkflowBusinessCallbackDispatcher(_connFactory);
        _runtimeService = new WorkflowRuntimeService(
            _connFactory,
            _assigneeResolver,
            _callbackDispatcher,
            alertPublisher: null,
            auditTrail: null);
    }

    public async Task InitializeAsync()
    {
        await DbSchemaInitializer.InitializeAsync(_connFactory);
        using var conn = _connFactory.CreateConnection();

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Ensure test departments exist with hierarchy
        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO sys_departments (id, campus_id, dept_code, dept_name, dept_category, parent_dept_id, manager_user_id, rated_bed_count, is_allow_appointment, is_active, schema_version, created_at_ms, updated_at_ms, version_no)
            VALUES 
            ('DEPT-ROOT', 'CAMPUS-01', 'ROOT', '总院部', 'ADMIN', NULL, 'USR-VP-01', 0, 0, 1, 1, @Now, @Now, 1),
            ('DEPT-SURG-DIR', 'CAMPUS-01', 'SURG_DIR', '外科大部', 'CLINICAL', 'DEPT-ROOT', 'USR-SURG-DIR', 0, 0, 1, 1, @Now, @Now, 1),
            ('DEPT-GEN-SURG', 'CAMPUS-01', 'GEN_SURG', '普外科', 'CLINICAL', 'DEPT-SURG-DIR', 'USR-DEPT-MGR', 0, 0, 1, 1, @Now, @Now, 1)",
            new { Now = now });

        // Ensure test users exist with supervisor hierarchy
        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO sys_users (id, user_name, display_name, employee_no, password_hash, direct_supervisor_id, prescribe_level, surgery_level, emr_sign_level, session_version, is_active, schema_version, created_at_ms, updated_at_ms, version_no)
            VALUES 
            ('USR-INIT-01', 'doctor_init', '开单医生张三', 'EMP_W01', 'hash', 'USR-DOC-LEAD', 1, 1, 1, 1, 1, 1, @Now, @Now, 1),
            ('USR-DOC-LEAD', 'doctor_lead', '医疗组长李四', 'EMP_W02', 'hash', 'USR-DEPT-MGR', 1, 1, 1, 1, 1, 1, @Now, @Now, 1),
            ('USR-DEPT-MGR', 'dept_manager', '普外科主任王五', 'EMP_W03', 'hash', 'USR-SURG-DIR', 1, 1, 1, 1, 1, 1, @Now, @Now, 1),
            ('USR-SURG-DIR', 'surg_director', '大外科部长赵六', 'EMP_W04', 'hash', 'USR-VP-01', 1, 1, 1, 1, 1, 1, @Now, @Now, 1),
            ('USR-VP-01', 'vp_medical', '医务副院长钱七', 'EMP_W05', 'hash', NULL, 1, 1, 1, 1, 1, 1, @Now, @Now, 1),
            ('USR-APPROVER-A', 'appr_a', '审核人甲', 'EMP_W06', 'hash', NULL, 1, 1, 1, 1, 1, 1, @Now, @Now, 1),
            ('USR-APPROVER-B', 'appr_b', '审核人乙', 'EMP_W07', 'hash', NULL, 1, 1, 1, 1, 1, 1, @Now, @Now, 1)",
            new { Now = now });

        // Assign roles
        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO sys_roles (id, role_code, role_name, data_scope, is_active, schema_version, created_at_ms, updated_at_ms, version_no)
            VALUES 
            ('ROLE-CHIEF', 'ROLE_CHIEF_DOC', '科室主任医师', 'DEPARTMENT', 1, 1, @Now, @Now, 1),
            ('ROLE-VP', 'ROLE_MED_VP', '分管副院长', 'HOSPITAL', 1, 1, @Now, @Now, 1)",
            new { Now = now });

        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO sys_user_dept_posts (id, user_id, dept_id, dept_category, role_id, is_default_context, schema_version, created_at_ms, updated_at_ms, version_no)
            VALUES 
            ('UDP-1', 'USR-DEPT-MGR', 'DEPT-GEN-SURG', 'CLINICAL', 'ROLE-CHIEF', 1, 1, @Now, @Now, 1),
            ('UDP-2', 'USR-VP-01', 'DEPT-ROOT', 'ADMIN', 'ROLE-VP', 1, 1, @Now, @Now, 1)",
            new { Now = now });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Test_01_Designer_Roundtrip_Persistence_And_Execution()
    {
        using var conn = _connFactory.CreateConnection();
        string defKey = "CSM_DEEP_TEST_" + Guid.NewGuid().ToString("N")[..8];

        var graph = new WorkflowGraphDto
        {
            Nodes = new List<WorkflowNodeDto>
            {
                new() { Id = "N-START", Name = "流程开始", NodeType = "START", X = 50, Y = 100 },
                new()
                {
                    Id = "N-APPROVE",
                    Name = "科主任审批",
                    NodeType = "APPROVAL",
                    X = 250,
                    Y = 100,
                    ApprovalMode = "SINGLE",
                    AssigneeConfig = new AssigneeConfigDto
                    {
                        Strategy = "DEPT_MANAGER"
                    }
                },
                new() { Id = "N-END", Name = "审批结束", NodeType = "END", X = 450, Y = 100 }
            },
            Edges = new List<WorkflowEdgeDto>
            {
                new() { Id = "E-1", SourceNodeId = "N-START", TargetNodeId = "N-APPROVE" },
                new() { Id = "E-2", SourceNodeId = "N-APPROVE", TargetNodeId = "N-END" }
            }
        };

        string graphJson = JsonSerializer.Serialize(graph, new JsonSerializerOptions { WriteIndented = true });

        // Save definition draft
        var controller = new WorkflowController(_connFactory, _runtimeService);
        var saveResult = await controller.SaveDefinition(new WorkflowController.SaveDefinitionDto(
            null,
            defKey,
            "科室耗材深度测试流程",
            "CSM_REQUISITION",
            "USR-DEPT-MGR",
            null,
            graphJson
        ), default);

        var okResult = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(saveResult.Result);
        var resEnvelope = Assert.IsType<ApiResponse<WfDefinition>>(okResult.Value);
        Assert.NotNull(resEnvelope.Data);
        string defId = resEnvelope.Data.Id;

        // Query back from DB
        var savedDef = await conn.QueryFirstOrDefaultAsync<WfDefinition>(
            "SELECT * FROM wf_definitions WHERE id = @Id", new { Id = defId });
        Assert.NotNull(savedDef);
        Assert.Equal(defKey, savedDef.DefKey);
        Assert.Equal("DRAFT", savedDef.Status);

        // Deserialize and assert fidelity
        var reloadedGraph = JsonSerializer.Deserialize<WorkflowGraphDto>(savedDef.GraphJson!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(reloadedGraph);
        Assert.Equal(3, reloadedGraph.Nodes.Count);
        Assert.Equal(2, reloadedGraph.Edges.Count);
        Assert.Equal(250, reloadedGraph.Nodes[1].X);
        Assert.Equal("DEPT_MANAGER", reloadedGraph.Nodes[1].AssigneeConfig?.Strategy);
    }

    [Fact]
    public async Task Test_02_Publish_PreValidation()
    {
        string defKey = "GW_VALID_TEST_" + Guid.NewGuid().ToString("N")[..8];

        // 1. Graph with Exclusive Gateway lacking a default branch
        var invalidGraph = new WorkflowGraphDto
        {
            Nodes = new List<WorkflowNodeDto>
            {
                new() { Id = "START", Name = "开始", NodeType = "START" },
                new() { Id = "GW-1", Name = "金额判定网关", NodeType = "EXCLUSIVE_GATEWAY" },
                new() { Id = "APP-A", Name = "科主任审核", NodeType = "APPROVAL", AssigneeConfig = new AssigneeConfigDto { Strategy = "ROLE", RoleCode = "ROLE_CHIEF_DOC" } },
                new() { Id = "END", Name = "结束", NodeType = "END" }
            },
            Edges = new List<WorkflowEdgeDto>
            {
                new() { Id = "E-0", SourceNodeId = "START", TargetNodeId = "GW-1" },
                new()
                {
                    Id = "E-1",
                    SourceNodeId = "GW-1",
                    TargetNodeId = "APP-A",
                    IsDefault = false,
                    ConditionGroup = new WorkflowConditionGroupDto
                    {
                        Rules = new List<WorkflowConditionRuleDto>
                        {
                            new() { Field = "total_amount", Operator = ">", Value = "1000" }
                        }
                    }
                },
                new() { Id = "E-2", SourceNodeId = "APP-A", TargetNodeId = "END" }
            }
        };

        var controller = new WorkflowController(_connFactory, _runtimeService);
        var saveResult = await controller.SaveDefinition(new WorkflowController.SaveDefinitionDto(
            null,
            defKey,
            "排他网关校验流程",
            "CSM_REQUISITION",
            "USR-DEPT-MGR",
            null,
            JsonSerializer.Serialize(invalidGraph)
        ), default);

        var def = ((Microsoft.AspNetCore.Mvc.OkObjectResult)saveResult.Result!).Value as ApiResponse<WfDefinition>;
        string defId = def!.Data!.Id;

        // Try publishing without default edge -> must fail with Chinese error message
        var publishFail = await controller.PublishDefinition(defId, default);
        var badReq = Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestObjectResult>(publishFail.Result);
        var errResponse = badReq.Value as ApiResponse<WfDefinition>;
        Assert.NotNull(errResponse);
        Assert.Contains("默认兜底分支", errResponse.Message);

        // Fix graph by adding default fallback edge to END
        invalidGraph.Edges.Add(new WorkflowEdgeDto
        {
            Id = "E-DEFAULT",
            SourceNodeId = "GW-1",
            TargetNodeId = "END",
            IsDefault = true
        });

        await controller.SaveDefinition(new WorkflowController.SaveDefinitionDto(
            defId,
            defKey,
            "排他网关校验流程",
            "CSM_REQUISITION",
            "USR-DEPT-MGR",
            null,
            JsonSerializer.Serialize(invalidGraph)
        ), default);

        // Publish now succeeds -> Version 1 published
        var publishSuccess = await controller.PublishDefinition(defId, default);
        var okPublish = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(publishSuccess.Result);
        var pubDef = (okPublish.Value as ApiResponse<WfDefinition>)!.Data!;
        Assert.Equal("PUBLISHED", pubDef.Status);
        Assert.Equal(1, pubDef.VersionNum);

        // Now save another draft with same defKey and publish -> version bumped to 2 and v1 archived
        var draft2 = await controller.SaveDefinition(new WorkflowController.SaveDefinitionDto(
            null,
            defKey,
            "排他网关校验流程 v2",
            "CSM_REQUISITION",
            "USR-DEPT-MGR",
            null,
            JsonSerializer.Serialize(invalidGraph)
        ), default);
        string def2Id = (((Microsoft.AspNetCore.Mvc.OkObjectResult)draft2.Result!).Value as ApiResponse<WfDefinition>)!.Data!.Id;

        var pub2 = await controller.PublishDefinition(def2Id, default);
        var pub2Def = (((Microsoft.AspNetCore.Mvc.OkObjectResult)pub2.Result!).Value as ApiResponse<WfDefinition>)!.Data!;
        Assert.Equal(2, pub2Def.VersionNum);

        using var conn = _connFactory.CreateConnection();
        var v1FromDb = await conn.QueryFirstOrDefaultAsync<WfDefinition>("SELECT * FROM wf_definitions WHERE id = @Id", new { Id = defId });
        Assert.Equal("ARCHIVED", v1FromDb!.Status);
    }

    [Fact]
    public async Task Test_03_Version_Isolation()
    {
        string defKey = "VER_ISO_" + Guid.NewGuid().ToString("N")[..8];
        var controller = new WorkflowController(_connFactory, _runtimeService);

        // Publish v1: 1 Approval node
        var v1Graph = new WorkflowGraphDto
        {
            Nodes = new List<WorkflowNodeDto>
            {
                new() { Id = "S", NodeType = "START" },
                new() { Id = "A1", NodeType = "APPROVAL", Name = "v1审核节点", AssigneeConfig = new AssigneeConfigDto { Strategy = "FIXED_USER", FixedUserIds = new List<string> { "USR-APPROVER-A" } } },
                new() { Id = "E", NodeType = "END" }
            },
            Edges = new List<WorkflowEdgeDto>
            {
                new() { Id = "E1", SourceNodeId = "S", TargetNodeId = "A1" },
                new() { Id = "E2", SourceNodeId = "A1", TargetNodeId = "E" }
            }
        };

        var def1 = await _runtimeService.SaveDefinitionDraftAsync(new WfDefinition
        {
            DefKey = defKey,
            DefName = "版本隔离流程",
            BoType = "CSM_REQUISITION",
            GraphJson = JsonSerializer.Serialize(v1Graph)
        }, "SYSTEM");
        await _runtimeService.PublishDefinitionAsync(def1.Id, "SYSTEM");

        // Start instance 1 -> bounds to v1
        string bizKey1 = "CSM-REQ-V1-" + Guid.NewGuid().ToString("N")[..6];
        var startResult = await _runtimeService.StartWorkflowAsync(
            "CSM_REQUISITION", bizKey1, "USR-INIT-01", "DEPT-GEN-SURG", new Dictionary<string, object?>());
        Assert.True(startResult.IsWorkflowTriggered);
        string inst1Id = startResult.InstanceId!;

        // Now publish v2 with node A2 instead of A1
        var v2Graph = new WorkflowGraphDto
        {
            Nodes = new List<WorkflowNodeDto>
            {
                new() { Id = "S", NodeType = "START" },
                new() { Id = "A2", NodeType = "APPROVAL", Name = "v2审核节点", AssigneeConfig = new AssigneeConfigDto { Strategy = "FIXED_USER", FixedUserIds = new List<string> { "USR-APPROVER-B" } } },
                new() { Id = "E", NodeType = "END" }
            },
            Edges = new List<WorkflowEdgeDto>
            {
                new() { Id = "E1", SourceNodeId = "S", TargetNodeId = "A2" },
                new() { Id = "E2", SourceNodeId = "A2", TargetNodeId = "E" }
            }
        };

        var def2 = await _runtimeService.SaveDefinitionDraftAsync(new WfDefinition
        {
            DefKey = defKey,
            DefName = "版本隔离流程 v2",
            BoType = "CSM_REQUISITION",
            GraphJson = JsonSerializer.Serialize(v2Graph)
        }, "SYSTEM");
        await _runtimeService.PublishDefinitionAsync(def2.Id, "SYSTEM");

        // Verify instance 1 still runs on v1
        var detail1 = await _runtimeService.GetInstanceByIdAsync(inst1Id);
        Assert.NotNull(detail1);
        Assert.Equal(1, detail1.DefinitionVersion);
        Assert.Equal("A1", detail1.CurrentNodeIds[0]);
    }

    [Fact]
    public async Task Test_04_Condition_Routing()
    {
        string defKey = "COND_ROUTING_" + Guid.NewGuid().ToString("N")[..8];

        var graph = new WorkflowGraphDto
        {
            Nodes = new List<WorkflowNodeDto>
            {
                new() { Id = "START", NodeType = "START" },
                new() { Id = "GW", NodeType = "EXCLUSIVE_GATEWAY", Name = "金额路由网关" },
                new() { Id = "HIGH_NODE", NodeType = "APPROVAL", Name = "高额副院长审批", AssigneeConfig = new AssigneeConfigDto { Strategy = "FIXED_USER", FixedUserIds = new List<string> { "USR-VP-01" } } },
                new() { Id = "LOW_NODE", NodeType = "APPROVAL", Name = "小额科主任审批", AssigneeConfig = new AssigneeConfigDto { Strategy = "FIXED_USER", FixedUserIds = new List<string> { "USR-DEPT-MGR" } } },
                new() { Id = "END", NodeType = "END" }
            },
            Edges = new List<WorkflowEdgeDto>
            {
                new() { Id = "E0", SourceNodeId = "START", TargetNodeId = "GW" },
                new()
                {
                    Id = "E-HIGH",
                    SourceNodeId = "GW",
                    TargetNodeId = "HIGH_NODE",
                    Priority = 1,
                    ConditionGroup = new WorkflowConditionGroupDto
                    {
                        Rules = new List<WorkflowConditionRuleDto>
                        {
                            new() { Field = "amount", Operator = ">", Value = "5000" }
                        }
                    }
                },
                new()
                {
                    Id = "E-LOW",
                    SourceNodeId = "GW",
                    TargetNodeId = "LOW_NODE",
                    IsDefault = true // Default branch for <= 5000
                },
                new() { Id = "E-H-END", SourceNodeId = "HIGH_NODE", TargetNodeId = "END" },
                new() { Id = "E-L-END", SourceNodeId = "LOW_NODE", TargetNodeId = "END" }
            }
        };

        var def = await _runtimeService.SaveDefinitionDraftAsync(new WfDefinition
        {
            DefKey = defKey,
            DefName = "金额条件路由测试",
            BoType = "CSM_REQUISITION",
            GraphJson = JsonSerializer.Serialize(graph)
        }, "SYSTEM");
        await _runtimeService.PublishDefinitionAsync(def.Id, "SYSTEM");

        // Case 1: amount = 8000 -> HIGH_NODE
        string bizKey1 = "REQ-8000-" + Guid.NewGuid().ToString("N")[..6];
        var res1 = await _runtimeService.StartWorkflowAsync(
            "CSM_REQUISITION", bizKey1, "USR-INIT-01", "DEPT-GEN-SURG",
            new Dictionary<string, object?> { ["amount"] = 8000m });
        var detail1 = await _runtimeService.GetInstanceByIdAsync(res1.InstanceId!);
        Assert.Equal("HIGH_NODE", detail1!.CurrentNodeIds[0]);

        // Case 2: amount = 2000 -> fallback to LOW_NODE (Default)
        string bizKey2 = "REQ-2000-" + Guid.NewGuid().ToString("N")[..6];
        var res2 = await _runtimeService.StartWorkflowAsync(
            "CSM_REQUISITION", bizKey2, "USR-INIT-01", "DEPT-GEN-SURG",
            new Dictionary<string, object?> { ["amount"] = 2000m });
        var detail2 = await _runtimeService.GetInstanceByIdAsync(res2.InstanceId!);
        Assert.Equal("LOW_NODE", detail2!.CurrentNodeIds[0]);
    }

    [Fact]
    public async Task Test_05_Five_Assignee_Strategies()
    {
        var dummyDef = new WfDefinition { Id = "DEF-01", DefKey = "TEST_DEF", BoType = "CSM_REQUISITION" };
        var dummyNode = new WorkflowNodeDto { Id = "N1", NodeType = "APPROVAL" };

        // 1. FIXED_USER
        var fixedAssignees = await _assigneeResolver.ResolveAssigneesAsync(
            new AssigneeConfigDto { Strategy = "FIXED_USER", FixedUserIds = new List<string> { "USR-APPROVER-A" } },
            dummyDef, dummyNode, "USR-INIT-01", "DEPT-GEN-SURG", new Dictionary<string, object?>());
        Assert.Single(fixedAssignees);
        Assert.Equal("USR-APPROVER-A", fixedAssignees[0].UserId);

        // 2. ROLE
        var roleAssignees = await _assigneeResolver.ResolveAssigneesAsync(
            new AssigneeConfigDto { Strategy = "ROLE", RoleCode = "ROLE_CHIEF_DOC" },
            dummyDef, dummyNode, "USR-INIT-01", "DEPT-GEN-SURG", new Dictionary<string, object?>());
        Assert.Contains(roleAssignees, x => x.UserId == "USR-DEPT-MGR");

        // 3. DEPT_MANAGER
        var deptMgrAssignees = await _assigneeResolver.ResolveAssigneesAsync(
            new AssigneeConfigDto { Strategy = "DEPT_MANAGER" },
            dummyDef, dummyNode, "USR-INIT-01", "DEPT-GEN-SURG", new Dictionary<string, object?>());
        Assert.Single(deptMgrAssignees);
        Assert.Equal("USR-DEPT-MGR", deptMgrAssignees[0].UserId);

        // 4. DIRECT_SUPERVISOR
        var supAssignees = await _assigneeResolver.ResolveAssigneesAsync(
            new AssigneeConfigDto { Strategy = "DIRECT_SUPERVISOR" },
            dummyDef, dummyNode, "USR-INIT-01", "DEPT-GEN-SURG", new Dictionary<string, object?>());
        Assert.Single(supAssignees);
        Assert.Equal("USR-DOC-LEAD", supAssignees[0].UserId);

        // 5. ESCALATE_N (level 3 from GEN_SURG -> SURG_DIR -> ROOT manager USR-VP-01)
        var escAssignees = await _assigneeResolver.ResolveAssigneesAsync(
            new AssigneeConfigDto { Strategy = "ESCALATE_N", EscalateLevels = 3 },
            dummyDef, dummyNode, "USR-INIT-01", "DEPT-GEN-SURG", new Dictionary<string, object?>());
        Assert.Single(escAssignees);
        Assert.Equal("USR-VP-01", escAssignees[0].UserId);
    }

    [Fact]
    public async Task Test_06_Approval_Modes_And_Actions()
    {
        string defKey = "MODES_AND_ACTIONS_" + Guid.NewGuid().ToString("N")[..8];

        var graph = new WorkflowGraphDto
        {
            Nodes = new List<WorkflowNodeDto>
            {
                new() { Id = "S", NodeType = "START" },
                new()
                {
                    Id = "AND_NODE",
                    NodeType = "APPROVAL",
                    Name = "双人会签节点",
                    ApprovalMode = "AND_SIGN",
                    AssigneeConfig = new AssigneeConfigDto
                    {
                        Strategy = "FIXED_USER",
                        FixedUserIds = new List<string> { "USR-APPROVER-A", "USR-APPROVER-B" }
                    }
                },
                new()
                {
                    Id = "CC_NODE",
                    NodeType = "CC",
                    Name = "抄送主管院长",
                    CcReceiverUserIds = new List<string> { "USR-VP-01" }
                },
                new() { Id = "E", NodeType = "END" }
            },
            Edges = new List<WorkflowEdgeDto>
            {
                new() { Id = "E1", SourceNodeId = "S", TargetNodeId = "AND_NODE" },
                new() { Id = "E2", SourceNodeId = "AND_NODE", TargetNodeId = "CC_NODE" },
                new() { Id = "E3", SourceNodeId = "CC_NODE", TargetNodeId = "E" }
            }
        };

        var def = await _runtimeService.SaveDefinitionDraftAsync(new WfDefinition
        {
            DefKey = defKey,
            DefName = "会签与动作全覆盖",
            BoType = "CSM_REQUISITION",
            GraphJson = JsonSerializer.Serialize(graph)
        }, "SYSTEM");
        await _runtimeService.PublishDefinitionAsync(def.Id, "SYSTEM");

        string bizKey = "REQ-AND-" + Guid.NewGuid().ToString("N")[..6];
        var startRes = await _runtimeService.StartWorkflowAsync(
            "CSM_REQUISITION", bizKey, "USR-INIT-01", "DEPT-GEN-SURG", new Dictionary<string, object?>());

        var inst = await _runtimeService.GetInstanceByIdAsync(startRes.InstanceId!);
        Assert.Equal(2, inst!.Tasks.Count); // 2 tasks generated for AND_SIGN

        var taskA = inst.Tasks.First(t => t.AssigneeUserId == "USR-APPROVER-A");
        var taskB = inst.Tasks.First(t => t.AssigneeUserId == "USR-APPROVER-B");

        // Approver A approves -> instance remains RUNNING because of AND_SIGN
        var actA = await _runtimeService.ExecuteTaskActionAsync(taskA.TaskId, "USR-APPROVER-A", "APPROVE", "同意通过");
        Assert.True(actA.Success);
        Assert.Equal("RUNNING", actA.NewStatus);

        // Approver B approves -> all approved, CC node triggered (creates notification), instance transitions to APPROVED
        var actB = await _runtimeService.ExecuteTaskActionAsync(taskB.TaskId, "USR-APPROVER-B", "APPROVE", "复核无误");
        Assert.True(actB.Success);
        Assert.Equal("APPROVED", actB.NewStatus);

        // Verify CC notification generated for USR-VP-01
        using var conn = _connFactory.CreateConnection();
        var notifications = (await conn.QueryAsync<WfNotification>(
            "SELECT * FROM wf_notifications WHERE receiver_user_id = 'USR-VP-01'")).ToList();
        Assert.NotEmpty(notifications);
        Assert.Equal("CC", notifications[0].NotifyType);

        // Verify history tracks recorded correctly
        var tracks = (await conn.QueryAsync<WfHistoryTrack>(
            "SELECT * FROM wf_history_tracks WHERE instance_id = @InstId ORDER BY occurred_at_ms ASC",
            new { InstId = startRes.InstanceId })).ToList();
        Assert.True(tracks.Count >= 3); // START, APPROVE(A), APPROVE(B)
    }

    [Fact]
    public async Task Test_07_Downstream_Business_Gate()
    {
        using var conn = _connFactory.CreateConnection();
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 1. Verify CSM_REQUISITION gating
        string reqId = "REQ-GATE-" + Guid.NewGuid().ToString("N")[..6];
        await conn.ExecuteAsync(@"
            INSERT INTO csm_requisitions (id, requisition_no, dept_id, ward_id, requested_by, status, approval_status, schema_version, created_at_ms, updated_at_ms, version_no)
            VALUES (@Id, @Id, 'DEPT-GEN-SURG', 'WARD-01', 'USR-INIT-01', 'PENDING', 'PENDING_APPROVAL', 1, @Now, @Now, 1)",
            new { Id = reqId, Now = now });

        // Simulate callback approved
        await _callbackDispatcher.OnWorkflowCompletedAsync("CSM_REQUISITION", reqId, "APPROVED", "USR-DEPT-MGR", now);

        var updatedReq = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT * FROM csm_requisitions WHERE id = @Id", new { Id = reqId });
        Assert.Equal("APPROVED", (string)updatedReq.approval_status);

        // 2. Verify BLOOD_REQUEST gating
        string bloodReqId = "BLD-REQ-GATE-" + Guid.NewGuid().ToString("N")[..6];
        await conn.ExecuteAsync(@"
            INSERT INTO mtc_blood_requests (id, encounter_id, patient_id, abo_type, rh_type, blood_product_code, volume_ml, empty_bag_returned, status, approval_status, schema_version, created_at_ms, updated_at_ms, version_no)
            VALUES (@Id, 'ENC01', 'PAT01', 'O', 'POSITIVE', 'RBC', 400, 0, 0, 'PENDING_APPROVAL', 1, @Now, @Now, 1)",
            new { Id = bloodReqId, Now = now });

        await _callbackDispatcher.OnWorkflowCompletedAsync("BLOOD_REQUEST", bloodReqId, "APPROVED", "USR-DEPT-MGR", now);

        var updatedBlood = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT * FROM mtc_blood_requests WHERE id = @Id", new { Id = bloodReqId });
        Assert.Equal("APPROVED", (string)updatedBlood.approval_status);

        // 3. Verify EMR_ACCESS_GRANT gating
        string grantId = "GRANT-GATE-" + Guid.NewGuid().ToString("N")[..6];
        await conn.ExecuteAsync(@"
            INSERT INTO sec_emr_access_grants (id, patient_id, grantee_user_id, grantee_org_code, is_external_grant, allowed_scopes_json, valid_from_ms, valid_to_ms, grant_reason, approved_by, status, approval_status, schema_version, created_at_ms, updated_at_ms, version_no)
            VALUES (@Id, 'PAT01', 'DOC_EXT', 'ORG-EXT', 1, '[""ALL""]', @Now, @Now + 86400000, '会诊调阅', 'USR-DEPT-MGR', 0, 'PENDING_APPROVAL', 1, @Now, @Now, 1)",
            new { Id = grantId, Now = now });

        await _callbackDispatcher.OnWorkflowCompletedAsync("EMR_ACCESS_GRANT", grantId, "APPROVED", "USR-DEPT-MGR", now);

        var updatedGrant = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT * FROM sec_emr_access_grants WHERE id = @Id", new { Id = grantId });
        Assert.Equal("APPROVED", (string)updatedGrant.approval_status);
        Assert.Equal(1, (int)updatedGrant.status); // Automatically activated upon approval!
    }
}
