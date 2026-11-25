using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Stations.OpdDoctor;

namespace HIS.Client.UrsaShell.ViewModels;

// New OPD station actions share WorkbenchModel's existing HTTP setup, DTOs and prescription helpers.
public partial class WorkbenchModel
{
	public async Task<OpdActionOutcome> ReloadSelectedOpdClinicalStateAsync()
	{
		PatientQueueItemModel? patient = SelectedPatient;
		if (patient == null)
		{
			return OpdActionOutcome.Failure("请先选择或叫号一位门诊患者");
		}
		await LoadCurrentPatientClinicalDataAsync();
		if (!string.Equals(patient.VisitId, _currentPrescriptionVisitId, StringComparison.OrdinalIgnoreCase))
		{
			return OpdActionOutcome.Failure("患者切换期间资料读取已取消，请重新选择");
		}
		return OpdActionOutcome.Success("已读取服务端最新病历与处方状态");
	}

	public async Task<OpdActionOutcome> CallNextOpdPatientAsync(PatientQueueItemModel? preferredPatient = null)
	{
		if (preferredPatient != null)
		{
			return await StartConsultationForPatientAsync(preferredPatient);
		}

		string deptId = SessionContext.Current.ActiveDeptId;
		if (string.IsNullOrWhiteSpace(deptId))
		{
			deptId = SelectedPatient?.OpdDeptId ?? PatientQueue.FirstOrDefault(x => x.IsWaiting)?.OpdDeptId ?? string.Empty;
		}
		if (string.IsNullOrWhiteSpace(deptId))
		{
			return OpdActionOutcome.Failure("当前登录科室为空，无法呼叫下一位患者");
		}

		string clinicRoomId = SelectedPatient?.ClinicRoomId ?? string.Empty;
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			string uri = "/api/v4/opd/queue/call-next?opdDeptId=" + Uri.EscapeDataString(deptId);
			if (!string.IsNullOrWhiteSpace(clinicRoomId))
			{
				uri += "&clinicRoomId=" + Uri.EscapeDataString(clinicRoomId);
			}
			using HttpResponseMessage response = await http.PostAsync(uri, content: null);
			ClientApiEnvelope<OpdVisitDto>? envelope =
				await response.Content.ReadFromJsonAsync<ClientApiEnvelope<OpdVisitDto>>(JsonOptions);
			if (envelope is not { Code: 200, Data: not null })
			{
				return OpdActionOutcome.Failure(envelope?.Message ?? "服务端未返回被呼患者");
			}

			await LoadOpdQueueAsync();
			PatientQueueItemModel? called = FindPatientByVisitId(envelope.Data.Id)
				?? PatientQueue.FirstOrDefault(x => string.Equals(x.VisitNo, envelope.Data.VisitNo, StringComparison.OrdinalIgnoreCase));
			if (called == null)
			{
				return OpdActionOutcome.Failure("患者已呼叫，但队列刷新后未找到该就诊，请重新读取队列");
			}
			await SelectPatientAsync(called);
			SystemNotice = $"已呼叫 [{called.PatientName}] (#{called.QueueNo})，就诊号 {called.VisitNo}";
			return OpdActionOutcome.Success($"已呼叫 {called.PatientName}，可开始录入诊断");
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
		{
			return OpdActionOutcome.Failure("呼叫下一位失败：" + ex.Message);
		}
	}

	public async Task<OpdActionOutcome> RecallOpdPatientAsync()
	{
		PatientQueueItemModel? patient = SelectedPatient;
		if (patient == null)
		{
			return OpdActionOutcome.Failure("请先选择或叫号需要重呼的门诊患者");
		}

		string clinicRoomId = patient.ClinicRoomId ?? string.Empty;
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			string uri = $"/api/v4/opd/queue/recall?visitId={Uri.EscapeDataString(patient.VisitId)}";
			if (!string.IsNullOrWhiteSpace(clinicRoomId))
			{
				uri += $"&clinicRoomId={Uri.EscapeDataString(clinicRoomId)}";
			}
			using HttpResponseMessage response = await http.PostAsync(uri, content: null);
			ClientApiEnvelope<OpdVisitDto>? envelope =
				await response.Content.ReadFromJsonAsync<ClientApiEnvelope<OpdVisitDto>>(JsonOptions);
			if (envelope is not { Code: 200 })
			{
				return OpdActionOutcome.Failure(envelope?.Message ?? "重呼失败");
			}
			SystemNotice = $"已再次呼叫 [{patient.PatientName}] (#{patient.QueueNo})";
			return OpdActionOutcome.Success($"已重呼患者 {patient.PatientName}");
		}
		catch (Exception ex)
		{
			return OpdActionOutcome.Failure("重呼失败：" + ex.Message);
		}
	}

	public async Task<OpdActionOutcome> PassOpdPatientAsync()
	{
		PatientQueueItemModel? patient = SelectedPatient;
		if (patient == null)
		{
			return OpdActionOutcome.Failure("请先选择需要过号的门诊患者");
		}

		try
		{
			using HttpClient http = CreateConfigHttpClient();
			string uri = $"/api/v4/opd/queue/pass?visitId={Uri.EscapeDataString(patient.VisitId)}";
			using HttpResponseMessage response = await http.PostAsync(uri, content: null);
			ClientApiEnvelope<bool>? envelope =
				await response.Content.ReadFromJsonAsync<ClientApiEnvelope<bool>>(JsonOptions);
			if (envelope is not { Code: 200 })
			{
				return OpdActionOutcome.Failure(envelope?.Message ?? "过号失败");
			}

			await LoadOpdQueueAsync();
			SelectedPatient = null;
			SystemNotice = $"患者 [{patient.PatientName}] (#{patient.QueueNo}) 已标记过号顺延";
			return OpdActionOutcome.Success($"患者 {patient.PatientName} 已标记过号");
		}
		catch (Exception ex)
		{
			return OpdActionOutcome.Failure("过号操作失败：" + ex.Message);
		}
	}

	private async Task<OpdActionOutcome> StartConsultationForPatientAsync(PatientQueueItemModel patient)
	{
		if (patient.IsCompleted)
		{
			return OpdActionOutcome.Failure("该患者本次就诊已完成");
		}
		bool alreadySelected = string.Equals(patient.VisitId, _currentPrescriptionVisitId, StringComparison.OrdinalIgnoreCase);
		if (!alreadySelected)
		{
			await SelectPatientAsync(patient);
		}
		if (patient.IsInProgress)
		{
			return OpdActionOutcome.Success($"已切换至就诊中患者 {patient.PatientName}");
		}
		await StartConsultationForSelectedPatientAsync();
		PatientQueueItemModel? refreshed = SelectedPatient;
		if (refreshed != null && refreshed.IsInProgress)
		{
			await ReloadSelectedOpdClinicalStateAsync();
			return OpdActionOutcome.Success($"已接诊 {patient.PatientName}");
		}
		return OpdActionOutcome.Failure(string.IsNullOrWhiteSpace(SystemNotice) ? "接诊未成功" : SystemNotice);
	}

	public async Task<OpdActionOutcome> SaveOpdDiagnosisAsync(string diagnosisName)
	{
		PatientQueueItemModel? patient = SelectedPatient;
		if (patient == null)
		{
			return OpdActionOutcome.Failure("请先选择门诊患者");
		}
		if (string.IsNullOrWhiteSpace(diagnosisName))
		{
			return OpdActionOutcome.Failure("请先录入初步诊断");
		}
		if (!patient.IsInProgress)
		{
			return OpdActionOutcome.Failure("请先叫号接诊患者");
		}
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			using HttpResponseMessage response = await http.PutAsJsonAsync(
				"/api/v4/opd/visits/" + Uri.EscapeDataString(patient.VisitId) + "/diagnosis",
				new { PrimaryDiagCode = DiagnosisCode ?? string.Empty, PrimaryDiagName = diagnosisName.Trim() },
				JsonOptions);
			ClientApiEnvelope<OpdVisitDto>? envelope =
				await response.Content.ReadFromJsonAsync<ClientApiEnvelope<OpdVisitDto>>(JsonOptions);
			if (envelope is not { Code: 200, Data: not null })
			{
				return OpdActionOutcome.Failure(envelope?.Message ?? "诊断保存失败：服务端未返回结果");
			}
			DiagnosisName = diagnosisName.Trim();
			EmrWorkspace.Diagnosis = diagnosisName.Trim();
			SystemNotice = "门诊初步诊断已保存";
			NotifyOpdWorkflowStateChanged();
			return OpdActionOutcome.Success("初步诊断已保存");
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
		{
			return OpdActionOutcome.Failure("诊断保存失败：" + ex.Message);
		}
	}

	public async Task<OpdActionOutcome> SaveAndSignOpdEmrAsync()
	{
		PatientQueueItemModel? patient = SelectedPatient;
		if (patient == null)
		{
			return OpdActionOutcome.Failure("请先选择接诊患者");
		}
		if (!patient.IsInProgress)
		{
			return OpdActionOutcome.Failure("请先叫号接诊患者");
		}
		if (!SessionContext.Current.CanSignEmr)
		{
			return OpdActionOutcome.Failure("当前身份没有病历审签权限");
		}
		if (!ValidateOpdEmrFields())
		{
			return OpdActionOutcome.Failure(OpdEmrValidationSummary);
		}

		string diagnosisName = ResolveOpdDiagnosisName();
		if (string.IsNullOrWhiteSpace(diagnosisName))
		{
			return OpdActionOutcome.Failure("请先在病历中填写初步诊断");
		}
		OpdActionOutcome diagnosis = await SaveOpdDiagnosisAsync(diagnosisName);
		if (!diagnosis.Succeeded)
		{
			return diagnosis;
		}

		try
		{
			OpdActionOutcome draft = await SaveOpdEmrDraftAsync();
			if (!draft.Succeeded)
			{
				return draft;
			}

			using HttpClient http = CreateConfigHttpClient();
			EmrDocumentDto? current = await ReadOpdEmrDocumentAsync(http, patient.VisitId);
			if (current == null)
			{
				return OpdActionOutcome.Failure("病历已保存，但重新读取服务端文档失败");
			}
			if (current.Status < 1 || current.Status == 5 || string.IsNullOrWhiteSpace(current.LatestOfdArchiveId ?? current.OfdArchiveId))
			{
				using HttpResponseMessage signResponse = await http.PostAsJsonAsync(
					"/api/v4/emr/documents/" + Uri.EscapeDataString(current.Id) + "/sign-and-generate-ofd",
					new { SignerId = CurrentOperatorId, SignerName = CurrentOperatorName, SignLevel = 1 },
					JsonOptions);
				ClientApiEnvelope<OfdArchiveDto>? signEnvelope =
					await signResponse.Content.ReadFromJsonAsync<ClientApiEnvelope<OfdArchiveDto>>(JsonOptions);
				if (signEnvelope is not { Code: 200, Data: not null })
				{
					EmrWorkspace.OfdStatus = "草稿已保存；审签未完成";
					return OpdActionOutcome.Failure(
						"病历审签失败：" + (signEnvelope?.Message ?? "服务端未返回归档结果"));
				}
				_currentOfdArchiveId = signEnvelope.Data.Id;
			}

			EmrDocumentDto? verified = await ReadOpdEmrDocumentAsync(http, patient.VisitId);
			if (verified == null)
			{
				return OpdActionOutcome.Failure("审签后重新读取病历失败");
			}
			ApplyOpdDocument(verified);
			QueueOpdPrintPreview();
			bool signed = verified.Status >= 1 && !string.IsNullOrWhiteSpace(verified.LatestOfdArchiveId ?? verified.OfdArchiveId);
			patient.SetEmrSigned(signed);
			NotifyOpdWorkflowStateChanged();
			SystemNotice = signed
				? $"门诊病历已完成签署归档，归档号 {EmrWorkspace.OfdNo ?? _currentOfdArchiveId}"
				: $"门诊病历已保存，当前审签状态 {GetOpdEmrStatusText(verified.Status)}";
			return signed
				? OpdActionOutcome.Success($"病历已签署归档，编号 {EmrWorkspace.OfdNo ?? _currentOfdArchiveId}")
				: OpdActionOutcome.Failure($"病历已保存，但尚未完成签署：{GetOpdEmrStatusText(verified.Status)}");
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
		{
			return OpdActionOutcome.Failure("病历保存或审签失败：" + ex.Message);
		}
	}

	public async Task<OpdActionOutcome> PreCheckCurrentPrescriptionAsync()
	{
		PatientQueueItemModel? patient = SelectedPatient;
		if (patient == null)
		{
			return OpdActionOutcome.Failure("请先选择接诊患者");
		}
		List<PrescriptionItemModel> items = Prescriptions.Where(x => !x.IsDeleted).ToList();
		if (items.Count == 0)
		{
			return OpdActionOutcome.Failure("处方暂无有效药品明细，请先加入药品");
		}

		try
		{
			using HttpClient http = CreateConfigHttpClient();
			var body = new
			{
				PatientId = patient.PatientId,
				PrescribeLevel = SessionContext.Current.PrescribeLevel,
				Items = items.Select(ToServerItem).ToList()
			};

			using HttpResponseMessage response = await http.PostAsJsonAsync("/api/v4/opd/prescriptions/pre-check", body, JsonOptions);
			ClientApiEnvelope<JsonElement>? envelope =
				await response.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOptions);

			if (envelope is not { Code: 200 })
			{
				string msg = envelope?.Message ?? "前置审方服务响应失败";
				SystemNotice = msg;
				return OpdActionOutcome.Failure("前置审方拦截：" + msg);
			}

			string summary = envelope.Message ?? "前置审方完成";
			SystemNotice = summary;
			return OpdActionOutcome.Success(summary);
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
		{
			return OpdActionOutcome.Failure("审方网络请求失败：" + ex.Message);
		}
	}

	public async Task<OpdActionOutcome> SubmitCurrentPrescriptionAsync()
	{
		PatientQueueItemModel? patient = SelectedPatient;
		if (patient == null)
		{
			return OpdActionOutcome.Failure("请先选择接诊患者");
		}
		if (!patient.IsInProgress)
		{
			return OpdActionOutcome.Failure("请先叫号接诊患者");
		}
		List<PrescriptionItemModel> editedItems = Prescriptions
			.Where(x => x.IsEditing && !x.IsDeleted)
			.ToList();
		foreach (PrescriptionItemModel item in editedItems)
		{
			await CommitPrescriptionItemAsync(item);
			if (item.IsEditing)
			{
				return OpdActionOutcome.Failure($"药品 [{item.DrugName}] 保存失败：" + SystemNotice);
			}
		}

		List<PrescriptionItemModel> items = Prescriptions.Where(x => !x.IsDeleted).ToList();
		if (items.Count == 0)
		{
			return OpdActionOutcome.Failure("请先添加处方药品，或完成前确认本次无处方");
		}
		if (HasBlockedPrescription)
		{
			return OpdActionOutcome.Failure("处方含过敏或皮试阻断项，请删除或替换后再提交");
		}
		if (items.Any(x => !x.IsPersisted))
		{
			return OpdActionOutcome.Failure("处方中有药品未完成后台保存，请逐行保存后再提交");
		}

		try
		{
			EnsurePrescriptionResult ensured = await EnsurePrescriptionAsync(patient.VisitId);
			if (string.IsNullOrWhiteSpace(ensured.RxId))
			{
				return OpdActionOutcome.Failure("处方未保存到服务端：" + SystemNotice);
			}
			using HttpClient http = CreateConfigHttpClient();
			using HttpResponseMessage response = await http.PostAsync(
				"/api/v4/opd/prescriptions/" + Uri.EscapeDataString(ensured.RxId) + "/submit",
				content: null);
			ClientApiEnvelope<PrescriptionDto>? envelope =
				await response.Content.ReadFromJsonAsync<ClientApiEnvelope<PrescriptionDto>>(JsonOptions);
			if (envelope is not { Code: 200, Data: not null })
			{
				return OpdActionOutcome.Failure("提交药师审方失败：" + (envelope?.Message ?? "服务端未返回结果"));
			}
			_currentPrescriptionId = envelope.Data.Id;
			_currentPrescriptionStatus = envelope.Data.Status;
			_currentPrescriptionNo = envelope.Data.PrescriptionNo;
			patient.IsOrdersPushed = true;
			NotifyOpdWorkflowStateChanged();
			RefreshPrescriptionMetrics();
			SystemNotice = $"处方 {envelope.Data.PrescriptionNo} 已提交药师审方";
			return OpdActionOutcome.Success($"处方已提交，状态：{GetPrescriptionStatusText(envelope.Data.Status)}");
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
		{
			return OpdActionOutcome.Failure("提交处方失败：" + ex.Message);
		}
	}

	public async Task<OpdActionOutcome> CompleteCurrentOpdVisitAsync(bool noPrescription)
	{
		PatientQueueItemModel? patient = SelectedPatient;
		if (patient == null)
		{
			return OpdActionOutcome.Failure("请先选择接诊患者");
		}
		if (!patient.IsInProgress)
		{
			return OpdActionOutcome.Failure(patient.IsCompleted ? "该患者就诊已完成" : "请先叫号接诊患者");
		}
		string diagnosisName = ResolveOpdDiagnosisName();
		if (string.IsNullOrWhiteSpace(diagnosisName))
		{
			return OpdActionOutcome.Failure("请先录入初步诊断");
		}
		if (!patient.IsEmrSigned || string.IsNullOrWhiteSpace(_currentEmrDocumentId))
		{
			return OpdActionOutcome.Failure("请先完成病历保存与审签");
		}
		bool hasPrescription = Prescriptions.Any(x => !x.IsDeleted);
		if (!noPrescription && !patient.IsOrdersPushed)
		{
			return OpdActionOutcome.Failure("请先提交处方，或明确选择本次无处方");
		}
		if (noPrescription && hasPrescription)
		{
			return OpdActionOutcome.Failure("本次已有处方记录，请提交现有处方后方可完成诊疗");
		}

		try
		{
			using HttpClient http = CreateConfigHttpClient();
			EmrDocumentDto? verified = await ReadOpdEmrDocumentAsync(http, patient.VisitId);
			if (verified == null || verified.Status < 1)
			{
				return OpdActionOutcome.Failure("病历尚未完成签署，不能完成本次诊疗");
			}
			string archiveId = string.IsNullOrWhiteSpace(verified.LatestOfdArchiveId)
				? verified.OfdArchiveId ?? string.Empty
				: verified.LatestOfdArchiveId;
			if (string.IsNullOrWhiteSpace(archiveId))
			{
				return OpdActionOutcome.Failure("服务端病历尚无 OFD 归档记录，不能完成本次诊疗");
			}
			using HttpResponseMessage response = await http.PostAsJsonAsync(
				"/api/v4/opd/visits/" + Uri.EscapeDataString(patient.VisitId) + "/complete",
				new
				{
					PrimaryDiagCode = DiagnosisCode ?? string.Empty,
					PrimaryDiagName = diagnosisName,
					EmrDocumentId = verified.Id,
					OfdArchiveId = archiveId
				}, JsonOptions);
			ClientApiEnvelope<OpdVisitDto>? envelope =
				await response.Content.ReadFromJsonAsync<ClientApiEnvelope<OpdVisitDto>>(JsonOptions);
			if (envelope is not { Code: 200, Data: not null })
			{
				return OpdActionOutcome.Failure("完成诊疗失败：" + (envelope?.Message ?? "服务端未返回结果"));
			}
			string patientName = patient.PatientName;
			patient.SetStatus(envelope.Data.Status, 3);
			_prescriptionCache.Remove(patient.VisitNo);
			await LoadOpdQueueAsync();
			PatientQueueItemModel? next = PatientQueue.FirstOrDefault(x => x.IsWaiting);
			if (next != null)
			{
				SelectedPatient = next;
				await ReloadSelectedOpdClinicalStateAsync();
			}
			SystemNotice = $"患者 {patientName} 已完成本次门诊诊疗";
			return OpdActionOutcome.Success($"{patientName} 的门诊诊疗已完成");
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
		{
			return OpdActionOutcome.Failure("完成诊疗失败：" + ex.Message);
		}
	}

	private void RestoreOpdEmrDocument(EmrDocumentDto document)
	{
		_currentEmrDocumentId = document.Id;
		_currentEmrStatus = Math.Max(document.Status, 0);
		_currentOfdArchiveId = string.IsNullOrWhiteSpace(document.LatestOfdArchiveId)
			? document.OfdArchiveId ?? string.Empty
			: document.LatestOfdArchiveId;
		EmrWorkspace.DocNo = document.DocNo;
		EmrWorkspace.DocTitle = document.DocTitle;
		OpdEmrTemplateId = document.TemplateId;
		ApplyOpdDocument(document);
		if (!string.IsNullOrWhiteSpace(document.TemplateId))
		{
			OpdEmrTemplateModel? template = OpdEmrTemplates.FirstOrDefault(x =>
				string.Equals(x.Id, document.TemplateId, StringComparison.OrdinalIgnoreCase));
			if (template != null)
			{
				SelectedOpdEmrTemplate = template;
				_ = LoadOpdEmrTemplateAsync(template, document.StructuredElements);
			}
		}
	}

	private string OpdEmrTemplateId { get; set; } = string.Empty;

	private string ResolveOpdDiagnosisName()
	{
		return EmrWorkspace.Fields
			.Where(IsOpdDiagnosisElement)
			.Select(x => x.Value?.Trim())
			.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
			?? EmrWorkspace.Diagnosis?.Trim()
			?? string.Empty;
	}

	private static bool IsOpdDiagnosisElement(OpdEmrFieldModel field)
	{
		string code = field.ElementCode ?? string.Empty;
		string name = field.ElementName ?? string.Empty;
		return code.Contains("DIAGNOSIS", StringComparison.OrdinalIgnoreCase) ||
			name.Contains("诊断", StringComparison.OrdinalIgnoreCase);
	}

	private async Task<EmrDocumentDto?> ReadOpdEmrDocumentAsync(HttpClient http, string visitId)
	{
		OpdEmrDocumentsEnvelope? envelope = await http.GetFromJsonAsync<OpdEmrDocumentsEnvelope>(
			"/api/v4/emr/documents?emrScene=OPD&visitOrEncounterId=" + Uri.EscapeDataString(visitId),
			JsonOptions);
		return envelope is { Code: 200, Data: not null }
			? envelope.Data.OrderByDescending(x => x.ClinicalTimeMs).FirstOrDefault()
			: null;
	}

	private static string GetOpdEmrStatusText(int status)
	{
		return status switch
		{
			0 => "草稿",
			1 => "一级审签",
			2 => "二级审签",
			3 => "终审锁定",
			4 => "归档",
			5 => "退回修改",
			_ => "未知状态"
		};
	}
}
