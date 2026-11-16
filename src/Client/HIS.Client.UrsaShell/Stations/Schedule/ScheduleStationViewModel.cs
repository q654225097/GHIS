using System.ComponentModel;
using System.Windows.Input;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.Schedule;

public sealed class ScheduleStationViewModel : ClientModelBase, IShortcutTarget
{
    private readonly ShellViewModel? _shell;
    private bool _subscribed;

    public ScheduleStationViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        NewTemplateCommand = new RelayCommand(ExecuteNewTemplate);
        SaveTemplateCommand = new RelayCommand(ExecuteSaveTemplate, () => Schedule.CanEditTemplate);
        GenerateSlotsCommand = new RelayCommand(ConfirmGenerateSlots, () => Schedule.CanEditTemplate && Schedule.HasTemplateSelection);
        FilterSlotsCommand = new RelayCommand(ExecuteFilterSlots, () => Schedule.IsNotBusy);
        ClearFilterCommand = new RelayCommand(ExecuteClearFilter, () => Schedule.IsNotBusy);
        SelectTemplateCommand = new RelayCommand(p => ExecuteSelectTemplate(p as ScheduleTemplateItem));
        UpdateSlotCapacityCommand = new RelayCommand(p => ExecuteUpdateCapacity(p as ScheduleSlotItem));
        ToggleSlotCommand = new RelayCommand(p => ConfirmToggleSlot(p as ScheduleSlotItem));
    }

    public WorkbenchModel Workbench { get; }
    public ScheduleWorkstationViewModel Schedule => Workbench.Schedule;

    public Action? FocusTemplateNameAction { get; set; }
    public Action? FocusQueryDateAction { get; set; }

    public ScheduleWorkstationViewModel.DepartmentChoice? SelectedFilterDepartment
    {
        get => Schedule.FilterDepartment;
        set
        {
            string nextId = value?.Id ?? string.Empty;
            if (Schedule.FilterDeptId != nextId)
            {
                Schedule.FilterDeptId = nextId;
                OnPropertyChanged(nameof(SelectedFilterDepartment));
            }
        }
    }

    public string GenerateBlockedReason => Schedule.HasTemplateSelection
        ? string.Empty
        : "请先在左侧选择一个已保存的排班模板后再批量发布号源。";

    public ICommand NewTemplateCommand { get; }
    public ICommand SaveTemplateCommand { get; }
    public ICommand GenerateSlotsCommand { get; }
    public ICommand FilterSlotsCommand { get; }
    public ICommand ClearFilterCommand { get; }
    public ICommand SelectTemplateCommand { get; }
    public ICommand UpdateSlotCapacityCommand { get; }
    public ICommand ToggleSlotCommand { get; }

    public void Attach()
    {
        if (!_subscribed)
        {
            Schedule.PropertyChanged += OnSchedulePropertyChanged;
            _subscribed = true;
        }
        _ = Schedule.EnsureLoadedAsync();
        RaiseCommandStates();
    }

    public void Detach()
    {
        if (_subscribed)
        {
            Schedule.PropertyChanged -= OnSchedulePropertyChanged;
            _subscribed = false;
        }
    }

    public bool OnShortcut(string keyGesture)
    {
        switch (keyGesture)
        {
            case "F2":
                ExecuteNewTemplate();
                return true;
            case "F3":
            case "F5":
                ExecuteFilterSlots();
                return true;
            case "Ctrl+S":
                ExecuteSaveTemplate();
                return true;
            default:
                return false;
        }
    }

    private void OnSchedulePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ScheduleWorkstationViewModel.FilterDeptId)
            or nameof(ScheduleWorkstationViewModel.FilterDepartment))
        {
            OnPropertyChanged(nameof(SelectedFilterDepartment));
        }

        if (e.PropertyName is nameof(ScheduleWorkstationViewModel.IsBusy)
            or nameof(ScheduleWorkstationViewModel.SelectedTemplate)
            or nameof(ScheduleWorkstationViewModel.HasTemplateSelection))
        {
            RaiseCommandStates();
            OnPropertyChanged(nameof(GenerateBlockedReason));
        }
    }

    private void RaiseCommandStates()
    {
        (SaveTemplateCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (GenerateSlotsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (FilterSlotsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ClearFilterCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void ExecuteNewTemplate()
    {
        Schedule.NewTemplate();
        FocusTemplateNameAction?.Invoke();
        _shell?.ShowToast("已切换至新建排班模板表单。", "Info");
    }

    private void ExecuteSelectTemplate(ScheduleTemplateItem? item)
    {
        if (item == null) return;
        Schedule.SelectTemplate(item);
    }

    private async void ExecuteSaveTemplate()
    {
        await Schedule.SaveTemplateAsync();
        NotifyResult("排班模板保存");
    }

    private void ConfirmGenerateSlots()
    {
        if (!Schedule.HasTemplateSelection || Schedule.SelectedTemplate == null)
        {
            _shell?.ShowToast(GenerateBlockedReason, "Warning");
            return;
        }

        _shell?.RequestConfirm(
            "批量发布号源确认",
            $"确定按模板【{Schedule.SelectedTemplate.TemplateName}】批量生成 {Schedule.GenerateStartDate} 至 {Schedule.GenerateEndDate} 的门诊号源？",
            "确认生成",
            async () =>
            {
                await Schedule.GenerateAsync();
                NotifyResult("号源发布");
            });
    }

    private async void ExecuteFilterSlots()
    {
        await Schedule.FilterAsync();
        if (!Schedule.HasError)
        {
            _shell?.ShowToast($"已筛选 {Schedule.Slots.Count} 条门诊号源。", "Info");
        }
        else
        {
            _shell?.ShowToast(Schedule.ErrorMessage, "Error");
        }
    }

    private async void ExecuteClearFilter()
    {
        Schedule.FilterDeptId = string.Empty;
        Schedule.QueryDate = string.Empty;
        OnPropertyChanged(nameof(SelectedFilterDepartment));
        await Schedule.FilterAsync();
        _shell?.ShowToast($"已显示全院 {Schedule.Slots.Count} 条号源。", "Info");
    }

    private async void ExecuteUpdateCapacity(ScheduleSlotItem? slot)
    {
        if (slot == null) return;
        await Schedule.UpdateSlotCapacityAsync(slot, slot.CapacityEditText);
        NotifyResult("号源容量更新");
    }

    private void ConfirmToggleSlot(ScheduleSlotItem? slot)
    {
        if (slot == null) return;
        if (slot.IsStopped)
        {
            _ = ExecuteToggleSlotAsync(slot);
            return;
        }

        _shell?.RequestConfirm(
            "门诊停诊确认",
            $"确定将 {slot.DateText} {slot.TimeText} {slot.DoctorName} 的号源设为停诊？已挂号数：{slot.ReservedCount}。",
            "确认停诊",
            async () => await ExecuteToggleSlotAsync(slot));
    }

    private async Task ExecuteToggleSlotAsync(ScheduleSlotItem slot)
    {
        await Schedule.ToggleSlotAsync(slot);
        NotifyResult(slot.IsStopped ? "停诊状态变更" : "开诊恢复");
    }

    private void NotifyResult(string actionName)
    {
        if (Schedule.HasError)
        {
            _shell?.ShowToast(Schedule.ErrorMessage, "Error");
        }
        else if (!string.IsNullOrWhiteSpace(Schedule.Notice))
        {
            _shell?.ShowToast(Schedule.Notice, "Success");
        }
        else
        {
            _shell?.ShowToast($"{actionName}完成。", "Success");
        }
    }
}
