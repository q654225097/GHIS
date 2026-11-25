using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Common;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Services;

namespace HIS.Client.UrsaShell.ViewModels;

public sealed class InpatientDailyCostCategoryDto
{
    public string CategoryCode { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int ItemCount { get; set; }
    public string AmountText => $"¥{Amount:0.00}";
}

public sealed class InpatientDailyCostItemDto
{
    public string Id { get; set; } = string.Empty;
    public int FeeDateInt { get; set; }
    public string ItemCategory { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string UnitName { get; set; } = "项";
    public decimal UnitPrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal TotalAmount { get; set; }
    public string ChargeDeptId { get; set; } = string.Empty;
    public string ExecDeptId { get; set; } = string.Empty;
}

public sealed class InpatientDailyCostSheetDto
{
    public string EncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string BedCode { get; set; } = string.Empty;
    public string DeptName { get; set; } = string.Empty;
    public int FeeDateInt { get; set; }
    public decimal TodayTotalAmount { get; set; }
    public decimal CumulativeTotalAmount { get; set; }
    public decimal PrepaymentBalance { get; set; }
    public decimal AvailableBalance { get; set; }
    public string ArrearsStatus { get; set; } = "NORMAL";
    public string ArrearsStatusText { get; set; } = string.Empty;
    public List<InpatientDailyCostCategoryDto> Categories { get; set; } = new();
    public List<InpatientDailyCostItemDto> TodayItems { get; set; } = new();
}

public sealed class ServerMedicalOrderDto
{
    public string Id { get; set; } = string.Empty;
    public string OrderNo { get; set; } = string.Empty;
    public string EncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string OrderIpdDeptId { get; set; } = string.Empty;
    public string PatientWardId { get; set; } = string.Empty;
    public string ExecTargetDeptId { get; set; } = string.Empty;
    public int OrderTermType { get; set; } = 1;
    public int GroupNo { get; set; } = 1;
    public string OrderCategory { get; set; } = "DRUG";
    public string OrderSubType { get; set; } = "NORMAL";
    public string? ParentOrderId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public decimal Dosage { get; set; }
    public string FrequencyCode { get; set; } = "QD";
    public string RouteCode { get; set; } = "IV_DRIP";
    public int Status { get; set; }
    public string OrderDoctorId { get; set; } = string.Empty;
    public string? VerifyNurseId { get; set; }
    public long PlanStartAtMs { get; set; }
    public long? ActualStopAtMs { get; set; }
    public string? StopDoctorId { get; set; }
    public string? VoidReason { get; set; }
    public string ApprovalStatus { get; set; } = "APPROVED";
    public short IsUrgent { get; set; }
}

public sealed class ServerExecPlanDto
{
    public string Id { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string EncounterId { get; set; } = string.Empty;
    public string WardId { get; set; } = string.Empty;
    public long PlannedTimeMs { get; set; }
    public int ExecStatus { get; set; }
    public string? ExecNurseId { get; set; }
    public string? DualCheckNurseId { get; set; }
    public long? ExecutedAtMs { get; set; }
    public string? PdaScanToken { get; set; }
}

public partial class WorkbenchModel
{
    private InpatientDailyCostSheetDto? _currentDailyCostSheet;
    private bool _isLoadingInpatientSync;

    public InpatientDailyCostSheetDto? CurrentDailyCostSheet
    {
        get => _currentDailyCostSheet;
        private set => SetField(ref _currentDailyCostSheet, value, nameof(CurrentDailyCostSheet));
    }

    public async Task RefreshInpatientsFromServerAsync()
    {
        if (_isLoadingInpatientSync || !SessionContext.Current.IsLoggedIn) return;
        _isLoadingInpatientSync = true;
        try
        {
            using HttpClient http = CreateConfigHttpClient();
            using var resp = await http.GetAsync("/api/v4/ipd/encounters/active");
            if (!resp.IsSuccessStatusCode) return;

            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<List<JsonElement>>>(JsonOptions);
            if (env?.Code is not (200 or 0) || env.Data == null || env.Data.Count == 0) return;

            string? prevEncId = SelectedInpatient?.EncounterId ?? SelectedInpatient?.AdmissionNo;
            var serverRecords = new List<InpatientRecordModel>();

            foreach (var el in env.Data)
            {
                string encId = JsonStr(el, "encounterId", "id");
                string admNo = JsonStr(el, "admissionNo", "encounterNo");
                if (string.IsNullOrWhiteSpace(admNo)) admNo = encId;
                string patId = JsonStr(el, "patientId", "medicalRecordNo");
                string mrn = JsonStr(el, "medicalRecordNo");
                if (string.IsNullOrWhiteSpace(mrn)) mrn = patId;

                long admittedMs = JsonLong(el, "admittedAtMs", "createdAtMs");
                var admittedLocal = admittedMs > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(admittedMs).ToOffset(TimeSpan.FromHours(8))
                    : DateTimeOffset.Now;
                int stayDays = Math.Max(1, (int)(DateTimeOffset.Now - admittedLocal).TotalDays + 1);
                decimal prepay = JsonDec(el, "prepaymentBalance");

                var rec = new InpatientRecordModel
                {
                    EncounterId = encId,
                    AdmissionNo = admNo,
                    PatientId = patId,
                    MedicalRecordNo = mrn,
                    PatientName = JsonStr(el, "patientName", "fullName"),
                    Gender = JsonStr(el, "genderName", "genderCode", "gender") is "1" or "M" ? "男" : JsonStr(el, "genderName", "genderCode", "gender") is "2" or "F" ? "女" : JsonStr(el, "genderName", "genderCode", "gender"),
                    Age = JsonInt(el, "age", "ageYears"),
                    AdmitDeptId = JsonStr(el, "admitDeptId", "deptId"),
                    AdmittingDept = JsonStr(el, "admitDeptName", "deptName", "admitDeptId"),
                    AdmitWardId = JsonStr(el, "admitWardId", "wardId"),
                    NursingWard = JsonStr(el, "admitWardName", "wardName", "admitWardId"),
                    BedCode = JsonStr(el, "bedCode"),
                    AttendingDoctorId = JsonStr(el, "attendingDoctorId"),
                    AttendingDoctor = JsonStr(el, "attendingDoctorName", "attendingDoctorId"),
                    PrimaryNurseId = JsonStr(el, "primaryNurseId"),
                    Diagnosis = JsonStr(el, "primaryDiagName", "primaryDiagnosisName", "diagnosis"),
                    NursingLevel = MapNursingLevelLabel(JsonStr(el, "nursingLevelCode", "nursingLevel")),
                    IsDischargeReady = JsonInt(el, "triageStatus") == 3,
                    AllergyTags = JsonStr(el, "allergyTags", "allergySummary"),
                    HasAllergyAlert = !string.IsNullOrWhiteSpace(JsonStr(el, "allergyTags", "allergySummary")),
                    DepositTotal = prepay,
                    AvailableBalance = prepay,
                    AdmissionDate = admittedLocal.ToString("yyyy-MM-dd"),
                    StayDays = stayDays,
                    AdmittedAtMs = admittedMs,
                    IsServerBacked = true
                };
                if (string.IsNullOrWhiteSpace(rec.PatientName)) rec.PatientName = rec.PatientId;
                if (string.IsNullOrWhiteSpace(rec.Diagnosis)) rec.Diagnosis = "住院待查";
                serverRecords.Add(rec);
            }

            if (serverRecords.Count > 0)
            {
                AllInpatientRecords.Clear();
                foreach (var rec in serverRecords)
                {
                    AllInpatientRecords.Add(rec);
                }
                RebuildScopedInpatientData();

                var toSelect = (!string.IsNullOrWhiteSpace(prevEncId)
                    ? InpatientRecords.FirstOrDefault(x => x.EncounterId == prevEncId || x.AdmissionNo == prevEncId)
                    : null) ?? InpatientRecords.FirstOrDefault();

                if (toSelect != null)
                {
                    SelectedInpatient = toSelect;
                    await LoadSelectedEncounterDetailsAsync(toSelect);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
        {
            ClientDiagnosticsLog.Error("ipd.sync", "加载在院患者列表失败", ex);
        }
        finally
        {
            _isLoadingInpatientSync = false;
        }
    }

    public async Task LoadSelectedEncounterDetailsAsync(InpatientRecordModel? target)
    {
        if (target == null || !SessionContext.Current.IsLoggedIn) return;
        string encId = !string.IsNullOrWhiteSpace(target.EncounterId) ? target.EncounterId : target.AdmissionNo;
        if (string.IsNullOrWhiteSpace(encId)) return;

        try
        {
            using HttpClient http = CreateConfigHttpClient();

            // 1. Orders
            using (var ordRes = await http.GetAsync($"/api/v4/orders/by-encounter/{Uri.EscapeDataString(encId)}"))
            {
                if (ordRes.IsSuccessStatusCode)
                {
                    var ordEnv = await ordRes.Content.ReadFromJsonAsync<ApiResponse<List<ServerMedicalOrderDto>>>(JsonOptions);
                    if (ordEnv?.Code is 200 or 0 && ordEnv.Data != null)
                    {
                        target.Orders.Clear();
                        var groupCounts = ordEnv.Data.Where(o => o.GroupNo > 0)
                            .GroupBy(o => o.GroupNo)
                            .ToDictionary(g => g.Key, g => g.Count());
                        var groupSeen = new Dictionary<int, int>();

                        int seq = 1;
                        foreach (var dto in ordEnv.Data)
                        {
                            string groupIcon = "-";
                            if (dto.GroupNo > 0 && groupCounts.TryGetValue(dto.GroupNo, out int gTotal) && gTotal > 1)
                            {
                                groupSeen.TryGetValue(dto.GroupNo, out int idx);
                                groupIcon = idx == 0 ? "┓" : (idx == gTotal - 1 ? "┛" : "┃");
                                groupSeen[dto.GroupNo] = idx + 1;
                            }

                            var startDt = dto.PlanStartAtMs > 0
                                ? DateTimeOffset.FromUnixTimeMilliseconds(dto.PlanStartAtMs).ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm")
                                : DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                            var stopDt = dto.ActualStopAtMs is > 0
                                ? DateTimeOffset.FromUnixTimeMilliseconds(dto.ActualStopAtMs.Value).ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm:ss")
                                : "--";

                            var matchedCatalog = AvailableDrugsCatalog.FirstOrDefault(d =>
                                string.Equals(d.DrugCode, dto.ItemCode, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(d.DrugName, dto.ItemName, StringComparison.OrdinalIgnoreCase));

                            var item = new InpatientOrderModel
                            {
                                Id = dto.Id,
                                OrderNo = dto.OrderNo,
                                EncounterId = dto.EncounterId,
                                ItemCode = dto.ItemCode,
                                OrderCategoryCode = dto.OrderCategory,
                                OrderSubType = string.IsNullOrWhiteSpace(dto.OrderSubType) ? "NORMAL" : dto.OrderSubType,
                                ParentOrderId = dto.ParentOrderId,
                                GroupNoInt = dto.GroupNo,
                                ExecTargetDeptId = dto.ExecTargetDeptId,
                                IsServerBacked = true,
                                SeqNo = seq++,
                                OrderType = dto.OrderTermType == 1 ? "长期医嘱" : "临时医嘱",
                                ItemCategory = MapCategoryToLabel(dto.OrderCategory),
                                IsUrgent = dto.IsUrgent == 1,
                                StartTime = startDt,
                                StartExecTime = startDt,
                                Content = dto.OrderSubType == "DISCHARGE_MED" ? $"[出院带药] {dto.ItemName}" : dto.ItemName,
                                Spec = string.IsNullOrWhiteSpace(matchedCatalog?.Spec) ? "标准规格" : matchedCatalog.Spec,
                                SingleDose = dto.Dosage.ToString("0.##"),
                                Dose = dto.Dosage.ToString("0.##"),
                                DoseUnit = "剂量",
                                UsageAmount = "1",
                                UsageUnit = "次",
                                Route = MapRouteToLabel(dto.RouteCode),
                                Frequency = dto.FrequencyCode,
                                DoctorName = dto.OrderDoctorId,
                                ExecDept = string.IsNullOrWhiteSpace(dto.ExecTargetDeptId) ? target.NursingWard : dto.ExecTargetDeptId,
                                DeductDept = dto.OrderCategory == "DRUG" ? "住院药房" : target.NursingWard,
                                GroupComboNo = dto.GroupNo > 0 ? dto.GroupNo.ToString() : string.Empty,
                                GroupNo = dto.GroupNo > 0 ? dto.GroupNo.ToString() : string.Empty,
                                GroupIcon = groupIcon,
                                Status = MapOrderStatusToLabel(dto.Status, dto.OrderTermType == 1),
                                NurseSign = string.IsNullOrWhiteSpace(dto.VerifyNurseId) ? "--" : $"{dto.VerifyNurseId} (已校对)",
                                StopDoctor = dto.StopDoctorId ?? "--",
                                StopReason = dto.VoidReason ?? "--",
                                StopTime = stopDt,
                                ApprovalStatus = dto.ApprovalStatus,
                                OrderDept = target.AdmittingDept
                            };
                            target.Orders.Add(item);
                        }
                        target.ApplyOrderFilter();
                    }
                }
            }

            // 2. Execution plans
            using (var planRes = await http.GetAsync($"/api/v4/orders/plans/by-encounter/{Uri.EscapeDataString(encId)}"))
            {
                if (planRes.IsSuccessStatusCode)
                {
                    var planEnv = await planRes.Content.ReadFromJsonAsync<ApiResponse<List<ServerExecPlanDto>>>(JsonOptions);
                    if (planEnv?.Code is 200 or 0 && planEnv.Data != null)
                    {
                        var orderById = target.Orders.Where(o => !string.IsNullOrWhiteSpace(o.Id))
                            .ToDictionary(o => o.Id, o => o);
                        target.NursingTasks.Clear();
                        int pSeq = 1;
                        foreach (var p in planEnv.Data)
                        {
                            orderById.TryGetValue(p.OrderId, out var srcOrder);
                            var planLocal = p.PlannedTimeMs > 0
                                ? DateTimeOffset.FromUnixTimeMilliseconds(p.PlannedTimeMs).ToOffset(TimeSpan.FromHours(8))
                                : DateTimeOffset.Now;
                            string execTime = p.ExecutedAtMs is > 0
                                ? DateTimeOffset.FromUnixTimeMilliseconds(p.ExecutedAtMs.Value).ToOffset(TimeSpan.FromHours(8)).ToString("HH:mm")
                                : "--";
                            string statusLabel = p.ExecStatus switch
                            {
                                1 => !string.IsNullOrWhiteSpace(p.DualCheckNurseId) ? "已核对" : "已执行",
                                2 => "已退回",
                                _ => "待执行"
                            };
                            target.NursingTasks.Add(new NursingTaskModel
                            {
                                PlanId = p.Id,
                                OrderId = p.OrderId,
                                EncounterId = p.EncounterId,
                                PdaScanToken = p.PdaScanToken ?? p.Id,
                                DualCheckNurseId = p.DualCheckNurseId ?? string.Empty,
                                IsServerBacked = true,
                                TaskNo = $"PLAN-{pSeq++:D3}",
                                ScheduledDate = planLocal.ToString("yyyy-MM-dd"),
                                TimePoint = planLocal.ToString("HH:mm"),
                                ScheduledTime = planLocal.ToString("HH:mm"),
                                Category = srcOrder?.ItemCategory == "检验" ? "检验" : (srcOrder?.OrderCategoryCode == "DRUG" ? "给药" : "护理"),
                                Content = srcOrder != null ? $"{srcOrder.Content} [{srcOrder.Dose}] ({srcOrder.Route})" : $"医嘱执行计划 {p.OrderId}",
                                SourceOrder = srcOrder?.OrderNo ?? p.OrderId,
                                Priority = srcOrder?.IsUrgent == true ? "高" : "常规",
                                FrequencySnapshot = srcOrder?.Frequency ?? "QD",
                                NurseName = p.ExecNurseId ?? string.Empty,
                                ExecutedAt = execTime,
                                Status = statusLabel,
                                IsDecomposedTask = true
                            });
                        }
                    }
                }
            }

            // 3. Unified Daily Cost Sheet (Prepayment - Cumulative Fees)
            await LoadDailyCostSheetAsync(target);
            NotifyInpatientChanged(target);
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
        {
            ClientDiagnosticsLog.Error("ipd.sync", $"同步患者 [{encId}] 医嘱与执行计划失败", ex);
        }
    }

    public async Task<InpatientDailyCostSheetDto?> LoadDailyCostSheetAsync(InpatientRecordModel? target = null, int? feeDateInt = null)
    {
        var rec = target ?? SelectedInpatient;
        if (rec == null || !SessionContext.Current.IsLoggedIn) return null;
        string encId = !string.IsNullOrWhiteSpace(rec.EncounterId) ? rec.EncounterId : rec.AdmissionNo;
        if (string.IsNullOrWhiteSpace(encId)) return null;

        try
        {
            using HttpClient http = CreateConfigHttpClient();
            string url = feeDateInt.HasValue
                ? $"/api/v4/ipd/encounters/{Uri.EscapeDataString(encId)}/daily-cost-sheet?feeDateInt={feeDateInt.Value}"
                : $"/api/v4/ipd/encounters/{Uri.EscapeDataString(encId)}/daily-cost-sheet";
            using var resp = await http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;

            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<InpatientDailyCostSheetDto>>(JsonOptions);
            if (env?.Code is 200 or 0 && env.Data != null)
            {
                CurrentDailyCostSheet = env.Data;
                rec.TotalCost = env.Data.CumulativeTotalAmount;
                rec.DepositTotal = env.Data.PrepaymentBalance;
                rec.AvailableBalance = env.Data.AvailableBalance;
                rec.NotifyFinanceChanged();
                return env.Data;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
        {
            ClientDiagnosticsLog.Error("ipd.cost", $"读取住院一日清单失败 [{encId}]", ex);
        }
        return null;
    }

    public async Task<(bool Succeeded, string Message)> PostOrderToServerVerifiedAsync(
        InpatientRecordModel patient,
        InpatientOrderModel order,
        decimal dosage,
        bool allowArrearsOverride = false)
    {
        if (!SessionContext.Current.IsLoggedIn)
            return (true, "离线演练模式：已本地记录医嘱");

        string encId = !string.IsNullOrWhiteSpace(patient.EncounterId) ? patient.EncounterId : patient.AdmissionNo;
        string patId = !string.IsNullOrWhiteSpace(patient.PatientId) ? patient.PatientId : patient.MedicalRecordNo;
        string deptId = !string.IsNullOrWhiteSpace(patient.AdmitDeptId) ? patient.AdmitDeptId : "DEPT-IPD-01";
        string wardId = !string.IsNullOrWhiteSpace(patient.AdmitWardId) ? patient.AdmitWardId : "DEPT-WARD-02";

        string catCode = NormalizeOrderCategoryCode(order.ItemCategory, order.Content);
        var drugMatch = AvailableDrugsCatalog.FirstOrDefault(d =>
            string.Equals(d.DrugName, order.Content, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(d.DrugCode, order.ItemCode, StringComparison.OrdinalIgnoreCase) ||
            order.Content.Contains(d.DrugName, StringComparison.OrdinalIgnoreCase));

        string itemCode = !string.IsNullOrWhiteSpace(order.ItemCode)
            ? order.ItemCode
            : (drugMatch != null ? drugMatch.DrugCode : "ITEM-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant());
        if (catCode == "DRUG" && drugMatch != null)
        {
            itemCode = drugMatch.DrugCode;
        }
        string execDeptId = catCode == "DRUG" ? "DEPT-PHM-02" : wardId;

        var req = new
        {
            EncounterId = encId,
            PatientId = patId,
            OrderIpdDeptId = deptId,
            PatientWardId = wardId,
            ExecTargetDeptId = execDeptId,
            OrderTermType = order.OrderType.Contains("长") ? 1 : 2,
            GroupNo = order.GroupNoInt > 0 ? order.GroupNoInt : Math.Max(1, patient.Orders.Count),
            OrderCategory = catCode,
            ItemCode = itemCode,
            ItemName = order.Content,
            Dosage = dosage > 0m ? dosage : 1m,
            FrequencyCode = NormalizeFrequencyCode(order.Frequency),
            RouteCode = NormalizeRouteCode(order.Route),
            OrderDoctorId = CurrentOperatorId,
            OrderSubType = string.IsNullOrWhiteSpace(order.OrderSubType) ? "NORMAL" : order.OrderSubType,
            ParentOrderId = order.ParentOrderId,
            IsUrgent = order.IsUrgent,
            AllowArrearsOverride = allowArrearsOverride
        };

        try
        {
            using HttpClient http = CreateConfigHttpClient();
            using var resp = await http.PostAsJsonAsync("/api/v4/orders", req, JsonOptions);
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<ServerMedicalOrderDto>>(JsonOptions);
            if (!resp.IsSuccessStatusCode || env?.Code is not (200 or 0) || env.Data == null)
            {
                string err = env?.Message ?? $"医嘱提交被服务端拦截 ({(int)resp.StatusCode})";
                SystemNotice = "❌ " + err;
                return (false, err);
            }

            order.Id = env.Data.Id;
            order.OrderNo = env.Data.OrderNo;
            order.EncounterId = env.Data.EncounterId;
            order.ItemCode = env.Data.ItemCode;
            order.OrderCategoryCode = env.Data.OrderCategory;
            order.OrderSubType = env.Data.OrderSubType;
            order.GroupNoInt = env.Data.GroupNo;
            order.ExecTargetDeptId = env.Data.ExecTargetDeptId;
            order.IsServerBacked = true;

            await LoadDailyCostSheetAsync(patient);
            SystemNotice = $"✓ {env.Message}";
            return (true, env.Message ?? "医嘱下达成功");
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
        {
            string err = "医嘱同步服务异常：" + ex.Message;
            SystemNotice = "❌ " + err;
            return (false, err);
        }
    }

    public async Task<(bool Succeeded, string Message)> VerifyOrderOnServerAsync(InpatientRecordModel patient, InpatientOrderModel order)
    {
        if (!SessionContext.Current.IsLoggedIn || string.IsNullOrWhiteSpace(order.Id))
            return (true, "已本地完成校对");

        string wardId = !string.IsNullOrWhiteSpace(patient.AdmitWardId) ? patient.AdmitWardId : "DEPT-WARD-02";
        try
        {
            using HttpClient http = CreateConfigHttpClient();
            using var resp = await http.PostAsJsonAsync($"/api/v4/orders/{Uri.EscapeDataString(order.Id)}/verify", new
            {
                VerifyNurseId = CurrentOperatorId,
                WardId = wardId
            }, JsonOptions);
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<ServerMedicalOrderDto>>(JsonOptions);
            if (!resp.IsSuccessStatusCode || env?.Code is not (200 or 0))
            {
                string err = env?.Message ?? $"医嘱校对失败 ({(int)resp.StatusCode})";
                SystemNotice = "❌ " + err;
                return (false, err);
            }
            SystemNotice = "✓ " + (env.Message ?? "医嘱校对成功");
            await LoadSelectedEncounterDetailsAsync(patient);
            return (true, env.Message ?? "医嘱校对成功");
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
        {
            return (false, "医嘱校对同步失败：" + ex.Message);
        }
    }

    public async Task<(bool Succeeded, string Message)> StopOrderOnServerAsync(InpatientRecordModel patient, InpatientOrderModel order)
    {
        if (!SessionContext.Current.IsLoggedIn || string.IsNullOrWhiteSpace(order.Id))
            return (true, "已停止本地医嘱");

        try
        {
            using HttpClient http = CreateConfigHttpClient();
            string url = $"/api/v4/orders/{Uri.EscapeDataString(order.Id)}/stop?doctorId={Uri.EscapeDataString(CurrentOperatorId)}";
            using var resp = await http.PostAsync(url, null);
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<ServerMedicalOrderDto>>(JsonOptions);
            if (!resp.IsSuccessStatusCode || env?.Code is not (200 or 0))
            {
                string err = env?.Message ?? $"停止医嘱失败 ({(int)resp.StatusCode})";
                SystemNotice = "❌ " + err;
                return (false, err);
            }
            await LoadSelectedEncounterDetailsAsync(patient);
            return (true, env.Message ?? "医嘱已停止");
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
        {
            return (false, "停止医嘱同步失败：" + ex.Message);
        }
    }

    public async Task<(bool Succeeded, string Message)> VoidOrderOnServerAsync(InpatientRecordModel patient, InpatientOrderModel order, string reason = "临床医师作废医嘱")
    {
        if (!SessionContext.Current.IsLoggedIn || string.IsNullOrWhiteSpace(order.Id))
            return (true, "已作废本地医嘱");

        try
        {
            using HttpClient http = CreateConfigHttpClient();
            string url = $"/api/v4/orders/{Uri.EscapeDataString(order.Id)}/discontinue?doctorId={Uri.EscapeDataString(CurrentOperatorId)}&reason={Uri.EscapeDataString(reason)}";
            using var resp = await http.PostAsync(url, null);
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<ServerMedicalOrderDto>>(JsonOptions);
            if (!resp.IsSuccessStatusCode || env?.Code is not (200 or 0))
            {
                string err = env?.Message ?? $"作废医嘱失败 ({(int)resp.StatusCode})";
                SystemNotice = "❌ " + err;
                return (false, err);
            }
            await LoadSelectedEncounterDetailsAsync(patient);
            return (true, env.Message ?? "医嘱已作废");
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
        {
            return (false, "作废医嘱同步失败：" + ex.Message);
        }
    }

    public async Task<(bool Succeeded, string Message)> PersistOrderGroupingAsync(InpatientRecordModel patient, List<InpatientOrderModel> selected, bool isUngroup)
    {
        var serverIds = selected.Where(o => !string.IsNullOrWhiteSpace(o.Id)).Select(o => o.Id).ToList();
        if (!SessionContext.Current.IsLoggedIn || serverIds.Count == 0)
            return (true, isUngroup ? "已解除本地成组" : "已本地成组");

        try
        {
            using HttpClient http = CreateConfigHttpClient();
            string url = isUngroup ? "/api/v4/orders/ungroup" : "/api/v4/orders/group";
            var payload = new
            {
                OrderIds = serverIds,
                GroupNo = isUngroup ? 0 : (int?)null,
                ParentOrderId = isUngroup ? null : serverIds[0]
            };
            using var resp = await http.PostAsJsonAsync(url, payload, JsonOptions);
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>(JsonOptions);
            if (!resp.IsSuccessStatusCode || env?.Code is not (200 or 0))
                return (false, env?.Message ?? "成组保存失败");

            await LoadSelectedEncounterDetailsAsync(patient);
            return (true, env.Message ?? (isUngroup ? "已解除成组" : "医嘱成组已保存"));
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
        {
            return (false, "成组持久化失败：" + ex.Message);
        }
    }

    public async Task<(bool Succeeded, string Message)> ExecutePlanOnServerAsync(
        InpatientRecordModel patient,
        NursingTaskModel task,
        string wristbandCode,
        string itemBarcode,
        string dualNurseId = "USER-NURSE-W1",
        string highRiskAck = "PDA床旁双码核对通过")
    {
        if (!SessionContext.Current.IsLoggedIn || string.IsNullOrWhiteSpace(task.PlanId))
            return (true, "已本地执行");

        try
        {
            using HttpClient http = CreateConfigHttpClient();
            var payload = new
            {
                PlanId = task.PlanId,
                ExecNurseId = CurrentOperatorId,
                DualCheckNurseId = string.Equals(dualNurseId, CurrentOperatorId, StringComparison.OrdinalIgnoreCase) ? "USER-NURSE-W1" : dualNurseId,
                PdaScanToken = string.IsNullOrWhiteSpace(task.PdaScanToken) ? task.PlanId : task.PdaScanToken,
                PatientWristbandCode = wristbandCode,
                ItemBarcode = itemBarcode,
                HighRiskAckReason = highRiskAck
            };
            using var resp = await http.PostAsJsonAsync("/api/v4/orders/plans/pda-execute", payload, JsonOptions);
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>(JsonOptions);
            if (!resp.IsSuccessStatusCode || env?.Code is not (200 or 0))
            {
                string err = env?.Message ?? $"PDA执行被服务端拦截 ({(int)resp.StatusCode})";
                SystemNotice = "❌ " + err;
                return (false, err);
            }
            await LoadSelectedEncounterDetailsAsync(patient);
            SystemNotice = "✓ " + (env.Message ?? "PDA执行成功");
            return (true, env.Message ?? "PDA执行成功");
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
        {
            return (false, "PDA执行同步失败：" + ex.Message);
        }
    }

    public async Task<(bool Succeeded, string Message)> TransferAndReorganizeAsync(
        InpatientRecordModel patient,
        string transferType,
        string toDeptId,
        string toWardId,
        string toBedCode,
        bool reorganizeOrders,
        string sbarS,
        string sbarB,
        string sbarA,
        string sbarR)
    {
        string encId = !string.IsNullOrWhiteSpace(patient.EncounterId) ? patient.EncounterId : patient.AdmissionNo;
        try
        {
            using HttpClient http = CreateConfigHttpClient();
            var payload = new
            {
                TransferType = transferType,
                ToDeptId = toDeptId,
                ToWardId = toWardId,
                ToBedCode = toBedCode,
                OperatorId = CurrentOperatorId,
                ReorganizeOrders = reorganizeOrders,
                SbarSituation = sbarS,
                SbarBackground = sbarB,
                SbarAssessment = sbarA,
                SbarRecommendation = sbarR
            };
            using var resp = await http.PostAsJsonAsync($"/api/v4/ipd/encounters/{Uri.EscapeDataString(encId)}/transfer", payload, JsonOptions);
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>(JsonOptions);
            if (!resp.IsSuccessStatusCode || env?.Code is not (200 or 0))
            {
                return (false, env?.Message ?? $"转科/换床失败 ({(int)resp.StatusCode})");
            }
            SystemNotice = "✓ " + (env.Message ?? "转科及SBAR交接完成");
            await RefreshInpatientsFromServerAsync();
            return (true, env.Message ?? "转科及SBAR交接完成");
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
        {
            return (false, "转科/换床同步失败：" + ex.Message);
        }
    }

    private static string NormalizeOrderCategoryCode(string? uiCategory, string? content)
    {
        string c = (uiCategory ?? string.Empty).Trim();
        string txt = (content ?? string.Empty).Trim();
        if (c is "DRUG" or "LAB" or "EXAM" or "TREATMENT" or "INSTRUCTION") return c;
        if (c.Contains("药") || txt.Contains("片") || txt.Contains("注射液") || txt.Contains("胶囊")) return "DRUG";
        if (c.Contains("检验") || txt.Contains("血常规") || txt.Contains("培养") || txt.Contains("生化")) return "LAB";
        if (c.Contains("检查") || txt.Contains("CT") || txt.Contains("MRI") || txt.Contains("B超")) return "EXAM";
        if (c.Contains("护") || c.Contains("嘱")) return "INSTRUCTION";
        return "TREATMENT";
    }

    private static string NormalizeFrequencyCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "QD";
        string token = raw.Split(' ', '(')[0].Trim().ToUpperInvariant();
        return token switch
        {
            "QD" or "BID" or "TID" or "QID" or "QN" or "ST" or "PRN" or "Q8H" or "Q12H" or "Q6H" => token,
            _ => token
        };
    }

    private static string NormalizeRouteCode(string? raw)
    {
        string r = (raw ?? string.Empty).Trim();
        return r switch
        {
            "口服" => "PO",
            "静脉滴注" => "IV_DRIP",
            "静脉注射" => "IV",
            "肌内注射" => "IM",
            "皮下注射" => "SC",
            "雾化吸入" => "INH",
            "外用" => "EXT",
            _ => string.IsNullOrWhiteSpace(r) ? "IV_DRIP" : r.ToUpperInvariant()
        };
    }

    private static string MapCategoryToLabel(string code) => code.ToUpperInvariant() switch
    {
        "DRUG" => "西药",
        "LAB" => "检验",
        "EXAM" => "检查",
        "INSTRUCTION" => "护理",
        _ => "治疗"
    };

    private static string MapRouteToLabel(string code) => code.ToUpperInvariant() switch
    {
        "PO" => "口服",
        "IV_DRIP" => "静脉滴注",
        "IV" => "静脉注射",
        "IM" => "肌内注射",
        "SC" => "皮下注射",
        "INH" => "雾化吸入",
        _ => code
    };

    private static string MapOrderStatusToLabel(int status, bool isLongTerm) => status switch
    {
        0 => "待护士校对",
        1 => isLongTerm ? "已校对待分解" : "已校对待执行",
        2 => "执行中",
        3 => "已停止",
        4 => "已作废",
        _ => "待护士校对"
    };

    private static string MapNursingLevelLabel(string? code) => (code ?? string.Empty).ToUpperInvariant() switch
    {
        "SPECIAL" => "特级护理",
        "LEVEL_1" => "一级护理",
        "LEVEL_2" => "二级护理",
        "LEVEL_3" => "三级护理",
        _ => string.IsNullOrWhiteSpace(code) ? "一级护理" : code
    };

    private static string JsonStr(JsonElement el, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (el.TryGetProperty(k, out var p) && p.ValueKind == JsonValueKind.String)
            {
                var v = p.GetString();
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
        }
        return string.Empty;
    }

    private static int JsonInt(JsonElement el, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (el.TryGetProperty(k, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out int val))
                return val;
        }
        return 0;
    }

    private static long JsonLong(JsonElement el, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (el.TryGetProperty(k, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out long val))
                return val;
        }
        return 0;
    }

    private static decimal JsonDec(JsonElement el, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (el.TryGetProperty(k, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out decimal val))
                return val;
        }
        return 0m;
    }
}
