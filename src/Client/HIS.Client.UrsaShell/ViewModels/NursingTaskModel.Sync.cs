namespace HIS.Client.UrsaShell.ViewModels;

public partial class NursingTaskModel
{
    public string PlanId { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string EncounterId { get; set; } = string.Empty;
    public string PdaScanToken { get; set; } = string.Empty;
    public string DualCheckNurseId { get; set; } = string.Empty;
    public bool IsServerBacked { get; set; }
}
