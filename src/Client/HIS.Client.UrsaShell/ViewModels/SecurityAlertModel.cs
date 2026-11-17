using System;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class SecurityAlertModel : ClientModelBase
{
	private string _id = string.Empty;
	private string _alertNo = string.Empty;
	private string _alertType = string.Empty;
	private string _severity = "HIGH";
	private string _sourceIp = string.Empty;
	private string _userId = string.Empty;
	private string _operatorName = string.Empty;
	private string _targetResource = string.Empty;
	private string _description = string.Empty;
	private string _threatIndicatorsJson = string.Empty;
	private string _status = "ACTIVE";
	private string _resolvedBy = string.Empty;
	private string _resolutionNote = string.Empty;
	private long _createdAtMs;

	public string Id
	{
		get => _id;
		set => SetField(ref _id, value, nameof(Id));
	}

	public string AlertNo
	{
		get => _alertNo;
		set => SetField(ref _alertNo, value, nameof(AlertNo));
	}

	public string AlertType
	{
		get => _alertType;
		set => SetField(ref _alertType, value, nameof(AlertType));
	}

	public string Severity
	{
		get => _severity;
		set
		{
			if (SetField(ref _severity, value, nameof(Severity)))
			{
				OnPropertyChanged(nameof(SeverityBadgeClass));
			}
		}
	}

	public string SourceIp
	{
		get => _sourceIp;
		set => SetField(ref _sourceIp, value, nameof(SourceIp));
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

	public string TargetResource
	{
		get => _targetResource;
		set => SetField(ref _targetResource, value, nameof(TargetResource));
	}

	public string Description
	{
		get => _description;
		set => SetField(ref _description, value, nameof(Description));
	}

	public string ThreatIndicatorsJson
	{
		get => _threatIndicatorsJson;
		set => SetField(ref _threatIndicatorsJson, value, nameof(ThreatIndicatorsJson));
	}

	public string Status
	{
		get => _status;
		set
		{
			if (SetField(ref _status, value, nameof(Status)))
			{
				OnPropertyChanged(nameof(StatusBadgeClass));
				OnPropertyChanged(nameof(IsActive));
			}
		}
	}

	public string ResolvedBy
	{
		get => _resolvedBy;
		set => SetField(ref _resolvedBy, value, nameof(ResolvedBy));
	}

	public string ResolutionNote
	{
		get => _resolutionNote;
		set => SetField(ref _resolutionNote, value, nameof(ResolutionNote));
	}

	public long CreatedAtMs
	{
		get => _createdAtMs;
		set
		{
			if (SetField(ref _createdAtMs, value, nameof(CreatedAtMs)))
			{
				OnPropertyChanged(nameof(FormattedTime));
			}
		}
	}

	public bool IsActive => Status == "ACTIVE";

	public string SeverityBadgeClass => Severity switch
	{
		"CRITICAL" => "Danger",
		"HIGH" => "Warning",
		"WARNING" => "Warning",
		_ => "Info"
	};

	public string StatusBadgeClass => Status == "ACTIVE" ? "Danger" : "Success";

	public string FormattedTime => CreatedAtMs > 0
		? DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtMs).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
		: "-";
}
