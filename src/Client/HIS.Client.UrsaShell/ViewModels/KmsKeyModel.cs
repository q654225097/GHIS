using System;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class KmsKeyModel : ClientModelBase
{
	private string _id = string.Empty;
	private string _keyName = string.Empty;
	private string _algorithm = string.Empty;
	private int _version = 1;
	private string _state = "ACTIVE";
	private short _hsmProtected = 1;
	private string _keyFingerprint = string.Empty;
	private long _expiresAtMs;
	private long? _rotatedAtMs;
	private string _description = string.Empty;

	public string Id
	{
		get => _id;
		set => SetField(ref _id, value, nameof(Id));
	}

	public string KeyName
	{
		get => _keyName;
		set => SetField(ref _keyName, value, nameof(KeyName));
	}

	public string Algorithm
	{
		get => _algorithm;
		set => SetField(ref _algorithm, value, nameof(Algorithm));
	}

	public int Version
	{
		get => _version;
		set => SetField(ref _version, value, nameof(Version));
	}

	public string State
	{
		get => _state;
		set
		{
			if (SetField(ref _state, value, nameof(State)))
			{
				OnPropertyChanged(nameof(StateBadgeClass));
				OnPropertyChanged(nameof(IsActive));
			}
		}
	}

	public short HsmProtected
	{
		get => _hsmProtected;
		set => SetField(ref _hsmProtected, value, nameof(HsmProtected));
	}

	public string KeyFingerprint
	{
		get => _keyFingerprint;
		set => SetField(ref _keyFingerprint, value, nameof(KeyFingerprint));
	}

	public long ExpiresAtMs
	{
		get => _expiresAtMs;
		set
		{
			if (SetField(ref _expiresAtMs, value, nameof(ExpiresAtMs)))
			{
				OnPropertyChanged(nameof(FormattedExpiry));
			}
		}
	}

	public long? RotatedAtMs
	{
		get => _rotatedAtMs;
		set => SetField(ref _rotatedAtMs, value, nameof(RotatedAtMs));
	}

	public string Description
	{
		get => _description;
		set => SetField(ref _description, value, nameof(Description));
	}

	public bool IsActive => State == "ACTIVE";

	public string StateBadgeClass => State switch
	{
		"ACTIVE" => "Success",
		"ROTATING" => "Warning",
		"RETIRED" => "Secondary",
		"DESTROYED" => "Danger",
		_ => "Info"
	};

	public string FormattedExpiry => ExpiresAtMs > 0
		? DateTimeOffset.FromUnixTimeMilliseconds(ExpiresAtMs).ToLocalTime().ToString("yyyy-MM-dd")
		: "永久";
}
