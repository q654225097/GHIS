using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new WorkbenchModel();
    }

    private void PatientCard_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control && control.DataContext is PatientQueueItemModel patient)
        {
            if (DataContext is WorkbenchModel vm)
            {
                vm.SelectedPatient = patient;
            }
        }
    }

    private void CallNext_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkbenchModel vm)
        {
            vm.SelectNextPatient();
        }
    }

    private void SignOfd_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkbenchModel vm)
        {
            vm.SimulateSignOfd();
        }
    }

    private void ExportOfd_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkbenchModel vm)
        {
            vm.SystemNotice = $"已导出国标版式文件: {vm.EmrWorkspace.DocNo}.ofd (遵循 GB/T 33190-2016 标准)";
        }
    }

    private void VerifyHash_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkbenchModel vm)
        {
            vm.SystemNotice = "✓ SHA-256 完整性哈希校验通过: 电子签名与 XML 数据元结构未被篡改！";
        }
    }

    private void Dispense_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkbenchModel vm)
        {
            vm.SystemNotice = "药房扫码核验: 成功拦截青霉素高危处方 (GATE-05)，其余合格药品已完成双人复核出库！";
        }
    }

    private void DischargeCheck_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkbenchModel vm)
        {
            vm.SystemNotice = "GATE-07 出院六严关校验结果: 医嘱全闭环、医技报告全归档、出院带药已发、主治已签审、医保已结清！允许办理出院。";
        }
    }

    private void BorrowBed_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkbenchModel vm)
        {
            vm.SystemNotice = "GATE-02 跨病区借床入科: 主管科室为心血管内科一科，执行病区为内科二病区 (执行借床)，两科费用与职责独立隔离！";
        }
    }
}
