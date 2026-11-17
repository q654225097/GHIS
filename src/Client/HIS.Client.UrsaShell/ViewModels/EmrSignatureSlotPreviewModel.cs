using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class EmrSignatureSlotPreviewModel : ClientModelBase
{
	private int _level = 1;
	private string _roleName = "经治医师";
	private string _label = "医师签名";
	private string _signerDisplay = "张建国 (EMP002)";
	private string _signedTimeText = "2026-11-05 15:48";
	private bool _isSigned = true;
	private string? _caSignSerialNo = "CA-2026-SM2-EMP002";
	private bool _isSignatureVerified = true;

	public int Level
	{
		get => _level;
		set => SetField(ref _level, value);
	}

	public string RoleName
	{
		get => _roleName;
		set => SetField(ref _roleName, value);
	}

	public string Label
	{
		get => _label;
		set => SetField(ref _label, value);
	}

	public string SignerDisplay
	{
		get => _signerDisplay;
		set => SetField(ref _signerDisplay, value);
	}

	public string SignedTimeText
	{
		get => _signedTimeText;
		set => SetField(ref _signedTimeText, value);
	}

	public bool IsSigned
	{
		get => _isSigned;
		set
		{
			if (SetField(ref _isSigned, value))
			{
				OnPropertyChanged(nameof(StatusBadge));
				OnPropertyChanged(nameof(StatusBrush));
			}
		}
	}

	public string? CaSignSerialNo
	{
		get => _caSignSerialNo;
		set
		{
			if (SetField(ref _caSignSerialNo, value))
			{
				OnPropertyChanged(nameof(StatusBadge));
				OnPropertyChanged(nameof(StatusBrush));
			}
		}
	}

	public bool IsSignatureVerified
	{
		get => _isSignatureVerified;
		set
		{
			if (SetField(ref _isSignatureVerified, value))
			{
				OnPropertyChanged(nameof(StatusBadge));
				OnPropertyChanged(nameof(StatusBrush));
			}
		}
	}

	public string StatusBadge
	{
		get
		{
			if (IsSigned && !string.IsNullOrWhiteSpace(CaSignSerialNo) && IsSignatureVerified)
			{
				return "✓ SM2已签";
			}
			if (IsSigned)
			{
				return "已签(待验签)";
			}
			return "待审签";
		}
	}

	public string StatusBrush
	{
		get
		{
			if (IsSigned && !string.IsNullOrWhiteSpace(CaSignSerialNo) && IsSignatureVerified)
			{
				return "#059669";
			}
			if (IsSigned)
			{
				return "#2563EB";
			}
			return "#D97706";
		}
	}
}
