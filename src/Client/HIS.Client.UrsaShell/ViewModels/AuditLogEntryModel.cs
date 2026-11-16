using System;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class AuditLogEntryModel : ClientModelBase
{
	private string _id = string.Empty;
	private string _eventType = string.Empty;
	private string _targetEntity = string.Empty;
	private string _targetId = string.Empty;
	private string _actionDetail = string.Empty;
	private string _userId = string.Empty;
	private string _operatorName = string.Empty;
	private string _deptId = string.Empty;
	private string _currentHash = string.Empty;
	private string _prevHash = string.Empty;
	private long _timestampMs;

	public string Id
	{
		get => _id;
		set => SetField(ref _id, value, nameof(Id));
	}

	public string EventType
	{
		get => _eventType;
		set => SetField(ref _eventType, value, nameof(EventType));
	}

	public string TargetEntity
	{
		get => _targetEntity;
		set => SetField(ref _targetEntity, value, nameof(TargetEntity));
	}

	public string TargetId
	{
		get => _targetId;
		set => SetField(ref _targetId, value, nameof(TargetId));
	}

	public string ActionDetail
	{
		get => _actionDetail;
		set => SetField(ref _actionDetail, value, nameof(ActionDetail));
	}

	public string UserId
	{
		get => _userId;
		set => SetField(ref _userId, value, nameof(UserId));
	}

	public string OperatorName
	{
		get => _operatorName;
		set => SetField(ref _operatorName, value, nameof(OperatorName));
	}

	public string DeptId
	{
		get => _deptId;
		set => SetField(ref _deptId, value, nameof(DeptId));
	}

	public string CurrentHash
	{
		get => _currentHash;
		set
		{
			if (SetField(ref _currentHash, value, nameof(CurrentHash)))
			{
				OnPropertyChanged(nameof(ShortHash));
			}
		}
	}

	public string PrevHash
	{
		get => _prevHash;
		set => SetField(ref _prevHash, value, nameof(PrevHash));
	}

	public long TimestampMs
	{
		get => _timestampMs;
		set
		{
			if (SetField(ref _timestampMs, value, nameof(TimestampMs)))
			{
				OnPropertyChanged(nameof(FormattedTime));
			}
		}
	}

	public string FormattedTime => TimestampMs > 0
		? DateTimeOffset.FromUnixTimeMilliseconds(TimestampMs).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
		: string.Empty;

	public string ShortHash => string.IsNullOrWhiteSpace(CurrentHash)
		? "-"
		: (CurrentHash.Length > 16 ? $"{CurrentHash[..8]}...{CurrentHash[^8..]}" : CurrentHash);

	public string EventBadgeClass => EventType switch
	{
		"LOGIN_FAILED" or "USER_LOCKED" or "PERMISSION_DENIED" => "Danger",
		"PATIENT_MERGE" or "PWD_CHANGE" or "SYS_ROLE_UPDATE" => "Warning",
		"FIN_SETTLE" or "PHM_DISPENSE_AND_CLOSE" or "IPD_PREPAYMENT" => "Success",
		_ => "Primary"
	};
}

