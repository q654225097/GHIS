using System;
using System.Data.Common;
using System.Threading.Tasks;
using Dapper;

namespace HIS.BuildingBlocks.Database;

public static partial class DbSchemaInitializer
{
	public static async Task EnsureWorkflowSchemaAsync(DbConnection conn)
	{
		// 1. 组织架构补齐（幂等加列）
		await EnsureColumnAsync(conn, "sys_departments", "parent_dept_id", "VARCHAR(36)");
		await EnsureColumnAsync(conn, "sys_departments", "manager_user_id", "VARCHAR(36)");
		await EnsureColumnAsync(conn, "sys_users", "direct_supervisor_id", "VARCHAR(36)");

		// 2. 现有核心业务单据表原生加列
		string[] businessTables = new[]
		{
			"csm_requisitions",
			"inv_transfer_orders",
			"inv_stock_takes",
			"mtc_blood_requests",
			"mte_surgery_schedules",
			"sec_emr_access_grants",
			"ord_medical_orders",
			"ipd_discharge_requests"
		};

		foreach (var table in businessTables)
		{
			await EnsureColumnAsync(conn, table, "approval_status", "VARCHAR(24) NOT NULL DEFAULT 'DRAFT'");
			await EnsureColumnAsync(conn, table, "workflow_instance_id", "VARCHAR(36)");
			await EnsureColumnAsync(conn, table, "submitted_at_ms", "BIGINT");
			await EnsureColumnAsync(conn, table, "approved_at_ms", "BIGINT");
			await EnsureColumnAsync(conn, table, "approved_by", "VARCHAR(36)");
		}

		// 3. 工作流引擎核心 6 张表
		await conn.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS wf_definitions (
    id VARCHAR(36) PRIMARY KEY,
    def_key VARCHAR(64) NOT NULL,
    def_name VARCHAR(128) NOT NULL,
    bo_type VARCHAR(64) NOT NULL,
    version_num INT NOT NULL DEFAULT 1,
    status VARCHAR(24) NOT NULL DEFAULT 'DRAFT',
    bo_fields_schema_json TEXT,
    graph_json TEXT,
    fallback_approver_id VARCHAR(36),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_wf_def_key_ver ON wf_definitions (def_key, version_num);
CREATE INDEX IF NOT EXISTS ix_wf_def_bo_status ON wf_definitions (bo_type, status);

CREATE TABLE IF NOT EXISTS wf_instances (
    id VARCHAR(36) PRIMARY KEY,
    instance_no VARCHAR(40) NOT NULL UNIQUE,
    definition_id VARCHAR(36) NOT NULL,
    def_key VARCHAR(64) NOT NULL,
    definition_version INT NOT NULL,
    bo_type VARCHAR(64) NOT NULL,
    business_key VARCHAR(64) NOT NULL,
    initiator_user_id VARCHAR(36) NOT NULL,
    initiator_dept_id VARCHAR(36),
    current_node_ids_json TEXT,
    bo_variables_json TEXT,
    status VARCHAR(24) NOT NULL DEFAULT 'RUNNING',
    started_at_ms BIGINT NOT NULL,
    finished_at_ms BIGINT,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_wf_inst_bo ON wf_instances (bo_type, business_key);
CREATE INDEX IF NOT EXISTS ix_wf_inst_initiator ON wf_instances (initiator_user_id, status);

CREATE TABLE IF NOT EXISTS wf_tasks (
    id VARCHAR(36) PRIMARY KEY,
    instance_id VARCHAR(36) NOT NULL,
    node_id VARCHAR(64) NOT NULL,
    node_name VARCHAR(128) NOT NULL,
    assignee_user_id VARCHAR(36) NOT NULL,
    assignee_name VARCHAR(64),
    approval_mode VARCHAR(24) NOT NULL DEFAULT 'SINGLE',
    status VARCHAR(24) NOT NULL DEFAULT 'PENDING',
    comment VARCHAR(1000),
    acted_at_ms BIGINT,
    delegated_from_user_id VARCHAR(36),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_wf_tasks_assignee_status ON wf_tasks (assignee_user_id, status);
CREATE INDEX IF NOT EXISTS ix_wf_tasks_instance ON wf_tasks (instance_id);

CREATE TABLE IF NOT EXISTS wf_history_tracks (
    id VARCHAR(36) PRIMARY KEY,
    instance_id VARCHAR(36) NOT NULL,
    task_id VARCHAR(36),
    node_id VARCHAR(64) NOT NULL,
    node_name VARCHAR(128) NOT NULL,
    operator_user_id VARCHAR(36) NOT NULL,
    operator_name VARCHAR(64),
    action_type VARCHAR(24) NOT NULL,
    comment VARCHAR(1000),
    snapshot_json TEXT,
    occurred_at_ms BIGINT NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_wf_tracks_instance ON wf_history_tracks (instance_id, occurred_at_ms);

CREATE TABLE IF NOT EXISTS wf_business_bindings (
    id VARCHAR(36) PRIMARY KEY,
    bo_type VARCHAR(64) NOT NULL,
    business_key VARCHAR(64) NOT NULL,
    instance_id VARCHAR(36) NOT NULL,
    workflow_status VARCHAR(24) NOT NULL,
    callback_status VARCHAR(24) NOT NULL DEFAULT 'SUCCESS',
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_wf_binding_bo ON wf_business_bindings (bo_type, business_key);

CREATE TABLE IF NOT EXISTS wf_notifications (
    id VARCHAR(36) PRIMARY KEY,
    instance_id VARCHAR(36) NOT NULL,
    receiver_user_id VARCHAR(36) NOT NULL,
    notify_type VARCHAR(24) NOT NULL,
    title VARCHAR(200) NOT NULL,
    content VARCHAR(1000) NOT NULL,
    is_read SMALLINT NOT NULL DEFAULT 0,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_wf_notif_receiver ON wf_notifications (receiver_user_id, is_read);
");
	}
}
