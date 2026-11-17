using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.OfdEngine;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Services;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.Stations.OpdDoctor;

namespace HIS.Client.UrsaShell.ViewModels;

public class EmrEditorWorkspaceModel : ClientModelBase
{
	public EmrEditorWorkspaceModel()
	{
		SignOfdCommand = new AsyncRelayCommand(SignOfdAsync, () => !IsBusy);
		ExportOfdCommand = new AsyncRelayCommand(ExportOfdAsync, () => !IsBusy);
	}

	public AsyncRelayCommand SignOfdCommand { get; }
	public AsyncRelayCommand ExportOfdCommand { get; }
	public string? LatestArchiveId { get; set; }
	// Kept at workspace level so replacing a template cannot leave old field handlers
	// holding the shared preview debounce state.
	internal System.ComponentModel.PropertyChangedEventHandler? DynamicFieldChanged { get; set; }

	private string _docNo = string.Empty;

	private string _docTitle = "门诊病历";

	private string _patientName = string.Empty;

	private string _gender = string.Empty;

	private int _age;

	private string _chiefComplaint = string.Empty;

	private string _presentIllness = string.Empty;

	private string _diagnosis = string.Empty;

	private string _prostateExam = string.Empty;

	private int _pregnancyCount = 0;

	private string _qcStatus = "未质控";

	private string _ofdStandard = "GB/T 33190-2016";

	private string _ofdStatus = "尚未审签";

	private string? _ofdNo;

	private bool _isBusy;

	private string? _selectedTemplateId;

	private string _templateName = "请选择病历模板";

	private int _requiredSignLevels = 1;

	private int _completedSignLevels;

	private string _templateHint = string.Empty;

	private string _signatureProgressText = "尚未审签";

	public string DocNo
	{
		get
		{
			return _docNo;
		}
		set
		{
			SetField(ref _docNo, value, "DocNo");
		}
	}

	public string DocTitle
	{
		get
		{
			return _docTitle;
		}
		set
		{
			SetField(ref _docTitle, value, "DocTitle");
		}
	}

	public string PatientName
	{
		get
		{
			return _patientName;
		}
		set
		{
			SetField(ref _patientName, value, "PatientName");
		}
	}

	public string Gender
	{
		get
		{
			return _gender;
		}
		set
		{
			SetField(ref _gender, value, "Gender");
		}
	}

	public int Age
	{
		get
		{
			return _age;
		}
		set
		{
			SetField(ref _age, value, "Age");
		}
	}

	public string ChiefComplaint
	{
		get
		{
			return _chiefComplaint;
		}
		set
		{
			SetField(ref _chiefComplaint, value, "ChiefComplaint");
		}
	}

	public string PresentIllness
	{
		get
		{
			return _presentIllness;
		}
		set
		{
			SetField(ref _presentIllness, value, "PresentIllness");
		}
	}

	public string Diagnosis
	{
		get
		{
			return _diagnosis;
		}
		set
		{
			SetField(ref _diagnosis, value, "Diagnosis");
		}
	}

	public string ProstateExam
	{
		get
		{
			return _prostateExam;
		}
		set
		{
			SetField(ref _prostateExam, value, "ProstateExam");
		}
	}

	public int PregnancyCount
	{
		get
		{
			return _pregnancyCount;
		}
		set
		{
			SetField(ref _pregnancyCount, value, "PregnancyCount");
		}
	}

	public string QcStatus
	{
		get
		{
			return _qcStatus;
		}
		set
		{
			SetField(ref _qcStatus, value, "QcStatus");
		}
	}

	public string OfdStandard
	{
		get
		{
			return _ofdStandard;
		}
		set
		{
			SetField(ref _ofdStandard, value, "OfdStandard");
		}
	}

	public string OfdStatus
	{
		get
		{
			return _ofdStatus;
		}
		set
		{
			SetField(ref _ofdStatus, value, "OfdStatus");
		}
	}

	public string? OfdNo
	{
		get
		{
			return _ofdNo;
		}
		set
		{
			SetField(ref _ofdNo, value, "OfdNo");
		}
	}

	public bool IsBusy
	{
		get
		{
			return _isBusy;
		}
		set
		{
			SetField(ref _isBusy, value, "IsBusy");
		}
	}

	public ObservableCollection<OpdEmrFieldModel> Fields { get; } = new();

	public ObservableCollection<OpdEmrSectionModel> Sections { get; } = new();

	public string? SelectedTemplateId
	{
		get => _selectedTemplateId;
		set => SetField(ref _selectedTemplateId, value, "SelectedTemplateId");
	}

	public string TemplateName
	{
		get => _templateName;
		set => SetField(ref _templateName, value, "TemplateName");
	}

	public int RequiredSignLevels
	{
		get => _requiredSignLevels;
		set
		{
			int normalized = Math.Clamp(value, 1, 3);
			if (SetField(ref _requiredSignLevels, normalized, "RequiredSignLevels"))
			{
				OnPropertyChanged("RequiredSignLevelsText");
			}
		}
	}

	public string RequiredSignLevelsText => $"需 {RequiredSignLevels} 级审签";

	public int CompletedSignLevels
	{
		get => _completedSignLevels;
		set
		{
			int normalized = Math.Clamp(value, 0, 3);
			if (SetField(ref _completedSignLevels, normalized, "CompletedSignLevels"))
			{
				OnPropertyChanged("SignProgressText");
				OnPropertyChanged("CanSaveDraft");
				OnPropertyChanged("NeedsRegistration");
			}
		}
	}

	public string SignProgressText
	{
		get => _signatureProgressText;
		set => SetField(ref _signatureProgressText, value, "SignProgressText");
	}

	public string TemplateHint
	{
		get => _templateHint;
		set => SetField(ref _templateHint, value, "TemplateHint");
	}

	public bool CanSaveDraft => CompletedSignLevels < RequiredSignLevels;

	public bool NeedsRegistration => CompletedSignLevels < RequiredSignLevels;

	public void ResetFields()
	{
		foreach (OpdEmrFieldModel field in Fields)
		{
			if (DynamicFieldChanged != null)
			{
				field.PropertyChanged -= DynamicFieldChanged;
			}
		}
		Fields.Clear();
		Sections.Clear();
	}

	public async Task SignOfdAsync()
	{
		IsBusy = true;
		try
		{
			string signerId = string.IsNullOrWhiteSpace(SessionContext.Current.UserId) ? "USER-DOC-01" : SessionContext.Current.UserId;
			string signerName = string.IsNullOrWhiteSpace(SessionContext.Current.DisplayName)
				? (string.IsNullOrWhiteSpace(SessionContext.Current.UserName) ? "张建国 (主治医师)" : SessionContext.Current.UserName)
				: SessionContext.Current.DisplayName;
			int signLevel = Math.Clamp(SessionContext.Current.EmrSignLevel > 0 ? SessionContext.Current.EmrSignLevel : 1, 1, 3);

			if (string.IsNullOrWhiteSpace(DocNo))
			{
				DocNo = "EMR-" + DateTime.Now.ToString("yyyyMMddHHmmss");
			}

			var structuredElements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				["DE_CHIEF_COMPLAINT"] = ChiefComplaint,
				["DE_PRESENT_ILLNESS"] = PresentIllness,
				["DE_DIAGNOSIS"] = Diagnosis
			};
			if (!string.IsNullOrWhiteSpace(ProstateExam))
			{
				structuredElements["DE_PROSTATE_EXAM"] = ProstateExam;
			}
			if (PregnancyCount > 0)
			{
				structuredElements["DE_PREGNANCY_COUNT"] = PregnancyCount.ToString();
			}

			foreach (OpdEmrFieldModel field in Fields)
			{
				if (!string.IsNullOrWhiteSpace(field.Value))
				{
					structuredElements[field.ElementCode] = field.Value;
				}
			}

			using var client = AuthenticatedHttpClientFactory.Create();
			var savePayload = new
			{
				DocNo = DocNo,
				Title = DocTitle,
				PatientId = string.IsNullOrWhiteSpace(PatientName) ? "PAT85061801" : PatientName,
				EncounterOrVisitId = "VISIT-OPD-DEMO",
				EmrScene = "OPD",
				TemplateId = SelectedTemplateId,
				StructuredElements = structuredElements,
				AuthorId = signerId,
				AuthorName = signerName
			};

			var saveResp = await client.PostAsJsonAsync("/api/v4/emr/documents", savePayload);
			string actualDocId = DocNo;
			if (saveResp.IsSuccessStatusCode)
			{
				var saveEnvelope = await saveResp.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>();
				if (saveEnvelope?.Data.ValueKind == JsonValueKind.Object && saveEnvelope.Data.TryGetProperty("id", out var idProp))
				{
					actualDocId = idProp.GetString() ?? DocNo;
				}
			}

			var signPayload = new
			{
				SignerId = signerId,
				SignerName = signerName,
				SignLevel = signLevel
			};
			var signResp = await client.PostAsJsonAsync($"/api/v4/emr/documents/{actualDocId}/sign-and-generate-ofd", signPayload);
			if (signResp.IsSuccessStatusCode)
			{
				var signEnvelope = await signResp.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>();
				if (signEnvelope?.Data.ValueKind == JsonValueKind.Object)
				{
					if (signEnvelope.Data.TryGetProperty("ofdNo", out var ofdNoProp))
					{
						OfdNo = ofdNoProp.GetString();
					}
					if (signEnvelope.Data.TryGetProperty("id", out var archiveIdProp))
					{
						LatestArchiveId = archiveIdProp.GetString();
					}
				}
				QcStatus = "质控通过";
				OfdStatus = "已生成国标 OFD (服务端真 CA/TSA 审签)";
				CompletedSignLevels = signLevel;
			}
			else
			{
				var errBody = await signResp.Content.ReadAsStringAsync();
				QcStatus = "审签失败";
				OfdStatus = $"服务端审签拒绝 ({(int)signResp.StatusCode}): {errBody}";
			}
		}
		catch (Exception ex)
		{
			QcStatus = "网络或服务异常";
			OfdStatus = $"审签请求失败: {ex.Message}";
			ClientDiagnosticsLog.Error("emr.sign", "Sign OFD failed", ex);
		}
		finally
		{
			IsBusy = false;
		}
	}

	public async Task ExportOfdAsync()
	{
		IsBusy = true;
		try
		{
			if (string.IsNullOrWhiteSpace(LatestArchiveId))
			{
				OfdStatus = "未找到已归档的 OFD 凭单，请先完成审签";
				return;
			}

			using var client = AuthenticatedHttpClientFactory.Create();
			var resp = await client.GetAsync($"/api/v4/emr/archives/{LatestArchiveId}/export-ofd");
			if (resp.IsSuccessStatusCode)
			{
				byte[] bytes = await resp.Content.ReadAsByteArrayAsync();
				string targetFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "OFD_Exports");
				Directory.CreateDirectory(targetFolder);
				string targetPath = Path.Combine(targetFolder, $"{(string.IsNullOrWhiteSpace(OfdNo) ? DocNo : OfdNo)}.ofd");
				await File.WriteAllBytesAsync(targetPath, bytes);
				OfdStatus = $"OFD 导出成功: {targetPath}";
			}
			else
			{
				OfdStatus = $"OFD 导出失败: 服务端响应 {(int)resp.StatusCode}";
			}
		}
		catch (Exception ex)
		{
			OfdStatus = $"OFD 导出异常: {ex.Message}";
			ClientDiagnosticsLog.Error("emr.export", "Export OFD failed", ex);
		}
		finally
		{
			IsBusy = false;
		}
	}
}
