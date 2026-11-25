using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.IpdDoctor;

public sealed partial class IpdDoctorViewModel
{
    public sealed record OrderSubTypeChoice(string Code, string Label);

    private OrderSubTypeChoice _selectedOrderSubType = new("NORMAL", "常规医嘱");
    public ObservableCollection<OrderSubTypeChoice> OrderSubTypeOptions { get; } = new()
    {
        new("NORMAL", "常规医嘱"),
        new("DISCHARGE_MED", "出院带药"),
        new("SELF_PREPARED", "患者自备药"),
        new("INSTRUCTION", "嘱托/护嘱")
    };

    public OrderSubTypeChoice SelectedOrderSubType
    {
        get => _selectedOrderSubType;
        set => SetField(ref _selectedOrderSubType, value);
    }

    private ICommand? _openDailyCostSheetCommand;
    public ICommand OpenDailyCostSheetCommand => _openDailyCostSheetCommand ??= new AsyncRelayCommand(OpenDailyCostSheetDrawerAsync);

    private ICommand? _openTransferSbarCommand;
    public ICommand OpenTransferSbarCommand => _openTransferSbarCommand ??= new RelayCommand(OpenTransferSbarDrawer);

    private ICommand? _refreshEncounterSyncCommand;
    public ICommand RefreshEncounterSyncCommand => _refreshEncounterSyncCommand ??= new AsyncRelayCommand(() => Workbench.RefreshInpatientsFromServerAsync());

    private async Task OpenDailyCostSheetDrawerAsync()
    {
        var patient = Workbench.SelectedInpatient;
        if (patient == null || _shell == null)
        {
            _shell?.ShowToast("请先选择住院患者", "Warning");
            return;
        }

        var sheet = await Workbench.LoadDailyCostSheetAsync(patient);
        var panel = new StackPanel { Spacing = 10 };
        if (sheet == null)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "暂未获取到服务端一日清单明细（请确认已登录并连接服务端）。",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
            _shell.OpenDrawer("住院一日清单", patient.PatientName, panel, 560);
            return;
        }

        panel.Children.Add(new TextBlock
        {
            Text = $"{sheet.PatientName} · {sheet.BedCode} · 账期 {sheet.FeeDateInt}",
            FontSize = 14,
            FontWeight = FontWeight.Bold
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"当日发生: ¥{sheet.TodayTotalAmount:0.00}  |  在院累计: ¥{sheet.CumulativeTotalAmount:0.00}  |  预交金: ¥{sheet.PrepaymentBalance:0.00}  |  可用结余: ¥{sheet.AvailableBalance:0.00}",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = sheet.ArrearsStatusText,
            FontSize = 12,
            FontWeight = FontWeight.Bold
        });

        if (sheet.Categories.Count > 0)
        {
            panel.Children.Add(new TextBlock { Text = "当日费用分类汇总", FontSize = 13, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 6, 0, 0) });
            foreach (var cat in sheet.Categories)
            {
                panel.Children.Add(new TextBlock { Text = $"• {cat.CategoryName} ({cat.ItemCount}项): ¥{cat.Amount:0.00}", FontSize = 12 });
            }
        }

        panel.Children.Add(new TextBlock { Text = $"明细项目 ({sheet.TodayItems.Count} 条)", FontSize = 13, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 6, 0, 0) });
        foreach (var it in sheet.TodayItems.Take(50))
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"[{it.FeeDateInt}] {it.ItemName} × {it.Quantity:0.##}{it.UnitName}  单价¥{it.UnitPrice:0.00}  小计¥{it.TotalAmount:0.00}",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            });
        }

        _shell.OpenDrawer("住院一日清单明细", "实时汇总临床医嘱/检查检验与滚费账本", panel, 600);
    }

    private void OpenTransferSbarDrawer()
    {
        var patient = Workbench.SelectedInpatient;
        if (patient == null || _shell == null)
        {
            _shell?.ShowToast("请先选择住院患者", "Warning");
            return;
        }

        string toDeptId = string.IsNullOrWhiteSpace(patient.AdmitDeptId) ? "DEPT-IPD-02" : "DEPT-IPD-02";
        string toWardId = string.IsNullOrWhiteSpace(patient.AdmitWardId) ? "DEPT-WARD-01" : "DEPT-WARD-01";
        string toBedCode = "03";
        bool reorg = true;
        string sbarS = $"患者{patient.PatientName}，因专科治疗需要拟转科至目标科室病区";
        string sbarB = $"入院诊断：{patient.Diagnosis}，在院第 {patient.StayDays} 天";
        string sbarA = "目前神志清，生命体征平稳，原科室长期医嘱待重整";
        string sbarR = "建议接收科室接入后重整长期医嘱并延续护理评估";

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = $"当前位置：{patient.AdmittingDept} / {patient.NursingWard} / {patient.BedCode}", FontSize = 12, FontWeight = FontWeight.Bold });

        var deptBox = new TextBox { Text = toDeptId, PlaceholderText = "目标住院科室ID (如 DEPT-IPD-02)" };
        var wardBox = new TextBox { Text = toWardId, PlaceholderText = "目标护理病区ID (如 DEPT-WARD-01)" };
        var bedBox = new TextBox { Text = toBedCode, PlaceholderText = "目标床号 (如 03)" };
        var reorgCheck = new CheckBox { Content = "跨科自动停止原长期医嘱并进入医嘱重整", IsChecked = true };
        var sBox = new TextBox { Text = sbarS, PlaceholderText = "S (现状 Situation)" };
        var bBox = new TextBox { Text = sbarB, PlaceholderText = "B (背景 Background)" };
        var aBox = new TextBox { Text = sbarA, PlaceholderText = "A (评估 Assessment)" };
        var rBox = new TextBox { Text = sbarR, PlaceholderText = "R (建议 Recommendation)" };

        panel.Children.Add(new TextBlock { Text = "目标住院科室 ID", FontSize = 11 });
        panel.Children.Add(deptBox);
        panel.Children.Add(new TextBlock { Text = "目标病区 ID", FontSize = 11 });
        panel.Children.Add(wardBox);
        panel.Children.Add(new TextBlock { Text = "目标床号", FontSize = 11 });
        panel.Children.Add(bedBox);
        panel.Children.Add(reorgCheck);
        panel.Children.Add(new TextBlock { Text = "SBAR 结构化交接记录", FontSize = 12, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 4, 0, 0) });
        panel.Children.Add(sBox);
        panel.Children.Add(bBox);
        panel.Children.Add(aBox);
        panel.Children.Add(rBox);

        var submitBtn = new Button
        {
            Content = "确认转科/换床并执行SBAR交接",
            Height = 34,
            Margin = new Thickness(0, 6, 0, 0),
            Command = new AsyncRelayCommand(async () =>
            {
                reorg = reorgCheck.IsChecked == true;
                var (ok, msg) = await Workbench.TransferAndReorganizeAsync(
                    patient,
                    "DEPT_TRANSFER",
                    deptBox.Text?.Trim() ?? toDeptId,
                    wardBox.Text?.Trim() ?? toWardId,
                    bedBox.Text?.Trim() ?? toBedCode,
                    reorg,
                    sBox.Text ?? string.Empty,
                    bBox.Text ?? string.Empty,
                    aBox.Text ?? string.Empty,
                    rBox.Text ?? string.Empty);
                _shell.ShowToast(msg, ok ? "Success" : "Error");
                if (ok) _shell.CloseDrawer();
            })
        };
        submitBtn.Classes.Add("Primary");
        panel.Children.Add(submitBtn);

        _shell.OpenDrawer("住院转科 / 换床与 SBAR 交接", "跨科自动重整原科室长期医嘱并释放原床位", panel, 520);
    }
}
