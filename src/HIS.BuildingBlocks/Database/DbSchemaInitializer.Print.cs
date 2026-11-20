using System.Data.Common;
using Dapper;

namespace HIS.BuildingBlocks.Database;

public static partial class DbSchemaInitializer
{
    private static async Task InitializePrintTablesAsync(DbConnection conn)
    {
        await conn.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS prt_templates (
    id VARCHAR(36) PRIMARY KEY,
    template_code VARCHAR(64) NOT NULL UNIQUE,
    template_name VARCHAR(128) NOT NULL,
    category VARCHAR(32) NOT NULL DEFAULT 'BARCODE',
    page_key VARCHAR(64) NOT NULL DEFAULT 'medtech',
    paper_width_mm DECIMAL(10,2) NOT NULL DEFAULT 60,
    paper_height_mm DECIMAL(10,2) NOT NULL DEFAULT 35,
    orientation VARCHAR(16) NOT NULL DEFAULT 'PORTRAIT',
    margin_top_mm DECIMAL(8,2) NOT NULL DEFAULT 2,
    margin_right_mm DECIMAL(8,2) NOT NULL DEFAULT 2,
    margin_bottom_mm DECIMAL(8,2) NOT NULL DEFAULT 2,
    margin_left_mm DECIMAL(8,2) NOT NULL DEFAULT 2,
    layout_json TEXT NOT NULL,
    is_published SMALLINT NOT NULL DEFAULT 1,
    is_active SMALLINT NOT NULL DEFAULT 1,
    remark VARCHAR(500),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_prt_templates_page ON prt_templates (page_key, is_active);

CREATE TABLE IF NOT EXISTS prt_trigger_bindings (
    id VARCHAR(36) PRIMARY KEY,
    template_id VARCHAR(36) NOT NULL,
    page_key VARCHAR(64) NOT NULL,
    trigger_slot VARCHAR(32) NOT NULL DEFAULT 'TOP_TOOLBAR',
    button_label VARCHAR(64) NOT NULL,
    button_icon VARCHAR(16),
    condition_expr VARCHAR(500),
    sort_order INT NOT NULL DEFAULT 10,
    is_active SMALLINT NOT NULL DEFAULT 1,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_prt_triggers_page_slot ON prt_trigger_bindings (page_key, trigger_slot, is_active);

CREATE TABLE IF NOT EXISTS prt_print_logs (
    id VARCHAR(36) PRIMARY KEY,
    template_id VARCHAR(36) NOT NULL,
    template_code VARCHAR(64) NOT NULL,
    template_name VARCHAR(128) NOT NULL,
    page_key VARCHAR(64) NOT NULL,
    trigger_slot VARCHAR(32) NOT NULL,
    business_key VARCHAR(64),
    patient_name VARCHAR(128),
    rendered_context_json TEXT,
    copies INT NOT NULL DEFAULT 1,
    printer_name VARCHAR(128),
    printed_by VARCHAR(36),
    printed_by_name VARCHAR(128),
    printed_at_ms BIGINT NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_prt_logs_time ON prt_print_logs (printed_at_ms DESC);
");

        // 初始化默认模板与触发位（INSERT OR IGNORE 保证绝不覆盖管理员已保存的配置）
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string medtechBarcodeLayout = """
[
  {"id":"el_title","type":"TEXT","xMm":2,"yMm":1.5,"widthMm":56,"heightMm":5,"fontSize":9,"isBold":true,"textAlign":"LEFT","staticText":"{{patient.fullName}}  {{patient.gender}} {{patient.age}}岁","bindingPath":"patient.fullName","barcodeFormat":"CODE128","showBarcodeText":false},
  {"id":"el_mrn","type":"FIELD","xMm":2,"yMm":6.5,"widthMm":56,"heightMm":4.5,"fontSize":8,"isBold":false,"textAlign":"LEFT","staticText":"病历号:{{patient.medicalRecordNo}}  {{medtech.deptName}}","bindingPath":"patient.medicalRecordNo","barcodeFormat":"CODE128","showBarcodeText":false},
  {"id":"el_barcode","type":"BARCODE","xMm":3,"yMm":11.5,"widthMm":54,"heightMm":13,"fontSize":8,"isBold":false,"textAlign":"CENTER","staticText":"{{medtech.requestNo}}","bindingPath":"medtech.requestNo","barcodeFormat":"CODE128","showBarcodeText":true},
  {"id":"el_item","type":"FIELD","xMm":2,"yMm":25.5,"widthMm":56,"heightMm":4.5,"fontSize":8,"isBold":true,"textAlign":"LEFT","staticText":"项目:{{medtech.itemName}}","bindingPath":"medtech.itemName","barcodeFormat":"CODE128","showBarcodeText":false},
  {"id":"el_loc","type":"TEXT","xMm":2,"yMm":30,"widthMm":56,"heightMm":4,"fontSize":7,"isBold":false,"textAlign":"LEFT","staticText":"地点:{{medtech.execLocation}}  {{system.printTime}}","bindingPath":"medtech.execLocation","barcodeFormat":"CODE128","showBarcodeText":false}
]
""";
        string opdSlipLayout = """
[
  {"id":"opd_h1","type":"TEXT","xMm":10,"yMm":8,"widthMm":128,"heightMm":10,"fontSize":16,"isBold":true,"textAlign":"CENTER","staticText":"GHIS 门诊诊疗导诊单","bindingPath":"","barcodeFormat":"CODE128","showBarcodeText":false},
  {"id":"opd_line1","type":"LINE","xMm":8,"yMm":20,"widthMm":132,"heightMm":1,"fontSize":8,"isBold":false,"textAlign":"LEFT","staticText":"","bindingPath":"","barcodeFormat":"CODE128","showBarcodeText":false},
  {"id":"opd_pat","type":"FIELD","xMm":10,"yMm":24,"widthMm":128,"heightMm":8,"fontSize":11,"isBold":true,"textAlign":"LEFT","staticText":"姓名：{{patient.fullName}}   性别：{{patient.gender}}   年龄：{{patient.age}}岁","bindingPath":"patient.fullName","barcodeFormat":"CODE128","showBarcodeText":false},
  {"id":"opd_visit","type":"FIELD","xMm":10,"yMm":34,"widthMm":128,"heightMm":7,"fontSize":10,"isBold":false,"textAlign":"LEFT","staticText":"就诊号：{{patient.visitNo}}   病历号：{{patient.medicalRecordNo}}   科室：{{patient.deptName}}","bindingPath":"patient.visitNo","barcodeFormat":"CODE128","showBarcodeText":false},
  {"id":"opd_bc","type":"BARCODE","xMm":34,"yMm":45,"widthMm":80,"heightMm":18,"fontSize":9,"isBold":false,"textAlign":"CENTER","staticText":"{{patient.visitNo}}","bindingPath":"patient.visitNo","barcodeFormat":"CODE128","showBarcodeText":true},
  {"id":"opd_cc","type":"FIELD","xMm":10,"yMm":68,"widthMm":128,"heightMm":24,"fontSize":10,"isBold":false,"textAlign":"LEFT","staticText":"主诉：{{patient.chiefComplaint}}","bindingPath":"patient.chiefComplaint","barcodeFormat":"CODE128","showBarcodeText":false},
  {"id":"opd_foot","type":"TEXT","xMm":10,"yMm":192,"widthMm":128,"heightMm":8,"fontSize":9,"isBold":false,"textAlign":"RIGHT","staticText":"接诊医生：{{patient.doctorName}}   打印时间：{{system.printTime}}","bindingPath":"system.printTime","barcodeFormat":"CODE128","showBarcodeText":false}
]
""";
        string ipdWristbandLayout = """
[
  {"id":"wb_name","type":"FIELD","xMm":3,"yMm":2,"widthMm":46,"heightMm":7,"fontSize":11,"isBold":true,"textAlign":"LEFT","staticText":"{{ipd.patientName}} {{ipd.gender}} {{ipd.age}}岁","bindingPath":"ipd.patientName","barcodeFormat":"CODE128","showBarcodeText":false},
  {"id":"wb_bed","type":"FIELD","xMm":50,"yMm":2,"widthMm":38,"heightMm":7,"fontSize":10,"isBold":true,"textAlign":"RIGHT","staticText":"床号:{{ipd.bedCode}}","bindingPath":"ipd.bedCode","barcodeFormat":"CODE128","showBarcodeText":false},
  {"id":"wb_dept","type":"TEXT","xMm":3,"yMm":9.5,"widthMm":84,"heightMm":5,"fontSize":8,"isBold":false,"textAlign":"LEFT","staticText":"病区:{{ipd.nursingWard}}  住院号:{{ipd.admissionNo}}","bindingPath":"ipd.admissionNo","barcodeFormat":"CODE128","showBarcodeText":false},
  {"id":"wb_bc","type":"BARCODE","xMm":6,"yMm":15,"widthMm":78,"heightMm":12.5,"fontSize":7,"isBold":false,"textAlign":"CENTER","staticText":"{{ipd.admissionNo}}","bindingPath":"ipd.admissionNo","barcodeFormat":"CODE128","showBarcodeText":true}
]
""";

        await conn.ExecuteAsync(@"
INSERT OR IGNORE INTO prt_templates (id, template_code, template_name, category, page_key, paper_width_mm, paper_height_mm, orientation, margin_top_mm, margin_right_mm, margin_bottom_mm, margin_left_mm, layout_json, is_published, is_active, remark, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('PRT-TPL-MEDTECH-BC', 'TPL-MEDTECH-BARCODE', '检验标本条码 (60×35mm)', 'BARCODE', 'medtech', 60, 35, 'PORTRAIT', 1.5, 1.5, 1.5, 1.5, @MedTechJson, 1, 1, '检验检查工作站默认标本条码', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('PRT-TPL-OPD-SLIP', 'TPL-OPD-GUIDE', '门诊导诊单 (A5 148×210mm)', 'SLIP', 'opd_clinic', 148, 210, 'PORTRAIT', 5, 5, 5, 5, @OpdJson, 1, 1, '门诊医生站默认导诊单', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('PRT-TPL-IPD-WB', 'TPL-IPD-WRISTBAND', '住院患者腕带 (90×30mm)', 'BARCODE', 'ipd_nurse', 90, 30, 'PORTRAIT', 1.5, 1.5, 1.5, 1.5, @IpdJson, 1, 1, '住院护士站患者腕带条码', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

INSERT OR IGNORE INTO prt_trigger_bindings (id, template_id, page_key, trigger_slot, button_label, button_icon, condition_expr, sort_order, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('PRT-TRG-MEDTECH-TOP', 'PRT-TPL-MEDTECH-BC', 'medtech', 'TOP_TOOLBAR', '打印标本条码', '🏷️', NULL, 10, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('PRT-TRG-MEDTECH-ROW', 'PRT-TPL-MEDTECH-BC', 'medtech', 'LIST_ROW', '打条码', '🏷️', NULL, 10, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('PRT-TRG-OPD-TOP', 'PRT-TPL-OPD-SLIP', 'opd_clinic', 'TOP_TOOLBAR', '打印导诊单', '🖨️', NULL, 10, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('PRT-TRG-IPD-TOP', 'PRT-TPL-IPD-WB', 'ipd_nurse', 'TOP_TOOLBAR', '打印患者腕带', '🏷️', NULL, 10, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);
", new { MedTechJson = medtechBarcodeLayout, OpdJson = opdSlipLayout, IpdJson = ipdWristbandLayout, Now = now });
    }
}
