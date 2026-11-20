using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.BuildingBlocks.Common;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;
using HIS.Contracts;

namespace HIS.Client.UrsaShell.Stations.Monitoring;

public class IcuBedTelemetryItem : ClientModelBase
{
    private string _encounterId = string.Empty;
    private string _patientId = string.Empty;
    private string _bedCode = string.Empty;
    private string _patientName = string.Empty;
    private string _diagnosis = string.Empty;
    private string _deviceCode = string.Empty;
    private int _hr = 75;
    private int _rr = 18;
    private decimal _sysBp = 120m;
    private decimal _diaBp = 80m;
    private decimal _spo2 = 98m;
    private decimal _tempC = 36.8m;
    private int _cvp = 8;
    private int _gcs = 15;
    private bool _hasAlarm;
    private string? _alarmId;
    private string _alarmMessage = "生命体征平稳";
    private int _mewsScore = 0;
    private int _apache2Score = 0;
    private int _sofaScore = 0;
    private string _riskLevel = "低风险";
    private string _guidanceText = "生命体征处于参考正常范围，继续维持常规重症监护。";

    public string EncounterId { get => _encounterId; set => SetField(ref _encounterId, value); }
    public string PatientId { get => _patientId; set => SetField(ref _patientId, value); }
    public string BedCode { get => _bedCode; set => SetField(ref _bedCode, value); }
    public string PatientName { get => _patientName; set => SetField(ref _patientName, value); }
    public string Diagnosis { get => _diagnosis; set => SetField(ref _diagnosis, value); }
    public string DeviceCode { get => _deviceCode; set => SetField(ref _deviceCode, value); }
    public int Hr { get => _hr; set => SetField(ref _hr, value); }
    public int Rr { get => _rr; set => SetField(ref _rr, value); }
    public decimal SysBp { get => _sysBp; set => SetField(ref _sysBp, value); }
    public decimal DiaBp { get => _diaBp; set => SetField(ref _diaBp, value); }
    public string Bp => $"{SysBp:0}/{DiaBp:0}";
    public decimal Spo2 { get => _spo2; set => SetField(ref _spo2, value); }
    public decimal TempC { get => _tempC; set => SetField(ref _tempC, value); }
    public int Cvp { get => _cvp; set => SetField(ref _cvp, value); }
    public int Gcs { get => _gcs; set => SetField(ref _gcs, value); }
    public bool HasAlarm { get => _hasAlarm; set => SetField(ref _hasAlarm, value); }
    public string? AlarmId { get => _alarmId; set => SetField(ref _alarmId, value); }
    public string AlarmMessage { get => _alarmMessage; set => SetField(ref _alarmMessage, value); }
    public int MewsScore { get => _mewsScore; set => SetField(ref _mewsScore, value); }
    public int Apache2Score { get => _apache2Score; set => SetField(ref _apache2Score, value); }
    public int SofaScore { get => _sofaScore; set => SetField(ref _sofaScore, value); }
    public string RiskLevel { get => _riskLevel; set => SetField(ref _riskLevel, value); }
    public string GuidanceText { get => _guidanceText; set => SetField(ref _guidanceText, value); }
}

public class MonDeviceItem : ClientModelBase
{
    public string Id { get; set; } = string.Empty;
    public string DeviceCode { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string BedCode { get; set; } = string.Empty;
    public string CurrentEncounterId { get; set; } = string.Empty;
    public int IsOnline { get; set; } = 1;
    public string StatusText => IsOnline == 1 ? "在线监护" : "离线";
}

public class MonAlarmItem : ClientModelBase
{
    public string Id { get; set; } = string.Empty;
    public string EncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string AlarmType { get; set; } = string.Empty;
    public string Severity { get; set; } = "WARNING";
    public string Message { get; set; } = string.Empty;
    public int Acked { get; set; }
    public long CreatedAtMs { get; set; }
    public string TimeText => DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtMs).ToLocalTime().ToString("HH:mm:ss");
}

public sealed class MonitoringStationViewModel : ClientModelBase, IStationLifecycle, IShortcutTarget
{
    private readonly WorkbenchModel _workbench;
    private readonly ShellViewModel? _shell;
    private readonly HttpClient _http;

    private IcuBedTelemetryItem? _selectedBed;
    private string _kbGuidance = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isLoading;

    // Panel toggles
    private bool _showDeviceBindPanel;
    private bool _showTelemetryInputPanel;
    private bool _showAlarmsPanel;

    // Device bind fields
    private string _bindDeviceCode = "MON-ICU-01";
    private string _bindDeviceName = "迈瑞 BeneVision N17 监护仪";
    private string _bindBedCode = string.Empty;

    // Manual / Stream ingest fields
    private int _inputHr = 78;
    private int _inputRr = 18;
    private decimal _inputSysBp = 120m;
    private decimal _inputDiaBp = 78m;
    private decimal _inputSpo2 = 98m;
    private decimal _inputTempC = 36.8m;
    private int _inputCvp = 8;
    private int _inputGcs = 15;

    public ObservableCollection<IcuBedTelemetryItem> Beds { get; } = new();
    public ObservableCollection<MonDeviceItem> Devices { get; } = new();
    public ObservableCollection<MonAlarmItem> Alarms { get; } = new();

    public IcuBedTelemetryItem? SelectedBed
    {
        get => _selectedBed;
        set
        {
            if (SetField(ref _selectedBed, value))
            {
                UpdateKbGuidance();
                if (value != null)
                {
                    BindBedCode = value.BedCode;
                    InputHr = value.Hr;
                    InputRr = value.Rr;
                    InputSysBp = value.SysBp;
                    InputDiaBp = value.DiaBp;
                    InputSpo2 = value.Spo2;
                    InputTempC = value.TempC;
                    InputCvp = value.Cvp;
                    InputGcs = value.Gcs;
                }
            }
        }
    }

    public string KbGuidance
    {
        get => _kbGuidance;
        set => SetField(ref _kbGuidance, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetField(ref _isLoading, value);
    }

    public bool ShowDeviceBindPanel
    {
        get => _showDeviceBindPanel;
        set => SetField(ref _showDeviceBindPanel, value);
    }

    public bool ShowTelemetryInputPanel
    {
        get => _showTelemetryInputPanel;
        set => SetField(ref _showTelemetryInputPanel, value);
    }

    public bool ShowAlarmsPanel
    {
        get => _showAlarmsPanel;
        set => SetField(ref _showAlarmsPanel, value);
    }

    public string BindDeviceCode
    {
        get => _bindDeviceCode;
        set => SetField(ref _bindDeviceCode, value);
    }

    public string BindDeviceName
    {
        get => _bindDeviceName;
        set => SetField(ref _bindDeviceName, value);
    }

    public string BindBedCode
    {
        get => _bindBedCode;
        set => SetField(ref _bindBedCode, value);
    }

    public int InputHr
    {
        get => _inputHr;
        set => SetField(ref _inputHr, value);
    }

    public int InputRr
    {
        get => _inputRr;
        set => SetField(ref _inputRr, value);
    }

    public decimal InputSysBp
    {
        get => _inputSysBp;
        set => SetField(ref _inputSysBp, value);
    }

    public decimal InputDiaBp
    {
        get => _inputDiaBp;
        set => SetField(ref _inputDiaBp, value);
    }

    public decimal InputSpo2
    {
        get => _inputSpo2;
        set => SetField(ref _inputSpo2, value);
    }

    public decimal InputTempC
    {
        get => _inputTempC;
        set => SetField(ref _inputTempC, value);
    }

    public int InputCvp
    {
        get => _inputCvp;
        set => SetField(ref _inputCvp, value);
    }

    public int InputGcs
    {
        get => _inputGcs;
        set => SetField(ref _inputGcs, value);
    }

    public ICommand SelectBedCommand { get; }
    public ICommand AckAlarmCommand { get; }
    public ICommand CalculateScoresCommand { get; }
    public ICommand BindDeviceCommand { get; }
    public ICommand IngestTelemetryCommand { get; }
    public ICommand ToggleDeviceBindPanelCommand { get; }
    public ICommand ToggleTelemetryInputPanelCommand { get; }
    public ICommand ToggleAlarmsPanelCommand { get; }
    public ICommand RefreshCommand { get; }

    public MonitoringStationViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        _workbench = workbench;
        _shell = shell;
        _http = new HttpClient(new AuthenticatedHttpHandler(SessionContext.Current))
        {
            BaseAddress = new Uri(ApiEndpoints.BaseAddress),
            Timeout = TimeSpan.FromSeconds(10)
        };

        SelectBedCommand = new RelayCommand(p => SelectedBed = p as IcuBedTelemetryItem);
        AckAlarmCommand = new AsyncRelayCommand(ExecuteAckAlarmAsync);
        CalculateScoresCommand = new AsyncRelayCommand(ExecuteCalculateScoresAsync);
        BindDeviceCommand = new AsyncRelayCommand(ExecuteBindDeviceAsync);
        IngestTelemetryCommand = new AsyncRelayCommand(ExecuteIngestTelemetryAsync);

        ToggleDeviceBindPanelCommand = new RelayCommand(() => ShowDeviceBindPanel = !ShowDeviceBindPanel);
        ToggleTelemetryInputPanelCommand = new RelayCommand(() => ShowTelemetryInputPanel = !ShowTelemetryInputPanel);
        ToggleAlarmsPanelCommand = new RelayCommand(() => ShowAlarmsPanel = !ShowAlarmsPanel);
        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
    }

    public async Task LoadDataAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        try
        {
            StatusMessage = "正在同步重症监护设备与实时生理时序数据...";

            // 1. Load active inpatients from real backend API
            var inpatRes = await _http.GetFromJsonAsync<ApiResponse<List<ActiveInpatientDto>>>("/api/v4/ipd/encounters/active");
            var activeInpatients = inpatRes?.Data ?? new List<ActiveInpatientDto>();

            // 2. Load devices
            var devRes = await _http.GetFromJsonAsync<ApiResponse<List<DeviceDto>>>("/api/v4/monitoring/devices");
            Devices.Clear();
            if (devRes?.Data != null)
            {
                foreach (var d in devRes.Data)
                {
                    Devices.Add(new MonDeviceItem
                    {
                        Id = d.Id,
                        DeviceCode = d.DeviceCode,
                        DeviceName = d.DeviceName,
                        BedCode = d.BedCode,
                        CurrentEncounterId = d.CurrentEncounterId ?? string.Empty,
                        IsOnline = d.IsOnline
                    });
                }
            }

            // 3. Load active alarms
            var alarmRes = await _http.GetFromJsonAsync<ApiResponse<List<AlarmDto>>>("/api/v4/monitoring/alarms?onlyUnacked=true");
            Alarms.Clear();
            var alarmMap = new Dictionary<string, AlarmDto>();
            if (alarmRes?.Data != null)
            {
                foreach (var a in alarmRes.Data)
                {
                    Alarms.Add(new MonAlarmItem
                    {
                        Id = a.Id,
                        EncounterId = a.EncounterId,
                        PatientId = a.PatientId,
                        AlarmType = a.AlarmType,
                        Severity = a.Severity,
                        Message = a.Message,
                        Acked = a.Acked,
                        CreatedAtMs = a.CreatedAtMs
                    });
                    if (!alarmMap.ContainsKey(a.EncounterId))
                    {
                        alarmMap[a.EncounterId] = a;
                    }
                }
            }

            // 4. Construct live bed items for each active patient
            Beds.Clear();
            foreach (var p in activeInpatients)
            {
                var bed = new IcuBedTelemetryItem
                {
                    EncounterId = p.Id,
                    PatientId = p.PatientId,
                    BedCode = p.BedCode ?? "01床",
                    PatientName = $"{p.PatientName} ({p.Gender} · {p.Age}岁)",
                    Diagnosis = p.Diagnosis ?? p.DeptName ?? "重症监护患者"
                };

                // Match device
                var dev = Devices.FirstOrDefault(d => d.CurrentEncounterId == p.Id || d.BedCode == p.BedCode);
                if (dev != null)
                {
                    bed.DeviceCode = dev.DeviceCode;
                }

                // Match alarm
                if (alarmMap.TryGetValue(p.Id, out var al))
                {
                    bed.HasAlarm = true;
                    bed.AlarmId = al.Id;
                    bed.AlarmMessage = al.Message;
                }

                // Query latest stream
                try
                {
                    var streamRes = await _http.GetFromJsonAsync<ApiResponse<List<VitalStreamDto>>>($"/api/v4/monitoring/streams/{Uri.EscapeDataString(p.Id)}?limit=1");
                    var latest = streamRes?.Data?.FirstOrDefault();
                    if (latest != null)
                    {
                        if (latest.Hr.HasValue) bed.Hr = (int)latest.Hr.Value;
                        if (latest.Rr.HasValue) bed.Rr = (int)latest.Rr.Value;
                        if (latest.SysBp.HasValue) bed.SysBp = latest.SysBp.Value;
                        if (latest.DiaBp.HasValue) bed.DiaBp = latest.DiaBp.Value;
                        if (latest.Spo2.HasValue) bed.Spo2 = latest.Spo2.Value;
                        if (latest.TempC.HasValue) bed.TempC = latest.TempC.Value;
                        if (latest.Cvp.HasValue) bed.Cvp = (int)latest.Cvp.Value;
                        if (latest.Gcs.HasValue) bed.Gcs = latest.Gcs.Value;
                        if (latest.IsAlarm == 1 && !bed.HasAlarm)
                        {
                            bed.HasAlarm = true;
                            bed.AlarmMessage = $"体征异常报警 ({latest.AlarmLevel ?? "CRITICAL"})";
                        }
                    }
                }
                catch { }

                Beds.Add(bed);
            }

            if (Beds.Count > 0)
            {
                SelectedBed = Beds[0];
            }

            StatusMessage = $"✓ 重症监护数据已同步：在院监护患者 {Beds.Count} 人，在线设备 {Devices.Count} 台，未确认报警 {Alarms.Count} 起。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载监护数据失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task ExecuteBindDeviceAsync()
    {
        if (SelectedBed == null)
        {
            _shell?.ShowToast("请先选择床位", "Warning");
            return;
        }
        if (string.IsNullOrWhiteSpace(BindDeviceCode))
        {
            _shell?.ShowToast("请输入设备序列号", "Warning");
            return;
        }

        try
        {
            var dto = new
            {
                DeviceCode = BindDeviceCode.Trim(),
                DeviceName = BindDeviceName.Trim(),
                WardId = "ICU-WARD",
                BedCode = SelectedBed.BedCode,
                EncounterId = SelectedBed.EncounterId,
                Protocol = "HL7_MLLP"
            };

            var res = await _http.PostAsJsonAsync("/api/v4/monitoring/devices/bind", dto);
            var result = await res.Content.ReadFromJsonAsync<ApiResponse<object>>();
            if (res.IsSuccessStatusCode && result?.Code == 200)
            {
                _shell?.ShowToast($"设备 {BindDeviceCode} 已成功绑定床位 {SelectedBed.BedCode}", "Success");
                ShowDeviceBindPanel = false;
                await LoadDataAsync();
            }
            else
            {
                _shell?.ShowToast($"绑定失败: {result?.Message ?? "服务异常"}", "Danger");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"绑定异常: {ex.Message}", "Danger");
        }
    }

    public async Task ExecuteIngestTelemetryAsync()
    {
        if (SelectedBed == null)
        {
            _shell?.ShowToast("请先选择录入体征的床位/患者", "Warning");
            return;
        }

        try
        {
            var dto = new
            {
                DeviceCode = string.IsNullOrWhiteSpace(SelectedBed.DeviceCode) ? "MANUAL-INPUT" : SelectedBed.DeviceCode,
                EncounterId = SelectedBed.EncounterId,
                PatientId = SelectedBed.PatientId,
                Hr = (decimal)InputHr,
                Rr = (decimal)InputRr,
                SysBp = InputSysBp,
                DiaBp = InputDiaBp,
                Spo2 = InputSpo2,
                TempC = InputTempC,
                Cvp = (decimal)InputCvp,
                Gcs = InputGcs,
                EcgRhythm = "SINUS"
            };

            var res = await _http.PostAsJsonAsync("/api/v4/monitoring/telemetry/ingest", dto);
            var result = await res.Content.ReadFromJsonAsync<ApiResponse<object>>();
            if (res.IsSuccessStatusCode && result?.Code == 200)
            {
                _shell?.ShowToast("生命体征时序流数据采集成功", "Success");
                ShowTelemetryInputPanel = false;
                await LoadDataAsync();
            }
            else
            {
                _shell?.ShowToast($"采集上报失败: {result?.Message ?? "服务异常"}", "Danger");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"录入异常: {ex.Message}", "Danger");
        }
    }

    public async Task ExecuteAckAlarmAsync()
    {
        if (SelectedBed == null) return;
        string? alarmId = SelectedBed.AlarmId;
        if (string.IsNullOrWhiteSpace(alarmId))
        {
            var matched = Alarms.FirstOrDefault(a => a.EncounterId == SelectedBed.EncounterId);
            alarmId = matched?.Id;
        }

        if (string.IsNullOrWhiteSpace(alarmId))
        {
            SelectedBed.HasAlarm = false;
            SelectedBed.AlarmMessage = "报警已解除";
            _shell?.ShowToast("报警状态已清除", "Success");
            return;
        }

        try
        {
            var res = await _http.PostAsync($"/api/v4/monitoring/alarms/{Uri.EscapeDataString(alarmId)}/ack", null);
            var result = await res.Content.ReadFromJsonAsync<ApiResponse<object>>();
            if (res.IsSuccessStatusCode && result?.Code == 200)
            {
                SelectedBed.HasAlarm = false;
                SelectedBed.AlarmMessage = "已由医护人员签收确认 (" + DateTime.Now.ToString("HH:mm:ss") + ")";
                StatusMessage = "✓ 监护报警已复核确认并关闭。";
                _shell?.ShowToast("监护报警已双人确认签收", "Success");
                await LoadDataAsync();
            }
            else
            {
                _shell?.ShowToast($"确认失败: {result?.Message ?? "服务异常"}", "Danger");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"签收异常: {ex.Message}", "Danger");
        }
    }

    public async Task ExecuteCalculateScoresAsync()
    {
        if (SelectedBed == null)
        {
            _shell?.ShowToast("请先选择患者", "Warning");
            return;
        }

        try
        {
            StatusMessage = "正在联合最新用药、检验指标与时序体征重新评估 APACHE II / SOFA / MEWS 评分...";

            // Evaluate MEWS
            var mewsRes = await _http.PostAsync($"/api/v4/monitoring/scores/evaluate/{Uri.EscapeDataString(SelectedBed.EncounterId)}?scoreType=MEWS", null);
            var mewsEnvelope = await mewsRes.Content.ReadFromJsonAsync<ApiResponse<RiskScoreEvaluationResultDto>>();

            // Evaluate APACHE II
            var apRes = await _http.PostAsync($"/api/v4/monitoring/scores/evaluate/{Uri.EscapeDataString(SelectedBed.EncounterId)}?scoreType=APACHE2", null);
            var apEnvelope = await apRes.Content.ReadFromJsonAsync<ApiResponse<RiskScoreEvaluationResultDto>>();

            // Evaluate SOFA
            var sofaRes = await _http.PostAsync($"/api/v4/monitoring/scores/evaluate/{Uri.EscapeDataString(SelectedBed.EncounterId)}?scoreType=SOFA", null);
            var sofaEnvelope = await sofaRes.Content.ReadFromJsonAsync<ApiResponse<RiskScoreEvaluationResultDto>>();

            if (mewsEnvelope?.Data != null)
            {
                SelectedBed.MewsScore = mewsEnvelope.Data.TotalScore;
                SelectedBed.RiskLevel = mewsEnvelope.Data.RiskLevel;
                SelectedBed.GuidanceText = mewsEnvelope.Data.GuidanceText;
            }
            if (apEnvelope?.Data != null)
            {
                SelectedBed.Apache2Score = apEnvelope.Data.TotalScore;
            }
            if (sofaEnvelope?.Data != null)
            {
                SelectedBed.SofaScore = sofaEnvelope.Data.TotalScore;
            }

            UpdateKbGuidance();
            StatusMessage = $"✓ 评分重新评估完成：MEWS={SelectedBed.MewsScore}分, APACHE II={SelectedBed.Apache2Score}分, SOFA={SelectedBed.SofaScore}分。";
            _shell?.ShowToast("ICU 综合重症风险评分计算成功", "Success");
        }
        catch (Exception ex)
        {
            StatusMessage = $"评估失败: {ex.Message}";
            _shell?.ShowToast($"计算异常: {ex.Message}", "Danger");
        }
    }

    private void UpdateKbGuidance()
    {
        if (SelectedBed == null) return;
        if (!string.IsNullOrWhiteSpace(SelectedBed.GuidanceText) && SelectedBed.GuidanceText != "生命体征处于参考正常范围，继续维持常规重症监护。")
        {
            KbGuidance = $"【CDSS 知识库评分结论与处置建议】\n风险等级: {SelectedBed.RiskLevel}\n{SelectedBed.GuidanceText}";
        }
        else if (SelectedBed.HasAlarm)
        {
            KbGuidance = "【CDSS 联合临床指南提示】\n1. 休克早期液体复苏：晶体液 30ml/kg 快速滴注，维持 MAP >= 65mmHg；\n2. 升压药推荐：去甲肾上腺素首选，目标 CVP 8-12cmH2O；\n3. 氧疗策略：高流量湿化氧疗 (HFNC) 或无创正压通气，目标 SpO2 94%~98%；\n4. 感染控制：结合抗感染用药分级，密切监测 PCT 与血常规白细胞变化。";
        }
        else
        {
            KbGuidance = "【CDSS 临床监护建议】\n患者当前生命体征相对平稳，各项连续监测指标处于目标范围。继续维持连续心电、血氧与无创血压监护。";
        }
    }

    public void Activate() => _ = LoadDataAsync();
    public void Deactivate() { }
    public void Dispose() => _http.Dispose();
    public bool OnShortcut(string gesture) => false;

    // Helper records for deserialization
    public record ActiveInpatientDto(
        string Id,
        string PatientId,
        string PatientName,
        string Gender,
        int Age,
        string BedCode,
        string AdmissionNo,
        string DeptName,
        string? Diagnosis
    );

    public record DeviceDto(
        string Id,
        string DeviceCode,
        string DeviceName,
        string BedCode,
        string? CurrentEncounterId,
        int IsOnline
    );

    public record AlarmDto(
        string Id,
        string StreamId,
        string EncounterId,
        string PatientId,
        string AlarmType,
        string Severity,
        string Message,
        int Acked,
        long CreatedAtMs
    );

    public record VitalStreamDto(
        string Id,
        string DeviceId,
        string EncounterId,
        string PatientId,
        long SampledAtMs,
        decimal? Hr,
        decimal? Rr,
        decimal? SysBp,
        decimal? DiaBp,
        decimal? Map,
        decimal? Spo2,
        decimal? TempC,
        decimal? Cvp,
        int? Gcs,
        string? EcgRhythm,
        short IsAlarm,
        string? AlarmLevel
    );
}
