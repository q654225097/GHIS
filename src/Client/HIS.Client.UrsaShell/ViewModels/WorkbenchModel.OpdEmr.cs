using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Stations.OpdDoctor;

namespace HIS.Client.UrsaShell.ViewModels;

// The OPD editor is driven by the server's template sections rather than by a fixed field list.
public partial class WorkbenchModel
{
	private sealed class OpdEmrTemplateEnvelope
	{
		public int Code { get; set; }
		public string? Message { get; set; }
		public List<EmrTemplateSummaryDto>? Data { get; set; }
	}

	private sealed class OpdEmrTemplateDetailEnvelope
	{
		public int Code { get; set; }
		public string? Message { get; set; }
		public EmrTemplateDetailDto? Data { get; set; }
	}

	private sealed class OpdEmrDocumentEnvelope
	{
		public int Code { get; set; }
		public string? Message { get; set; }
		public EmrDocumentDto? Data { get; set; }
	}

	private sealed class OpdEmrDocumentsEnvelope
	{
		public int Code { get; set; }
		public string? Message { get; set; }
		public List<EmrDocumentDto>? Data { get; set; }
	}

	private sealed class OpdPrintPreviewDto
	{
		public string? DocNo { get; set; }
		public string? EmrScene { get; set; }
		public string? TemplateCode { get; set; }
		public string? TemplateName { get; set; }
		public string? LayoutCode { get; set; }
		public string? LayoutName { get; set; }
		public string? PageSizeCode { get; set; }
		public decimal PageWidthMm { get; set; }
		public decimal PageHeightMm { get; set; }
		public decimal MarginTopMm { get; set; }
		public decimal MarginBottomMm { get; set; }
		public decimal MarginLeftMm { get; set; }
		public decimal MarginRightMm { get; set; }
		public string? HospitalName { get; set; }
		public string? SubTitle { get; set; }
		public string? AccentColor { get; set; }
		public string? WatermarkText { get; set; }
		public string? FooterText { get; set; }
		public bool ShowPatientBanner { get; set; }
		public bool ShowBedAndAdmissionNo { get; set; }
		public bool ShowVitalSignsBanner { get; set; }
		public bool ShowNursingScales { get; set; }
		public bool ShowTrackChanges { get; set; }
		public string? PatientBannerSummary { get; set; }
		public List<OpdPrintPreviewSectionDto>? Sections { get; set; }
		public List<OpdPrintPreviewSignatureSlotDto>? SignatureSlots { get; set; }
		public int OfdByteSize { get; set; }
		public string? ContentSha256 { get; set; }
		public bool OfdVerifyPassed { get; set; }
	}

	private sealed class OpdPrintPreviewSectionDto
	{
		public string? SectionCode { get; set; }
		public string? SectionTitle { get; set; }
		public string? RenderMode { get; set; }
		public List<OpdPrintPreviewFieldDto>? Fields { get; set; }
	}

	private sealed class OpdPrintPreviewFieldDto
	{
		public string? ElementCode { get; set; }
		public string? ElementName { get; set; }
		public string? NhcStdCode { get; set; }
		public string? ValueText { get; set; }
		public string? UnitText { get; set; }
		public string? GenderExclusive { get; set; }
	}

	private sealed class OpdPrintPreviewSignatureSlotDto
	{
		public int Level { get; set; }
		public string? RoleName { get; set; }
		public string? Label { get; set; }
		public string? SignerDisplay { get; set; }
		public bool IsSigned { get; set; }
	}

	private sealed class OpdPrintPreviewEnvelope
	{
		public int Code { get; set; }
		public string? Message { get; set; }
		public OpdPrintPreviewDto? Data { get; set; }
	}

	private readonly SemaphoreSlim _opdPreviewGate = new(1, 1);
	private CancellationTokenSource? _opdPreviewDebounceCts;
	private long _opdPreviewRevision;
	private long _opdPreviewAppliedRevision;
	private int _opdTemplateRevision;
	private bool _opdTemplateCatalogLoaded;
	private bool _isOpdEmrBusy;
	private bool _isOpdPreviewBusy;
	private string _opdTemplateSearch = string.Empty;
	private string _opdEmrValidationSummary = "尚未检查";
	private string _opdPreviewError = string.Empty;
	private string _opdPreviewStatus = "等待书写病历";
	private string _opdPreviewPatientBanner = string.Empty;
	private string _opdPreviewHospitalName = string.Empty;
	private string _opdPreviewTemplateName = string.Empty;
	private string _opdPreviewLayoutName = string.Empty;
	private string _opdPreviewPageSize = string.Empty;
	private string _opdPreviewFooterText = string.Empty;
	private string _opdPreviewWatermarkText = string.Empty;
	private string _opdPreviewOfdSummary = string.Empty;
	private string _opdCurrentTemplateCode = string.Empty;
	private string _opdCurrentDocTypeCode = string.Empty;
	private OpdEmrTemplateModel? _selectedOpdEmrTemplate;

	public ObservableCollection<OpdEmrTemplateModel> OpdEmrTemplates { get; } = new();
	public ObservableCollection<OpdEmrTemplateModel> FilteredOpdEmrTemplates { get; } = new();
	public ObservableCollection<OpdPrintSectionPreviewModel> OpdPreviewSections { get; } = new();
	public ObservableCollection<OpdPrintSignatureSlotPreviewModel> OpdPreviewSignatureSlots { get; } = new();

	public bool IsOpdEmrBusy
	{
		get => _isOpdEmrBusy;
		private set => SetField(ref _isOpdEmrBusy, value, nameof(IsOpdEmrBusy));
	}

	public bool IsOpdPreviewBusy
	{
		get => _isOpdPreviewBusy;
		private set => SetField(ref _isOpdPreviewBusy, value, nameof(IsOpdPreviewBusy));
	}

	public string OpdTemplateSearch
	{
		get => _opdTemplateSearch;
		set
		{
			if (SetField(ref _opdTemplateSearch, value ?? string.Empty, nameof(OpdTemplateSearch)))
			{
				ApplyOpdTemplateFilter();
			}
		}
	}

	public string OpdEmrValidationSummary
	{
		get => _opdEmrValidationSummary;
		private set => SetField(ref _opdEmrValidationSummary, value, nameof(OpdEmrValidationSummary));
	}

	public string OpdPreviewError
	{
		get => _opdPreviewError;
		private set
		{
			if (SetField(ref _opdPreviewError, value ?? string.Empty, nameof(OpdPreviewError)))
			{
				OnPropertyChanged(nameof(HasOpdPreviewError));
			}
		}
	}

	public bool HasOpdPreviewError => !string.IsNullOrWhiteSpace(OpdPreviewError);

	public string OpdPreviewPatientBanner
	{
		get => _opdPreviewPatientBanner;
		private set => SetField(ref _opdPreviewPatientBanner, value ?? string.Empty, nameof(OpdPreviewPatientBanner));
	}

	public string OpdPreviewHospitalName
	{
		get => _opdPreviewHospitalName;
		private set => SetField(ref _opdPreviewHospitalName, value ?? string.Empty, nameof(OpdPreviewHospitalName));
	}

	public string OpdPreviewTemplateName
	{
		get => _opdPreviewTemplateName;
		private set => SetField(ref _opdPreviewTemplateName, value ?? string.Empty, nameof(OpdPreviewTemplateName));
	}

	public string OpdPreviewLayoutName
	{
		get => _opdPreviewLayoutName;
		private set => SetField(ref _opdPreviewLayoutName, value ?? string.Empty, nameof(OpdPreviewLayoutName));
	}

	public string OpdPreviewPageSize
	{
		get => _opdPreviewPageSize;
		private set => SetField(ref _opdPreviewPageSize, value ?? string.Empty, nameof(OpdPreviewPageSize));
	}

	public string OpdPreviewFooterText
	{
		get => _opdPreviewFooterText;
		private set => SetField(ref _opdPreviewFooterText, value ?? string.Empty, nameof(OpdPreviewFooterText));
	}

	public string OpdPreviewWatermarkText
	{
		get => _opdPreviewWatermarkText;
		private set => SetField(ref _opdPreviewWatermarkText, value ?? string.Empty, nameof(OpdPreviewWatermarkText));
	}

	public string OpdPreviewOfdSummary
	{
		get => _opdPreviewOfdSummary;
		private set => SetField(ref _opdPreviewOfdSummary, value ?? string.Empty, nameof(OpdPreviewOfdSummary));
	}

	public string OpdPreviewStatus
	{
		get => _opdPreviewStatus;
		private set => SetField(ref _opdPreviewStatus, value, nameof(OpdPreviewStatus));
	}

	public OpdEmrTemplateModel? SelectedOpdEmrTemplate
	{
		get => _selectedOpdEmrTemplate;
		set
		{
			if (!SetField(ref _selectedOpdEmrTemplate, value, nameof(SelectedOpdEmrTemplate)))
			{
				return;
			}
			OnPropertyChanged(nameof(HasSelectedOpdEmrTemplate));
			OnPropertyChanged(nameof(OpdTemplateTitle));
			if (value != null)
			{
				_ = LoadOpdEmrTemplateAsync(value);
			}
		}
	}

	public bool HasSelectedOpdEmrTemplate => SelectedOpdEmrTemplate != null;

	public string OpdTemplateTitle => SelectedOpdEmrTemplate?.TemplateName ?? "请选择门诊病历模板";

	public bool HasOpdEmrSections => EmrWorkspace.Sections.Any(x => x.IsVisible && x.HasVisibleFields);

	public string OpdDynamicFieldsEmptyHint => HasOpdEmrSections
		? string.Empty
		: SelectedOpdEmrTemplate == null
			? "选择模板后显示该模板的病历字段"
			: "该模板暂无适用于当前患者的可见字段";

	public async Task<OpdActionOutcome> EnsureOpdEmrTemplatesAsync()
	{
		if (_opdTemplateCatalogLoaded && OpdEmrTemplates.Count > 0)
		{
			return OpdActionOutcome.Success(string.Empty);
		}
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			OpdEmrTemplateEnvelope? envelope = await http.GetFromJsonAsync<OpdEmrTemplateEnvelope>(
				"/api/v4/emr/templates?emrScene=OPD", JsonOptions);
			if (envelope is not { Code: 200, Data: not null })
			{
				return OpdActionOutcome.Failure(envelope?.Message ?? "门诊病历模板读取失败");
			}
			OpdEmrTemplates.Clear();
			foreach (EmrTemplateSummaryDto dto in envelope.Data)
			{
				OpdEmrTemplates.Add(new OpdEmrTemplateModel
				{
					Id = dto.Id,
					TemplateCode = dto.TemplateCode,
					TemplateVersion = dto.TemplateVersion,
					TemplateName = dto.TemplateName,
					EmrScene = dto.EmrScene,
					DocTypeCode = dto.DocTypeCode,
					DeptCategory = dto.DeptCategory,
					TargetDeptId = dto.TargetDeptId,
					TimeLimitHours = dto.TimeLimitHours,
					RequiredSignLevels = Math.Clamp(dto.RequiredSignLevels, 1, 3),
					OfdLayoutId = dto.OfdLayoutId,
					LayoutName = dto.LayoutName,
					PageSizeCode = dto.PageSizeCode,
					SectionCount = dto.SectionCount,
					IsPublished = dto.IsPublished
				});
			}
			_opdTemplateCatalogLoaded = true;
			ApplyOpdTemplateFilter();
			SelectedOpdEmrTemplate ??= FilteredOpdEmrTemplates.FirstOrDefault() ?? OpdEmrTemplates.FirstOrDefault();
			OnPropertyChanged(nameof(HasSelectedOpdEmrTemplate));
			OnPropertyChanged(nameof(OpdTemplateTitle));
			return OpdEmrTemplates.Count == 0
				? OpdActionOutcome.Failure("尚未发布门诊病历模板，请联系管理员配置模板")
				: OpdActionOutcome.Success($"已加载 {OpdEmrTemplates.Count} 个门诊病历模板");
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
		{
			return OpdActionOutcome.Failure("门诊病历模板读取失败：" + ex.Message);
		}
	}

	public async Task<OpdActionOutcome> LoadOpdEmrTemplateAsync(OpdEmrTemplateModel template, string? documentJson = null)
	{
		int revision = Interlocked.Increment(ref _opdTemplateRevision);
		IsOpdEmrBusy = true;
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			OpdEmrTemplateDetailEnvelope? envelope = await http.GetFromJsonAsync<OpdEmrTemplateDetailEnvelope>(
				"/api/v4/emr/templates/" + Uri.EscapeDataString(template.Id), JsonOptions);
			if (revision != _opdTemplateRevision)
			{
				return OpdActionOutcome.Success(string.Empty);
			}
			if (envelope is not { Code: 200, Data: not null })
			{
				return OpdActionOutcome.Failure(envelope?.Message ?? "病历模板详情读取失败");
			}

			EmrTemplateDetailDto detail = envelope.Data;
			Dictionary<string, string> values = ParseStructuredElements(documentJson);
			EmrWorkspace.ResetFields();
			foreach (EmrSectionDetailDto sectionDto in detail.Sections.OrderBy(x => x.SortOrder))
			{
				OpdEmrSectionModel section = new()
				{
					SectionId = sectionDto.SectionId,
					SectionCode = sectionDto.SectionCode,
					SectionTitle = string.IsNullOrWhiteSpace(sectionDto.SectionTitle) ? "病历内容" : sectionDto.SectionTitle,
					RenderMode = sectionDto.RenderMode,
					IsRequired = sectionDto.IsRequired != 0,
					VisibilityExpr = string.IsNullOrWhiteSpace(sectionDto.VisibilityExpr) ? "ALL" : sectionDto.VisibilityExpr,
					SortOrder = sectionDto.SortOrder
				};
				foreach (EmrDataElementDto element in sectionDto.Elements ?? new List<EmrDataElementDto>())
				{
					string code = DynamicFieldControlResolver.NormalizeElementCode(element.ElementCode);
					IReadOnlyList<string> options = DynamicFieldControlResolver.ParseOptions(
						ReadStringProperty(element.ValidationRuleJson, "options"));
					string value = string.Empty;
					if (!string.IsNullOrWhiteSpace(code))
					{
						values.TryGetValue(code, out value);
						if (string.IsNullOrWhiteSpace(value))
						{
							values.TryGetValue(code.ToLowerInvariant(), out value);
						}
					}
					OpdEmrFieldModel field = new()
					{
						ElementCode = code,
						ElementName = string.IsNullOrWhiteSpace(element.ElementName) ? code : element.ElementName,
						DataType = element.DataType ?? "STRING",
						ControlType = element.UrsaControlType ?? "u:TextBox",
						DictCode = element.DictCode,
						UnitText = element.UnitText,
						GenderExclusive = string.IsNullOrWhiteSpace(element.GenderExclusive) ? "ALL" : element.GenderExclusive,
						SectionCode = section.SectionCode,
						SectionTitle = section.SectionTitle,
						IsRequired = section.IsRequired,
						SortOrder = sectionDto.SortOrder,
						Options = options,
						ValidationRuleJson = element.ValidationRuleJson ?? string.Empty,
						PatientGender = SelectedPatient?.Gender ?? string.Empty,
						Minimum = ReadIntProperty(element.ValidationRuleJson, "min"),
						Maximum = ReadIntProperty(element.ValidationRuleJson, "max"),
						DecimalStep = ReadDecimalProperty(element.ValidationRuleJson, "step"),
						Placeholder = ReadStringProperty(element.ValidationRuleJson, "placeholder")
					};
					field.Value = value ?? string.Empty;
					field.RefreshResolvedControl();
					field.ApplyVisibility(values, section.VisibilityExpr);
					section.Fields.Add(field);
					EmrWorkspace.Fields.Add(field);
				}
				section.NotifyFieldsChanged();
				EmrWorkspace.Sections.Add(section);
			}

			EmrWorkspace.SelectedTemplateId = template.Id;
			EmrWorkspace.TemplateName = template.TemplateName;
			EmrWorkspace.RequiredSignLevels = template.RequiredSignLevels;
			EmrWorkspace.TemplateHint = template.Summary;
			_opdCurrentTemplateCode = template.TemplateCode;
			_opdCurrentDocTypeCode = template.DocTypeCode;
			SubscribeOpdDynamicFields();
			OnPropertyChanged(nameof(HasOpdEmrSections));
			OnPropertyChanged(nameof(OpdDynamicFieldsEmptyHint));
			UpdateOpdFieldVisibilityAndValidation(false);
			QueueOpdPrintPreview();
			return OpdActionOutcome.Success($"已载入模板：{template.TemplateName}");
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
		{
			return OpdActionOutcome.Failure("病历模板详情读取失败：" + ex.Message);
		}
		finally
		{
			if (revision == _opdTemplateRevision)
			{
				IsOpdEmrBusy = false;
			}
		}
	}

	public Dictionary<string, string> CollectOpdEmrElements()
	{
		Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
		foreach (OpdEmrFieldModel field in EmrWorkspace.Fields.Where(x => x.IsVisible && !string.IsNullOrWhiteSpace(x.ElementCode)))
		{
			values[field.ElementCode] = field.Value?.Trim() ?? string.Empty;
		}
		// Keep the compatibility aliases consumed by older OPD documents.
		if (!string.IsNullOrWhiteSpace(EmrWorkspace.ChiefComplaint))
		{
			values.TryAdd("CHIEF_COMPLAINT", EmrWorkspace.ChiefComplaint.Trim());
		}
		if (!string.IsNullOrWhiteSpace(EmrWorkspace.PresentIllness))
		{
			values.TryAdd("PRESENT_ILLNESS", EmrWorkspace.PresentIllness.Trim());
		}
		if (!string.IsNullOrWhiteSpace(EmrWorkspace.Diagnosis))
		{
			values.TryAdd("PRIMARY_DIAGNOSIS", EmrWorkspace.Diagnosis.Trim());
		}
		return values;
	}

	public string BuildOpdPlainTextSummary()
	{
		List<string> lines = new();
		foreach (OpdEmrSectionModel section in EmrWorkspace.Sections.Where(x => x.IsVisible))
		{
			List<string> visibleValues = section.Fields
				.Where(x => x.IsVisible && !string.IsNullOrWhiteSpace(x.Value))
				.Select(x => $"{x.ElementName}：{x.Value.Trim()}")
				.ToList();
			if (visibleValues.Count > 0)
			{
				lines.Add($"【{section.SectionTitle}】");
				lines.AddRange(visibleValues);
			}
		}
		return string.Join(Environment.NewLine, lines);
	}

	public bool ValidateOpdEmrFields()
	{
		UpdateOpdFieldVisibilityAndValidation(false);
		List<OpdEmrFieldModel> invalid = EmrWorkspace.Fields
			.Where(x => x.IsVisible && !x.Validate())
			.ToList();
		OpdEmrValidationSummary = invalid.Count == 0
			? "字段校验通过"
			: $"有 {invalid.Count} 个字段需要补充";
		return invalid.Count == 0;
	}

	public void UpdateOpdFieldVisibilityAndValidation(bool validate)
	{
		Dictionary<string, string> values = EmrWorkspace.Fields
			.ToDictionary(x => x.ElementCode, x => x.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);
		foreach (OpdEmrSectionModel section in EmrWorkspace.Sections)
		{
			foreach (OpdEmrFieldModel field in section.Fields)
			{
				field.PatientGender = SelectedPatient?.Gender ?? string.Empty;
				field.ApplyVisibility(values, section.VisibilityExpr);
				if (validate && field.IsVisible)
				{
					field.Validate();
				}
			}
			section.IsVisible = section.VisibilityExpr.Equals("ALL", StringComparison.OrdinalIgnoreCase) ||
				section.Fields.Any(x => x.IsVisible);
			section.NotifyVisibility();
			section.NotifyFieldsChanged();
		}
		OnPropertyChanged(nameof(HasOpdEmrSections));
		OnPropertyChanged(nameof(OpdDynamicFieldsEmptyHint));
	}

	public void QueueOpdPrintPreview()
	{
		Interlocked.Increment(ref _opdPreviewRevision);
		_opdPreviewDebounceCts?.Cancel();
		_opdPreviewDebounceCts = new CancellationTokenSource();
		CancellationToken token = _opdPreviewDebounceCts.Token;
		_ = DebouncePreviewAsync(token);
	}

	private async Task DebouncePreviewAsync(CancellationToken token)
	{
		try
		{
			await Task.Delay(350, token);
			await RefreshOpdPrintPreviewAsync(token);
		}
		catch (OperationCanceledException)
		{
		}
	}

	public async Task<OpdActionOutcome> RefreshOpdPrintPreviewAsync(CancellationToken token = default)
	{
		if (SelectedPatient == null || SelectedOpdEmrTemplate == null)
		{
			OpdPreviewStatus = "选择患者和模板后显示实时预览";
			return OpdActionOutcome.Failure(OpdPreviewStatus);
		}
		long requestedRevision = Interlocked.Read(ref _opdPreviewRevision);
		bool gateAcquired = false;
		try
		{
			await _opdPreviewGate.WaitAsync(token);
			gateAcquired = true;
			IsOpdPreviewBusy = true;
			OpdPreviewError = string.Empty;
			PatientQueueItemModel patient = SelectedPatient;
			OpdEmrTemplateModel template = SelectedOpdEmrTemplate;
			Dictionary<string, string> elements = CollectOpdEmrElements();
			using HttpClient http = CreateConfigHttpClient();
			var request = new
			{
				DocumentIdOrNo = string.IsNullOrWhiteSpace(_currentEmrDocumentId) ? EmrWorkspace.DocNo : _currentEmrDocumentId,
				EmrScene = "OPD",
				TemplateIdOrCode = template.Id,
				PatientId = patient.PatientId,
				PatientName = patient.PatientName,
				Gender = patient.Gender,
				Age = patient.Age,
				VisitOrEncounterNo = patient.VisitNo,
				DeptAndWardName = patient.OpdDeptName,
				DraftElements = elements,
				SignerName = CurrentOperatorName
			};
			using HttpResponseMessage response = await http.PostAsJsonAsync("/api/v4/emr/print-preview", request, JsonOptions, token);
			OpdPrintPreviewEnvelope? envelope =
				await response.Content.ReadFromJsonAsync<OpdPrintPreviewEnvelope>(JsonOptions, token);
			if (envelope is not { Code: 200, Data: not null })
			{
				OpdPreviewError = envelope?.Message ?? "实时预览生成失败";
				return OpdActionOutcome.Failure(OpdPreviewError);
			}

			OpdPrintPreviewDto preview = envelope.Data;
			if (requestedRevision < Interlocked.Read(ref _opdPreviewAppliedRevision))
			{
				return OpdActionOutcome.Success(string.Empty);
			}
			OpdPreviewSections.Clear();
			foreach (OpdPrintPreviewSectionDto section in preview.Sections ?? new())
			{
				OpdPrintSectionPreviewModel model = new()
				{
					SectionCode = section.SectionCode ?? string.Empty,
					SectionTitle = string.IsNullOrWhiteSpace(section.SectionTitle) ? "病历内容" : section.SectionTitle,
					RenderMode = section.RenderMode ?? string.Empty
				};
				foreach (OpdPrintPreviewFieldDto field in section.Fields ?? new())
				{
					model.Fields.Add(new OpdPrintFieldPreviewModel
					{
						Label = string.IsNullOrWhiteSpace(field.ElementName) ? field.ElementCode ?? string.Empty : field.ElementName,
						Value = string.IsNullOrWhiteSpace(field.UnitText) || string.IsNullOrWhiteSpace(field.ValueText)
							? field.ValueText ?? string.Empty
							: $"{field.ValueText} {field.UnitText}"
					});
				}
				OpdPreviewSections.Add(model);
			}
			OpdPreviewSignatureSlots.Clear();
			foreach (OpdPrintPreviewSignatureSlotDto slot in preview.SignatureSlots ?? new())
			{
				OpdPreviewSignatureSlots.Add(new OpdPrintSignatureSlotPreviewModel
				{
					Level = slot.Level,
					RoleName = slot.RoleName ?? string.Empty,
					Label = slot.Label ?? string.Empty,
					SignerDisplay = slot.SignerDisplay ?? string.Empty,
					IsSigned = slot.IsSigned
				});
			}
			OpdPreviewPatientBanner = preview.PatientBannerSummary ?? string.Empty;
			OpdPreviewHospitalName = preview.HospitalName ?? string.Empty;
			OpdPreviewTemplateName = preview.TemplateName ?? string.Empty;
			OpdPreviewLayoutName = preview.LayoutName ?? string.Empty;
			OpdPreviewPageSize = string.IsNullOrWhiteSpace(preview.PageSizeCode)
				? "A4"
				: $"{preview.PageSizeCode} · {preview.PageWidthMm:0.#} × {preview.PageHeightMm:0.#} mm";
			OpdPreviewFooterText = preview.FooterText ?? string.Empty;
			OpdPreviewWatermarkText = preview.WatermarkText ?? string.Empty;
			OpdPreviewOfdSummary = $"OFD {preview.OfdByteSize / 1024.0:0.0} KB · " +
				$"{(preview.OfdVerifyPassed ? "验签通过" : "验签未通过")} · " +
				$"{(string.IsNullOrWhiteSpace(preview.ContentSha256) ? "无哈希" : preview.ContentSha256[..Math.Min(12, preview.ContentSha256.Length)])}";
			Interlocked.Exchange(ref _opdPreviewAppliedRevision, requestedRevision);
			OpdPreviewStatus = $"实时预览 · {DateTime.Now:HH:mm:ss}";
			OnPropertyChanged(nameof(OpdPreviewSections));
			OnPropertyChanged(nameof(OpdPreviewSignatureSlots));
			return OpdActionOutcome.Success(string.Empty);
		}
		catch (OperationCanceledException)
		{
			return OpdActionOutcome.Success(string.Empty);
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
		{
			OpdPreviewError = "实时预览生成失败：" + ex.Message;
			return OpdActionOutcome.Failure(OpdPreviewError);
		}
		finally
		{
			IsOpdPreviewBusy = false;
			if (gateAcquired)
			{
				_opdPreviewGate.Release();
			}
		}
	}

	public async Task<OpdActionOutcome> SaveOpdEmrDraftAsync()
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
		if (SelectedOpdEmrTemplate == null)
		{
			return OpdActionOutcome.Failure("请先选择门诊病历模板");
		}
		OpdEmrValidationSummary = "草稿允许未完成字段，审签时再严格校验";
		if (!string.IsNullOrWhiteSpace(EmrWorkspace.Diagnosis))
		{
			OpdActionOutcome diagnosis = await SaveOpdDiagnosisAsync(EmrWorkspace.Diagnosis);
			if (!diagnosis.Succeeded)
			{
				return diagnosis;
			}
		}

		try
		{
			IsOpdEmrBusy = true;
			string docNo = string.IsNullOrWhiteSpace(EmrWorkspace.DocNo) ||
				EmrWorkspace.DocNo.StartsWith("DOC-OPD-", StringComparison.OrdinalIgnoreCase)
				? EmrWorkspace.DocNo
				: EmrWorkspace.DocNo;
			Dictionary<string, string> elements = CollectOpdEmrElements();
			using HttpClient http = CreateConfigHttpClient();
			using HttpResponseMessage response = await http.PostAsJsonAsync("/api/v4/emr/documents", new
			{
				DocNo = docNo,
				EmrScene = "OPD",
				VisitOrEncounterId = patient.VisitId,
				PatientId = patient.PatientId,
				DeptId = SessionContext.Current.ActiveDeptId,
				WardId = (string?)null,
				TemplateId = SelectedOpdEmrTemplate.Id,
				DocTypeCode = string.IsNullOrWhiteSpace(_opdCurrentDocTypeCode)
					? SelectedOpdEmrTemplate.DocTypeCode
					: _opdCurrentDocTypeCode,
				DocTitle = SelectedOpdEmrTemplate.TemplateName,
				StructuredElements = elements,
				PlainTextSummary = BuildOpdPlainTextSummary(),
				AuthorId = CurrentOperatorId
			}, JsonOptions);
			OpdEmrDocumentEnvelope? envelope =
				await response.Content.ReadFromJsonAsync<OpdEmrDocumentEnvelope>(JsonOptions);
			if (envelope is not { Code: 200, Data: not null })
			{
				return OpdActionOutcome.Failure(envelope?.Message ?? "病历草稿保存失败");
			}
			ApplyOpdDocument(envelope.Data);
			OpdPreviewStatus = "草稿已保存";
			SystemNotice = $"门诊病历草稿已保存：{envelope.Data.DocNo}";
			QueueOpdPrintPreview();
			return OpdActionOutcome.Success("病历草稿已保存");
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
		{
			return OpdActionOutcome.Failure("病历草稿保存失败：" + ex.Message);
		}
		finally
		{
			IsOpdEmrBusy = false;
		}
	}

	public async Task<OpdActionOutcome> LoadLatestOpdEmrAsync()
	{
		PatientQueueItemModel? patient = SelectedPatient;
		if (patient == null)
		{
			return OpdActionOutcome.Failure("请先选择患者");
		}
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			string uri = "/api/v4/emr/documents?emrScene=OPD&visitOrEncounterId=" +
				Uri.EscapeDataString(patient.VisitId);
			OpdEmrDocumentsEnvelope? envelope = await http.GetFromJsonAsync<OpdEmrDocumentsEnvelope>(uri, JsonOptions);
			if (envelope is not { Code: 200, Data: not null } || envelope.Data.Count == 0)
			{
				return OpdActionOutcome.Success("该患者暂无既往门诊病历");
			}
			EmrDocumentDto document = envelope.Data
				.OrderByDescending(x => x.ClinicalTimeMs)
				.First();
			ApplyOpdDocument(document);
			if (!string.IsNullOrWhiteSpace(document.TemplateId))
			{
				OpdEmrTemplateModel? template = OpdEmrTemplates.FirstOrDefault(x =>
					string.Equals(x.Id, document.TemplateId, StringComparison.OrdinalIgnoreCase));
				if (template != null)
				{
					_selectedOpdEmrTemplate = template;
					OnPropertyChanged(nameof(SelectedOpdEmrTemplate));
					OnPropertyChanged(nameof(OpdTemplateTitle));
					await LoadOpdEmrTemplateAsync(template, document.StructuredElements);
				}
			}
			return OpdActionOutcome.Success("已恢复最近一次门诊病历");
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
		{
			return OpdActionOutcome.Failure("读取门诊病历失败：" + ex.Message);
		}
	}

	public void ResetOpdEmrForPatient()
	{
		_opdTemplateRevision++;
		Interlocked.Increment(ref _opdPreviewRevision);
		_opdPreviewDebounceCts?.Cancel();
		OpdPreviewSections.Clear();
		OpdPreviewSignatureSlots.Clear();
		OpdPreviewError = string.Empty;
		OpdPreviewStatus = "等待书写病历";
		OpdPreviewPatientBanner = string.Empty;
		OpdPreviewHospitalName = string.Empty;
		OpdPreviewTemplateName = string.Empty;
		OpdPreviewLayoutName = string.Empty;
		OpdPreviewPageSize = string.Empty;
		OpdPreviewFooterText = string.Empty;
		OpdPreviewWatermarkText = string.Empty;
		OpdPreviewOfdSummary = string.Empty;
		OpdEmrValidationSummary = "尚未检查";
		OpdTemplateSearch = string.Empty;
		ApplyOpdTemplateFilter();
		OnPropertyChanged(nameof(HasOpdEmrSections));
		OnPropertyChanged(nameof(OpdDynamicFieldsEmptyHint));
		if (_opdTemplateCatalogLoaded)
		{
			SelectedOpdEmrTemplate = FilteredOpdEmrTemplates.FirstOrDefault() ?? OpdEmrTemplates.FirstOrDefault();
		}
	}

	private void ApplyOpdDocument(EmrDocumentDto document)
	{
		_currentEmrDocumentId = document.Id;
		_currentEmrStatus = Math.Max(document.Status, 0);
		_currentOfdArchiveId = string.IsNullOrWhiteSpace(document.LatestOfdArchiveId)
			? document.OfdArchiveId ?? string.Empty
			: document.LatestOfdArchiveId;
		EmrWorkspace.DocNo = document.DocNo;
		EmrWorkspace.DocTitle = document.DocTitle;
		EmrWorkspace.OfdNo = string.IsNullOrWhiteSpace(_currentOfdArchiveId) ? null : _currentOfdArchiveId;
		EmrWorkspace.OfdStatus = document.IsSigned ? "已审签归档" : "已保存，尚未审签";
		EmrWorkspace.QcStatus = document.IsSigned ? "已审签" : "草稿";
		OpdEmrTemplateId = document.TemplateId ?? string.Empty;
		if (SelectedPatient != null)
		{
			SelectedPatient.IsEmrSigned = document.IsSigned;
		}
		NotifyOpdWorkflowStateChanged();
	}

	private void ApplyOpdTemplateFilter()
	{
		string query = _opdTemplateSearch?.Trim() ?? string.Empty;
		FilteredOpdEmrTemplates.Clear();
		foreach (OpdEmrTemplateModel template in OpdEmrTemplates.Where(x =>
			string.IsNullOrWhiteSpace(query) ||
			x.TemplateName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
			x.TemplateCode.Contains(query, StringComparison.OrdinalIgnoreCase) ||
			(x.DeptCategory?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)))
		{
			FilteredOpdEmrTemplates.Add(template);
		}
	}

	private static Dictionary<string, string> ParseStructuredElements(string? json)
	{
		Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrWhiteSpace(json))
		{
			return values;
		}
		try
		{
			using JsonDocument document = JsonDocument.Parse(json);
			if (document.RootElement.ValueKind != JsonValueKind.Object)
			{
				return values;
			}

			JsonElement source = document.RootElement;
			if (source.TryGetProperty("elements", out JsonElement elements) &&
				elements.ValueKind == JsonValueKind.Object)
			{
				source = elements;
			}
			else if (source.TryGetProperty("values", out JsonElement wrappedValues) &&
				wrappedValues.ValueKind == JsonValueKind.Object)
			{
				source = wrappedValues;
			}

			foreach (JsonProperty property in source.EnumerateObject())
			{
				ExtractStructuredValue(property.Name, property.Value, values, 0);
			}
		}
		catch (JsonException)
		{
		}
		return values;
	}

	private static void ExtractStructuredValue(
		string? fallbackCode,
		JsonElement element,
		Dictionary<string, string> values,
		int depth)
	{
		if (depth > 12)
		{
			return;
		}
		if (element.ValueKind == JsonValueKind.Object)
		{
			string code = fallbackCode ?? string.Empty;
			if (element.TryGetProperty("elementCode", out JsonElement elementCode) &&
				elementCode.ValueKind == JsonValueKind.String)
			{
				code = elementCode.GetString() ?? code;
			}
			else if (element.TryGetProperty("code", out JsonElement fallbackElementCode) &&
				fallbackElementCode.ValueKind == JsonValueKind.String)
			{
				code = fallbackElementCode.GetString() ?? code;
			}

			if (element.TryGetProperty("value", out JsonElement value) &&
				value.ValueKind is not JsonValueKind.Object and not JsonValueKind.Array)
			{
				if (!string.IsNullOrWhiteSpace(code))
				{
					values[code] = value.ValueKind switch
					{
						JsonValueKind.String => value.GetString() ?? string.Empty,
						JsonValueKind.Null => string.Empty,
						_ => value.GetRawText()
					};
				}
				return;
			}

			foreach (JsonProperty property in element.EnumerateObject())
			{
				ExtractStructuredValue(property.Name, property.Value, values, depth + 1);
			}
			return;
		}

		if (element.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in element.EnumerateArray())
			{
				ExtractStructuredValue(null, item, values, depth + 1);
			}
			return;
		}

		if (!string.IsNullOrWhiteSpace(fallbackCode))
		{
			values[fallbackCode] = element.ValueKind switch
			{
				JsonValueKind.String => element.GetString() ?? string.Empty,
				JsonValueKind.Null => string.Empty,
				_ => element.GetRawText()
			};
		}
	}

	private void SubscribeOpdDynamicFields()
	{
		void OnFieldChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
		{
			if (args.PropertyName == nameof(OpdEmrFieldModel.Value))
			{
				UpdateOpdFieldVisibilityAndValidation(false);
				QueueOpdPrintPreview();
			}
		}

		EmrWorkspace.DynamicFieldChanged = OnFieldChanged;
		foreach (OpdEmrFieldModel field in EmrWorkspace.Fields)
		{
			field.PropertyChanged -= OnFieldChanged;
			field.PropertyChanged += OnFieldChanged;
		}
	}

	private static string? ReadStringProperty(string? json, string name)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}
		try
		{
			using JsonDocument doc = JsonDocument.Parse(json);
			if (doc.RootElement.ValueKind == JsonValueKind.Object &&
				doc.RootElement.TryGetProperty(name, out JsonElement value))
			{
				return value.ValueKind switch
				{
					JsonValueKind.String => value.GetString(),
					JsonValueKind.Array => value.GetRawText(),
					_ => value.GetRawText()
				};
			}
		}
		catch (JsonException)
		{
		}
		return null;
	}

	private static int? ReadIntProperty(string? json, string name)
	{
		string? raw = ReadStringProperty(json, name);
		return int.TryParse(raw, out int value) ? value : null;
	}

	private static decimal? ReadDecimalProperty(string? json, string name)
	{
		string? raw = ReadStringProperty(json, name);
		return decimal.TryParse(raw, out decimal value) ? value : null;
	}
}

public sealed class OpdPrintSectionPreviewModel
{
	public string SectionCode { get; set; } = string.Empty;
	public string SectionTitle { get; set; } = string.Empty;
	public string RenderMode { get; set; } = string.Empty;
	public ObservableCollection<OpdPrintFieldPreviewModel> Fields { get; } = new();
}

public sealed class OpdPrintFieldPreviewModel
{
	public string Label { get; set; } = string.Empty;
	public string Value { get; set; } = string.Empty;
	public bool HasValue => !string.IsNullOrWhiteSpace(Value);
}

public sealed class OpdPrintSignatureSlotPreviewModel
{
	public int Level { get; set; }
	public string RoleName { get; set; } = string.Empty;
	public string Label { get; set; } = string.Empty;
	public string SignerDisplay { get; set; } = string.Empty;
	public bool IsSigned { get; set; }
	public string StatusText => IsSigned ? "已签" : "待签";
}
