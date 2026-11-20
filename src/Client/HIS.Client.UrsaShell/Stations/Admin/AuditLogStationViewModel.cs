using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Media;
using HIS.BuildingBlocks.Common;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.Admin;

public class EmrReadAuditItemModel : ClientModelBase
{
    public string Id { get; set; } = string.Empty;
    public string AccessUserId { get; set; } = string.Empty;
    public string AccessUserName { get; set; } = string.Empty;
    public string AccessOrgCode { get; set; } = string.Empty;
    public bool IsExternal { get; set; }
    public string PatientId { get; set; } = string.Empty;
    public string EncounterId { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public string DenyReason { get; set; } = string.Empty;
    public string PrevHash { get; set; } = string.Empty;
    public string CurrHash { get; set; } = string.Empty;
    public long CreatedAtMs { get; set; }
    public string TimeText => CreatedAtMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtMs).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "-";
}

public class DigitalSignatureItemModel : ClientModelBase
{
    private bool? _isVerified;
    private string _verifyMessage = string.Empty;

    public string Id { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string SignerUserId { get; set; } = string.Empty;
    public string SignerName { get; set; } = string.Empty;
    public string SignerRole { get; set; } = string.Empty;
    public string CertFingerprint { get; set; } = string.Empty;
    public string SignatureAlgorithm { get; set; } = string.Empty;
    public string TimestampToken { get; set; } = string.Empty;
    public long SignedAtMs { get; set; }
    public bool? IsVerified { get => _isVerified; set => SetField(ref _isVerified, value); }
    public string VerifyMessage { get => _verifyMessage; set => SetField(ref _verifyMessage, value); }
    public string TimeText => SignedAtMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(SignedAtMs).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "-";
}

public class EmrAccessGrantItemModel : ClientModelBase
{
    private int _status = 1;

    public string Id { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string EncounterId { get; set; } = string.Empty;
    public string GranteeUserId { get; set; } = string.Empty;
    public string GranteeOrgCode { get; set; } = string.Empty;
    public bool IsExternalGrant { get; set; }
    public IReadOnlyList<string> AllowedScopes { get; set; } = new List<string>();
    public long ValidFromMs { get; set; }
    public long ValidToMs { get; set; }
    public string GrantReason { get; set; } = string.Empty;
    public string ApprovedBy { get; set; } = string.Empty;
    public int Status { get => _status; set { if (SetField(ref _status, value)) OnPropertyChanged(nameof(StatusText)); } }
    public string StatusText => Status == 1 ? "有效授权" : "已撤销";
    public string ValidRangeText => $"{DateTimeOffset.FromUnixTimeMilliseconds(ValidFromMs).ToLocalTime():MM-dd HH:mm} ~ {DateTimeOffset.FromUnixTimeMilliseconds(ValidToMs).ToLocalTime():MM-dd HH:mm}";
}

public sealed class AuditLogStationViewModel : ClientModelBase, IShortcutTarget
{
    private readonly ShellViewModel? _shell;
    private int _selectedSubTabIndex = 0;

    public AuditLogStationViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        QueryLogsCommand = new RelayCommand(async () =>
        {
            await Workbench.LoadAuditLogsAsync();
            await Workbench.LoadSecurityPostureAsync();
            _shell?.ShowToast(Workbench.AuditSummaryInfo, "Info");
        });

        VerifyChainCommand = new RelayCommand(async () =>
        {
            await Workbench.VerifyAuditChainAsync();
            _shell?.ShowToast(
                Workbench.AuditVerificationStatus,
                Workbench.AuditVerificationBadgeClass == "Success" ? "Success" : "Warning");
        });

        CreateAnchorCommand = new RelayCommand(ConfirmCreateAnchor);

        ClearFilterCommand = new RelayCommand(async () =>
        {
            Workbench.AuditEventTypeFilter = string.Empty;
            Workbench.AuditTargetEntityFilter = string.Empty;
            Workbench.AuditOperatorFilter = string.Empty;
            await Workbench.LoadAuditLogsAsync();
            _shell?.ShowToast("已清空过滤条件并刷新审计台账。", "Info");
        });

        InspectEntryCommand = new RelayCommand(p => OpenEntryDrawer(p as AuditLogEntryModel));

        ScanThreatsCommand = new RelayCommand(async () =>
        {
            await Workbench.ScanSecurityThreatsAsync();
            _shell?.ShowToast("全网威胁与异常行为扫描已完成，已更新态势看板。", "Info");
        });

        ResolveAlertCommand = new RelayCommand(p => ConfirmResolveAlert(p as SecurityAlertModel));

        RotateKeyCommand = new RelayCommand(p => ConfirmRotateKey(p as KmsKeyModel));

        SwitchSubTabCommand = new RelayCommand(p =>
        {
            if (p is int idx)
            {
                SelectedSubTabIndex = idx;
            }
            else if (p is string s && int.TryParse(s, out int parsed))
            {
                SelectedSubTabIndex = parsed;
            }
        });

        // Level 7 Commands
        QueryReadAuditsCommand = new AsyncRelayCommand(QueryReadAuditsAsync);
        VerifyReadAuditChainCommand = new AsyncRelayCommand(VerifyReadAuditChainAsync);
        QuerySignaturesCommand = new AsyncRelayCommand(QuerySignaturesAsync);
        VerifySignatureCommand = new AsyncRelayCommand(p => VerifySignatureAsync(p as DigitalSignatureItemModel));
        QueryAccessGrantsCommand = new AsyncRelayCommand(QueryAccessGrantsAsync);
        CreateAccessGrantCommand = new AsyncRelayCommand(CreateAccessGrantAsync);
        RevokeAccessGrantCommand = new AsyncRelayCommand(p => RevokeAccessGrantAsync(p as EmrAccessGrantItemModel));
    }

    public WorkbenchModel Workbench { get; }
    public Action? FocusFilterAction { get; set; }

    public int SelectedSubTabIndex
    {
        get => _selectedSubTabIndex;
        set => SetField(ref _selectedSubTabIndex, value, nameof(SelectedSubTabIndex));
    }

    public ICommand QueryLogsCommand { get; }
    public ICommand VerifyChainCommand { get; }
    public ICommand CreateAnchorCommand { get; }
    public ICommand ClearFilterCommand { get; }
    public ICommand InspectEntryCommand { get; }
    public ICommand ScanThreatsCommand { get; }
    public ICommand ResolveAlertCommand { get; }
    public ICommand RotateKeyCommand { get; }
    public ICommand SwitchSubTabCommand { get; }

    // Level 7 Collections & Properties
    public ObservableCollection<EmrReadAuditItemModel> ReadAudits { get; } = new();
    public ObservableCollection<DigitalSignatureItemModel> DigitalSignatures { get; } = new();
    public ObservableCollection<EmrAccessGrantItemModel> AccessGrants { get; } = new();

    private string _readAuditPatientIdFilter = string.Empty;
    public string ReadAuditPatientIdFilter { get => _readAuditPatientIdFilter; set => SetField(ref _readAuditPatientIdFilter, value); }
    private string _readAuditUserIdFilter = string.Empty;
    public string ReadAuditUserIdFilter { get => _readAuditUserIdFilter; set => SetField(ref _readAuditUserIdFilter, value); }
    private string _readAuditChainStatus = "就绪 (就绪执行 SM3 审计链完整性验真)";
    public string ReadAuditChainStatus { get => _readAuditChainStatus; set => SetField(ref _readAuditChainStatus, value); }

    private string _sigEntityType = "EMR_DOC";
    public string SigEntityType { get => _sigEntityType; set => SetField(ref _sigEntityType, value); }
    private string _sigEntityId = string.Empty;
    public string SigEntityId { get => _sigEntityId; set => SetField(ref _sigEntityId, value); }

    private string _grantPatientId = string.Empty;
    public string GrantPatientId { get => _grantPatientId; set => SetField(ref _grantPatientId, value); }
    private string _granteeUserId = string.Empty;
    public string GranteeUserId { get => _granteeUserId; set => SetField(ref _granteeUserId, value); }
    private string _grantReason = "跨院专家会诊与转诊调阅";
    public string GrantReason { get => _grantReason; set => SetField(ref _grantReason, value); }
    private int _grantValidHours = 24;
    public int GrantValidHours { get => _grantValidHours; set => SetField(ref _grantValidHours, value); }
    private bool _grantIsExternal = true;
    public bool GrantIsExternal { get => _grantIsExternal; set => SetField(ref _grantIsExternal, value); }
    private string _grantAllowedScopes = "EMR,LAB,PACS";
    public string GrantAllowedScopes { get => _grantAllowedScopes; set => SetField(ref _grantAllowedScopes, value); }

    public ICommand QueryReadAuditsCommand { get; }
    public ICommand VerifyReadAuditChainCommand { get; }
    public ICommand QuerySignaturesCommand { get; }
    public ICommand VerifySignatureCommand { get; }
    public ICommand QueryAccessGrantsCommand { get; }
    public ICommand CreateAccessGrantCommand { get; }
    public ICommand RevokeAccessGrantCommand { get; }

    public async Task QueryReadAuditsAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            string url = $"/api/v4/security/read-audits?patientId={Uri.EscapeDataString(_readAuditPatientIdFilter)}&accessUserId={Uri.EscapeDataString(_readAuditUserIdFilter)}";
            var res = await http.GetFromJsonAsync<ApiResponse<List<EmrReadAuditItemModel>>>(url);
            if (res?.Data != null)
            {
                ReadAudits.Clear();
                foreach (var a in res.Data)
                {
                    ReadAudits.Add(a);
                }
                _shell?.ShowToast($"已加载病历查阅审计流水 {ReadAudits.Count} 条", "Info");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"获取读审计异常: {ex.Message}", "Error");
        }
    }

    public async Task VerifyReadAuditChainAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.GetFromJsonAsync<ApiResponse<System.Text.Json.JsonElement>>("/api/v4/security/read-audits/verify-chain");
            if (res?.Data.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                bool intact = res.Data.TryGetProperty("chainIntact", out var p1) && p1.GetBoolean();
                int checkedCount = res.Data.TryGetProperty("totalChecked", out var p2) ? p2.GetInt32() : 0;
                string msg = res.Data.TryGetProperty("message", out var p3) ? p3.GetString() ?? "" : "";

                ReadAuditChainStatus = intact ? $"✓ SM3 哈希链验真通过（校验记录数: {checkedCount}）" : $"⚠ 哈希链断裂拦截: {msg}";
                _shell?.ShowToast(ReadAuditChainStatus, intact ? "Success" : "Error");
            }
        }
        catch (Exception ex)
        {
            ReadAuditChainStatus = $"验真异常: {ex.Message}";
            _shell?.ShowToast($"链验真异常: {ex.Message}", "Error");
        }
    }

    public async Task QuerySignaturesAsync()
    {
        if (string.IsNullOrWhiteSpace(_sigEntityId))
        {
            _shell?.ShowToast("请输入待验真临床实体ID", "Warning");
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            string url = $"/api/v4/security/signatures/{Uri.EscapeDataString(_sigEntityType)}/{Uri.EscapeDataString(_sigEntityId.Trim())}";
            var res = await http.GetFromJsonAsync<ApiResponse<List<DigitalSignatureItemModel>>>(url);
            if (res?.Data != null)
            {
                DigitalSignatures.Clear();
                foreach (var sig in res.Data)
                {
                    DigitalSignatures.Add(sig);
                }
                _shell?.ShowToast($"已检索到过程数字签名 {DigitalSignatures.Count} 份", "Info");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"查询签名异常: {ex.Message}", "Error");
        }
    }

    public async Task VerifySignatureAsync(DigitalSignatureItemModel? sig)
    {
        if (sig == null) return;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.PostAsync($"/api/v4/security/signatures/{Uri.EscapeDataString(sig.Id)}/verify", null);
            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadFromJsonAsync<ApiResponse<System.Text.Json.JsonElement>>();
                bool valid = body?.Data.TryGetProperty("overallValid", out var ov) == true && ov.GetBoolean();
                sig.IsVerified = valid;
                sig.VerifyMessage = valid ? "✓ CA证书有效 + RFC 3161 时间戳连贯 + 原文哈希无篡改" : "⚠ 验签失败：证书撤销或原文指纹不匹配";
                _shell?.ShowToast($"签名验真结果: {sig.VerifyMessage}", valid ? "Success" : "Error");
            }
            else
            {
                sig.IsVerified = false;
                sig.VerifyMessage = "验签服务返回错误";
                _shell?.ShowToast("签名验真服务请求失败", "Warning");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"签名验真异常: {ex.Message}", "Error");
        }
    }

    public async Task QueryAccessGrantsAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            string url = $"/api/v4/security/access-grants?patientId={Uri.EscapeDataString(_grantPatientId)}&granteeUserId={Uri.EscapeDataString(_granteeUserId)}";
            var res = await http.GetFromJsonAsync<ApiResponse<List<EmrAccessGrantItemModel>>>(url);
            if (res?.Data != null)
            {
                AccessGrants.Clear();
                foreach (var g in res.Data)
                {
                    AccessGrants.Add(g);
                }
                _shell?.ShowToast($"已加载限时授权记录 {AccessGrants.Count} 条", "Info");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"获取授权记录异常: {ex.Message}", "Error");
        }
    }

    public async Task CreateAccessGrantAsync()
    {
        if (string.IsNullOrWhiteSpace(_grantPatientId) || string.IsNullOrWhiteSpace(_granteeUserId))
        {
            _shell?.ShowToast("患者编号与授权访问人工号不能为空", "Warning");
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var payload = new
            {
                PatientId = _grantPatientId.Trim(),
                EncounterId = (string?)null,
                GranteeUserId = _granteeUserId.Trim(),
                GranteeOrgCode = _grantIsExternal ? "EXT-HOSP-HIE" : (string?)null,
                IsExternalGrant = _grantIsExternal,
                AllowedScopes = string.IsNullOrWhiteSpace(_grantAllowedScopes) ? new List<string>() : _grantAllowedScopes.Split(',', ';').Select(s => s.Trim()).ToList(),
                ValidFromMs = now,
                ValidToMs = now + (long)_grantValidHours * 3600 * 1000,
                GrantReason = _grantReason,
                PatientConsentId = "CONSENT-" + Guid.NewGuid().ToString("N")[..8],
                ApprovedBy = SessionContext.Current.UserId ?? "ADMIN"
            };
            var res = await http.PostAsJsonAsync("/api/v4/security/access-grants", payload);
            if (res.IsSuccessStatusCode)
            {
                _shell?.ShowToast("病历限时细粒度访问授权创建成功", "Success");
                _ = QueryAccessGrantsAsync();
            }
            else
            {
                var err = await res.Content.ReadAsStringAsync();
                _shell?.ShowToast($"创建授权失败: {err}", "Warning");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"创建授权异常: {ex.Message}", "Error");
        }
    }

    public async Task RevokeAccessGrantAsync(EmrAccessGrantItemModel? grant)
    {
        if (grant == null) return;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.PostAsync($"/api/v4/security/access-grants/{Uri.EscapeDataString(grant.Id)}/revoke", null);
            if (res.IsSuccessStatusCode)
            {
                grant.Status = 0;
                _shell?.ShowToast($"授权 [{grant.Id}] 已立即回收作废", "Success");
            }
            else
            {
                _shell?.ShowToast("撤销授权失败", "Warning");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"撤销授权异常: {ex.Message}", "Error");
        }
    }

    public void Attach()
    {
        _ = Workbench.LoadAuditLogsAsync();
        _ = Workbench.LoadSecurityPostureAsync();
        _ = Workbench.LoadSecurityAlertsAsync();
        _ = Workbench.LoadKmsKeysAsync();
    }

    public void Detach()
    {
    }

    public bool OnShortcut(string keyGesture)
    {
        switch (keyGesture)
        {
            case "F2":
                VerifyChainCommand.Execute(null);
                return true;
            case "F3":
                ConfirmCreateAnchor();
                return true;
            case "F5":
                QueryLogsCommand.Execute(null);
                return true;
            case "Ctrl+F":
                FocusFilterAction?.Invoke();
                return true;
            default:
                return false;
        }
    }

    private void ConfirmCreateAnchor()
    {
        _shell?.RequestConfirm(
            "固化防截断外部锚点",
            "确定立即基于当前最新审计哈希链节点生成外部校验锚点？生成后可有效防止数据库尾部截断攻击。",
            "确认固化锚点",
            async () =>
            {
                await Workbench.CreateAuditAnchorAsync();
                _shell?.ShowToast(
                    Workbench.AuditVerificationStatus,
                    Workbench.AuditVerificationBadgeClass == "Success" ? "Success" : "Warning");
            });
    }

    private void ConfirmResolveAlert(SecurityAlertModel? alert)
    {
        if (alert == null) return;
        _shell?.RequestConfirm(
            "安全告警闭环处置",
            $"确定对告警 [{alert.AlertNo}] ({alert.AlertType}) 执行确认与闭环防范处置？",
            "确认处置闭环",
            async () =>
            {
                await Workbench.ResolveSecurityAlertAsync(alert.Id, "安全管理员已核实风险，执行拦截与安全加固处置");
                _shell?.ShowToast($"告警 [{alert.AlertNo}] 处置完成", "Success");
            });
    }

    private void ConfirmRotateKey(KmsKeyModel? key)
    {
        if (key == null) return;
        _shell?.RequestConfirm(
            "密码学主密钥平滑轮换",
            $"确定触发对 [{key.KeyName}] (当前版本 v{key.Version}) 的平滑密钥轮换？系统将生成新密钥版本，历史版本平滑保留用于验签，不会造成业务中断。",
            "确认执行轮换",
            async () =>
            {
                await Workbench.RotateKmsKeyAsync(key.KeyName, "安全管理员执行等保三级定期密钥轮换");
                _shell?.ShowToast($"主密钥 [{key.KeyName}] 已成功轮换至新版本", "Success");
            });
    }

    private void OpenEntryDrawer(AuditLogEntryModel? entry)
    {
        if (entry == null) return;
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(CreateField("事件编号 / 类型", $"{entry.Id} · {entry.EventType}"));
        panel.Children.Add(CreateField("发生时间", entry.FormattedTime));
        panel.Children.Add(CreateField("操作人 / 科室", $"{entry.OperatorName} ({entry.UserId}) | 科室 {entry.DeptId}"));
        panel.Children.Add(CreateField("目标实体 / ID", $"{entry.TargetEntity} -> {entry.TargetId}"));
        panel.Children.Add(CreateField("操作明细载荷", entry.ActionDetail));
        panel.Children.Add(CreateField("当前节点 SHA-256 / SM3", entry.CurrentHash, mono: true));
        panel.Children.Add(CreateField("前驱节点 PrevHash", entry.PrevHash, mono: true));

        _shell?.OpenDrawer("安全审计节点密码学存证详情", entry.EventType, panel, 520);
    }

    private static Border CreateField(string label, string value, bool mono = false)
    {
        return new Border
        {
            Padding = new Avalonia.Thickness(12),
            CornerRadius = new Avalonia.CornerRadius(6),
            BorderBrush = ThemeBrushes.Border,
            BorderThickness = new Avalonia.Thickness(1),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = ThemeBrushes.Text2 },
                    new SelectableTextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(value) ? "-" : value,
                        FontSize = 12,
                        FontFamily = mono ? new FontFamily("Consolas") : FontFamily.Default,
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        };
    }
}
