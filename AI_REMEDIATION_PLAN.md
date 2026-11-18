# GHIS 医院信息系统 AI 整改执行文档

> 基于全量代码评估报告（2026-11-18）生成，供 AI 逐批次执行。
> 每项整改需独立提交，完成后运行全量构建与测试确认无回归。

---

## 当前技术基线

- 项目根目录：`D:\GHIS`
- 解决方案：`HIS.slnx`
- 构建：`dotnet build HIS.slnx --no-restore -v:minimal`
- 测试：`dotnet test HIS.slnx --no-build -v:minimal`
- 当前状态：构建 0 错误 0 警告；测试 52 项中 50 通过、2 项集成测试因共享状态失败
- 服务端口：`http://localhost:5200`
- 客户端：Avalonia 11 + Ursa，入口 `src/Client/HIS.Client.Platform.Desktop`
- 数据库：SQLite，66 张表，种子数据在 `src/Server/HIS.Server.Host/DbSeedData.cs`
- 所有开发账号密码统一为 `123`（上线前重置）

---

## 批次总览

| 批次 | 内容 | 目标 |
|---|---|---|
| P0-A | 入院建档一站式 | 修复入院流程断裂 |
| P0-B | 测试隔离修复 | 恢复全套件绿色 |
| P0-C | 医保适配器架构 | 预留真实医保对接通道 |
| P1-A | 耗材/供应室 CSSD | 补齐医院物流第二支柱 |
| P1-B | LIS/PACS 集成架构 | 预留检验影像对接 |
| P1-C | 手术麻醉管理 | 补齐围术期闭环 |
| P1-D | 护理管理 | 补齐护理文书与质量管理 |
| P1-E | 感控管理 | 补齐院感上报与防控 |
| P1-F | 统计分析工作站 | 给院领导提供运营视图 |
| P2-A | 患者服务 | 满意度/投诉/知情同意 |
| P2-B | 管理支撑 | 成本核算/设备管理 |
| P2-C | 可维护性重构 | 拆分超大 ViewModel |

---

## P0-A：入院建档一站式

### 问题

服务端已有患者建档接口：
```
POST /api/v4/patients/register
权限：Admin, DoctorOpd, DoctorIpd, NurseIpd, Cashier
DTO：RegisterPatientRequestDto(FullName, GenderCode, BirthDateInt, IdCardNo, Phone, GuardianName?, GuardianIdCardNo?, AllergyTags?)
```

但客户端建档 UI 仅在门诊挂号收费站（`RegistrationCashierViewModel`）。住院收费站（`InpatientCashierView`）无建档入口。开住院卡接口 `POST /api/v4/ipd/cards` 限制仅门诊医生或管理员，收费员无法代开。

**实际后果**：新患者直接来住院，必须跨三个站点才能完成建档→开卡→登记。

### 整改内容

#### 1. 服务端：允许收费员代开住院卡

文件：`src/Server/HIS.Modules.Inpatient/Controllers/InpatientAdmissionController.cs`

将 `CreateCard` 方法中的角色检查：

```csharp
// 当前（约第 111 行）：
if (!HasAnyRole("OPD_DOC", "ADMIN") || ...)
```

改为：

```csharp
if (!HasAnyRole("OPD_DOC", "CASHIER", "ADMIN") || ...)
```

同时将错误消息从 `仅门诊医生可以为患者开立住院卡` 改为 `仅门诊医生或收费员可以为患者开立住院卡`。

将审计消息从 `门诊医生为患者 {patient.FullName} 开立住院卡` 改为动态：
```csharp
var creatorRole = IsCurrentUserAdmin() ? "管理员" :
    _userSession?.Roles?.Contains("CASHIER") == true ? "收费员" : "门诊医生";
// ...
$"{creatorRole}为患者 {patient.FullName} 开立住院卡 ..."
```

#### 2. 服务端：新增一站式入院建档组合接口

文件：`src/Server/HIS.Modules.Inpatient/Controllers/InpatientAdmissionController.cs`

新增：

```csharp
public sealed record CreateAdmissionWithPatientRequest(
    // 患者信息
    string FullName,
    string GenderCode,
    int BirthDateInt,
    string IdCardNo,
    string Phone,
    string? GuardianName = null,
    string? GuardianIdCardNo = null,
    List<string>? AllergyTags = null,
    // 住院卡信息
    string SuggestedIpdDeptId,
    string? SuggestedWardId = null,
    string? AdmissionDiagnosis = null,
    string? Remark = null,
    string? RequestKey = null
);
```

```
POST /api/v4/ipd/admission/register-with-patient
权限：CASHIER, ADMIN
```

逻辑：
1. 调用 `POST /api/v4/patients/register` 同等逻辑建档（或直接注入 Patient 模块 service）
2. 调用 `CreateCard` 同等逻辑开立住院卡
3. 返回 `IpdAdmissionCardView`
4. 两个子步骤审计独立记录（`PATIENT_REGISTER` + `IPD_ADMISSION_CARD_CREATE`）
5. 遇到患者证件重复（409），返回已有病历号提示但不阻断流程，允许用户确认用已有患者继续

#### 3. 客户端：住院收费站增加"入院建档与代开住院卡"面板

文件：`src/Client/HIS.Client.UrsaShell/ViewModels/InpatientCashierViewModel.cs`

新增属性（参照 `RegistrationCashierViewModel` 的建档表单模式）：

```csharp
// 新建档表单
public string NewPatientName { get; set; }
public string NewPatientGender { get; set; }          // "1" 或 "2"
public string NewPatientBirthDate { get; set; }        // yyyyMMdd 或 yyyy-MM-dd
public string NewPatientIdCard { get; set; }
public string NewPatientPhone { get; set; }
public string NewPatientGuardianName { get; set; }
public string NewPatientGuardianIdCard { get; set; }
public string NewPatientAllergy { get; set; }

// 住院卡信息
public string NewCardDiagnosis { get; set; }
public string NewCardRemark { get; set; }
public InpatientDeptChoice? NewCardDept { get; set; }   // 复用已有 Departments 列表
public InpatientWardChoice? NewCardWard { get; set; }

// 状态
public bool ShowNewPatientForm { get; set; }
public string PatientFormError { get; set; }
public string PatientFormNotice { get; set; }

// 命令
public AsyncRelayCommand RegisterAndCreateCardCommand { get; }
public AsyncRelayCommand ToggleNewPatientFormCommand { get; }
```

命令逻辑 `RegisterAndCreateCardAsync()`：
1. 客户端验证：姓名、证件、出生日期、联系电话、科室必填
2. 调用 `POST /api/v4/ipd/admission/register-with-patient`
3. 成功后刷新住院卡列表（`LoadCardsAsync()`），自动选中新建的卡
4. 展示"建档并开卡成功，请继续分配床位并收取押金"
5. 请求携带 `RequestKey = "IPD_CASHIER_ADC_" + Guid.NewGuid().ToString("N")`

文件：`src/Client/HIS.Client.UrsaShell/Views/InpatientCashierView.axaml`

在"办理入院登记" TabItem 内，当 `HasSelectedCard == false` 时显示建档表单：

```xml
<StackPanel IsVisible="{Binding !HasSelectedCard}" Spacing="14">
    <!-- 按钮栏 -->
    <Button Content="＋ 入院建档并代开住院卡" Command="{Binding ToggleNewPatientFormCommand}" />

    <!-- 建档+开卡表单 -->
    <Border IsVisible="{Binding ShowNewPatientForm}" Classes="InpCard">
        <StackPanel Spacing="12">
            <!-- 患者基本信息 -->
            <Grid ColumnDefinitions="*,*,*" RowDefinitions="Auto,Auto,Auto" RowSpacing="10" ColumnSpacing="12">
                <!-- 姓名 / 性别 / 出生日期 -->
                <!-- 证件号 / 电话 / 监护人 -->
                <!-- 过敏史（选填） -->
            </Grid>
            <!-- 住院卡信息 -->
            <Grid ColumnDefinitions="*,*,*" RowDefinitions="Auto,Auto" RowSpacing="10" ColumnSpacing="12">
                <!-- 建议科室 / 建议病区 / 初步诊断 -->
            </Grid>
            <Button Content="建档并开立住院卡" Command="{Binding RegisterAndCreateCardCommand}" />
            <TextBlock Text="{Binding PatientFormError}" IsVisible="{Binding PatientFormError}" />
        </StackPanel>
    </Border>
</StackPanel>
```

### 验收标准

1. 使用 `cashier_01 / 123` 登录，进入住院收费站
2. 无需先去门诊，可直接输入新患者信息
3. 点击"建档并开立住院卡"后，系统返回新住院卡
4. 左侧列表自动刷新并选中新卡
5. 继续分配床位、收取押金，点击"办理入院登记并收取押金"成功
6. 全流程审计日志包含 `PATIENT_REGISTER` 和 `IPD_ADMISSION_CARD_CREATE`
7. 新患者的重复证件号触发 409 并提示已有病历号
8. 原有流程（门诊建档→医生开卡→收费登记）不受影响

---

## P0-B：测试隔离修复

### 问题

以下测试在全套件运行时失败，单独运行通过：

- `M019_IntrusionDetection_ScanThreats_AlertsList_And_Resolve`
- `M021_KmsKeyManagement_QueryKeys_RotateKey_And_HsmStatus`

原因：共享测试数据库，前序测试修改了状态（安全告警已清空 / KMS 密钥已被轮换）。

### 整改内容

文件：`tests/HIS.IntegrationTests/ComplianceRemediationVerificationTests.cs`

**方案 A（推荐）**：每个测试类使用独立 SQLite 数据库实例

1. 检查 `ComplianceRemediationVerificationTests` 的构造函数和 fixture
2. 为每个测试方法生成独立数据库名称（如 `test_{Guid}.db`）或使用 `[Collection]` 隔离
3. 确保测试结束后清理临时数据库文件

**方案 B（最小修改）**：修改断言使其容忍已有状态

- `M019`：将 `Assert.NotEmpty(alertsData.Data)` 改为先扫描，若无告警则先用已知威胁签名触发扫描，再断言非空
- `M021`：将 `Assert.Equal(HttpStatusCode.OK, ...)` 改为接受 200 或 409（密钥已轮换），并在 409 时跳过后续断言

### 验收标准

1. `dotnet test HIS.slnx --no-build -v:minimal` 全量通过（52/52）
2. 单独运行和全量运行结果一致

---

## P0-C：医保适配器架构

### 问题

`MockMedicalInsuranceAdapter` 是唯一的医保适配器，无真实医保接口。

### 整改内容

文件：`src/Server/HIS.Modules.Integration/Services/IntegrationServices.cs`

1. 将 `IMedicalInsuranceAdapter` 接口扩展为完整医保业务：

```csharp
public interface IMedicalInsuranceAdapter
{
    // 现有
    Task<string> PreSettleAsync(string patientId, decimal totalAmount, CancellationToken ct = default);
    Task<string> SettleAsync(string patientId, decimal totalAmount, CancellationToken ct = default);

    // 新增
    Task<InsurancePatientMatch> MatchPatientAsync(string idCardNo, string name, CancellationToken ct = default);
    Task<InsuranceCatalogSync> SyncCatalogAsync(CancellationToken ct = default);
    Task<InsuranceSettleResult> RealTimeSettleAsync(InsuranceSettleRequest req, CancellationToken ct = default);
    Task<InsuranceRefundResult> RefundAsync(string insuranceSettleNo, decimal amount, CancellationToken ct = default);
    Task<InsuranceBalance> QueryBalanceAsync(string insuranceCardNo, CancellationToken ct = default);
}
```

2. 新增配置化适配器选择：

文件：`src/Server/HIS.Server.Host/Program.cs`

```csharp
string insuranceProvider = builder.Configuration["HIS_INSURANCE_PROVIDER"] ?? "Mock";
builder.Services.AddSingleton<IMedicalInsuranceAdapter>(insuranceProvider switch
{
    "Mock" => new MockMedicalInsuranceAdapter(),
    _ => throw new InvalidOperationException($"未知医保适配器: {insuranceProvider}")
});
```

3. `MockMedicalInsuranceAdapter` 保持默认实现，通过环境变量 `HIS_INSURANCE_PROVIDER=Mock` 切换
4. 预留 `MedicalInsuranceProviderBase` 抽象类，真实适配器只需继承并实现具体调用

### 验收标准

1. 默认不配置时系统正常运行（Mock）
2. `HIS_INSURANCE_PROVIDER=Mock` 显式指定时正常运行
3. `HIS_INSURANCE_PROVIDER=Unknown` 启动时报错
4. 接口具备目录同步、实时结算、退费、余额查询的方法签名

---

## P1-A：耗材/供应室 CSSD 模块

### 整改内容

#### 新增模块

```
src/Server/HIS.Modules.Consumables/
├── Controllers/
│   └── ConsumablesController.cs
├── Models/
│   └── ConsumablesEntities.cs
├── Services/
│   └── ConsumableSupplyService.cs
└── HIS.Modules.Consumables.csproj
```

#### 数据表（新增约 10 张）

```sql
-- 耗材主数据（与药品平行，不混用）
csm_consumables (id, code, name, spec, unit, category, is_sterile, is_high_value,
                 price, is_active, ...)

-- 耗材库存（按库房）
csm_stocks (id, consumable_id, warehouse_id, quantity, reserved_qty, ...)

-- 耗材批次
csm_batches (id, consumable_id, batch_no, expiry_date, manufacture_date, ...)

-- 出入库流水
csm_stock_movements (id, consumable_id, batch_id, from_warehouse_id, to_warehouse_id,
                     movement_type, quantity, unit_price, reference_id, ...)

-- 科室请领
csm_requisitions (id, ward_id, status, requested_by, approved_by, ...)

-- 请领明细
csm_requisition_lines (id, requisition_id, consumable_id, quantity, ...)

-- 灭菌包
csm_sterile_packages (id, package_code, contents_json, sterilization_batch_no, status, ...)

-- 灭菌批次
csm_sterilization_batches (id, batch_no, method, started_at, completed_at, result, ...)

-- 包流转记录
csm_package_movements (id, package_id, from_location, to_location, movement_type, ...)

-- 耗材追溯
csm_trace_codes (id, consumable_id, trace_code, patient_id, encounter_id, status, ...)
```

#### API 端点

```
GET    /api/v4/csm/consumables            # 查询耗材目录
POST   /api/v4/csm/consumables            # 新增耗材（管理员）
POST   /api/v4/csm/consumables/{id}/toggle-active
POST   /api/v4/csm/consumables/{id}/adjust-price
GET    /api/v4/csm/stock-summary           # 按库房汇总库存
GET    /api/v4/csm/batches                # 查询批次
POST   /api/v4/csm/batches/stock-in       # 采购入库
POST   /api/v4/csm/batches/{id}/freeze    # 冻结批次
POST   /api/v4/csm/requisitions           # 科室请领
POST   /api/v4/csm/requisitions/{id}/approve
POST   /api/v4/csm/requisitions/{id}/issue  # 药库/供应室发放
POST   /api/v4/csm/transfers              # 库房调拨
POST   /api/v4/csm/sterile-packages        # 灭菌包管理
POST   /api/v4/csm/sterile-packages/{id}/sterilize
POST   /api/v4/csm/sterile-packages/{id}/issue
POST   /api/v4/csm/sterile-packages/{id}/return
POST   /api/v4/csm/trace-codes/scan-use   # 患者使用登记
POST   /api/v4/csm/trace-codes/scan-recall
GET    /api/v4/csm/finance-report           # 财务对账
```

#### 权限

- 耗材目录维护、调价：Admin
- 入库、调拨、灭菌包：Admin, Warehouse
- 科室请领、使用登记：Admin, NurseIpd, NurseOpd, DoctorIpd, DoctorOpd
- 财务报表：Admin, Cashier

#### 客户端

新增站点：`src/Client/HIS.Client.UrsaShell/Stations/Consumables/`
- `ConsumablesWarehouseView.axaml`（中心库房 + 供应室）
- `ConsumablesWardView.axaml`（临床科室请领）

#### 参考实现

现有药品模块：
- 服务端：`src/Server/HIS.Modules.Inventory/Controllers/InventoryController.cs`
- 药库客户端：`src/Client/HIS.Client.UrsaShell/Stations/PharmacyWarehouse/PharmacyWarehouseView.axaml`

### 验收标准

1. 可新增耗材主数据并入库
2. 临床科室可提交请领单
3. 库房可审批并发放
4. 发放后库存扣减、流水记录
5. 灭菌包可创建、灭菌、发放、回收
6. 患者使用可追溯
7. 审计日志完整覆盖所有写操作
8. 高值耗材可追溯到患者级别

---

## P1-B：LIS/PACS 集成架构

### 整改内容

#### 新增集成模块

```
src/Server/HIS.Modules.Integration/
├── Interfaces/
│   ├── ILisAdapter.cs
│   └── IPacsAdapter.cs
├── Services/
│   ├── MockLisAdapter.cs
│   └── MockPacsAdapter.cs
```

#### LIS 接口

```csharp
public interface ILisAdapter
{
    Task<LisTestOrder> SendTestOrderAsync(LisOrderRequest req, CancellationToken ct = default);
    Task<LisResult> QueryResultAsync(string lisOrderId, CancellationToken ct = default);
    Task<List<LisResult>> BatchSyncResultsAsync(CancellationToken ct = default);
    Task<bool> CancelOrderAsync(string lisOrderId, CancellationToken ct = default);
}
```

#### PACS 接口

```csharp
public interface IPacsAdapter
{
    Task<PacsStudyOrder> SendStudyOrderAsync(PacsOrderRequest req, CancellationToken ct = default);
    Task<PacsReport> QueryReportAsync(string accessionNo, CancellationToken ct = default);
    Task<byte[]> GetImageAsync(string imageId, CancellationToken ct = default);
    Task<bool> CancelStudyAsync(string accessionNo, CancellationToken ct = default);
}
```

#### 对接点

在 `MedTechController` 中：
- `POST /api/v4/medtech/requests` 确认收费后，调用 `ILisAdapter.SendTestOrderAsync` 或 `IPacsAdapter.SendStudyOrderAsync`
- `POST /api/v4/medtech/requests/{reqId}/publish-report` 检查是否已有 LIS/PACS 回传结果
- 新增端点：
  ```
  POST /api/v4/medtech/lis/sync-results       # 手动批量同步 LIS 结果
  POST /api/v4/medtech/pacs/sync-reports      # 手动批量同步 PACS 报告
  GET  /api/v4/medtech/lis/pending            # 待同步列表
  ```

#### 配置化

```
HIS_LIS_PROVIDER=Mock
HIS_PACS_PROVIDER=Mock
HIS_LIS_ENDPOINT=
HIS_PACS_ENDPOINT=
```

### 验收标准

1. 医技申请确认收费后，Mock LIS/PACS 收到指令
2. Mock 模拟回传结果后，报告发布页可见结果
3. 手动同步可批量拉取
4. 全流程有审计
5. 切换 `HIS_LIS_PROVIDER` 配置即可替换真实实现

---

## P1-C：手术麻醉管理

### 整改内容

#### 新增数据表

```sql
mte_anesthesia_records (id, encounter_id, schedule_id, patient_id,
    asa_grade, anesthesia_type, anesthesia_start_ms, anesthesia_end_ms,
    anesthesiologist_id, preop_diagnosis, intraop_events_json,
    blood_loss_ml, urine_output_ml, fluid_input_ml, status, ...)

mte_anesthesia_medications (id, record_id, drug_id, dose, route, given_at_ms, ...)

mte_surgery_schedules (id, encounter_id, patient_id, surgery_code, surgery_name,
    surgeon_id, assistant_ids_json, anesthesiologist_id, circulating_nurse_id,
    or_room_id, scheduled_start_ms, estimated_duration_min, status, ...)

mte_recovery_records (id, anesthesia_record_id, recovery_score, discharge_criteria_json, ...)

mte_postop_followups (id, anesthesia_record_id, pain_score, nausea_vomiting, followup_at_ms, ...)
```

#### API 端点

```
POST   /api/v4/medtech/surgeries/schedule          # 手术排班
GET    /api/v4/medtech/surgeries/schedules          # 排班查询
PUT    /api/v4/medtech/surgeries/schedules/{id}     # 修改排班
POST   /api/v4/medtech/surgeries/{id}/start          # 开始手术
POST   /api/v4/medtech/surgeries/{id}/finish          # 完成手术（已有）
POST   /api/v4/medtech/anesthesia/records            # 新建麻醉记录
PUT    /api/v4/medtech/anesthesia/records/{id}       # 更新麻醉记录
POST   /api/v4/medtech/anesthesia/records/{id}/medications  # 用药记录
POST   /api/v4/medtech/anesthesia/records/{id}/recovery     # 术后恢复记录
POST   /api/v4/medtech/anesthesia/records/{id}/followup     # 术后随访
```

#### 客户端

在 `MedTechStationView` 中新增"手术麻醉" Tab：
- 手术排班列表 + 手术间状态看板
- 麻醉记录编辑（ASA 分级、麻醉方式、用药、出血量）
- 术后恢复评分
- 术后随访记录

#### 与现有 WHO 核查的衔接

- 手术排班 `start` 后，自动加载已有的 WHO 三方核查入口
- 麻醉记录完成是 `finish` 的前置条件
- 出院检查（`DischargeChecklistService`）引用手术麻醉完成状态

### 验收标准

1. 可为住院患者排手术并分配手术间、术者、麻醉师
2. 术前可填写 ASA 分级和麻醉计划
3. 术中可记录麻醉用药、出血量、尿量
4. WHO 三方核查与手术排班关联
5. 术后可填写恢复评分和随访
6. 麻醉未完成时无法结束手术

---

## P1-D：护理管理

### 整改内容

#### 新增数据表

```sql
nur_care_plans (id, encounter_id, patient_id, problem, goal, interventions_json,
                status, created_by, ...)

nur_care_plan_evaluations (id, plan_id, evaluation, evaluated_at_ms, ...)

nur_quality_indicators (id, encounter_id, indicator_code, score, assessor_id, ...)

nur_pressure_ulcer_assessments (id, encounter_id, braden_score, risk_level, interventions_json, ...)

nur_fall_assessments (id, encounter_id, morse_score, risk_level, interventions_json, ...)
```

#### API 端点

```
POST   /api/v4/nursing/care-plans                  # 新建护理计划
GET    /api/v4/nursing/care-plans/{encounterId}    # 按住院就诊查询
PUT    /api/v4/nursing/care-plans/{id}             # 修改
POST   /api/v4/nursing/care-plans/{id}/evaluate    # 评估
POST   /api/v4/nursing/assessments/braden           # 压疮评估
POST   /api/v4/nursing/assessments/morse            # 跌倒评估
GET    /api/v4/nursing/quality-report               # 质量指标汇总
```

#### 客户端

在 `IpdNurseView` 中新增：
- 护理计划 Tab（问题列表、护理目标、护理措施、评价记录）
- 压疮评估 Tab（Braden 评分、风险等级、预防措施）
- 跌倒评估 Tab（Morse 评分、风险等级、预防措施）
- 护理质量指标 Tab

#### 与现有功能的衔接

- 入院接诊后自动触发首次压疮/跌倒评估
- 评估结果为高危时，生成护理任务（复用 `NursingTaskModel`）
- 出院检查引用压疮/跌倒评估完成情况

### 验收标准

1. 护士站可为住院患者创建护理计划
2. 可记录 Braden/Morse 评估并自动判定风险等级
3. 高风险患者自动生成预防护理任务
4. 护理质量指标可按病区汇总查看
5. 出院前检查引用评估完成状态

---

## P1-E：感控管理

### 整改内容

#### 新增数据表

```sql
ipc_infection_reports (id, encounter_id, patient_id, infection_site, pathogen,
                      report_date, status, ...)

ipc_isolation_orders (id, encounter_id, isolation_type, start_ms, end_ms, reason, ...)

ipc_occupational_exposures (id, staff_id, exposure_type, source_patient_id,
                            exposed_at_ms, disposition, ...)

ipc_adverse_events (id, reporter_id, event_type, severity, description,
                    patient_id, encounter_id, status, ...)

ipc_hand_hygiene_audits (id, ward_id, observer_id, observed_count, compliant_count, audit_date, ...)
```

#### API 端点

```
POST   /api/v4/ipc/infection-reports            # 院感上报
GET    /api/v4/ipc/infection-reports             # 上报查询
POST   /api/v4/ipc/isolation-orders              # 隔离医嘱
POST   /api/v4/ipc/isolation-orders/{id}/end     # 解除隔离
POST   /api/v4/ipc/occupational-exposures         # 职业暴露上报
POST   /api/v4/ipc/adverse-events                # 不良事件上报
GET    /api/v4/ipc/adverse-events                 # 不良事件查询
POST   /api/v4/ipc/hand-hygiene-audits            # 手卫生督查
GET    /api/v4/ipc/summary                        # 感控统计汇总
```

#### 客户端

在 IpdNurseView 或独立"感控管理"站中新增：
- 院感上报表单
- 隔离医嘱管理
- 职业暴露登记
- 不良事件上报
- 手卫生督查录入

#### 与现有功能衔接

- 传染病报卡（已有）与感控上报互相关联但不合并
- 隔离医嘱生成护理任务
- 不良事件按等级通知相应管理员

### 验收标准

1. 可上报院感病例并追踪处置
2. 可开具和解除隔离医嘱
3. 职业暴露可登记并记录处置流程
4. 不良事件可上报、分级、跟踪闭环
5. 手卫生督查数据可按病区汇总

---

## P1-F：统计分析工作站

### 整改内容

#### 新增服务端统计 API

文件：`src/Server/HIS.Modules.Operations/Controllers/AnalyticsController.cs`（新建）

```
GET /api/v4/analytics/overview          # 全院总览（门诊量、住院量、收入、药占比）
GET /api/v4/analytics/outpatient        # 门诊分析（科室/医生/时段）
GET /api/v4/analytics/inpatient         # 住院分析（科室/病区/床位利用率）
GET /api/v4/analytics/pharmacy          # 药品分析（DDD/药占比/抗生素使用率）
GET /api/v4/analytics/finance           # 收入分析（科室/项目/支付方式）
GET /api/v4/analytics/quality           # 质量指标（诊断符合率/平均住院日/再入院率）
GET /api/v4/analytics/infection         # 感控指标
GET /api/v4/analytics/export            # 导出报表
```

统计查询直接基于现有表编写 SQL，无需新建数据表。

#### 客户端

新增站点：`src/Client/HIS.Client.UrsaShell/Stations/Analytics/AnalyticsView.axaml`

设计原则：
- 顶部：日期范围选择器 + 科室/病区筛选 + 刷新/导出
- KPI 卡片行：门诊人次、在院人数、今日收入、药占比、床位利用率
- Tab 切换：门诊分析 / 住院分析 / 药品分析 / 收入分析 / 质量指标
- 每个 Tab 内使用 DataGrid 展示明细
- 图表使用简单 Bar/Line（可用 LiveCharts 或手绘 Canvas）

#### 权限

Admin, 可扩展"运营分析"角色

### 验收标准

1. 管理员登录可见统计分析站
2. 选择日期范围后 KPI 正确计算
3. 科室/病区筛选生效
4. 可导出 CSV
5. 数据来源为真实数据库而非 Mock

---

## P2-A：患者服务

### 整改内容

#### 满意度调查

```
srv_satisfaction_surveys (id, encounter_id, patient_id, score, comment, submitted_at_ms, ...)

POST /api/v4/service/satisfaction    # 提交满意度
GET  /api/v4/service/satisfaction   # 管理查询
```

#### 投诉管理

```
srv_complaints (id, patient_id, complaint_type, description, status, handler_id, ...)

POST /api/v4/service/complaints            # 提交投诉
GET  /api/v4/service/complaints             # 查询
PUT  /api/v4/service/complaints/{id}        # 受理/处理
POST /api/v4/service/complaints/{id}/close  # 结案
```

#### 知情同意管理

```
srv_informed_consents (id, patient_id, encounter_id, consent_type, document_id,
                       signed_at_ms, signature_image, ...)

POST /api/v4/service/informed-consents   # 记录签署
GET  /api/v4/service/informed-consents   # 查询
```

#### 客户端

- 门诊/住院工作台增加"满意度评价"入口
- 管理端增加"患者服务"站点（满意度报表 + 投诉列表 + 知情同意管理）

### 验收标准

1. 患者可在就诊后提交满意度评分
2. 投诉可提交、受理、处理、结案
3. 知情同意签署记录可追溯
4. 管理员可查看统计汇总

---

## P2-B：管理支撑

### 整改内容

#### 成本核算

```
fin_cost_centers (id, dept_id, period, total_cost, ...)

POST /api/v4/finance/cost-centers/allocate    # 成本分摊
GET  /api/v4/finance/cost-centers/report      # 成本报表
```

#### 设备管理

```
eqp_equipments (id, code, name, location_id, purchase_date, price, warranty_end, status, ...)
eqp_maintenance_records (id, equipment_id, type, date, cost, vendor, result, ...)

POST /api/v4/equipment/equipments            # 新增设备
POST /api/v4/equipment/equipments/{id}/maintenance  # 维保记录
GET  /api/v4/equipment/report
```

#### 客户端

管理端新增"运营管理"站，包含成本核算、设备台账、维保记录子页面。

### 验收标准

1. 成本可按科室分摊并生成报表
2. 设备台账可维护，维保记录可追溯
3. 设备状态看板可展示正常/维修/停用

---

## P2-C：可维护性重构

### 问题

`WorkbenchModel.cs` 约 10,700 行，已拆出 4 个分部文件但主文件仍过大。

### 整改内容

按业务域继续拆分为独立 ViewModel 类，目标每个不超过 2,000 行：

```
WorkbenchModel.cs               → 保留 Shell 导航 + 全局状态（目标 < 3,000 行）
WorkbenchModel.OpdEmr.cs        → 已有，保持
WorkbenchModel.OpdWorkflow.cs   → 已有，保持
WorkbenchModel.ShellBridge.cs   → 已有，保持
WorkbenchModel.UiState.cs       → 已有，保持
WorkbenchModel.Inpatient.cs     → 新增：住院业务逻辑
WorkbenchModel.Pharmacy.cs      → 新增：药房业务逻辑
WorkbenchModel.Finance.cs       → 新增：财务业务逻辑
WorkbenchModel.MedTech.cs      → 新增：医技业务逻辑
```

同步更新 `MainWindow.axaml` 和所有 Station 的 `Binding` 路径。

#### 注意事项

- 这是纯重构，不改任何业务行为
- 每拆一个域就跑全量测试
- 命名空间保持 `HIS.Client.UrsaShell.ViewModels` 不变
- XAML 中的 `Binding` 路径必须同步验证

### 验收标准

1. `WorkbenchModel.cs` 主文件不超过 3,000 行
2. 所有 XAML 绑定不报错
3. 全量测试通过
4. 客户端功能与重构前完全一致

---

## 附录 A：统一技术规范

### 代码风格

- 服务端控制器：统一使用 `[RequireRole]` 或 `HasAnyRole` 权限检查
- 审计日志：所有写操作必须调用 `Audit()`
- 幂等：涉及扣费/发药/入库的操作必须接入 `IIdempotencyContract`
- API 路由前缀：`/api/v4/{module}`
- 响应格式：`ApiResponse<T>`

### 数据库

- 所有新表需在 `DbSeedData.cs` 中添加 `CREATE TABLE IF NOT EXISTS`
- 主键用字符串 ID（如 `CSM-xxx`、`IPC-xxx`）
- 需包含 `schema_version`、`created_at_ms`、`updated_at_ms`、`version_no` 字段
- 种子数据仅用于演示，上线前需清理

### 客户端

- 站点文件放置于 `Stations/{Domain}/`
- ViewModel 继承 `ClientModelBase`
- HTTP 使用 `AuthenticatedHttpClientFactory.Create()`
- 错误处理使用 `SetValidation` / `SetFailure` / `SetException`
- 每个站点在 `MainShellWindow` 中注册导航入口

### 测试

- 每个新模块必须有对应集成测试
- 每个测试使用独立数据库或可重置状态
- 命名规范：`M{序号}_{模块}_{功能描述}`

### 安全

- 新增写端点必须有权限检查和审计
- 涉及资金/药品/耗材的操作必须有幂等保护
- 患者敏感数据加解密使用 `CryptoHelper`

---

## 附录 B：整体验收检查清单

- [ ] `dotnet build HIS.slnx --no-restore -v:minimal` → 0 错误 0 警告
- [ ] `dotnet test HIS.slnx --no-build -v:minimal` → 全部通过
- [ ] 入院建档一站式流程可用
- [ ] 测试隔离修复后全套件绿色
- [ ] 医保适配器可配置切换
- [ ] 耗材/CSSD 模块可用
- [ ] LIS/PACS 集成架构就绪
- [ ] 手术麻醉管理可用
- [ ] 护理管理可用
- [ ] 感控管理可用
- [ ] 统计分析工作站可用
- [ ] 患者服务可用
- [ ] 管理支撑可用
- [ ] WorkbenchModel 拆分完成
- [ ] 全部审计日志链校验通过
- [ ] 全部权限拒绝场景有测试覆盖
- [ ] 演示密码已从 `123` 更换为安全密码

---

## 附录 C：建议执行顺序

```
第 1 轮：P0-A → P0-B → P0-C
第 2 轮：P1-A → P1-F
第 3 轮：P1-B → P1-C
第 4 轮：P1-D → P1-E
第 5 轮：P2-A → P2-B → P2-C
```

每轮完成后：
1. 全量构建 + 全量测试
2. 手动验证该轮验收标准
3. 提交一次 git commit
4. 更新本文档中对应项的勾选状态
