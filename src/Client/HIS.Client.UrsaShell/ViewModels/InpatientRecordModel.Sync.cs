namespace HIS.Client.UrsaShell.ViewModels;

public partial class InpatientRecordModel
{
    public string EncounterId { get; set; } = string.Empty;
    public string AdmitDeptId { get; set; } = string.Empty;
    public string AdmitWardId { get; set; } = string.Empty;
    public string BedId { get; set; } = string.Empty;
    public string AttendingDoctorId { get; set; } = string.Empty;
    public string PrimaryNurseId { get; set; } = string.Empty;
    public long AdmittedAtMs { get; set; }
    public bool IsServerBacked { get; set; }

    public void NotifyFinanceChanged()
    {
        OnPropertyChanged(nameof(AvailableBalance));
        OnPropertyChanged(nameof(AvailableBalanceText));
        OnPropertyChanged(nameof(IsBalanceNegative));
        OnPropertyChanged(nameof(TotalCost));
        OnPropertyChanged(nameof(TotalCostDisplay));
        OnPropertyChanged(nameof(DepositTotal));
        OnPropertyChanged(nameof(DepositTotalDisplay));
    }
}
