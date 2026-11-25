namespace HIS.Client.UrsaShell.ViewModels;

public partial class InpatientOrderModel
{
    public string Id { get; set; } = string.Empty;
    public string EncounterId { get; set; } = string.Empty;
    public string OrderSubType { get; set; } = "NORMAL";
    public string? ParentOrderId { get; set; }
    public int GroupNoInt { get; set; } = 1;
    public string ItemCode { get; set; } = string.Empty;
    public string OrderCategoryCode { get; set; } = "DRUG";
    public string ExecTargetDeptId { get; set; } = string.Empty;
    public bool IsServerBacked { get; set; }
}
