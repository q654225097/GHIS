using System;
using System.Collections.ObjectModel;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.BuildingBlocks.Common;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;

namespace HIS.Client.UrsaShell.ViewModels;

public sealed partial class InpatientSettlementViewModel
{
    private InpatientDailyCostSheetDto? _dailyCostSheet;
    private string _dailyCostSheetSummary = "点击「核对住院一日清单」从收费明细与医技/滚费账本同步实时明细";

    public ObservableCollection<InpatientDailyCostCategoryDto> DailyCostCategories { get; } = new();
    public ObservableCollection<InpatientDailyCostItemDto> DailyCostTodayItems { get; } = new();

    public string DailyCostSheetSummary
    {
        get => _dailyCostSheetSummary;
        private set => SetField(ref _dailyCostSheetSummary, value);
    }

    public bool HasDailyCostSheet => _dailyCostSheet != null;

    private ICommand? _loadDailyCostSheetCommand;
    public ICommand LoadDailyCostSheetCommand => _loadDailyCostSheetCommand ??= new AsyncRelayCommand(LoadDailyCostSheetForPreviewAsync);

    private async Task LoadDailyCostSheetForPreviewAsync()
    {
        string? encId = SelectedRequest?.EncounterId ?? Preview?.EncounterId;
        if (string.IsNullOrWhiteSpace(encId))
        {
            DailyCostSheetSummary = "请先在左侧选择一名住院出院患者";
            return;
        }
        try
        {
            using var http = _createHttpClient();
            using var resp = await http.GetAsync($"/api/v4/ipd/encounters/{Uri.EscapeDataString(encId)}/daily-cost-sheet");
            var env = await ReadEnvelopeAsync<InpatientDailyCostSheetDto>(resp);
            if (!resp.IsSuccessStatusCode || env.Code is not (200 or 0) || env.Data == null)
            {
                DailyCostSheetSummary = env.Message ?? "读取一日清单失败";
                return;
            }
            _dailyCostSheet = env.Data;
            DailyCostCategories.Clear();
            foreach (var c in env.Data.Categories) DailyCostCategories.Add(c);
            DailyCostTodayItems.Clear();
            foreach (var it in env.Data.TodayItems) DailyCostTodayItems.Add(it);

            DailyCostSheetSummary = $"账期 {env.Data.FeeDateInt} · 当日 ¥{env.Data.TodayTotalAmount:0.00} · 累计 ¥{env.Data.CumulativeTotalAmount:0.00} · 预交 ¥{env.Data.PrepaymentBalance:0.00} · {env.Data.ArrearsStatusText}";
            OnPropertyChanged(nameof(HasDailyCostSheet));
        }
        catch (Exception ex)
        {
            DailyCostSheetSummary = "读取一日清单异常：" + ex.Message;
        }
    }
}
