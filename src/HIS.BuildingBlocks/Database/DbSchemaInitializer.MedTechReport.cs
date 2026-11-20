using System.Data.Common;
using Dapper;

namespace HIS.BuildingBlocks.Database;

public static partial class DbSchemaInitializer
{
    private static async Task InitializeMedTechReportTablesAsync(DbConnection conn)
    {
        await conn.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS mtc_report_templates (
    id VARCHAR(36) PRIMARY KEY,
    template_code VARCHAR(64) NOT NULL UNIQUE,
    template_name VARCHAR(128) NOT NULL,
    medtech_dept_code VARCHAR(32) NOT NULL DEFAULT 'RAD',
    medtech_dept_name VARCHAR(64) NOT NULL DEFAULT '放射科',
    modality_type VARCHAR(32) NOT NULL DEFAULT 'CT/DR',
    editor_schema_json TEXT NOT NULL,
    print_paper_width_mm DECIMAL(10,2) NOT NULL DEFAULT 210,
    print_paper_height_mm DECIMAL(10,2) NOT NULL DEFAULT 297,
    print_header_title VARCHAR(128) NOT NULL DEFAULT 'GHIS 示范医院 医技诊断报告单',
    print_sub_title VARCHAR(128),
    print_footer_note VARCHAR(255) DEFAULT '本报告仅供临床医师参考，不作法律证明；如有疑问请在报告发布后48小时内联系本科室。',
    print_template_id VARCHAR(36),
    is_default SMALLINT NOT NULL DEFAULT 0,
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
CREATE INDEX IF NOT EXISTS ix_mtc_rpt_tpl_dept ON mtc_report_templates (medtech_dept_code, is_active);

CREATE TABLE IF NOT EXISTS mtc_diagnostic_reports (
    id VARCHAR(36) PRIMARY KEY,
    report_no VARCHAR(40) NOT NULL UNIQUE,
    service_request_id VARCHAR(36),
    request_no VARCHAR(40),
    patient_id VARCHAR(36),
    patient_name VARCHAR(128) NOT NULL,
    gender VARCHAR(16) NOT NULL DEFAULT '男',
    age INT NOT NULL DEFAULT 35,
    medical_record_no VARCHAR(32),
    order_dept_name VARCHAR(64),
    medtech_dept_code VARCHAR(32) NOT NULL DEFAULT 'RAD',
    medtech_dept_name VARCHAR(64) NOT NULL DEFAULT '放射科',
    exam_item_code VARCHAR(64),
    exam_item_name VARCHAR(200) NOT NULL,
    template_id VARCHAR(36),
    template_name VARCHAR(128),
    result_source VARCHAR(32) NOT NULL DEFAULT 'MANUAL',
    device_name VARCHAR(128),
    status VARCHAR(32) NOT NULL DEFAULT 'PENDING_WRITE',
    content_json TEXT NOT NULL,
    findings_summary TEXT,
    diagnosis_impression TEXT,
    is_critical_value SMALLINT NOT NULL DEFAULT 0,
    writer_id VARCHAR(36),
    writer_name VARCHAR(64),
    written_at_ms BIGINT,
    reviewer_id VARCHAR(36),
    reviewer_name VARCHAR(64),
    reviewed_at_ms BIGINT,
    reject_reason VARCHAR(500),
    signature_hash VARCHAR(64),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_mtc_diag_rpt_status ON mtc_diagnostic_reports (medtech_dept_code, status, updated_at_ms DESC);
CREATE INDEX IF NOT EXISTS ix_mtc_diag_rpt_req ON mtc_diagnostic_reports (service_request_id);
");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        string radSchema = """
[
  {"key":"body_part","label":"检查部位","fieldType":"TEXT","required":true,"defaultValue":"胸部平扫","options":""},
  {"key":"scan_method","label":"扫描方式与层厚","fieldType":"SELECT","required":false,"defaultValue":"螺旋CT平扫 1.25mm","options":"螺旋CT平扫 1.25mm|增强CT扫描|DR正侧位摄片|MRI多序列平扫"},
  {"key":"findings","label":"影像学表现","fieldType":"MULTILINE","required":true,"defaultValue":"胸廓对称，双肺纹理清晰，未见明显实质性渗出或占位病变；气管及主支气管通畅，纵隔居中未见肿大淋巴结，心影大小形态正常，双侧肋膈角锐利。","options":""},
  {"key":"impression","label":"影像诊断意见","fieldType":"MULTILINE","required":true,"defaultValue":"胸部CT平扫未见明显异常，请结合临床。","options":""},
  {"key":"advice","label":"随访/复查建议","fieldType":"TEXT","required":false,"defaultValue":"必要时临床随访复查。","options":""}
]
""";
        string usSchema = """
[
  {"key":"body_part","label":"超声检查部位","fieldType":"TEXT","required":true,"defaultValue":"肝、胆、胰、脾","options":""},
  {"key":"device_model","label":"超声设备型号","fieldType":"TEXT","required":false,"defaultValue":"GE Voluson E10 彩超仪","options":""},
  {"key":"findings","label":"超声所见（含径线测量）","fieldType":"MULTILINE","required":true,"defaultValue":"肝脏形态大小正常，包膜光滑，实质回声均匀，肝内管道结构清晰。胆囊大小约6.8×2.6cm，壁薄光滑，腔内透声好，未见结石及占位。胰腺及脾脏大小形态回声未见异常。","options":""},
  {"key":"cdfi","label":"CDFI 血流信号","fieldType":"TEXT","required":false,"defaultValue":"CDFI：上述脏器未见明显异常血流信号。","options":""},
  {"key":"impression","label":"超声提示","fieldType":"MULTILINE","required":true,"defaultValue":"肝、胆、胰、脾超声检查未见明显异常。","options":""}
]
""";
        string labSchema = """
[
  {"key":"specimen_type","label":"标本类型","fieldType":"SELECT","required":true,"defaultValue":"静脉全血(EDTA-K2)","options":"静脉全血(EDTA-K2)|血清(黄头促凝管)|尿液中段|咽拭子|脑脊液"},
  {"key":"analyzer","label":"检测仪器","fieldType":"TEXT","required":false,"defaultValue":"迈瑞 BC-6800Plus / 罗氏 Cobas c702","options":""},
  {"key":"findings","label":"检验指标结果明细（机器直传自动填充）","fieldType":"MULTILINE","required":true,"defaultValue":"","options":""},
  {"key":"impression","label":"检验审核提示","fieldType":"MULTILINE","required":false,"defaultValue":"质控在控，结果与仪器原始数据核对无误。","options":""}
]
""";
        string ecgSchema = """
[
  {"key":"heart_rate","label":"心率 (次/分)","fieldType":"NUMBER","required":true,"defaultValue":"74","options":""},
  {"key":"rhythm","label":"主导心律","fieldType":"SELECT","required":true,"defaultValue":"窦性心律","options":"窦性心律|窦性心动过速|窦性心动过缓|心房颤动|室性早搏"},
  {"key":"intervals","label":"PR / QRS / QT 间期 (ms)","fieldType":"TEXT","required":false,"defaultValue":"PR: 156ms | QRS: 88ms | QT/QTc: 392/418ms","options":""},
  {"key":"findings","label":"波形特征描述","fieldType":"MULTILINE","required":true,"defaultValue":"各导联P波形态正常，ST段无明显抬高或压低，T波直立。","options":""},
  {"key":"impression","label":"心电图诊断","fieldType":"MULTILINE","required":true,"defaultValue":"正常心电图（窦性心律）。","options":""}
]
""";

        await conn.ExecuteAsync(@"
INSERT OR IGNORE INTO mtc_report_templates (id, template_code, template_name, medtech_dept_code, medtech_dept_name, modality_type, editor_schema_json, print_paper_width_mm, print_paper_height_mm, print_header_title, print_sub_title, print_footer_note, is_default, is_active, remark, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('MTC-TPL-RAD', 'TPL-RAD-CT',   '放射科 CT/DR 影像诊断报告模板', 'RAD', '放射科',   'CT/DR', @RadJson, 210, 297, 'GHIS 示范医院 放射科影像诊断报告单', '医学影像中心 · 数字化放射科', '本报告仅供本院临床医师诊疗参考，图像以 PACS 归档为准。', 1, 1, '放射科默认模板', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('MTC-TPL-US',  'TPL-US-COLOR', '超声科 彩色多普勒超声报告模板', 'US',  '超声科',   'US',    @UsJson,  210, 297, 'GHIS 示范医院 超声医学科诊断报告单', '彩色多普勒超声诊断室', '超声检查受声窗及气体干扰，请结合临床及其他检查综合判断。', 1, 1, '超声科默认模板', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('MTC-TPL-LAB', 'TPL-LAB-BIO',  '检验科 临床检验报告模板',       'LAB', '检验科',   'LIS',   @LabJson, 148, 210, 'GHIS 示范医院 临床检验报告单',       '医学检验科（仪器直连审核）', '本结果仅对本次接收标本负责，危急值已按规范通知临床。', 1, 1, '检验科默认模板', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('MTC-TPL-ECG', 'TPL-ECG-12',   '心电图室 十二导联诊断报告模板', 'ECG', '心电图室', 'ECG',   @EcgJson, 148, 210, 'GHIS 示范医院 心电图诊断报告单',     '心电功能检查科', '请结合临床症状动态复查心电图。', 1, 1, '心电图室默认模板', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);
", new { RadJson = radSchema, UsJson = usSchema, LabJson = labSchema, EcgJson = ecgSchema, Now = now });
    }
}
