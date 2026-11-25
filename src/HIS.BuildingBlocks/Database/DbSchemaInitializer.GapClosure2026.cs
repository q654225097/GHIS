using System.Data.Common;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;

namespace HIS.BuildingBlocks.Database;

public static partial class DbSchemaInitializer
{
    private static async Task InitializeGapClosure2026TablesAsync(DbConnection conn)
    {
        await conn.ExecuteAsync(@"
-- 药库合格供应商与资质台账 (GSP)
CREATE TABLE IF NOT EXISTS inv_suppliers (
    id VARCHAR(36) PRIMARY KEY,
    supplier_code VARCHAR(40) NOT NULL UNIQUE,
    supplier_name VARCHAR(200) NOT NULL,
    license_no VARCHAR(80) NOT NULL,
    license_expiry_int INT NOT NULL,
    contact_name VARCHAR(64),
    contact_phone VARCHAR(32),
    is_cold_chain_qualified SMALLINT NOT NULL DEFAULT 1,
    is_active SMALLINT NOT NULL DEFAULT 1,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

-- 药品采购订单头表 (PO)
CREATE TABLE IF NOT EXISTS inv_purchase_orders (
    id VARCHAR(36) PRIMARY KEY,
    po_no VARCHAR(40) NOT NULL UNIQUE,
    warehouse_id VARCHAR(36) NOT NULL,
    supplier_id VARCHAR(36) NOT NULL,
    supplier_name VARCHAR(200) NOT NULL,
    status VARCHAR(32) NOT NULL DEFAULT 'DRAFT',
    total_estimated_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
    applicant_id VARCHAR(36),
    approved_by VARCHAR(36),
    approved_at_ms BIGINT,
    remark VARCHAR(500),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_inv_purchase_orders_status ON inv_purchase_orders (warehouse_id, status);

-- 药品采购订单明细行
CREATE TABLE IF NOT EXISTS inv_purchase_order_lines (
    id VARCHAR(36) PRIMARY KEY,
    po_id VARCHAR(36) NOT NULL,
    drug_id VARCHAR(36) NOT NULL,
    drug_code VARCHAR(64) NOT NULL,
    drug_name VARCHAR(200) NOT NULL,
    spec VARCHAR(100),
    requested_qty INT NOT NULL,
    received_qty INT NOT NULL DEFAULT 0,
    unit_cost_price DECIMAL(18,4) NOT NULL DEFAULT 0,
    created_at_ms BIGINT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_inv_po_lines_po ON inv_purchase_order_lines (po_id);

-- GSP 到货双人验收记录
CREATE TABLE IF NOT EXISTS inv_gsp_acceptances (
    id VARCHAR(36) PRIMARY KEY,
    acceptance_no VARCHAR(40) NOT NULL UNIQUE,
    po_id VARCHAR(36),
    batch_id VARCHAR(36) NOT NULL,
    warehouse_id VARCHAR(36) NOT NULL,
    drug_id VARCHAR(36) NOT NULL,
    batch_no VARCHAR(80) NOT NULL,
    quantity INT NOT NULL,
    appearance_ok SMALLINT NOT NULL DEFAULT 1,
    package_intact SMALLINT NOT NULL DEFAULT 1,
    label_consistent SMALLINT NOT NULL DEFAULT 1,
    cold_chain_temp DECIMAL(18,4),
    receiver_id VARCHAR(36) NOT NULL,
    checker_id VARCHAR(36) NOT NULL,
    accepted_at_ms BIGINT NOT NULL,
    conclusion VARCHAR(32) NOT NULL DEFAULT 'QUALIFIED',
    remark VARCHAR(500),
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36)
);
CREATE INDEX IF NOT EXISTS ix_inv_gsp_acceptances_batch ON inv_gsp_acceptances (batch_id);

-- 检验标本病区采集-转运-检验窗口核收交接链路
CREATE TABLE IF NOT EXISTS mtc_specimen_handovers (
    id VARCHAR(36) PRIMARY KEY,
    handover_no VARCHAR(40) NOT NULL UNIQUE,
    request_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    ward_id VARCHAR(36),
    sample_barcode VARCHAR(64) NOT NULL,
    drawn_by_nurse_id VARCHAR(36) NOT NULL,
    drawn_at_ms BIGINT NOT NULL,
    courier_id VARCHAR(64),
    dispatched_at_ms BIGINT,
    lab_receiver_id VARCHAR(36),
    received_at_ms BIGINT,
    status VARCHAR(32) NOT NULL DEFAULT 'DRAWN',
    rejection_reason VARCHAR(500),
    notes VARCHAR(500),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_mtc_specimen_handover_req ON mtc_specimen_handovers (request_id, status);
");

        await EnsureColumnAsync(conn, "ord_medical_orders", "is_urgent", "SMALLINT NOT NULL DEFAULT 0");
        await EnsureColumnAsync(conn, "opd_appointments", "checkin_status", "INT NOT NULL DEFAULT 1");
        await EnsureColumnAsync(conn, "opd_schedule_slots", "slot_interval_minutes", "INT NOT NULL DEFAULT 0");
        await EnsureColumnAsync(conn, "opd_schedule_templates", "slot_interval_minutes", "INT NOT NULL DEFAULT 0");
        await EnsureColumnAsync(conn, "inv_drug_batches", "supplier_id", "VARCHAR(36)");
        await EnsureColumnAsync(conn, "inv_drug_batches", "po_id", "VARCHAR(36)");

        long now = SystemTime.UtcNowMs;
        await conn.ExecuteAsync(@"
INSERT OR IGNORE INTO inv_suppliers
    (id, supplier_code, supplier_name, license_no, license_expiry_int, contact_name, contact_phone, is_cold_chain_qualified, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('SUP-CQYY-01', 'SUP-GZ-001', '国药控股医疗供应链有限公司', 'GSP-JY-2026-0089', 20291231, '陈经理', '13800009001', 1, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('SUP-SHYY-02', 'SUP-SH-002', '上海医药分销冷链有限公司', 'GSP-JY-2026-0156', 20281031, '林主管', '13900009002', 1, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);
", new { Now = now });
    }
}
