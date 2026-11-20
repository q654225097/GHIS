using System;
using System.Data.Common;
using System.Threading.Tasks;
using Dapper;

namespace HIS.Server.Host;

public static partial class DbSeedData
{
    public static async Task SeedWorkflowDataAsync(DbConnection conn, long nowMs)
    {
        // 1. 临床用血审批流 (BLOOD_REQUEST)
        string bloodGraphJson = """
{
  "nodes": [
    { "id": "node_start", "name": "提交用血申请", "nodeType": "START", "x": 80, "y": 180 },
    { "id": "gw_volume", "name": "用血量排他网关", "nodeType": "EXCLUSIVE_GATEWAY", "x": 260, "y": 180 },
    { "id": "node_dept_mgr", "name": "科室主任审批", "nodeType": "APPROVAL", "x": 480, "y": 100, "approvalMode": "SINGLE", "assigneeConfig": { "strategy": "DEPT_MANAGER" }, "fallbackApproverId": "USR-ADMIN" },
    { "id": "node_med_director", "name": "医务部大量用血审批", "nodeType": "APPROVAL", "x": 480, "y": 260, "approvalMode": "AND_SIGN", "assigneeConfig": { "strategy": "ROLE", "roleCode": "DOCTOR" }, "fallbackApproverId": "USR-ADMIN" },
    { "id": "node_cc_bld", "name": "输血科备血抄送", "nodeType": "CC", "x": 720, "y": 180, "ccReceiverUserIds": ["USR-ADMIN"] },
    { "id": "node_end", "name": "准予发血", "nodeType": "END", "x": 900, "y": 180 }
  ],
  "edges": [
    { "id": "e1", "sourceNodeId": "node_start", "targetNodeId": "gw_volume", "label": "提交" },
    { "id": "e2", "sourceNodeId": "gw_volume", "targetNodeId": "node_med_director", "label": ">=800ml大量用血", "priority": 1, "isDefault": false, "conditionGroup": { "logicalOp": "AND", "rules": [{ "field": "bloodVolumeMl", "operator": ">=", "value": "800" }] } },
    { "id": "e3", "sourceNodeId": "gw_volume", "targetNodeId": "node_dept_mgr", "label": "常规用血兜底", "priority": 2, "isDefault": true },
    { "id": "e4", "sourceNodeId": "node_dept_mgr", "targetNodeId": "node_cc_bld", "label": "主任通过" },
    { "id": "e5", "sourceNodeId": "node_med_director", "targetNodeId": "node_cc_bld", "label": "医务部会签通过" },
    { "id": "e6", "sourceNodeId": "node_cc_bld", "targetNodeId": "node_end", "label": "归档放行" }
  ]
}
""";

        // 2. 耗材请领审批流 (CSM_REQUISITION)
        string csmGraphJson = """
{
  "nodes": [
    { "id": "node_start", "name": "提交耗材请领", "nodeType": "START", "x": 80, "y": 180 },
    { "id": "gw_amount", "name": "请领金额网关", "nodeType": "EXCLUSIVE_GATEWAY", "x": 260, "y": 180 },
    { "id": "node_dept_mgr", "name": "科室负责人审核", "nodeType": "APPROVAL", "x": 480, "y": 100, "approvalMode": "SINGLE", "assigneeConfig": { "strategy": "DEPT_MANAGER" }, "fallbackApproverId": "USR-ADMIN" },
    { "id": "node_vp", "name": "分管院领导审批", "nodeType": "APPROVAL", "x": 480, "y": 260, "approvalMode": "SINGLE", "assigneeConfig": { "strategy": "FIXED_USER", "fixedUserIds": ["USR-ADMIN"] }, "fallbackApproverId": "USR-ADMIN" },
    { "id": "node_cc_wh", "name": "库房备货抄送", "nodeType": "CC", "x": 720, "y": 180, "ccReceiverUserIds": ["USR-ADMIN"] },
    { "id": "node_end", "name": "准予出库", "nodeType": "END", "x": 900, "y": 180 }
  ],
  "edges": [
    { "id": "e1", "sourceNodeId": "node_start", "targetNodeId": "gw_amount", "label": "提交" },
    { "id": "e2", "sourceNodeId": "gw_amount", "targetNodeId": "node_vp", "label": ">5000元大额耗材", "priority": 1, "isDefault": false, "conditionGroup": { "logicalOp": "AND", "rules": [{ "field": "totalAmount", "operator": ">", "value": "5000" }] } },
    { "id": "e3", "sourceNodeId": "gw_amount", "targetNodeId": "node_dept_mgr", "label": "常规耗材兜底", "priority": 2, "isDefault": true },
    { "id": "e4", "sourceNodeId": "node_dept_mgr", "targetNodeId": "node_cc_wh", "label": "科室通过" },
    { "id": "e5", "sourceNodeId": "node_vp", "targetNodeId": "node_cc_wh", "label": "院领导通过" },
    { "id": "e6", "sourceNodeId": "node_cc_wh", "targetNodeId": "node_end", "label": "通知出库" }
  ]
}
""";

        // 3. 病历跨科外院调阅审批流 (EMR_ACCESS_GRANT)
        string emrGraphJson = """
{
  "nodes": [
    { "id": "node_start", "name": "提交调阅申请", "nodeType": "START", "x": 80, "y": 180 },
    { "id": "gw_org", "name": "调阅范围网关", "nodeType": "EXCLUSIVE_GATEWAY", "x": 260, "y": 180 },
    { "id": "node_dept", "name": "归属科室主任审批", "nodeType": "APPROVAL", "x": 480, "y": 100, "approvalMode": "SINGLE", "assigneeConfig": { "strategy": "DEPT_MANAGER" }, "fallbackApproverId": "USR-ADMIN" },
    { "id": "node_legal", "name": "医务处外院调阅审批", "nodeType": "APPROVAL", "x": 480, "y": 260, "approvalMode": "AND_SIGN", "assigneeConfig": { "strategy": "FIXED_USER", "fixedUserIds": ["USR-ADMIN"] }, "fallbackApproverId": "USR-ADMIN" },
    { "id": "node_end", "name": "授权生效", "nodeType": "END", "x": 760, "y": 180 }
  ],
  "edges": [
    { "id": "e1", "sourceNodeId": "node_start", "targetNodeId": "gw_org", "label": "申请" },
    { "id": "e2", "sourceNodeId": "gw_org", "targetNodeId": "node_legal", "label": "外院跨机构共享", "priority": 1, "isDefault": false, "conditionGroup": { "logicalOp": "AND", "rules": [{ "field": "isExternalOrg", "operator": "==", "value": "true" }] } },
    { "id": "e3", "sourceNodeId": "gw_org", "targetNodeId": "node_dept", "label": "院内跨科兜底", "priority": 2, "isDefault": true },
    { "id": "e4", "sourceNodeId": "node_dept", "targetNodeId": "node_end", "label": "科室授权" },
    { "id": "e5", "sourceNodeId": "node_legal", "targetNodeId": "node_end", "label": "医务处授权" }
  ]
}
""";

        await conn.ExecuteAsync("""
INSERT OR REPLACE INTO wf_definitions
    (id, def_key, def_name, bo_type, version_num, status, bo_fields_schema_json, graph_json, fallback_approver_id, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('WF-DEF-BLOOD-01', 'WF_DEF_BLOOD_REQUEST', '临床用血分级审批流', 'BLOOD_REQUEST', 1, 'PUBLISHED', '[]', @BloodGraph, 'USR-ADMIN', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('WF-DEF-CSM-01', 'WF_DEF_CSM_REQUISITION', '科室耗材请领审批流', 'CSM_REQUISITION', 1, 'PUBLISHED', '[]', @CsmGraph, 'USR-ADMIN', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('WF-DEF-EMR-01', 'WF_DEF_EMR_ACCESS_GRANT', '病历跨科与外院调阅审批流', 'EMR_ACCESS_GRANT', 1, 'PUBLISHED', '[]', @EmrGraph, 'USR-ADMIN', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);
""", new { BloodGraph = bloodGraphJson, CsmGraph = csmGraphJson, EmrGraph = emrGraphJson, Now = nowMs });
    }
}
