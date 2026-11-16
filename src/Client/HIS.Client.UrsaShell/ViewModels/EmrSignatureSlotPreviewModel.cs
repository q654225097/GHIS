using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class EmrSignatureSlotPreviewModel : ClientModelBase
{
	public int Level { get; set; } = 1;

	public string RoleName { get; set; } = "经治医师";

	public string Label { get; set; } = "医师签名";

	public string SignerDisplay { get; set; } = "张建国 (EMP002)";

	public string SignedTimeText { get; set; } = "2026-11-05 15:48";

	public bool IsSigned { get; set; } = true;

	public string StatusBadge => IsSigned ? "✓ SM2已签" : "待审签";

	public string StatusBrush => IsSigned ? "#059669" : "#D97706";
}
