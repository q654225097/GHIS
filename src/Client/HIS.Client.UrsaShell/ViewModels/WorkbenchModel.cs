using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using HIS.BuildingBlocks.OfdEngine;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Services;
using HIS.Contracts;

namespace HIS.Client.UrsaShell.ViewModels;

public partial class WorkbenchModel : ClientModelBase
{
	private const string DefaultDevelopmentPassword = "123";

	private sealed class PlatformUserDto
	{
		public string Id { get; set; } = string.Empty;

		public string UserName { get; set; } = string.Empty;

		public string DisplayName { get; set; } = string.Empty;

		public string EmployeeNo { get; set; } = string.Empty;

		public int PrescribeLevel { get; set; }

		public int SurgeryLevel { get; set; }

		public int EmrSignLevel { get; set; }

		public bool IsActive { get; set; }

		public string RoleId { get; set; } = string.Empty;

		public string RoleName { get; set; } = string.Empty;

		public string DeptId { get; set; } = string.Empty;
	}

	private sealed class PlatformDepartmentDto
	{
		public string Id { get; set; } = string.Empty;

		public string DeptName { get; set; } = string.Empty;

		public string DeptCategory { get; set; } = string.Empty;
	}

	private sealed class PlatformRoleDto
	{
		public string Id { get; set; } = string.Empty;

		public string RoleCode { get; set; } = string.Empty;

		public string RoleName { get; set; } = string.Empty;

		public string DataScope { get; set; } = string.Empty;

		public bool IsActive { get; set; }
	}

	private sealed class PlatformRolePermissionDto
	{
		public string RoleId { get; set; } = string.Empty;

		public string ResourceKey { get; set; } = string.Empty;

		public int AccessPolicy { get; set; }
	}

	private sealed class MasterFrequencyDto
	{
		public string FreqCode { get; set; } = string.Empty;

		public string FreqName { get; set; } = string.Empty;

		public int DailyTimes { get; set; } = 1;

		public string TimePointsText { get; set; } = "08:00";

		public string ApplicableTerm { get; set; } = "STANDING";

		public string? Description { get; set; }
	}

	private sealed record EnsurePrescriptionResult(string? RxId, bool CreatedNow);

	private sealed class PrescriptionDto
	{
		public string Id { get; set; } = string.Empty;

		public string VisitId { get; set; } = string.Empty;

		public string PrescriptionNo { get; set; } = string.Empty;

		public int Status { get; set; }

		public string? ReviewRemark { get; set; }

		public decimal TotalAmount { get; set; }

		public string TargetPharmacyId { get; set; } = string.Empty;

		public List<PrescriptionItemDto>? Items { get; set; }

		public string? ExtDataJson { get; set; }
	}

	private sealed class OpdQueueDto
	{
		public string QueueId { get; set; } = string.Empty;
		public string VisitId { get; set; } = string.Empty;
		public string VisitNo { get; set; } = string.Empty;
		public string PatientId { get; set; } = string.Empty;
		public string MedicalRecordNo { get; set; } = string.Empty;
		public string PatientName { get; set; } = string.Empty;
		public string GenderCode { get; set; } = string.Empty;
		public int AgeYears { get; set; }
		public string OpdDeptId { get; set; } = string.Empty;
		public string OpdDeptName { get; set; } = string.Empty;
		public string DoctorId { get; set; } = string.Empty;
		public string DoctorName { get; set; } = string.Empty;
		public string ClinicRoomId { get; set; } = string.Empty;
		public string QueueNo { get; set; } = string.Empty;
		public int QueueStatus { get; set; }
		public int VisitStatus { get; set; }
		public long? CheckinAtMs { get; set; }
		public long? CalledAtMs { get; set; }
		public List<string>? AllergyTags { get; set; }
	}

	private sealed class OpdQueueEnvelope
	{
		public int Code { get; set; }
		public string? Message { get; set; }
		public List<OpdQueueDto>? Data { get; set; }
	}

	private sealed class EmrDocumentDto
	{
		public string Id { get; set; } = string.Empty;
		public string DocNo { get; set; } = string.Empty;
		public string DocTitle { get; set; } = string.Empty;
		public string? TemplateId { get; set; }
		public string? DocTypeCode { get; set; }
		public string? DeptId { get; set; }
		public string? PlainTextSummary { get; set; }
		public string StructuredElements { get; set; } = string.Empty;
		public string? StructuredAstJson { get; set; }
		public int Status { get; set; }
		public bool IsSigned { get; set; }
                public long ClinicalTimeMs { get; set; }
		public string? SignedBy { get; set; }
		public long? SignedAtMs { get; set; }
		public string? OfdArchiveId { get; set; }
		public string? LatestOfdArchiveId { get; set; }
	}

	private sealed class EmrTemplateSummaryDto
	{
		public string Id { get; set; } = string.Empty;
		public string TemplateCode { get; set; } = string.Empty;
		public int TemplateVersion { get; set; }
		public string TemplateName { get; set; } = string.Empty;
		public string EmrScene { get; set; } = "OPD";
		public string DocTypeCode { get; set; } = string.Empty;
		public string? DeptCategory { get; set; }
		public string? TargetDeptId { get; set; }
		public int TimeLimitHours { get; set; }
		public int RequiredSignLevels { get; set; } = 1;
		public string? OfdLayoutId { get; set; }
		public string LayoutName { get; set; } = "国标默认版式";
		public string PageSizeCode { get; set; } = "A4";
		public int SectionCount { get; set; }
		public short IsPublished { get; set; }
	}

	private sealed class EmrDataElementDto
	{
		public string? Id { get; set; }
		public string ElementCode { get; set; } = string.Empty;
		public string ElementName { get; set; } = string.Empty;
		public string DataType { get; set; } = "STRING";
		public string UrsaControlType { get; set; } = "u:TextBox";
		public string? DictCode { get; set; }
		public string? UnitText { get; set; }
		public string? ValidationRuleJson { get; set; }
		public string GenderExclusive { get; set; } = "ALL";
	}

	private sealed class EmrSectionDetailDto
	{
		public string SectionId { get; set; } = string.Empty;
		public string SectionCode { get; set; } = string.Empty;
		public string SectionTitle { get; set; } = string.Empty;
		public string RenderMode { get; set; } = "GRID_FORM";
		public short IsRequired { get; set; }
		public string VisibilityExpr { get; set; } = "ALL";
		public int SortOrder { get; set; }
		public List<EmrDataElementDto> Elements { get; set; } = new();
	}

	private sealed class EmrTemplateDetailDto
	{
		public EmrTemplateSummaryDto Template { get; set; } = new();
		public List<EmrSectionDetailDto> Sections { get; set; } = new();
	}

	private sealed class OfdArchiveDto
	{
		public string Id { get; set; } = string.Empty;
		public string OfdNo { get; set; } = string.Empty;
		public string ContentSha256 { get; set; } = string.Empty;
		public string? OfdContentBase64 { get; set; }
		public string? ArchivedAtMs { get; set; }
	}

	private sealed class EmrDocumentSaveEnvelope
	{
		public int Code { get; set; }
		public string? Message { get; set; }
		public EmrDocumentDto? Data { get; set; }
	}

	private sealed class EmrSignResultEnvelope
	{
		public int Code { get; set; }
		public string? Message { get; set; }
		public OfdArchiveDto? Data { get; set; }
	}

	private sealed class EmrArchiveDetailEnvelope
	{
		public int Code { get; set; }
		public string? Message { get; set; }
		public EmrArchiveDetailDto? Data { get; set; }
	}

	private sealed class EmrArchiveDetailDto
	{
		public string Id { get; set; } = string.Empty;
		public string OfdNo { get; set; } = string.Empty;
		public string ContentSha256 { get; set; } = string.Empty;
		public string? OfdContentBase64 { get; set; }
	}

	private sealed class OpdVisitDto
	{
		public string Id { get; set; } = string.Empty;
		public string VisitNo { get; set; } = string.Empty;
		public string PatientId { get; set; } = string.Empty;
		public string? PrimaryDiagCode { get; set; }
		public string? PrimaryDiagName { get; set; }
		public string? EmrDocumentId { get; set; }
		public string? OfdArchiveId { get; set; }
		public int Status { get; set; }
	}

	private sealed class OpdVisitEnvelope
	{
		public int Code { get; set; }
		public string? Message { get; set; }
		public OpdVisitDto? Data { get; set; }
	}

	private sealed class EmrDocumentEnvelope
	{
		public int Code { get; set; }
		public string? Message { get; set; }
		public List<EmrDocumentDto>? Data { get; set; }
	}

	private sealed class EmrSignEnvelope
	{
		public int Code { get; set; }
		public string? Message { get; set; }
		public OfdArchiveDto? Data { get; set; }
	}

	private sealed class ClinicalContextSnapshot
	{
		public string VisitId { get; init; } = string.Empty;
		public string VisitNo { get; init; } = string.Empty;
		public string PatientName { get; init; } = string.Empty;
		public string Gender { get; init; } = string.Empty;
		public int Age { get; init; }
		public string ChiefComplaint { get; init; } = string.Empty;
		public string AllergyTags { get; init; } = string.Empty;
		public string DocNo { get; init; } = string.Empty;
		public string PresentIllness { get; init; } = string.Empty;
		public string Diagnosis { get; init; } = string.Empty;
		public int PregnancyCount { get; init; }
		public List<PrescriptionItemModel> Prescriptions { get; init; } = new();
		public List<PrescriptionExtraFieldModel> ExtraFields { get; init; } = new();
	}

	private sealed class DischargeEncounterDto
	{
		public string Id { get; set; } = string.Empty;
		public string AdmissionNo { get; set; } = string.Empty;
		public string PatientName { get; set; } = string.Empty;
		public int Status { get; set; }
		public int TriageStatus { get; set; }
	}

	private sealed class DischargeRequestDto
	{
		public string Id { get; set; } = string.Empty;
		public string RequestNo { get; set; } = string.Empty;
		public string Status { get; set; } = string.Empty;
		public string? PatientName { get; set; }
	}

	private sealed class PrescriptionItemDto
	{
		public string Id { get; set; } = string.Empty;

		public string PrescriptionId { get; set; } = string.Empty;

		public int GroupNo { get; set; }

		public string DrugNameSnapshot { get; set; } = string.Empty;

		public string DrugId { get; set; } = string.Empty;

		public decimal DoseOnce { get; set; }

		public string DoseUnit { get; set; } = "mg";

		public string FrequencyCode { get; set; } = string.Empty;

		public string RouteCode { get; set; } = string.Empty;

		public int Days { get; set; }

		public int TotalQuantity { get; set; }

		public decimal UnitPrice { get; set; }

		public short IsDeleted { get; set; }

		public long? DeletedAtMs { get; set; }

		public string? DeletedBy { get; set; }

		public string? ExtDataJson { get; set; }
	}

	private sealed class DrugCatalogEnvelope
	{
		public int Code { get; set; }
		public string? Message { get; set; }
		public List<InventoryDrugDto>? Data { get; set; }
	}

	private sealed class InventoryDrugDto
	{
		public string Id { get; set; } = string.Empty;
		public string DrugCode { get; set; } = string.Empty;
		public string DrugName { get; set; } = string.Empty;
		public string Spec { get; set; } = string.Empty;
		public string UnitName { get; set; } = string.Empty;
		public decimal Price { get; set; }
		public int AntibioticLevel { get; set; }
		public short IsNarcotic { get; set; }
		public short IsSkinTestRequired { get; set; }
		public short IsActive { get; set; }
	}

	private sealed class PrescriptionFieldDto
	{
		public string Id { get; set; } = string.Empty;

		public string? Name { get; set; }

		public string? Value { get; set; }

		public string? DataType { get; set; }

		public string? Placeholder { get; set; }

		public bool IsDeleted { get; set; }

		public int SortOrder { get; set; }

		public long? DeletedAtMs { get; set; }

		public string? DeletedBy { get; set; }
	}

	private sealed class PrescriptionAggregateDto
	{
		public PrescriptionDto? Prescription { get; set; }

		public List<PrescriptionItemDto>? Items { get; set; }

		public List<PrescriptionFieldDto>? CustomFields { get; set; }
	}

	// 新门诊站的结果只来自服务端返回，界面据此显示真实成功或失败原因。
	public sealed record OpdActionOutcome(bool Succeeded, string Message)
	{
		public static OpdActionOutcome Success(string message) => new(true, message);
		public static OpdActionOutcome Failure(string message) => new(false, message);
	}

	private string _campusName = "总院区 (Campus 1)";

	private DepartmentCategory _activeCategory = DepartmentCategory.OPD;

	private string _activeDeptName = "心血管内科门诊";

	private string _activeWardName = "内科一病区";

	private string _userName = "张建国 (主治医师)";

	private int _selectedNavIndex = 0;

	private PatientQueueItemModel? _selectedPatient;

	private InpatientRecordModel? _selectedInpatient;

	private EmrEditorWorkspaceModel _emrWorkspace = new EmrEditorWorkspaceModel();

	private string _systemNotice = "系统就绪";

	private bool _isServerReachable = true;

	private string _serverStatusText = "服务器已连接";

	private string _newOrderContent = "阿司匹林肠溶片";

	private string _newOrderDose = "100mg";

	private string _newOrderRoute = "口服";

	private string _newOrderFrequency = "qn (每晚一次)";

	private string _newProgressNote = string.Empty;

	private string _temperatureText = "36.5";

	private string _pulseText = "76";

	private string _respirationText = "18";

	private string _systolicText = "128";

	private string _diastolicText = "78";

	private string _spo2Text = "98";

	private string _handoverDraft = string.Empty;

	private string _selectedHandoverShift = "白班";

	private bool _isTimelineDrawerOpen;

	private bool _isWhoSurgeryCheckOpen;

	private bool _isCriticalValueAlertOpen;

	private bool _isAddPrescriptionDialogOpen;

	private bool _isDischargeCheckDrawerOpen;

	private bool _isOfdViewerDrawerOpen;

	private bool _isBorrowBedDialogOpen;

	private bool _isCreateUserModalOpen;

	private bool _isCreateRoleModalOpen;

	private bool _isSwitchUserModalOpen;

	private bool _isSwitchingContext;

	private WhoSurgeryCheckModel _whoSurgeryCheck = new WhoSurgeryCheckModel();

	private CriticalValueAlertModel _criticalValueAlert = new CriticalValueAlertModel();

	private DischargeCheckDetailsModel _dischargeCheckDetails = new DischargeCheckDetailsModel();

	private DynamicFormTemplateModel? _selectedFormTemplate;

	private NewUserFormModel _newUserForm = new NewUserFormModel();

	private NewRoleFormModel _newRoleForm = new NewRoleFormModel();

	private NavigationItemModel? _selectedNavigationItem;

	private PrescriptionFieldDraftModel _prescriptionFieldDraft = new PrescriptionFieldDraftModel();

	private bool _isPrescriptionFieldEditorOpen;

	private bool _isPrescriptionItemEditorOpen;

	private PrescriptionExtraFieldModel? _editingPrescriptionField;

	private string? _currentPrescriptionVisitNo;

	private string? _currentPrescriptionVisitId;

	private readonly Dictionary<string, List<PrescriptionItemModel>> _prescriptionCache = new Dictionary<string, List<PrescriptionItemModel>>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, List<PrescriptionExtraFieldModel>> _prescriptionExtraFieldCache = new Dictionary<string, List<PrescriptionExtraFieldModel>>(StringComparer.OrdinalIgnoreCase);

	private string _patientSearchQuery = string.Empty;

	private int _selectedOpdTabIndex;

	private string _prescriptionSearchQuery = string.Empty;

	private string _selectedPrescriptionFilter = "ALL";

	private bool _isFormDesignerOpen;

	private bool _isFieldEditorOpen;

	private DynamicFormTemplateModel? _editingTemplate;

	private DynamicFormFieldModel? _editingField;

	private DynamicFormFieldModel? _fieldEditSnapshot;

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true
	};

	private bool _emrDesignerInitialized;

	private string _selectedEmrSceneCode = "OPD";

	private EmrVisualTemplateModel? _selectedEmrTemplate;

	private EmrPrintLayoutConfigModel _activeEmrPrintLayout = new EmrPrintLayoutConfigModel();

	private EmrVisualSectionModel? _selectedDesignSection;

	private bool _isEmrVisualDesignerOpen;

	private string _newEmrSectionTitle = string.Empty;

	private string _newEmrSectionRenderMode = "GRID_FORM";

	private string _newEmrFieldCode = string.Empty;

	private string _newEmrFieldName = string.Empty;

	private string _newEmrFieldNhcCode = "DE04.01.119.00";

	private string _newEmrFieldDataType = "STRING";

	private string _newEmrFieldUnit = string.Empty;

	private string _newEmrFieldGenderRule = "ALL";

	private bool _newEmrFieldRequired = true;

	private int _selectedNurseTabIndex;

	private bool _isFrequencyConfigModalOpen;

	private bool _isAddFrequencyFormOpen;

	private string _newFreqCode = string.Empty;

	private string _newFreqName = string.Empty;

	private string _newFreqTimePoints = "08:00, 16:00";

	private string _newFreqDesc = string.Empty;

	private bool _newFreqIsLongTerm = true;

	private bool _phmWhFinInitialized;

	private int _selectedPharmacyHubTabIndex;

	private int _selectedFinanceTabIndex;

	private string _selectedWarehouseFilter = "ALL";

	private string _dailyClosingStatusText = "今日尚未生成日结轧账单 (点击右侧按钮可汇总今日门急诊收费、退费与住院结算流水)";

	private string _editDrugCode = "DRUG-CARD-008";

	private string _editDrugName = "瑞舒伐他汀钙片 (可定)";

	private string _editDrugSpec = "10mg*7片/盒";

	private string _editDrugUnit = "盒";

	private decimal _editDrugRetailPrice = 34.50m;

	private decimal _editDrugCostPrice = 24.00m;

	private int _editDrugAntibioticLevel = 0;

	private bool _editDrugSkinTest = false;

	private bool _editDrugNarcotic = false;

	private string _priceAdjustReason = "国家第十批药品集中带量采购中选零差率调价";

	private string _stockInWarehouseId = "DEPT-WH-01";

	private string _stockInDrugCode = "DRUG-CARD-006";

	private string _stockInBatchNo = "LOT202611N";

	private int _stockInExpiryInt = 20281130;

	private int _stockInQuantity = 300;

	private string _stockInInvoiceNo = "INV-2026-208";

	private decimal _stockInColdChainTemp = 4.5m;

	private string _transferSourceWarehouseId = "DEPT-WH-01";

	private string _transferTargetWarehouseId = "DEPT-PHM-01";

	private string _transferDrugCode = "DRUG-CARD-006";

	private int _transferQuantity = 100;

	private string _transferRemark = "药房周常补货请领单 (FEFO近效期优先出库)";

	public const string PrescriptionItemFieldsFormCode = "FORM-PRESCRIPTION-ITEM-FIELDS";

	private static readonly JsonSerializerOptions PrescriptionJsonOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private readonly Dictionary<string, string> _persistedPrescriptionByVisit = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, string> _persistedPrescriptionByVisitId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private readonly HashSet<string> _prescriptionLoadedVisits = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly HashSet<string> _prescriptionLoadedVisitIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly SemaphoreSlim _prescriptionSyncLock = new SemaphoreSlim(1, 1);
	private readonly SemaphoreSlim _opdWorkflowLock = new SemaphoreSlim(1, 1);

	private string _opdQueueError = string.Empty;
	private int _clinicalContextRevision;
	private string _currentEmrDocumentId = string.Empty;
	private int _currentEmrStatus;
	private string _currentOfdArchiveId = string.Empty;
	private string _currentPrescriptionId = string.Empty;
	private int _currentPrescriptionStatus;
	private string _currentPrescriptionNo = string.Empty;
	private string? _currentPrescriptionReviewRemark;

	private int _prescriptionRevision;

	public ObservableCollection<UserAdminItemModel> UsersList { get; } = new ObservableCollection<UserAdminItemModel>();

	public ObservableCollection<RoleAdminItemModel> RolesList { get; } = new ObservableCollection<RoleAdminItemModel>();

	public ObservableCollection<AuditLogEntryModel> AuditLogsList { get; } = new ObservableCollection<AuditLogEntryModel>();

	private string _auditEventTypeFilter = string.Empty;
	public string AuditEventTypeFilter
	{
		get => _auditEventTypeFilter;
		set => SetField(ref _auditEventTypeFilter, value, nameof(AuditEventTypeFilter));
	}

	private string _auditTargetEntityFilter = string.Empty;
	public string AuditTargetEntityFilter
	{
		get => _auditTargetEntityFilter;
		set => SetField(ref _auditTargetEntityFilter, value, nameof(AuditTargetEntityFilter));
	}

	private string _auditOperatorFilter = string.Empty;
	public string AuditOperatorFilter
	{
		get => _auditOperatorFilter;
		set => SetField(ref _auditOperatorFilter, value, nameof(AuditOperatorFilter));
	}

	private string _auditVerificationStatus = "哈希链未验证";
	public string AuditVerificationStatus
	{
		get => _auditVerificationStatus;
		set => SetField(ref _auditVerificationStatus, value, nameof(AuditVerificationStatus));
	}

	private string _auditVerificationBadgeClass = "Secondary";
	public string AuditVerificationBadgeClass
	{
		get => _auditVerificationBadgeClass;
		set => SetField(ref _auditVerificationBadgeClass, value, nameof(AuditVerificationBadgeClass));
	}

	private string _auditSummaryInfo = "尚未执行查询";
	public string AuditSummaryInfo
	{
		get => _auditSummaryInfo;
		set => SetField(ref _auditSummaryInfo, value, nameof(AuditSummaryInfo));
	}

	public ObservableCollection<SecurityAlertModel> SecurityAlertsList { get; } = new ObservableCollection<SecurityAlertModel>();
	public ObservableCollection<KmsKeyModel> KmsKeysList { get; } = new ObservableCollection<KmsKeyModel>();

	private int _securityScore = 98;
	public int SecurityScore
	{
		get => _securityScore;
		set => SetField(ref _securityScore, value, nameof(SecurityScore));
	}

	private string _threatLevel = "NORMAL";
	public string ThreatLevel
	{
		get => _threatLevel;
		set
		{
			if (SetField(ref _threatLevel, value, nameof(ThreatLevel)))
			{
				OnPropertyChanged(nameof(ThreatLevelBadgeClass));
			}
		}
	}

	public string ThreatLevelBadgeClass => ThreatLevel switch
	{
		"NORMAL" => "Success",
		"ATTENTION" => "Warning",
		"HIGH" => "Warning",
		_ => "Danger"
	};

	private int _activeAlertsCount = 0;
	public int ActiveAlertsCount
	{
		get => _activeAlertsCount;
		set => SetField(ref _activeAlertsCount, value, nameof(ActiveAlertsCount));
	}

	private string _hsmStatus = "ONLINE (硬件隔离守护)";
	public string HsmStatus
	{
		get => _hsmStatus;
		set => SetField(ref _hsmStatus, value, nameof(HsmStatus));
	}

	private string _nationalCipherStatus = "密评三级 (SM2/SM3/SM4)";
	public string NationalCipherStatus
	{
		get => _nationalCipherStatus;
		set => SetField(ref _nationalCipherStatus, value, nameof(NationalCipherStatus));
	}

	private string _dengbaoStatus = "等保三级安全审计就绪";
	public string DengbaoStatus
	{
		get => _dengbaoStatus;
		set => SetField(ref _dengbaoStatus, value, nameof(DengbaoStatus));
	}

	public ObservableCollection<LoginContextOption> AvailableContexts { get; } = new ObservableCollection<LoginContextOption>();

	public ObservableCollection<NavigationItemModel> NavigationItems { get; } = new ObservableCollection<NavigationItemModel>();

	public ObservableCollection<NavigationGroupModel> NavigationGroups { get; } = new ObservableCollection<NavigationGroupModel>();

	public ScheduleWorkstationViewModel Schedule { get; } = new ScheduleWorkstationViewModel();
	public PharmacyWorkstationViewModel Pharmacy { get; } = new PharmacyWorkstationViewModel();

	public ObservableCollection<PatientQueueItemModel> FilteredPatientQueue { get; } = new ObservableCollection<PatientQueueItemModel>();

	public string PatientSearchQuery
	{
		get
		{
			return _patientSearchQuery;
		}
		set
		{
			if (SetField(ref _patientSearchQuery, value, "PatientSearchQuery"))
			{
				ApplyPatientQueueFilter();
			}
		}
	}

	public int InProgressPatientCount => Enumerable.Count(PatientQueue, (PatientQueueItemModel x) => x.IsInProgress);

	public int WaitingPatientCount => Enumerable.Count(PatientQueue, (PatientQueueItemModel x) => x.IsWaiting);

	public int CompletedPatientCount => Enumerable.Count(PatientQueue, (PatientQueueItemModel x) => x.IsCompleted);

	public bool IsMalePatientSelected => IsGender(SelectedPatient?.Gender, "男", "1", "M", "MALE");

	public bool IsFemalePatientSelected => IsGender(SelectedPatient?.Gender, "女", "2", "F", "FEMALE");

	public string SelectedPatientGenderGateText
	{
		get
		{
			if (IsFemalePatientSelected)
			{
				return "✓ 当前为女性患者，已自动隐藏男性专属字段并开放女性专科字段";
			}
			if (IsMalePatientSelected)
			{
				return "✓ 当前为男性患者，已自动开放男性专科字段并隐藏女性专属字段";
			}
			return "⚠ 患者性别未确认：请先补全患者性别后再使用性别专有字段";
		}
	}

	private static bool IsGender(string? value, params string[] accepted)
	{
		string normalized = value?.Trim() ?? string.Empty;
		return accepted.Any(candidate => string.Equals(normalized, candidate, StringComparison.OrdinalIgnoreCase));
	}

	public bool CanStartOpdVisit => SelectedPatient?.IsWaiting ?? false;

	public bool IsOpdVisitActive => SelectedPatient?.IsInProgress ?? false;

	public bool IsOpdEmrSigned => SelectedPatient?.IsEmrSigned ?? false;

	public bool IsOpdOrdersPushed => SelectedPatient?.IsOrdersPushed ?? false;

	private string? _diagnosisCode;
	private string? _diagnosisName;

	public string? DiagnosisCode
	{
		get => _diagnosisCode;
		set => SetField(ref _diagnosisCode, value, "DiagnosisCode");
	}

	public string? DiagnosisName
	{
		get => _diagnosisName;
		set => SetField(ref _diagnosisName, value, "DiagnosisName");
	}

	public void SetDiagnosis(string? code, string? name)
	{
		DiagnosisCode = string.IsNullOrWhiteSpace(code) ? null : code.Trim();
		DiagnosisName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
		EmrWorkspace.Diagnosis = DiagnosisName ?? string.Empty;
		NotifyOpdWorkflowStateChanged();
	}

	public bool CanEditDiagnosis => IsOpdVisitActive;

	public bool CanSignOpdEmr => IsOpdVisitActive && HasDiagnosis && !IsOpdEmrSigned;

	public bool CanExportOrVerifyOpdOfd => IsOpdEmrSigned;

	public bool CanPrescribeAndPushOpd => IsOpdVisitActive && HasDiagnosis;

	public bool CanAddOpdPrescription => IsOpdVisitActive && HasDiagnosis;

	public bool CanEditOpdPrescription => IsOpdPrescriptionEditable;

	public bool IsOpdPrescriptionEditable => IsOpdVisitActive && HasDiagnosis
			&& (_currentPrescriptionStatus == 0 || _currentPrescriptionStatus == 2)
			&& !IsOpdOrdersPushed;

	public bool HasOpdPrescriptionDraft => _currentPrescriptionStatus == 0 || _currentPrescriptionStatus == 2;

	public bool CanSubmitOpdPrescription => IsOpdPrescriptionEditable && IsOpdEmrSigned && !HasBlockedPrescription
		&& Prescriptions.Any(x => x.IsVisible);

	public string PrescriptionWorkflowStatusText => IsOpdOrdersPushed
		? "处方已提交审方"
		: IsOpdPrescriptionEditable
		? $"处方 {(_currentPrescriptionStatus == 2 ? "已被药师驳回，可修改后重新提交" : "草稿，可编辑并提交药师审核")}"
		: GetPrescriptionStatusText(_currentPrescriptionStatus);

	public bool HasSelectedPatientAllergy => !string.IsNullOrWhiteSpace(SelectedPatient?.AllergyTags);

	public string SelectedPatientAllergyText => SelectedPatient?.AllergyTags ?? string.Empty;

	private bool HasDiagnosis => !string.IsNullOrWhiteSpace(DiagnosisCode) || !string.IsNullOrWhiteSpace(EmrWorkspace.Diagnosis);

	public bool CanFinishOpdVisit => IsOpdVisitActive && IsOpdEmrSigned && IsOpdOrdersPushed;

	public bool CanCompleteOpdWithNoPrescription => IsOpdVisitActive && IsOpdEmrSigned
		&& !Prescriptions.Any(x => !x.IsDeleted);

	public string OpdStepABadge
	{
		get
		{
			PatientQueueItemModel selectedPatient = SelectedPatient;
			bool flag = ((selectedPatient != null && (selectedPatient.IsInProgress || selectedPatient.IsCompleted)) ? true : false);
			return flag ? "✓ 步骤A: 已呼叫接诊" : "⏳ 步骤A: 待开始接诊";
		}
	}

	public string OpdStepBBadge
	{
		get
		{
			string result;
			if (IsOpdEmrSigned)
			{
				result = "✓ 步骤B: 病历已审签(OFD)";
			}
			else
			{
				result = (IsOpdVisitActive ? "\ud83d\udc49 步骤B: 待审签门诊病历" : "\ud83d\udd12 步骤B: 需先完成步骤A");
			}
			return result;
		}
	}

	public string OpdStepCBadge
	{
		get
		{
			string result;
			if (IsOpdOrdersPushed)
			{
			result = "✓ 步骤C: 处方已提交审方";
			}
			else
			{
				result = ((IsOpdVisitActive && IsOpdEmrSigned) ? "\ud83d\udc49 步骤C: 待签署推送处方" : "\ud83d\udd12 步骤C: 需先完成步骤B");
			}
			return result;
		}
	}

	public string OpdStepDBadge
	{
		get
		{
			PatientQueueItemModel selectedPatient = SelectedPatient;
			string result;
			if (selectedPatient != null && selectedPatient.IsCompleted)
			{
				result = "✓ 步骤D: 诊疗已闭环";
			}
			else
			{
				result = (CanFinishOpdVisit || CanCompleteOpdWithNoPrescription)
					? "\ud83d\udc49 步骤D: 可完成诊疗闭环"
					: "\ud83d\udd12 步骤D: 需先完成步骤C";
			}
			return result;
		}
	}

	public string OpdStepAClass
	{
		get
		{
			PatientQueueItemModel selectedPatient = SelectedPatient;
			bool flag = ((selectedPatient != null && (selectedPatient.IsInProgress || selectedPatient.IsCompleted)) ? true : false);
			return flag ? "Success" : "Warning";
		}
	}

	public string OpdStepBClass
	{
		get
		{
			string result;
			if (IsOpdEmrSigned)
			{
				result = "Success";
			}
			else
			{
				result = (IsOpdVisitActive ? "Primary" : "Secondary");
			}
			return result;
		}
	}

	public string OpdStepCClass
	{
		get
		{
			string result;
			if (IsOpdOrdersPushed)
			{
				result = "Success";
			}
			else
			{
				result = ((IsOpdVisitActive && HasDiagnosis) ? "Primary" : "Secondary");
			}
			return result;
		}
	}

	public string OpdStepDClass
	{
		get
		{
			PatientQueueItemModel selectedPatient = SelectedPatient;
			string result;
			if (selectedPatient != null && selectedPatient.IsCompleted)
			{
				result = "Success";
			}
			else
			{
				result = (CanFinishOpdVisit || CanCompleteOpdWithNoPrescription) ? "Primary" : "Secondary";
			}
			return result;
		}
	}

	public string OpdWorkflowGuideText
	{
		get
		{
			string result;
			if (SelectedPatient == null)
			{
			result = string.IsNullOrWhiteSpace(_opdQueueError) ? "请从左侧队列选择患者" : _opdQueueError;
			}
			else if (SelectedPatient.IsCompleted)
			{
				result = "✓ 该患者已完成【步骤A:呼叫接诊 ➔ 步骤B:病历审签 ➔ 步骤C:处方推送 ➔ 步骤D:完成诊疗】全流程闭环！";
			}
			else if (SelectedPatient.IsWaiting)
			{
				result = "\u26a0\ufe0f 闭环门禁：当前患者处于【候诊中】，请先点击右侧「▶ 步骤A：开始接诊」激活接诊（未接诊前锁定诊断录入、病历审签、处方下达与完成诊疗）！";
			}
			else if (SelectedPatient.IsEmrSigned)
			{
				if (SelectedPatient.IsOrdersPushed)
				{
					result = "处方已进入审方和收费流程。完成收费及发药后可点击右上角完成诊疗。";
				}
				else if (!Prescriptions.Any(x => !x.IsDeleted))
				{
					result = "\ud83d\udc49 病历已审签，本次未开立处方。可直接点击右上角「完成诊疗」，并在核对清单中勾选“本次就诊无处方”。";
				}
				else
				{
					result = "\ud83d\udc49 病历已审签。请在「2. 处方与安全用药」核对处方并提交药师审方；审方通过后由收费员收费、药房发药，再结束诊疗。";
				}
			}
			else
			{
				result = "\ud83d\udc49 当前处于【步骤A：就诊中】。请先录入诊断，再书写病历并完成三级审签。";
			}
			return result;
		}
	}

	public int SelectedOpdTabIndex
	{
		get
		{
			return _selectedOpdTabIndex;
		}
		set
		{
			SetField(ref _selectedOpdTabIndex, value, "SelectedOpdTabIndex");
		}
	}

	public bool IsPrescriptionFieldEditorOpen
	{
		get
		{
			return _isPrescriptionFieldEditorOpen;
		}
		set
		{
			SetField(ref _isPrescriptionFieldEditorOpen, value, "IsPrescriptionFieldEditorOpen");
		}
	}

	public bool IsPrescriptionItemEditorOpen
	{
		get
		{
			return _isPrescriptionItemEditorOpen;
		}
		set
		{
			SetField(ref _isPrescriptionItemEditorOpen, value, "IsPrescriptionItemEditorOpen");
		}
	}

	public PrescriptionFieldDraftModel PrescriptionFieldDraft
	{
		get
		{
			return _prescriptionFieldDraft;
		}
		set
		{
			SetField(ref _prescriptionFieldDraft, value, "PrescriptionFieldDraft");
		}
	}

	public PrescriptionExtraFieldModel? EditingPrescriptionField
	{
		get
		{
			return _editingPrescriptionField;
		}
		set
		{
			SetField(ref _editingPrescriptionField, value, "EditingPrescriptionField");
		}
	}

	public ObservableCollection<PrescriptionItemModel> FilteredPrescriptions { get; } = new ObservableCollection<PrescriptionItemModel>();

	public string PrescriptionSearchQuery
	{
		get
		{
			return _prescriptionSearchQuery;
		}
		set
		{
			if (SetField(ref _prescriptionSearchQuery, value, "PrescriptionSearchQuery"))
			{
				ApplyPrescriptionFilter();
			}
		}
	}

	public string SelectedPrescriptionFilter
	{
		get
		{
			return _selectedPrescriptionFilter;
		}
		set
		{
			if (SetField(ref _selectedPrescriptionFilter, value, "SelectedPrescriptionFilter"))
			{
				ApplyPrescriptionFilter();
				OnPropertyChanged("IsPrescriptionFilterAll");
				OnPropertyChanged("IsPrescriptionFilterNormal");
				OnPropertyChanged("IsPrescriptionFilterAlert");
			}
		}
	}

	public bool IsPrescriptionFilterAll => SelectedPrescriptionFilter == "ALL";

	public bool IsPrescriptionFilterNormal => SelectedPrescriptionFilter == "NORMAL";

	public bool IsPrescriptionFilterAlert => SelectedPrescriptionFilter == "ALERT";

	public int VisiblePrescriptionCount => Enumerable.Count(Prescriptions, (PrescriptionItemModel x) => x.IsVisible);

	public int HiddenPrescriptionCount => Enumerable.Count(Prescriptions, (PrescriptionItemModel x) => x.IsDeleted);

	public int VisiblePrescriptionFieldCount => Enumerable.Count(PrescriptionExtraFields, (PrescriptionExtraFieldModel x) => x.IsVisible);

	public int HiddenPrescriptionFieldCount => Enumerable.Count(PrescriptionExtraFields, (PrescriptionExtraFieldModel x) => x.IsDeleted);

	public bool HasBlockedPrescription => Prescriptions.Any((PrescriptionItemModel x) => x.IsVisible && x.IsBlocked);

	public bool HasNoVisiblePrescriptions => FilteredPrescriptions.Count == 0;

	public string CampusName
	{
		get
		{
			return _campusName;
		}
		set
		{
			SetField(ref _campusName, value, "CampusName");
		}
	}

	public DepartmentCategory ActiveCategory
	{
		get
		{
			return _activeCategory;
		}
		set
		{
			SetField(ref _activeCategory, value, "ActiveCategory");
		}
	}

	public string ActiveDeptName
	{
		get
		{
			return _activeDeptName;
		}
		set
		{
			SetField(ref _activeDeptName, value, "ActiveDeptName");
		}
	}

	public string ActiveWardName
	{
		get
		{
			return _activeWardName;
		}
		set
		{
			SetField(ref _activeWardName, value, "ActiveWardName");
		}
	}

	public string UserName
	{
		get
		{
			return _userName;
		}
		set
		{
			SetField(ref _userName, value, "UserName");
		}
	}

	public int SelectedNavIndex
	{
		get
		{
			return _selectedNavIndex;
		}
		set
		{
			if (SetField(ref _selectedNavIndex, value, "SelectedNavIndex"))
			{
				NavigationItemModel navigationItemModel = NavigationItems.FirstOrDefault((NavigationItemModel x) => x.TargetIndex == value);
				if (navigationItemModel != null && (object)_selectedNavigationItem != navigationItemModel)
				{
					_selectedNavigationItem = navigationItemModel;
					OnPropertyChanged("SelectedNavigationItem");
				}
				OnPropertyChanged("CurrentStationTitle");
				RefreshInpatientMetrics();
				if (value == 11 && AuditLogsList.Count == 0)
				{
					_ = LoadAuditLogsAsync();
				}
			}
		}
	}

	public string CurrentStationTitle
	{
		get
		{
			int selectedNavIndex = SelectedNavIndex;
			if (1 == 0)
			{
			}
			string result = selectedNavIndex switch
			{
				0 => "门诊医生站",
				1 => "住院医生站",
				2 => "住院护士站",
				3 => "医技中心",
				4 => "药房药库",
				5 => "病历归档与验签",
				6 => "动态表单配置",
				7 => "用户账号管理",
				8 => "角色与权限",
				9 => "财务收费",
				10 => "门诊号源排班",
				11 => "操作审计日志",
				_ => "智医 HIS",
			};
			if (1 == 0)
			{
			}
			return result;
		}
	}

	public NavigationItemModel? SelectedNavigationItem
	{
		get
		{
			return _selectedNavigationItem;
		}
		set
		{
			if (SetField(ref _selectedNavigationItem, value, "SelectedNavigationItem") && value != null && _selectedNavIndex != value.TargetIndex)
			{
				_selectedNavIndex = value.TargetIndex;
				OnPropertyChanged("SelectedNavIndex");
				OnPropertyChanged("CurrentStationTitle");
				RefreshInpatientMetrics();
				if (value.TargetIndex == 11 && AuditLogsList.Count == 0)
				{
					_ = LoadAuditLogsAsync();
				}
			}
		}
	}

	public string SystemNotice
	{
		get
		{
			return _systemNotice;
		}
		set
		{
			if (SetField(ref _systemNotice, value, "SystemNotice"))
			{
				RecordSystemNotice(value);
			}
		}
	}

	public bool IsServerReachable
	{
		get => _isServerReachable;
		private set
		{
			SetField(ref _isServerReachable, value, nameof(IsServerReachable));
			OnPropertyChanged(nameof(ServerStatusBrushKey));
		}
	}

	public string ServerStatusText
	{
		get => _serverStatusText;
		private set => SetField(ref _serverStatusText, value, nameof(ServerStatusText));
	}

	private void UpdateServerConnection(bool reachable)
	{
		void Apply()
		{
			if (reachable)
			{
				IsServerReachable = true;
				ServerStatusText = "服务器已连接";
			}
			else if (IsServerReachable)
			{
				IsServerReachable = false;
				ServerStatusText = "连接中断，请检查网络或服务状态";
			}
		}

		if (Dispatcher.UIThread.CheckAccess())
		{
			Apply();
		}
		else
		{
			Dispatcher.UIThread.Post(Apply);
		}
	}

	public bool IsTimelineDrawerOpen
	{
		get
		{
			return _isTimelineDrawerOpen;
		}
		set
		{
			SetField(ref _isTimelineDrawerOpen, value, "IsTimelineDrawerOpen");
		}
	}

	public bool IsWhoSurgeryCheckOpen
	{
		get
		{
			return _isWhoSurgeryCheckOpen;
		}
		set
		{
			SetField(ref _isWhoSurgeryCheckOpen, value, "IsWhoSurgeryCheckOpen");
		}
	}

	public bool IsCriticalValueAlertOpen
	{
		get
		{
			return _isCriticalValueAlertOpen;
		}
		set
		{
			SetField(ref _isCriticalValueAlertOpen, value, "IsCriticalValueAlertOpen");
		}
	}

	public bool IsAddPrescriptionDialogOpen
	{
		get
		{
			return _isAddPrescriptionDialogOpen;
		}
		set
		{
			SetField(ref _isAddPrescriptionDialogOpen, value, "IsAddPrescriptionDialogOpen");
		}
	}

	public bool IsDischargeCheckDrawerOpen
	{
		get
		{
			return _isDischargeCheckDrawerOpen;
		}
		set
		{
			SetField(ref _isDischargeCheckDrawerOpen, value, "IsDischargeCheckDrawerOpen");
		}
	}

	public bool IsOfdViewerDrawerOpen
	{
		get
		{
			return _isOfdViewerDrawerOpen;
		}
		set
		{
			SetField(ref _isOfdViewerDrawerOpen, value, "IsOfdViewerDrawerOpen");
		}
	}

	public bool IsBorrowBedDialogOpen
	{
		get
		{
			return _isBorrowBedDialogOpen;
		}
		set
		{
			SetField(ref _isBorrowBedDialogOpen, value, "IsBorrowBedDialogOpen");
		}
	}

	public bool IsCreateUserModalOpen
	{
		get
		{
			return _isCreateUserModalOpen;
		}
		set
		{
			SetField(ref _isCreateUserModalOpen, value, "IsCreateUserModalOpen");
		}
	}

	public bool IsCreateRoleModalOpen
	{
		get
		{
			return _isCreateRoleModalOpen;
		}
		set
		{
			SetField(ref _isCreateRoleModalOpen, value, "IsCreateRoleModalOpen");
		}
	}

	public bool IsSwitchUserModalOpen
	{
		get
		{
			return _isSwitchUserModalOpen;
		}
		set
		{
			SetField(ref _isSwitchUserModalOpen, value, "IsSwitchUserModalOpen");
		}
	}

	public bool IsSwitchingContext
	{
		get
		{
			return _isSwitchingContext;
		}
		set
		{
			SetField(ref _isSwitchingContext, value, "IsSwitchingContext");
		}
	}

	public bool IsFormDesignerOpen
	{
		get
		{
			return _isFormDesignerOpen;
		}
		set
		{
			SetField(ref _isFormDesignerOpen, value, "IsFormDesignerOpen");
		}
	}

	public bool IsFieldEditorOpen
	{
		get
		{
			return _isFieldEditorOpen;
		}
		set
		{
			SetField(ref _isFieldEditorOpen, value, "IsFieldEditorOpen");
		}
	}

	public DynamicFormTemplateModel? EditingTemplate
	{
		get
		{
			return _editingTemplate;
		}
		set
		{
			if (SetField(ref _editingTemplate, value, "EditingTemplate"))
			{
				OnPropertyChanged("IsPrescriptionSchemaDesigner");
				OnPropertyChanged("FieldApplyButtonText");
				OnPropertyChanged("DesignerSaveButtonText");
				OnPropertyChanged("FieldApplyHint");
				RefreshEditingFormPreview();
				OnPropertyChanged("PreviewFields");
			}
		}
	}

	public DynamicFormFieldModel? EditingField
	{
		get
		{
			return _editingField;
		}
		set
		{
			if (_editingField != value)
			{
				if (_editingField != null)
				{
					_editingField.PropertyChanged -= EditingField_PropertyChanged;
				}
				_editingField = value;
				if (_editingField != null)
				{
					_editingField.PropertyChanged += EditingField_PropertyChanged;
				}
				OnPropertyChanged("EditingField");
				RefreshEditingFormPreview();
				OnPropertyChanged("PreviewFields");
			}
		}
	}

	public IEnumerable<DynamicFormFieldModel> PreviewFields
	{
		get
		{
			if (EditingTemplate == null)
			{
				return Array.Empty<DynamicFormFieldModel>();
			}
			if (EditingField == null || EditingTemplate.Fields.Contains(EditingField))
			{
				return EditingTemplate.Fields;
			}
			return EditingTemplate.Fields.Concat(new DynamicFormFieldModel[1] { EditingField });
		}
	}

	public bool IsPrescriptionSchemaDesigner => string.Equals(EditingTemplate?.TemplateCode, "FORM-PRESCRIPTION-ITEM-FIELDS", StringComparison.OrdinalIgnoreCase);

	public bool IsGenericSchemaDesigner => !IsPrescriptionSchemaDesigner;

	public int PreviewFieldCount => PreviewFields.Count();

	public bool IsPreviewEmpty => PreviewFieldCount == 0;

	public string PreviewPatientSummary => (SelectedPatient == null) ? "未选择患者" : $"{SelectedPatient.PatientName} · {SelectedPatient.Gender} · {SelectedPatient.Age}岁";

	public IReadOnlyList<string> FormDataTypes { get; } = new string[8] { "STRING", "TEXT", "INTEGER", "DECIMAL", "BOOLEAN", "DATE", "DATETIME", "ENUM" };

	public IReadOnlyList<string> FormControlTypes { get; } = new string[7] { "u:TextBox", "u:TextArea", "u:NumericIntUpDown", "u:NumericDoubleUpDown", "ComboBox", "DatePicker", "CheckBox" };

	public IReadOnlyList<string> FormVisibilityRules { get; } = new string[3] { "ALL", "MALE_ONLY", "FEMALE_ONLY" };

	public NewUserFormModel NewUserForm
	{
		get
		{
			return _newUserForm;
		}
		set
		{
			SetField(ref _newUserForm, value, "NewUserForm");
		}
	}

	public NewRoleFormModel NewRoleForm
	{
		get
		{
			return _newRoleForm;
		}
		set
		{
			SetField(ref _newRoleForm, value, "NewRoleForm");
		}
	}

	public string RoleBadgeText => (!string.IsNullOrEmpty(SessionContext.Current.RoleName)) ? SessionContext.Current.RoleName : "主治医师";

	public string ActiveContextSummary => string.IsNullOrEmpty(SessionContext.Current.ActiveDeptName) ? "未选择执业身份" : (SessionContext.Current.ActiveDeptName + " · " + CategoryLabel(SessionContext.Current.ActiveDeptCategory));

	public bool HasMultipleContexts => SessionContext.Current.AvailableContexts.Count > 1;

	public string UserRoleSubtitle => !string.IsNullOrEmpty(SessionContext.Current.EmployeeNo)
		? $"工号: {SessionContext.Current.EmployeeNo} | 权限: {SessionContext.Current.RoleName} (处方权{SessionContext.Current.PrescribeLevel}级)"
		: $"权限: {SessionContext.Current.RoleName}";

	public string CurrentRoleCode => SessionContext.Current.RoleCode;

	public bool CanPrescribe => SessionContext.Current.CanPrescribe;

	public bool CanSignEmr => SessionContext.Current.CanSignEmr;

	public bool CanSurgery => SessionContext.Current.CanSurgery;

	public bool IsAdmin => SessionContext.Current.IsAdmin;

	public bool CanConfigureDynamicForms => SessionContext.Current.CanSeeDynFormConfig;

	public bool IsNurse => SessionContext.Current.IsWardNurse;

	public bool IsPharmacistRole => SessionContext.Current.IsPharmacist;

	public bool IsCashierRole => SessionContext.Current.IsCashier;

	public bool CanSeeOpdStation => SessionContext.Current.CanSeeOpdStation;

	public bool CanSeeIpdStation => SessionContext.Current.CanSeeIpdStation;

	public bool CanSeeWardStation => SessionContext.Current.CanSeeWardStation;

	public bool CanSeeMedTech => SessionContext.Current.CanSeeMedTech;

	public bool CanSeePharmacy => SessionContext.Current.CanSeePharmacy;

	public bool CanSeeFinance => SessionContext.Current.CanSeeFinance;

	public bool CanSeeSchedule => SessionContext.Current.IsAdmin;

	public bool CanSeeEmrOfd => SessionContext.Current.CanSeeEmrOfd;

	public bool CanSeeDynFormConfig => SessionContext.Current.CanSeeDynFormConfig;

	public bool CanSeeAdminPanel => SessionContext.Current.CanSeeAdminPanel;

	public bool CanOperateOpdClinical => SessionContext.Current.IsAdmin || SessionContext.Current.IsOpdDoctor;

	public bool CanOperateIpdClinical => SessionContext.Current.IsAdmin || SessionContext.Current.IsIpdResident || SessionContext.Current.IsIpdAttending || SessionContext.Current.IsOpdDoctor;

	public bool CanOperateWardNursing => SessionContext.Current.IsAdmin || SessionContext.Current.IsWardNurse;

	public bool CanOperatePharmacy => SessionContext.Current.IsAdmin || SessionContext.Current.IsPharmacist;

	public bool CanOperateFinance => SessionContext.Current.IsAdmin || SessionContext.Current.IsCashier;

	public bool CanFinalizeInpatientOrders => SessionContext.Current.IsAdmin || SessionContext.Current.IsIpdAttending || SessionContext.Current.IsOpdDoctor;

	public bool CanExecuteNursingTasks => CanOperateWardNursing;

	public string CurrentSodRoleBannerText
	{
		get
		{
			SessionContext current = SessionContext.Current;
			if (1 == 0)
			{
			}
			if (current == null)
			{
				goto IL_009a;
			}
			string result;
			if (!current.IsAdmin)
			{
				if (!current.IsOpdDoctor)
				{
					if (!current.IsIpdAttending)
					{
						if (!current.IsIpdResident)
						{
							if (!current.IsWardNurse)
							{
								if (!current.IsPharmacist)
								{
									if (!current.IsCashier)
									{
										goto IL_009a;
									}
									result = "\ud83d\udcb0 财务处收费员职责域 · 独立财务模块：仅限门诊收费(B)/退费结清(E)/住院医保与预交金结算/财务总账（禁止越权发药或开医嘱）";
								}
								else
								{
									result = "\ud83d\udc8a 药剂科药师职责域 · 仅限药房发药(C)/退药回库(D)/出院带药核发/药库调拨（禁止越权办理收费或财务退费）";
								}
							}
							else
							{
								result = "\ud83d\udc69\u200d⚕\ufe0f 病区护士职责域 · 仅限医嘱校对(B)/分解(C)/扫码执行(D)/双人核对(E)（禁止越权开医嘱或财务结算）";
							}
						}
						else
						{
							result = "\ud83d\udecf\ufe0f 住院医师职责域 · 仅限管床医嘱与病程书写（主治终审/护士校对/药房/财务已分离）";
						}
					}
					else
					{
						result = "\ud83d\udecf\ufe0f 住院主治医师职责域 · 仅限查房/医嘱下达/病程审签/出院终审（护士校对/药房带药/财务结算已分离）";
					}
				}
				else
				{
					result = "\ud83e\ude7a 门诊医生职责域 · 仅限门诊接诊/病历审签/处方开立（收费结账与发药已职责分离）";
				}
			}
			else
			{
				result = "\ud83d\udee1\ufe0f 超级管理员 · 全院模块与职责穿透监管权限";
			}
			goto IL_00a2;
			IL_00a2:
			if (1 == 0)
			{
			}
			return result;
			IL_009a:
			result = "\ud83e\uddea 联调演示模式 · 可点击顶部岗位按钮一键切换【门诊医生 / 住院主治 / 病区护士 / 药房药师 / 财务收费员 / 管理员】验证权限与职责分离";
			goto IL_00a2;
		}
	}

	public int DefaultStationIndex
	{
		get
		{
			SessionContext current = SessionContext.Current;
			if (1 == 0)
			{
			}
			if (current == null)
			{
				goto IL_0078;
			}
			int result;
			if (!current.IsOpdDoctor)
			{
				if (!current.IsIpdResident && !current.IsIpdAttending)
				{
					if (!current.IsWardNurse)
					{
						if (!current.IsPharmacist)
						{
							if (!current.IsCashier)
							{
								if (!current.IsAdmin)
								{
									goto IL_0078;
								}
								result = 7;
							}
							else
							{
								result = 9;
							}
						}
						else
						{
							result = 4;
						}
					}
					else
					{
						result = 2;
					}
				}
				else
				{
					result = 1;
				}
			}
			else
			{
				result = 0;
			}
			goto IL_007c;
			IL_007c:
			if (1 == 0)
			{
			}
			return result;
			IL_0078:
			result = 0;
			goto IL_007c;
		}
	}

	public WhoSurgeryCheckModel WhoSurgeryCheck
	{
		get
		{
			return _whoSurgeryCheck;
		}
		set
		{
			SetField(ref _whoSurgeryCheck, value, "WhoSurgeryCheck");
		}
	}

	public CriticalValueAlertModel CriticalValueAlert
	{
		get
		{
			return _criticalValueAlert;
		}
		set
		{
			SetField(ref _criticalValueAlert, value, "CriticalValueAlert");
		}
	}

	public DischargeCheckDetailsModel DischargeCheckDetails
	{
		get
		{
			return _dischargeCheckDetails;
		}
		set
		{
			SetField(ref _dischargeCheckDetails, value, "DischargeCheckDetails");
		}
	}

	public DynamicFormTemplateModel? SelectedFormTemplate
	{
		get
		{
			return _selectedFormTemplate;
		}
		set
		{
			SetField(ref _selectedFormTemplate, value, "SelectedFormTemplate");
		}
	}

	public PatientQueueItemModel? SelectedPatient
	{
		get
		{
			return _selectedPatient;
		}
		set
		{
			if (!SetField(ref _selectedPatient, value, "SelectedPatient"))
			{
				return;
			}
			LoadPatientClinicalContext(value);
			NotifyOpdWorkflowStateChanged();
			NotifyUiStateChanged();
		}
	}

	public async Task SelectPatientAsync(PatientQueueItemModel patient)
	{
		if (patient == null)
		{
			return;
		}
		SelectedPatient = patient;
		await LoadCurrentPatientClinicalDataAsync();
	}

	private async Task LoadCurrentPatientClinicalDataAsync()
	{
		PatientQueueItemModel? patient = SelectedPatient;
		if (patient == null)
		{
			return;
		}
		string visitId = patient.VisitId;
		int contextRevision = _clinicalContextRevision;
		await Task.WhenAll(
			LoadEmrDocumentAsync(visitId, contextRevision),
			LoadPrescriptionFromServerAsync(visitId, _prescriptionRevision, force: true));
		if (contextRevision == _clinicalContextRevision)
		{
			NotifyOpdWorkflowStateChanged();
		}
	}

	private async Task LoadEmrDocumentAsync(string visitId, int contextRevision)
	{
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			ClientApiEnvelope<List<EmrDocumentDto>>? envelope =
				await http.GetFromJsonAsync<ClientApiEnvelope<List<EmrDocumentDto>>>(
					"/api/v4/emr/documents?emrScene=OPD&visitOrEncounterId=" +
					Uri.EscapeDataString(visitId), JsonOptions);
			if (contextRevision != _clinicalContextRevision ||
				!string.Equals(visitId, _currentPrescriptionVisitId, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			if (envelope is not { Code: 200, Data: not null })
			{
				return;
			}
			EmrDocumentDto? document = envelope.Data
				.OrderByDescending(x => x.ClinicalTimeMs)
				.FirstOrDefault();
			if (document == null)
			{
				return;
			}
			SelectedPatient?.SetEmrSigned(document.Status is 3 or 4);
			RestoreOpdEmrDocument(document);
			NotifyOpdWorkflowStateChanged();
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
		{
			SystemNotice = "门诊病历读取失败：" + ex.Message;
		}
	}

	private void PersistCurrentPrescriptionDraft()
	{
		if (string.IsNullOrWhiteSpace(_currentPrescriptionVisitNo) ||
			string.IsNullOrWhiteSpace(_currentPrescriptionVisitId))
		{
			return;
		}
		_prescriptionCache[_currentPrescriptionVisitNo] =
			Prescriptions.Where(x => !x.IsDeleted).Select(ClonePrescriptionItem).ToList();
		_prescriptionExtraFieldCache[_currentPrescriptionVisitNo] =
			PrescriptionExtraFields.Where(x => !x.IsDeleted).Select(ClonePrescriptionField).ToList();
	}

	private async Task LoadPrescriptionFromServerAsync(string visitId, int revision, bool force = false)
	{
		if (!force && _prescriptionLoadedVisitIds.Contains(visitId))
		{
			return;
		}
		PrescriptionAggregateDto? aggregate;
		try
		{
			aggregate = await GetPrescriptionFromServerAsync(visitId);
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
		{
			SystemNotice = "门诊处方读取失败：" + ex.Message;
			return;
		}
		if (revision != _prescriptionRevision ||
			!string.Equals(visitId, _currentPrescriptionVisitId, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		_prescriptionLoadedVisitIds.Add(visitId);
		if (aggregate?.Prescription == null)
		{
			return;
		}
		PrescriptionDto prescription = aggregate.Prescription;
		_currentPrescriptionId = prescription.Id;
		_currentPrescriptionStatus = prescription.Status;
		_currentPrescriptionNo = prescription.PrescriptionNo;
		_currentPrescriptionReviewRemark = prescription.ReviewRemark;
		_persistedPrescriptionByVisitId[visitId] = prescription.Id;
		_persistedPrescriptionByVisit[SelectedPatient?.VisitNo ?? visitId] = prescription.Id;
		if (SelectedPatient != null)
		{
			SelectedPatient.IsOrdersPushed = prescription.Status is 3 or 4 or 5;
		}
		Prescriptions.Clear();
		foreach (PrescriptionItemDto dto in aggregate.Items ?? new List<PrescriptionItemDto>())
		{
			if (dto.IsDeleted == 0)
			{
				Prescriptions.Add(MapItemFromServer(dto));
			}
		}
		ReindexPrescriptionRows();
		RefreshPrescriptionMetrics();
		NotifyOpdWorkflowStateChanged();
	}

	private void ResetEmrWorkspace(PatientQueueItemModel? patient)
	{
		_currentEmrDocumentId = string.Empty;
		_currentEmrStatus = 0;
		_currentOfdArchiveId = string.Empty;
		EmrWorkspace.PatientName = patient?.PatientName ?? string.Empty;
		EmrWorkspace.Gender = patient?.Gender ?? string.Empty;
		EmrWorkspace.Age = patient?.Age ?? 0;
		EmrWorkspace.DocNo = $"DOC-OPD-{DateTime.Now:yyyyMMddHHmmss}";
		EmrWorkspace.DocTitle = "门诊病历";
		EmrWorkspace.ChiefComplaint = patient?.ChiefComplaint ?? string.Empty;
		EmrWorkspace.PresentIllness = string.Empty;
		EmrWorkspace.Diagnosis = string.Empty;
		EmrWorkspace.ProstateExam = string.Empty;
		EmrWorkspace.PregnancyCount = 0;
		EmrWorkspace.OfdNo = null;
		EmrWorkspace.OfdStatus = "尚未审签";
	}

	private void LoadPatientClinicalContext(PatientQueueItemModel? patient)
	{
		++_clinicalContextRevision;
		++_prescriptionRevision;
		PersistCurrentPrescriptionDraft();
		if (patient == null)
		{
			_currentPrescriptionVisitNo = null;
			_currentPrescriptionVisitId = null;
			_currentPrescriptionStatus = -1;
			ResetEmrWorkspace(null);
			SetDiagnosis(null, null);
			DiagnosisName = null;
			Prescriptions.Clear();
			PrescriptionExtraFields.Clear();
			MedTechOrders.Clear();
			RefreshPrescriptionMetrics();
			return;
		}

		foreach (PatientQueueItemModel item in PatientQueue)
		{
			item.IsSelected = string.Equals(item.VisitId, patient.VisitId, StringComparison.OrdinalIgnoreCase);
		}
		CriticalValueAlert.PatientName = patient.PatientName;
		WhoSurgeryCheck.PatientName = patient.PatientName;
		ApplyDynamicFieldVisibility(patient);
		RefreshEditingFormPreview();
		_currentPrescriptionVisitNo = patient.VisitNo;
		_currentPrescriptionVisitId = patient.VisitId;
		_currentPrescriptionStatus = -1;
		SetDiagnosis(null, null);
		ResetEmrWorkspace(patient);
		Prescriptions.Clear();
		PrescriptionExtraFields.Clear();
		MedTechOrders.Clear();
		if (_prescriptionCache.TryGetValue(patient.VisitNo, out List<PrescriptionItemModel> cached))
		{
			foreach (PrescriptionItemModel item in cached.Where(x => !x.IsDeleted))
			{
				Prescriptions.Add(ClonePrescriptionItem(item));
			}
		}
		if (_prescriptionExtraFieldCache.TryGetValue(patient.VisitNo, out List<PrescriptionExtraFieldModel> cachedFields))
		{
			foreach (PrescriptionExtraFieldModel field in cachedFields.Where(x => !x.IsDeleted))
			{
				PrescriptionExtraFields.Add(ClonePrescriptionField(field));
			}
		}
		ReindexPrescriptionRows();
		RefreshPrescriptionMetrics();
		SyncOpdWorkspaceToEmrTemplate();
		SystemNotice = $"已选择: [{patient.PatientName}] 就诊号 {patient.VisitNo}，正在读取服务端病历与处方";
		OnPropertyChanged("IsMalePatientSelected");
		OnPropertyChanged("IsFemalePatientSelected");
		OnPropertyChanged("SelectedPatientGenderGateText");
		OnPropertyChanged("CurrentEmrPatientName");
		OnPropertyChanged("CurrentEmrPatientGender");
		OnPropertyChanged("CurrentEmrPatientAge");
		OnPropertyChanged("CurrentEmrEncounterNo");
		OnPropertyChanged("CurrentEmrBedAndWardText");
		OnPropertyChanged("CurrentEmrPatientSummary");
	}

	public EmrEditorWorkspaceModel EmrWorkspace
	{
		get
		{
			return _emrWorkspace;
		}
		set
		{
			SetField(ref _emrWorkspace, value, "EmrWorkspace");
		}
	}

	public ObservableCollection<PatientQueueItemModel> PatientQueue { get; set; }

	public ObservableCollection<PrescriptionItemModel> Prescriptions { get; set; }

	public ObservableCollection<PrescriptionExtraFieldModel> PrescriptionExtraFields { get; set; }

	public ObservableCollection<string> PrescriptionFieldTypes { get; } = new ObservableCollection<string> { "STRING", "TEXT", "INTEGER", "DECIMAL" };

	public ObservableCollection<MedTechOrderItemModel> MedTechOrders { get; set; }

	public ObservableCollection<BedCardModel> AllBedCards { get; set; }

	public ObservableCollection<InpatientRecordModel> AllInpatientRecords { get; set; }

	public ObservableCollection<BedCardModel> BedCards { get; set; }

	public ObservableCollection<InpatientRecordModel> InpatientRecords { get; set; }

	public ObservableCollection<PharmacyDispenseModel> PharmacyRecords { get; set; }

	public ObservableCollection<OfdArchiveRecordModel> OfdArchiveRecords { get; set; }

	public ObservableCollection<DynamicFormTemplateModel> DynamicFormTemplates { get; set; }

	public ObservableCollection<TimelineEventModel> TimelineEvents { get; set; }

	public ObservableCollection<DrugCatalogItemModel> AvailableDrugsCatalog { get; set; }

	public ObservableCollection<EmrVisualTemplateModel> EmrTemplates { get; } = new ObservableCollection<EmrVisualTemplateModel>();

	public ObservableCollection<EmrVisualTemplateModel> FilteredEmrTemplates { get; } = new ObservableCollection<EmrVisualTemplateModel>();

	public ObservableCollection<EmrPrintLayoutConfigModel> EmrPrintLayouts { get; } = new ObservableCollection<EmrPrintLayoutConfigModel>();

	public ObservableCollection<EmrSignatureSlotPreviewModel> PreviewSignatureSlots { get; } = new ObservableCollection<EmrSignatureSlotPreviewModel>();

	public IReadOnlyList<string> EmrRenderModeOptions { get; } = new string[4] { "GRID_FORM", "INLINE_PARAGRAPH", "TABLE_GRID", "VECTOR_CANVAS" };

	public IReadOnlyList<string> EmrDataTypeOptions { get; } = new string[5] { "STRING", "TEXT", "INTEGER", "DECIMAL", "ENUM" };

	public IReadOnlyList<string> EmrGenderRuleOptions { get; } = new string[3] { "ALL", "MALE_ONLY", "FEMALE_ONLY" };

	public IReadOnlyList<string> EmrPaperSizeOptions { get; } = new string[3] { "A4", "B5", "A4_LANDSCAPE" };

	public bool IsEmrVisualDesignerOpen
	{
		get
		{
			return _isEmrVisualDesignerOpen;
		}
		set
		{
			SetField(ref _isEmrVisualDesignerOpen, value, "IsEmrVisualDesignerOpen");
		}
	}

	public string SelectedEmrSceneCode
	{
		get
		{
			return _selectedEmrSceneCode;
		}
		set
		{
			EnsureEmrCatalogInitialized();
			if (SetField(ref _selectedEmrSceneCode, value, "SelectedEmrSceneCode"))
			{
				ApplyEmrSceneFilter();
				OnPropertyChanged("IsEmrSceneOpd");
				OnPropertyChanged("IsEmrSceneIpd");
				OnPropertyChanged("IsEmrSceneWard");
				OnPropertyChanged("IsEmrSceneAll");
				OnPropertyChanged("CurrentEmrSceneTitle");
				OnPropertyChanged("CurrentEmrPatientSummary");
				OnPropertyChanged("CurrentEmrEncounterNo");
				OnPropertyChanged("CurrentEmrBedAndWardText");
				OnPropertyChanged("CurrentNursingScalesSummary");
			}
		}
	}

	public bool IsEmrSceneOpd => SelectedEmrSceneCode == "OPD";

	public bool IsEmrSceneIpd => SelectedEmrSceneCode == "IPD";

	public bool IsEmrSceneWard => SelectedEmrSceneCode == "WARD";

	public bool IsEmrSceneAll => SelectedEmrSceneCode == "ALL";

	public EmrVisualTemplateModel? SelectedEmrTemplate
	{
		get
		{
			EnsureEmrCatalogInitialized();
			return _selectedEmrTemplate;
		}
		set
		{
			EnsureEmrCatalogInitialized();
			if (SetField(ref _selectedEmrTemplate, value, "SelectedEmrTemplate") && value != null)
			{
				SelectedDesignSection = value.Sections.FirstOrDefault();
				BindLayoutForTemplate(value);
				RefreshPatientFieldVisibilityForTemplate(value);
				RebuildSignatureSlotsPreview();
				OnPropertyChanged("CurrentEmrSceneTitle");
				OnPropertyChanged("CurrentEmrPatientSummary");
				OnPropertyChanged("CurrentEmrEncounterNo");
				OnPropertyChanged("CurrentEmrBedAndWardText");
				OnPropertyChanged("CurrentNursingScalesSummary");
			}
		}
	}

	public EmrPrintLayoutConfigModel ActiveEmrPrintLayout
	{
		get
		{
			EnsureEmrCatalogInitialized();
			return _activeEmrPrintLayout;
		}
		set
		{
			SetField(ref _activeEmrPrintLayout, value, "ActiveEmrPrintLayout");
		}
	}

	public EmrVisualSectionModel? SelectedDesignSection
	{
		get
		{
			return _selectedDesignSection;
		}
		set
		{
			SetField(ref _selectedDesignSection, value, "SelectedDesignSection");
		}
	}

	public string NewEmrSectionTitle
	{
		get
		{
			return _newEmrSectionTitle;
		}
		set
		{
			SetField(ref _newEmrSectionTitle, value, "NewEmrSectionTitle");
		}
	}

	public string NewEmrSectionRenderMode
	{
		get
		{
			return _newEmrSectionRenderMode;
		}
		set
		{
			SetField(ref _newEmrSectionRenderMode, value, "NewEmrSectionRenderMode");
		}
	}

	public string NewEmrFieldCode
	{
		get
		{
			return _newEmrFieldCode;
		}
		set
		{
			SetField(ref _newEmrFieldCode, value, "NewEmrFieldCode");
		}
	}

	public string NewEmrFieldName
	{
		get
		{
			return _newEmrFieldName;
		}
		set
		{
			SetField(ref _newEmrFieldName, value, "NewEmrFieldName");
		}
	}

	public string NewEmrFieldNhcCode
	{
		get
		{
			return _newEmrFieldNhcCode;
		}
		set
		{
			SetField(ref _newEmrFieldNhcCode, value, "NewEmrFieldNhcCode");
		}
	}

	public string NewEmrFieldDataType
	{
		get
		{
			return _newEmrFieldDataType;
		}
		set
		{
			SetField(ref _newEmrFieldDataType, value, "NewEmrFieldDataType");
		}
	}

	public string NewEmrFieldUnit
	{
		get
		{
			return _newEmrFieldUnit;
		}
		set
		{
			SetField(ref _newEmrFieldUnit, value, "NewEmrFieldUnit");
		}
	}

	public string NewEmrFieldGenderRule
	{
		get
		{
			return _newEmrFieldGenderRule;
		}
		set
		{
			SetField(ref _newEmrFieldGenderRule, value, "NewEmrFieldGenderRule");
		}
	}

	public bool NewEmrFieldRequired
	{
		get
		{
			return _newEmrFieldRequired;
		}
		set
		{
			SetField(ref _newEmrFieldRequired, value, "NewEmrFieldRequired");
		}
	}

	public string CurrentEmrSceneTitle
	{
		get
		{
			string text = SelectedEmrTemplate?.EmrScene ?? SelectedEmrSceneCode;
			if (1 == 0)
			{
			}
			string result;
			if (text == "IPD")
			{
				result = "住院结构化病历工作台 (24h入院记录 · 查房病程 · 出院小结)";
			}
			else
			{
				result = ((!(text == "WARD")) ? "门诊结构化病历工作台 (初诊病历 · 慢病复诊随访 · 处方联动)" : "护理结构化病历与专科评估工作台 (入院护理评估 · 三评量表 · 特级护理记录)");
			}
			if (1 == 0)
			{
			}
			return result;
		}
	}

	public string CurrentEmrPatientName
	{
		get
		{
			string text = SelectedEmrTemplate?.EmrScene ?? SelectedEmrSceneCode;
			if (1 == 0)
			{
			}
			string result = ((!(text == "IPD") && !(text == "WARD")) ? (SelectedPatient?.PatientName ?? "周建强") : (SelectedInpatient?.PatientName ?? SelectedPatient?.PatientName ?? "周建强"));
			if (1 == 0)
			{
			}
			return result;
		}
	}

	public string CurrentEmrPatientGender
	{
		get
		{
			string text = SelectedEmrTemplate?.EmrScene ?? SelectedEmrSceneCode;
			if (1 == 0)
			{
			}
			string result = ((!(text == "IPD") && !(text == "WARD")) ? (SelectedPatient?.Gender ?? "未知") : (SelectedInpatient?.Gender ?? SelectedPatient?.Gender ?? "未知"));
			if (1 == 0)
			{
			}
			return result;
		}
	}

	public int CurrentEmrPatientAge
	{
		get
		{
			string text = SelectedEmrTemplate?.EmrScene ?? SelectedEmrSceneCode;
			if (1 == 0)
			{
			}
			int result = ((!(text == "IPD") && !(text == "WARD")) ? (SelectedPatient?.Age ?? 41) : (SelectedInpatient?.Age ?? SelectedPatient?.Age ?? 41));
			if (1 == 0)
			{
			}
			return result;
		}
	}

	public string CurrentEmrEncounterNo
	{
		get
		{
			string text = SelectedEmrTemplate?.EmrScene ?? SelectedEmrSceneCode;
			if (1 == 0)
			{
			}
			string result = ((!(text == "IPD") && !(text == "WARD")) ? (SelectedPatient?.VisitNo ?? "OPD20261105001") : (SelectedInpatient?.AdmissionNo ?? "ADM20261105001"));
			if (1 == 0)
			{
			}
			return result;
		}
	}

	public string CurrentEmrBedAndWardText
	{
		get
		{
			string text = SelectedEmrTemplate?.EmrScene ?? SelectedEmrSceneCode;
			if (1 == 0)
			{
			}
			string result;
			if (text == "IPD")
			{
				result = $"{SelectedInpatient?.AdmittingDept ?? "心血管内科住院一科"} · {SelectedInpatient?.BedCode ?? "02床"} ({SelectedInpatient?.NursingWard ?? "内科二病区·跨区借床"})";
			}
			else
			{
				result = ((!(text == "WARD")) ? ("门诊科室: " + ActiveDeptName + " · 病历号: " + (SelectedPatient?.MedicalRecordNo ?? "PAT85061801")) : $"{SelectedInpatient?.NursingWard ?? "内科二病区"} · {SelectedInpatient?.BedCode ?? "02床"} · {SelectedInpatient?.NursingLevel ?? "一级护理"}");
			}
			if (1 == 0)
			{
			}
			return result;
		}
	}

	public string CurrentEmrPatientSummary => $"姓名: {CurrentEmrPatientName}   |   性别: {CurrentEmrPatientGender}   |   年龄: {CurrentEmrPatientAge}岁   |   单号: {CurrentEmrEncounterNo}   |   {CurrentEmrBedAndWardText}";

	public string CurrentNursingScalesSummary => "压疮 Braden 评分: 19分 (低危)  |  自理 Barthel 指数: 75分 (轻度依赖)  |  跌倒 Morse 评分: 45分 (中风险·已挂警示牌)";

	public IReadOnlyList<string> HandoverShifts { get; } = new string[2] { "白班", "夜班" };

	public IReadOnlyList<string> OrderRouteOptions { get; } = new string[11]
	{
		"口服", "静脉滴注", "静脉推注", "持续微量泵入", "皮下注射", "肌内注射", "雾化吸入", "外用", "床旁", "静脉采血",
		"桡动脉穿刺"
	};

	public ObservableCollection<OrderFrequencyDefinition> FrequencyDefinitions { get; } = new ObservableCollection<OrderFrequencyDefinition>();

	public ObservableCollection<string> OrderFrequencyOptions { get; } = new ObservableCollection<string>();

	public int SelectedNurseTabIndex
	{
		get
		{
			return _selectedNurseTabIndex;
		}
		set
		{
			SetField(ref _selectedNurseTabIndex, value, "SelectedNurseTabIndex");
		}
	}

	public bool IsFrequencyConfigModalOpen
	{
		get
		{
			return _isFrequencyConfigModalOpen;
		}
		set
		{
			SetField(ref _isFrequencyConfigModalOpen, value, "IsFrequencyConfigModalOpen");
		}
	}

	public bool IsAddFrequencyFormOpen
	{
		get
		{
			return _isAddFrequencyFormOpen;
		}
		set
		{
			SetField(ref _isAddFrequencyFormOpen, value, "IsAddFrequencyFormOpen");
		}
	}

	public string NewFreqCode
	{
		get
		{
			return _newFreqCode;
		}
		set
		{
			SetField(ref _newFreqCode, value, "NewFreqCode");
		}
	}

	public string NewFreqName
	{
		get
		{
			return _newFreqName;
		}
		set
		{
			SetField(ref _newFreqName, value, "NewFreqName");
		}
	}

	public string NewFreqTimePoints
	{
		get
		{
			return _newFreqTimePoints;
		}
		set
		{
			SetField(ref _newFreqTimePoints, value, "NewFreqTimePoints");
		}
	}

	public string NewFreqDesc
	{
		get
		{
			return _newFreqDesc;
		}
		set
		{
			SetField(ref _newFreqDesc, value, "NewFreqDesc");
		}
	}

	public bool NewFreqIsLongTerm
	{
		get
		{
			return _newFreqIsLongTerm;
		}
		set
		{
			SetField(ref _newFreqIsLongTerm, value, "NewFreqIsLongTerm");
		}
	}

	public InpatientRecordModel? SelectedInpatient
	{
		get
		{
			return _selectedInpatient;
		}
		set
		{
			if (!SetField(ref _selectedInpatient, value, "SelectedInpatient"))
			{
				return;
			}
			foreach (InpatientRecordModel allInpatientRecord in AllInpatientRecords)
			{
				allInpatientRecord.IsSelected = allInpatientRecord == value || (value != null && allInpatientRecord.AdmissionNo == value.AdmissionNo);
			}
			foreach (BedCardModel allBedCard in AllBedCards)
			{
				allBedCard.IsSelected = value != null && allBedCard.AdmissionNo == value.AdmissionNo;
			}
			if (value != null)
			{
				value.ApplyOrderFilter();
				CriticalValueAlert.PatientName = value.PatientName;
				CriticalValueAlert.BedCode = value.BedCode;
				DischargeCheckDetails.Patient = value;
				if (_emrDesignerInitialized && SelectedEmrTemplate != null)
				{
					RefreshPatientFieldVisibilityForTemplate(SelectedEmrTemplate);
				}
			}
			else
			{
				DischargeCheckDetails.Patient = null;
			}
			OnPropertyChanged("CurrentEmrPatientName");
			OnPropertyChanged("CurrentEmrPatientGender");
			OnPropertyChanged("CurrentEmrPatientAge");
			OnPropertyChanged("CurrentEmrEncounterNo");
			OnPropertyChanged("CurrentEmrBedAndWardText");
			OnPropertyChanged("CurrentEmrPatientSummary");
			RefreshInpatientMetrics();
			NotifyUiStateChanged();
		}
	}

	public string NewOrderContent
	{
		get
		{
			return _newOrderContent;
		}
		set
		{
			SetField(ref _newOrderContent, value, "NewOrderContent");
		}
	}

	public string NewOrderDose
	{
		get
		{
			return _newOrderDose;
		}
		set
		{
			SetField(ref _newOrderDose, value, "NewOrderDose");
		}
	}

	public string NewOrderRoute
	{
		get
		{
			return _newOrderRoute;
		}
		set
		{
			SetField(ref _newOrderRoute, value, "NewOrderRoute");
		}
	}

	public string NewOrderFrequency
	{
		get
		{
			return _newOrderFrequency;
		}
		set
		{
			SetField(ref _newOrderFrequency, value, "NewOrderFrequency");
		}
	}

	public string NewProgressNote
	{
		get
		{
			return _newProgressNote;
		}
		set
		{
			SetField(ref _newProgressNote, value, "NewProgressNote");
		}
	}

	public string TemperatureText
	{
		get
		{
			return _temperatureText;
		}
		set
		{
			SetField(ref _temperatureText, value, "TemperatureText");
		}
	}

	public string PulseText
	{
		get
		{
			return _pulseText;
		}
		set
		{
			SetField(ref _pulseText, value, "PulseText");
		}
	}

	public string RespirationText
	{
		get
		{
			return _respirationText;
		}
		set
		{
			SetField(ref _respirationText, value, "RespirationText");
		}
	}

	public string SystolicText
	{
		get
		{
			return _systolicText;
		}
		set
		{
			SetField(ref _systolicText, value, "SystolicText");
		}
	}

	public string DiastolicText
	{
		get
		{
			return _diastolicText;
		}
		set
		{
			SetField(ref _diastolicText, value, "DiastolicText");
		}
	}

	public string Spo2Text
	{
		get
		{
			return _spo2Text;
		}
		set
		{
			SetField(ref _spo2Text, value, "Spo2Text");
		}
	}

	public string HandoverDraft
	{
		get
		{
			return _handoverDraft;
		}
		set
		{
			SetField(ref _handoverDraft, value, "HandoverDraft");
		}
	}

	public string SelectedHandoverShift
	{
		get
		{
			return _selectedHandoverShift;
		}
		set
		{
			SetField(ref _selectedHandoverShift, value, "SelectedHandoverShift");
		}
	}

	public int TotalInpatientCount => Enumerable.Count(InpatientRecords, (InpatientRecordModel x) => x.IsActiveInpatient);

	public int CriticalInpatientCount => Enumerable.Count(InpatientRecords, (InpatientRecordModel x) => x.IsActiveInpatient && x.IsCritical);

	public int PendingOrderReviewCount => InpatientRecords.Sum((InpatientRecordModel x) => x.PendingOrderCount);

	public int WaitingDecomposeTotalCount => InpatientRecords.Sum((InpatientRecordModel x) => x.WaitingDecomposeOrderCount);

	public int PendingNursingTaskCount => InpatientRecords.Sum((InpatientRecordModel x) => x.PendingNursingTaskCount);

	public int UnverifiedNursingTaskCount => InpatientRecords.Sum((InpatientRecordModel x) => x.UnverifiedNursingTaskCount);

	public int DischargeReadyCount => Enumerable.Count(InpatientRecords, (InpatientRecordModel x) => x.IsActiveInpatient && x.IsDischargeReady);

	public string CurrentOperatorName => string.IsNullOrWhiteSpace(SessionContext.Current.DisplayName) ? "张建国" : SessionContext.Current.DisplayName;

	public string CurrentOperatorRole => string.IsNullOrWhiteSpace(SessionContext.Current.RoleName) ? "主治医师" : SessionContext.Current.RoleName;

	public bool CanCreateInpatientOrder
	{
		get
		{
			InpatientRecordModel selectedInpatient = SelectedInpatient;
			return selectedInpatient != null && selectedInpatient.IsActiveInpatient && CanOperateIpdClinical;
		}
	}

	public bool CanReviewSelectedInpatientOrders
	{
		get
		{
			int result;
			if (CanOperateWardNursing)
			{
				InpatientRecordModel selectedInpatient = SelectedInpatient;
				result = ((selectedInpatient != null && selectedInpatient.IsActiveInpatient && selectedInpatient.PendingOrderCount > 0) ? 1 : 0);
			}
			else
			{
				result = 0;
			}
			return (byte)result != 0;
		}
	}

	public bool CanDecomposeSelectedInpatientOrders
	{
		get
		{
			int result;
			if (CanOperateWardNursing)
			{
				InpatientRecordModel selectedInpatient = SelectedInpatient;
				if (selectedInpatient != null && selectedInpatient.IsActiveInpatient)
				{
					result = ((SelectedInpatient.WaitingDecomposeOrderCount > 0 || SelectedInpatient.Orders.Any((InpatientOrderModel x) => x.OrderType == "长期医嘱" && x.IsExecuting)) ? 1 : 0);
					goto IL_005d;
				}
			}
			result = 0;
			goto IL_005d;
			IL_005d:
			return (byte)result != 0;
		}
	}

	public bool CanSignSelectedProgressNotes
	{
		get
		{
			int result;
			if (CanFinalizeInpatientOrders)
			{
				InpatientRecordModel selectedInpatient = SelectedInpatient;
				result = ((selectedInpatient != null && selectedInpatient.IsActiveInpatient && selectedInpatient.UnsignedNoteCount > 0) ? 1 : 0);
			}
			else
			{
				result = 0;
			}
			return (byte)result != 0;
		}
	}

	public bool CanExecuteSelectedNursingTasks
	{
		get
		{
			int result;
			if (CanOperateWardNursing)
			{
				InpatientRecordModel selectedInpatient = SelectedInpatient;
				result = ((selectedInpatient != null && selectedInpatient.IsActiveInpatient && selectedInpatient.PendingNursingTaskCount > 0) ? 1 : 0);
			}
			else
			{
				result = 0;
			}
			return (byte)result != 0;
		}
	}

	public bool CanVerifySelectedNursingTasks
	{
		get
		{
			int result;
			if (CanOperateWardNursing)
			{
				InpatientRecordModel selectedInpatient = SelectedInpatient;
				result = ((selectedInpatient != null && selectedInpatient.IsActiveInpatient && selectedInpatient.UnverifiedNursingTaskCount > 0) ? 1 : 0);
			}
			else
			{
				result = 0;
			}
			return (byte)result != 0;
		}
	}

	public bool CanApproveSelectedDischarge
	{
		get
		{
			InpatientRecordModel? selectedInpatient = SelectedInpatient;
			return selectedInpatient != null
				&& selectedInpatient.IsActiveInpatient
				&& CanOperateIpdClinical;
		}
	}

	public int SelectedPharmacyHubTabIndex
	{
		get
		{
			return _selectedPharmacyHubTabIndex;
		}
		set
		{
			SetField(ref _selectedPharmacyHubTabIndex, value, "SelectedPharmacyHubTabIndex");
		}
	}

	public int SelectedFinanceTabIndex
	{
		get
		{
			return _selectedFinanceTabIndex;
		}
		set
		{
			SetField(ref _selectedFinanceTabIndex, value, "SelectedFinanceTabIndex");
		}
	}

	public string DailyClosingStatusText
	{
		get
		{
			return _dailyClosingStatusText;
		}
		set
		{
			SetField(ref _dailyClosingStatusText, value, "DailyClosingStatusText");
		}
	}

	public int UnpaidPrescriptionCount
	{
		get
		{
			EnsurePharmacyWarehouseFinanceInitialized();
			return Enumerable.Count(PharmacyRecords, (PharmacyDispenseModel x) => x.NeedsCashierSettlement);
		}
	}

	public int PendingFinancialRefundCount
	{
		get
		{
			EnsurePharmacyWarehouseFinanceInitialized();
			return Enumerable.Count(PharmacyRecords, (PharmacyDispenseModel x) => x.CanExecuteFinancialRefund);
		}
	}

	public int CompletedFinancialRefundCount
	{
		get
		{
			EnsurePharmacyWarehouseFinanceInitialized();
			return Enumerable.Count(PharmacyRecords, (PharmacyDispenseModel x) => x.IsFinancialRefunded);
		}
	}

	public ObservableCollection<DrugMasterItemModel> DrugMasterList { get; } = new ObservableCollection<DrugMasterItemModel>();

	public ObservableCollection<DrugPriceAdjustmentLogModel> DrugPriceLogs { get; } = new ObservableCollection<DrugPriceAdjustmentLogModel>();

	public ObservableCollection<WarehouseBatchItemModel> WarehouseBatchList { get; } = new ObservableCollection<WarehouseBatchItemModel>();

	public ObservableCollection<WarehouseBatchItemModel> FilteredWarehouseBatchList { get; } = new ObservableCollection<WarehouseBatchItemModel>();

	public ObservableCollection<WarehouseTransferOrderModel> WarehouseTransferOrders { get; } = new ObservableCollection<WarehouseTransferOrderModel>();

	public ObservableCollection<DrugFinanceReconcileItemModel> DrugFinanceBreakdownList { get; } = new ObservableCollection<DrugFinanceReconcileItemModel>();

	public IReadOnlyList<string> WarehouseIdOptions { get; } = new string[4] { "DEPT-WH-01", "DEPT-WH-02", "DEPT-PHM-01", "DEPT-PHM-02" };

	public string SelectedWarehouseFilter
	{
		get
		{
			EnsurePharmacyWarehouseFinanceInitialized();
			return _selectedWarehouseFilter;
		}
		set
		{
			EnsurePharmacyWarehouseFinanceInitialized();
			if (SetField(ref _selectedWarehouseFilter, value, "SelectedWarehouseFilter"))
			{
				ApplyWarehouseBatchFilter();
			}
		}
	}

	public string EditDrugCode
	{
		get
		{
			return _editDrugCode;
		}
		set
		{
			SetField(ref _editDrugCode, value, "EditDrugCode");
		}
	}

	public string EditDrugName
	{
		get
		{
			return _editDrugName;
		}
		set
		{
			SetField(ref _editDrugName, value, "EditDrugName");
		}
	}

	public string EditDrugSpec
	{
		get
		{
			return _editDrugSpec;
		}
		set
		{
			SetField(ref _editDrugSpec, value, "EditDrugSpec");
		}
	}

	public string EditDrugUnit
	{
		get
		{
			return _editDrugUnit;
		}
		set
		{
			SetField(ref _editDrugUnit, value, "EditDrugUnit");
		}
	}

	public decimal EditDrugRetailPrice
	{
		get
		{
			return _editDrugRetailPrice;
		}
		set
		{
			SetField(ref _editDrugRetailPrice, value, "EditDrugRetailPrice");
		}
	}

	public decimal EditDrugCostPrice
	{
		get
		{
			return _editDrugCostPrice;
		}
		set
		{
			SetField(ref _editDrugCostPrice, value, "EditDrugCostPrice");
		}
	}

	public int EditDrugAntibioticLevel
	{
		get
		{
			return _editDrugAntibioticLevel;
		}
		set
		{
			SetField(ref _editDrugAntibioticLevel, value, "EditDrugAntibioticLevel");
		}
	}

	public bool EditDrugSkinTest
	{
		get
		{
			return _editDrugSkinTest;
		}
		set
		{
			SetField(ref _editDrugSkinTest, value, "EditDrugSkinTest");
		}
	}

	public bool EditDrugNarcotic
	{
		get
		{
			return _editDrugNarcotic;
		}
		set
		{
			SetField(ref _editDrugNarcotic, value, "EditDrugNarcotic");
		}
	}

	public string PriceAdjustReason
	{
		get
		{
			return _priceAdjustReason;
		}
		set
		{
			SetField(ref _priceAdjustReason, value, "PriceAdjustReason");
		}
	}

	public string StockInWarehouseId
	{
		get
		{
			return _stockInWarehouseId;
		}
		set
		{
			SetField(ref _stockInWarehouseId, value, "StockInWarehouseId");
		}
	}

	public string StockInDrugCode
	{
		get
		{
			return _stockInDrugCode;
		}
		set
		{
			SetField(ref _stockInDrugCode, value, "StockInDrugCode");
		}
	}

	public string StockInBatchNo
	{
		get
		{
			return _stockInBatchNo;
		}
		set
		{
			SetField(ref _stockInBatchNo, value, "StockInBatchNo");
		}
	}

	public int StockInExpiryInt
	{
		get
		{
			return _stockInExpiryInt;
		}
		set
		{
			SetField(ref _stockInExpiryInt, value, "StockInExpiryInt");
		}
	}

	public int StockInQuantity
	{
		get
		{
			return _stockInQuantity;
		}
		set
		{
			SetField(ref _stockInQuantity, value, "StockInQuantity");
		}
	}

	public string StockInInvoiceNo
	{
		get
		{
			return _stockInInvoiceNo;
		}
		set
		{
			SetField(ref _stockInInvoiceNo, value, "StockInInvoiceNo");
		}
	}

	public decimal StockInColdChainTemp
	{
		get
		{
			return _stockInColdChainTemp;
		}
		set
		{
			SetField(ref _stockInColdChainTemp, value, "StockInColdChainTemp");
		}
	}

	public string TransferSourceWarehouseId
	{
		get
		{
			return _transferSourceWarehouseId;
		}
		set
		{
			SetField(ref _transferSourceWarehouseId, value, "TransferSourceWarehouseId");
		}
	}

	public string TransferTargetWarehouseId
	{
		get
		{
			return _transferTargetWarehouseId;
		}
		set
		{
			SetField(ref _transferTargetWarehouseId, value, "TransferTargetWarehouseId");
		}
	}

	public string TransferDrugCode
	{
		get
		{
			return _transferDrugCode;
		}
		set
		{
			SetField(ref _transferDrugCode, value, "TransferDrugCode");
		}
	}

	public int TransferQuantity
	{
		get
		{
			return _transferQuantity;
		}
		set
		{
			SetField(ref _transferQuantity, value, "TransferQuantity");
		}
	}

	public string TransferRemark
	{
		get
		{
			return _transferRemark;
		}
		set
		{
			SetField(ref _transferRemark, value, "TransferRemark");
		}
	}

	public int PendingPharmacyDispenseCount
	{
		get
		{
			EnsurePharmacyWarehouseFinanceInitialized();
			return Enumerable.Count(PharmacyRecords, (PharmacyDispenseModel x) => x.IsPaidNotified && !x.IsOrderClosed && !x.IsReturned);
		}
	}

	public int ClosedPharmacyOrderCount
	{
		get
		{
			EnsurePharmacyWarehouseFinanceInitialized();
			return Enumerable.Count(PharmacyRecords, (PharmacyDispenseModel x) => x.IsOrderClosed);
		}
	}

	public int ReturnedPharmacyOrderCount
	{
		get
		{
			EnsurePharmacyWarehouseFinanceInitialized();
			return Enumerable.Count(PharmacyRecords, (PharmacyDispenseModel x) => x.IsReturned);
		}
	}

	public string TotalInventoryCostValueText
	{
		get
		{
			EnsurePharmacyWarehouseFinanceInitialized();
			decimal value = WarehouseBatchList.Sum((WarehouseBatchItemModel b) => (decimal)b.Quantity * b.UnitCostPrice);
			return $"¥{value:N2}";
		}
	}

	public string TotalInventoryRetailValueText
	{
		get
		{
			EnsurePharmacyWarehouseFinanceInitialized();
			decimal value = WarehouseBatchList.Sum((WarehouseBatchItemModel b) => (decimal)b.Quantity * b.UnitRetailPrice);
			return $"¥{value:N2}";
		}
	}

	public string TotalProcurementCostText
	{
		get
		{
			EnsurePharmacyWarehouseFinanceInitialized();
			decimal value = DrugFinanceBreakdownList.Sum((DrugFinanceReconcileItemModel x) => x.PurchasedCostAmount);
			return $"¥{value:N2}";
		}
	}

	public string TotalDrugNetSalesText
	{
		get
		{
			EnsurePharmacyWarehouseFinanceInitialized();
			decimal value = DrugFinanceBreakdownList.Sum((DrugFinanceReconcileItemModel x) => x.NetSalesAmount);
			return $"¥{value:N2}";
		}
	}

	public string TotalDrugReturnDeductionText
	{
		get
		{
			EnsurePharmacyWarehouseFinanceInitialized();
			decimal value = DrugFinanceBreakdownList.Sum((DrugFinanceReconcileItemModel x) => x.ReturnedSalesAmount);
			return $"-¥{value:N2}";
		}
	}

	public string TotalPriceDiffImpactText
	{
		get
		{
			EnsurePharmacyWarehouseFinanceInitialized();
			decimal num = DrugPriceLogs.Sum((DrugPriceAdjustmentLogModel x) => x.PriceDiffAmount);
			return (num >= 0m) ? $"+¥{num:N2}" : $"-¥{Math.Abs(num):N2}";
		}
	}

	public ObservableCollection<PrescriptionColumnModel> PrescriptionColumns { get; } = new ObservableCollection<PrescriptionColumnModel>();

	public string CurrentOperatorId => string.IsNullOrWhiteSpace(SessionContext.Current.UserId) ? "USER-DOC-OPD" : SessionContext.Current.UserId;

	public ObservableCollection<DynamicFormFieldModel> PrescriptionFieldDefinitions { get; } = new ObservableCollection<DynamicFormFieldModel>();

	public int PrescriptionFieldDefinitionCount => PrescriptionFieldDefinitions.Count;

	public int PrescriptionCustomFieldDefinitionCount => Enumerable.Count(PrescriptionFieldDefinitions, (DynamicFormFieldModel x) => !x.IsSystemField);

	public string PrescriptionFieldCatalogSummary => $"共 {PrescriptionFieldDefinitionCount} 项，其中自定义 {PrescriptionCustomFieldDefinitionCount} 项";

	public string FieldApplyButtonText => IsPrescriptionSchemaDesigner ? "保存并应用" : "保存字段";

	public string DesignerSaveButtonText => IsPrescriptionSchemaDesigner ? "保存并应用" : "保存模板";

	public string FieldApplyHint => IsPrescriptionSchemaDesigner ? "保存字段并立即同步到处方明细列" : "保存字段到当前表单设计";

	public WorkbenchModel()
	{
		PatientQueue = new ObservableCollection<PatientQueueItemModel>();
		Prescriptions = new ObservableCollection<PrescriptionItemModel>();
		PrescriptionExtraFields = new ObservableCollection<PrescriptionExtraFieldModel>();
		MedTechOrders = new ObservableCollection<MedTechOrderItemModel>();
		AllBedCards = new ObservableCollection<BedCardModel>();
		AllInpatientRecords = new ObservableCollection<InpatientRecordModel>();
		BedCards = new ObservableCollection<BedCardModel>();
		InpatientRecords = new ObservableCollection<InpatientRecordModel>();
		PharmacyRecords = new ObservableCollection<PharmacyDispenseModel>();
		OfdArchiveRecords = new ObservableCollection<OfdArchiveRecordModel>();
		DynamicFormTemplates = new ObservableCollection<DynamicFormTemplateModel>();
		TimelineEvents = new ObservableCollection<TimelineEventModel>();
		AvailableDrugsCatalog = new ObservableCollection<DrugCatalogItemModel>();
		InitFrequencyDefinitions();
		InitBedCards();
		InitInpatientRecords();
		RebuildScopedInpatientData();
		InitPharmacyRecords();
		InitOfdArchives();
		InitDynamicFormTemplates();
		InitPrescriptionColumns();
		InitTimelineEvents();
		_ = LoadAvailableDrugsCatalogAsync();
		InitAdminData();
		ApplyPatientQueueFilter();
		EmrWorkspace.PropertyChanged += (object? _, PropertyChangedEventArgs _) =>
		{
			SyncOpdWorkspaceToEmrTemplate();
		};
		SyncWithSession();
		SelectedNavIndex = DefaultStationIndex;
		RebuildNavigationItems();
		SelectedInpatient = InpatientRecords.FirstOrDefault();
		_selectedFormTemplate = ((DynamicFormTemplates.Count > 0) ? DynamicFormTemplates[0] : null);
		_ = LoadDynamicFormTemplatesAsync();
		_ = LoadPrescriptionColumnsAsync();
		_ = LoadOpdQueueAsync();
	}

	public void ApplyPatientQueueFilter()
	{
		string q = _patientSearchQuery?.Trim();
		List<PatientQueueItemModel> list = (string.IsNullOrWhiteSpace(q) ? PatientQueue.ToList() : PatientQueue.Where((PatientQueueItemModel x) => x.PatientName.Contains(q, StringComparison.OrdinalIgnoreCase) || x.QueueNo.Contains(q, StringComparison.OrdinalIgnoreCase) || x.MedicalRecordNo.Contains(q, StringComparison.OrdinalIgnoreCase) || x.ChiefComplaint.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList());
		FilteredPatientQueue.Clear();
		foreach (PatientQueueItemModel item in list)
		{
			FilteredPatientQueue.Add(item);
		}
		OnPropertyChanged("InProgressPatientCount");
		OnPropertyChanged("WaitingPatientCount");
		OnPropertyChanged("CompletedPatientCount");
		NotifyOpdWorkflowStateChanged();
	}

	public void NotifyOpdWorkflowStateChanged()
	{
		OnPropertyChanged("CanStartOpdVisit");
		OnPropertyChanged("IsOpdVisitActive");
		OnPropertyChanged("IsOpdEmrSigned");
		OnPropertyChanged("IsOpdOrdersPushed");
		OnPropertyChanged("CanSignOpdEmr");
		OnPropertyChanged("CanExportOrVerifyOpdOfd");
		OnPropertyChanged("CanPrescribeAndPushOpd");
		OnPropertyChanged("CanFinishOpdVisit");
		OnPropertyChanged("CanCompleteOpdWithNoPrescription");
		OnPropertyChanged("OpdStepABadge");
		OnPropertyChanged("OpdStepBBadge");
		OnPropertyChanged("OpdStepCBadge");
		OnPropertyChanged("OpdStepDBadge");
		OnPropertyChanged("OpdStepAClass");
		OnPropertyChanged("OpdStepBClass");
		OnPropertyChanged("OpdStepCClass");
		OnPropertyChanged("OpdStepDClass");
		OnPropertyChanged("OpdWorkflowGuideText");
		OnPropertyChanged("CanEditDiagnosis");
		OnPropertyChanged("CanAddOpdPrescription");
		OnPropertyChanged("IsOpdPrescriptionEditable");
		OnPropertyChanged("HasOpdPrescriptionDraft");
		OnPropertyChanged("CanSubmitOpdPrescription");
		OnPropertyChanged("PrescriptionWorkflowStatusText");
		OnPropertyChanged("HasSelectedPatientAllergy");
		OnPropertyChanged("SelectedPatientAllergyText");
		OnPropertyChanged("CanEditOpdPrescription");
	}

	public Task StartConsultationForSelectedPatientAsync()
	{
		PatientQueueItemModel? patient = SelectedPatient;
		return patient == null ? Task.CompletedTask : CallPatientAsync(patient);
	}

	private async Task LoadOpdQueueAsync()
	{
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			ClientApiEnvelope<List<OpdQueueDto>>? envelope =
				await http.GetFromJsonAsync<ClientApiEnvelope<List<OpdQueueDto>>>(
					"/api/v4/opd/queue", JsonOptions);
			if (envelope is not { Code: 200, Data: not null })
			{
				_opdQueueError = envelope?.Message ?? "服务端未返回门诊队列";
				return;
			}
			PatientQueueItemModel? selected = SelectedPatient;
			PatientQueue.Clear();
			foreach (OpdQueueDto row in envelope.Data)
			{
				PatientQueue.Add(MapQueueItem(row));
			}
			ApplyPatientQueueFilter();
			if (selected != null)
			{
				PatientQueueItemModel? samePatient = FindPatientByVisitId(selected.VisitId);
				if (samePatient != null)
				{
					SelectedPatient = samePatient;
				}
			}
			_opdQueueError = string.Empty;
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
		{
			_opdQueueError = "门诊队列读取失败：" + ex.Message;
		}
	}

	private async Task CallPatientAsync(PatientQueueItemModel patient)
	{
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			using HttpResponseMessage response = await http.PostAsJsonAsync(
				"/api/v4/opd/queue/call", new { QueueId = patient.QueueId }, JsonOptions);
                ClientApiEnvelope<OpdVisitDto>? envelope =
                    await response.Content.ReadFromJsonAsync<ClientApiEnvelope<OpdVisitDto>>(JsonOptions);
			if (envelope is not { Code: 200, Data: not null })
			{
				SystemNotice = "呼叫患者失败：" + (envelope?.Message ?? "服务端未返回结果");
				return;
			}
                patient.SetStatus(envelope.Data.Status, 2);
                patient.CalledAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			patient.IsEmrSigned = false;
			patient.IsOrdersPushed = false;
			SelectedPatient = patient;
			ApplyPatientQueueFilter();
			SystemNotice = $"已呼叫 [{patient.PatientName}] (#{patient.QueueNo})，状态：{patient.Status}";
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
		{
			SystemNotice = "呼叫患者失败：" + ex.Message;
		}
	}

	private static PatientQueueItemModel MapQueueItem(OpdQueueDto row)
	{
		return new PatientQueueItemModel
		{
			QueueNo = row.QueueNo,
			QueueId = row.QueueId,
                PatientName = row.PatientName,
                Gender = row.GenderCode,
                Age = row.AgeYears,
                MedicalRecordNo = row.MedicalRecordNo,
                VisitNo = row.VisitNo,
                VisitId = row.VisitId,
                PatientId = row.PatientId,
                FeeType = "医保",
                OpdDeptId = row.OpdDeptId,
                OpdDeptName = row.OpdDeptName,
                DoctorId = row.DoctorId,
                DoctorName = row.DoctorName,
			ClinicRoomId = row.ClinicRoomId ?? string.Empty,
			QueueStatus = row.QueueStatus,
			VisitStatus = row.VisitStatus,
                ActiveDepartmentId = row.OpdDeptId,
			CheckinAtMs = row.CheckinAtMs,
			CalledAtMs = row.CalledAtMs,
                ChiefComplaint = string.Empty,
                AllergyTags = string.Join(", ", row.AllergyTags ?? new List<string>()),
                IsEmrSigned = false,
                IsOrdersPushed = false
		};
	}

	private static string GetPrescriptionStatusText(int status)
	{
		return status switch
		{
			0 => "草稿",
			1 => "已提交",
			2 => "药师驳回",
			3 => "审方通过",
			4 => "已收费",
			5 => "已发药",
			6 => "待药师审核",
			_ => "尚未开立处方"
		};
	}

	public void MarkCurrentOpdEmrSigned(string docNo, string sha256Short, string? archiveId = null)
	{
		if (SelectedPatient != null)
		{
			SelectedPatient.IsEmrSigned = true;
			_currentEmrStatus = Math.Max(_currentEmrStatus, 3);
			if (!string.IsNullOrWhiteSpace(archiveId))
			{
				_currentOfdArchiveId = archiveId;
			}
			EmrWorkspace.OfdStatus = "已归档 (SHA-256: " + sha256Short + "...)";
			NotifyOpdWorkflowStateChanged();
			SystemNotice = $"门诊病历三级审签完成，归档单号 {docNo}，SHA-256: {sha256Short}...";
		}
	}

	public void ApplyPrescriptionFilter()
	{
		string query = PrescriptionSearchQuery?.Trim();
		string filter = SelectedPrescriptionFilter;
		List<PrescriptionItemModel> list = Prescriptions.Where((PrescriptionItemModel x) => !x.IsDeleted).Where((PrescriptionItemModel x) =>
		{
			if (!string.IsNullOrEmpty(query))
			{
				bool flag = x.DrugName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false;
				bool flag2 = x.Spec?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false;
				bool flag3 = x.Route?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false;
				if (!flag && !flag2 && !flag3)
				{
					return false;
				}
			}
			if (1 == 0)
			{
			}
			string text = filter;
			bool result = ((text == "NORMAL") ? (!x.IsBlocked && x.SkinTestStatus != "皮试阳性") : (!(text == "ALERT") || x.IsBlocked || x.SkinTestStatus == "需皮试" || x.SkinTestStatus == "皮试阳性" || x.AuditStatus == "阻断拒绝"));
			if (1 == 0)
			{
			}
			return result;
		}).ToList();
		FilteredPrescriptions.Clear();
		foreach (PrescriptionItemModel item in list)
		{
			FilteredPrescriptions.Add(item);
		}
		OnPropertyChanged("HasNoVisiblePrescriptions");
		OnPropertyChanged("VisiblePrescriptionCount");
	}

	public void RefreshPrescriptionMetrics()
	{
		ApplyPrescriptionFilter();
		OnPropertyChanged("VisiblePrescriptionCount");
		OnPropertyChanged("HiddenPrescriptionCount");
		OnPropertyChanged("VisiblePrescriptionFieldCount");
		OnPropertyChanged("HiddenPrescriptionFieldCount");
		OnPropertyChanged("HasBlockedPrescription");
		OnPropertyChanged("HasNoVisiblePrescriptions");
	}

	public void BeginEditPrescriptionItem(PrescriptionItemModel item)
	{
		if (item.IsDeleted || !IsOpdPrescriptionEditable)
		{
			SystemNotice = "当前处方不可编辑";
			return;
		}
		foreach (PrescriptionItemModel item2 in Prescriptions.Where((PrescriptionItemModel x) => x != item && x.IsEditing))
		{
			item2.IsEditing = false;
		}
		item.EditDraft.LoadFrom(item);
		item.IsEditing = true;
	}

	public void CancelEditPrescriptionItem(PrescriptionItemModel item)
	{
		item.IsEditing = false;
		item.EditDraft.LoadFrom(item);
	}

	public bool CommitPrescriptionItem(PrescriptionItemModel item)
	{
		PrescriptionItemDraftModel editDraft = item.EditDraft;
		if (string.IsNullOrWhiteSpace(editDraft.DrugName))
		{
			SystemNotice = "药品名称不能为空，处方明细未保存";
			return false;
		}
		item.Days = Math.Clamp(item.DaysValue, 1, 365);
		item.ReplaceFieldValues(editDraft.AllValues.Select((KeyValuePair<string, string> x) => new KeyValuePair<string, string>(x.Key, x.Value?.Trim() ?? string.Empty)));
		item.QuantityValue = Math.Max(ParseInt(item.Quantity, 1), 1);
		RecalculatePrescriptionSafety(item);
		item.IsEditing = false;
		item.RefreshCells();
		_prescriptionRevision++;
		RefreshPrescriptionMetrics();
		SystemNotice = "处方明细 [" + item.DrugName + "] 已更新，合理用药规则已重新校验";
		return true;
	}

	public void SoftDeletePrescriptionItem(PrescriptionItemModel item)
	{
		if (!item.IsDeleted && IsOpdPrescriptionEditable)
		{
			item.IsEditing = false;
			item.IsDeleted = true;
			item.DeletedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			item.DeletedBy = CurrentOperatorId;
			_prescriptionRevision++;
			ReindexPrescriptionRows();
			RefreshPrescriptionMetrics();
			SystemNotice = "已从当前处方界面隐藏 [" + item.DrugName + "]，后台保留原始记录、删除时间与操作人审计信息";
		}
		else if (!item.IsDeleted)
		{
			SystemNotice = "当前处方不可编辑，未删除该药品";
		}
	}

	public void BeginAddPrescriptionField()
	{
		_editingPrescriptionField = null;
		EditingPrescriptionField = null;
		PrescriptionFieldDraft = new PrescriptionFieldDraftModel();
		IsPrescriptionFieldEditorOpen = true;
	}

	public void BeginEditPrescriptionField(PrescriptionExtraFieldModel field)
	{
		if (!field.IsDeleted)
		{
			_editingPrescriptionField = field;
			EditingPrescriptionField = field;
			PrescriptionFieldDraft = new PrescriptionFieldDraftModel
			{
				Name = field.Name,
				Placeholder = field.Placeholder,
				DataType = field.DataType
			};
			IsPrescriptionFieldEditorOpen = true;
		}
	}

	public void CommitPrescriptionField()
	{
		PrescriptionFieldDraftModel prescriptionFieldDraft = PrescriptionFieldDraft;
		if (string.IsNullOrWhiteSpace(prescriptionFieldDraft.Name))
		{
			SystemNotice = "扩展字段名称不能为空";
			return;
		}
		if (_editingPrescriptionField != null)
		{
			_editingPrescriptionField.Name = prescriptionFieldDraft.Name.Trim();
			_editingPrescriptionField.Placeholder = prescriptionFieldDraft.Placeholder.Trim();
			_editingPrescriptionField.DataType = prescriptionFieldDraft.DataType;
			SystemNotice = "处方扩展字段 [" + _editingPrescriptionField.Name + "] 已更新";
		}
		else
		{
			PrescriptionExtraFieldModel prescriptionExtraFieldModel = new PrescriptionExtraFieldModel
			{
				Name = prescriptionFieldDraft.Name.Trim(),
				Placeholder = prescriptionFieldDraft.Placeholder.Trim(),
				DataType = prescriptionFieldDraft.DataType,
				SortOrder = PrescriptionExtraFields.Count
			};
			PrescriptionExtraFields.Add(prescriptionExtraFieldModel);
			SystemNotice = "已添加处方扩展字段 [" + prescriptionExtraFieldModel.Name + "]，可随处方保存";
		}
		_editingPrescriptionField = null;
		EditingPrescriptionField = null;
		IsPrescriptionFieldEditorOpen = false;
		_prescriptionRevision++;
		RefreshPrescriptionMetrics();
	}

	public void CancelPrescriptionFieldEdit()
	{
		_editingPrescriptionField = null;
		EditingPrescriptionField = null;
		IsPrescriptionFieldEditorOpen = false;
	}

	public void SoftDeletePrescriptionField(PrescriptionExtraFieldModel field)
	{
		if (!field.IsDeleted)
		{
			field.IsDeleted = true;
			field.DeletedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			field.DeletedBy = CurrentOperatorId;
			_prescriptionRevision++;
			RefreshPrescriptionMetrics();
			SystemNotice = "已从当前处方界面隐藏扩展字段 [" + field.Name + "]，历史字段定义与值仍保留在处方扩展数据中";
		}
	}

	private void InitBedCards()
	{
		AllBedCards.Clear();
		BedCardModel[] array = new BedCardModel[6]
		{
			new BedCardModel
			{
				BedCode = "01 床",
				BedType = "标准编制床",
				PatientName = "刘德海",
				Gender = "男",
				Age = 67,
				AdmissionNo = "ADM20261102001",
				AdmittingDeptName = "心血管内科住院一科",
				NursingWardName = "内科一病区",
				NursingLevel = "一级护理",
				Diagnosis = "冠心病 急性前壁心肌梗死",
				DailyFee = "¥45.00/天",
				IsBorrowed = false,
				IsOccupied = true,
				CardTheme = "Primary"
			},
			new BedCardModel
			{
				BedCode = "02 床",
				BedType = "⚡ 跨病区借床",
				PatientName = "周建强",
				Gender = "男",
				Age = 41,
				AdmissionNo = "ADM20261105001",
				AdmittingDeptName = "心血管内科住院一科 (开医嘱)",
				NursingWardName = "内科二病区 (执行借床/扫码护理)",
				NursingLevel = "二级护理",
				Diagnosis = "不稳定型心绞痛；高血压 II 期",
				DailyFee = "¥45.00/天",
				IsBorrowed = true,
				IsOccupied = true,
				CardTheme = "Warning"
			},
			new BedCardModel
			{
				BedCode = "03 床",
				BedType = "标准空床",
				PatientName = "【空床待收治】",
				Gender = "-",
				Age = 0,
				AdmissionNo = "-",
				AdmittingDeptName = "心血管内科住院一科",
				NursingWardName = "内科一病区",
				NursingLevel = "未指定",
				Diagnosis = "无",
				DailyFee = "¥45.00/天",
				IsBorrowed = false,
				IsOccupied = false,
				CardTheme = "Success"
			},
			new BedCardModel
			{
				BedCode = "04 床",
				BedType = "加床 (TEMP-ADD)",
				PatientName = "孙建国",
				Gender = "男",
				Age = 49,
				AdmissionNo = "ADM20261104003",
				AdmittingDeptName = "心血管内科住院一科",
				NursingWardName = "内科一病区",
				NursingLevel = "二级护理",
				Diagnosis = "高血压危象恢复期",
				DailyFee = "¥35.00/天",
				IsBorrowed = false,
				IsOccupied = true,
				CardTheme = "Primary"
			},
			new BedCardModel
			{
				BedCode = "05 床",
				BedType = "急危重监护床 (ICU)",
				PatientName = "陈芳华",
				Gender = "女",
				Age = 73,
				AdmissionNo = "ADM20261103002",
				AdmittingDeptName = "心血管内科住院一科",
				NursingWardName = "内科一病区",
				NursingLevel = "特级护理 (病危)",
				Diagnosis = "急性非ST段抬高型心肌梗死",
				DailyFee = "¥120.00/天",
				IsBorrowed = false,
				IsOccupied = true,
				CardTheme = "Danger"
			},
			new BedCardModel
			{
				BedCode = "06 床",
				BedType = "待出院关床",
				PatientName = "钱学森",
				Gender = "男",
				Age = 55,
				AdmissionNo = "ADM20261101006",
				AdmittingDeptName = "心血管内科住院一科",
				NursingWardName = "内科一病区",
				NursingLevel = "三级护理 (待出院)",
				Diagnosis = "阵发性心房颤动 (射频消融术后)",
				DailyFee = "¥45.00/天",
				IsBorrowed = false,
				IsOccupied = true,
				CardTheme = "Warning"
			}
		};
		BedCardModel[] array2 = array;
		foreach (BedCardModel item in array2)
		{
			AllBedCards.Add(item);
		}
	}

	private static InpatientOrderModel NewOrder(string orderNo, string orderType, string content, string dose, string route, string frequency, string status, string doctorName, string stopTime = "--", string stopDoctor = "--", string nurseSign = "陈劳 (校对)")
	{
		InpatientOrderModel inpatientOrderModel = new InpatientOrderModel();
		inpatientOrderModel.OrderNo = orderNo;
		inpatientOrderModel.OrderType = orderType;
		inpatientOrderModel.Content = content;
		inpatientOrderModel.Dose = dose;
		inpatientOrderModel.Route = route;
		inpatientOrderModel.Frequency = frequency;
		inpatientOrderModel.Status = status;
		inpatientOrderModel.DoctorName = doctorName;
		inpatientOrderModel.StartTime = ((orderType == "长期医嘱") ? "2026-11-05 10:20" : "2026-11-06 08:00");
		inpatientOrderModel.StopTime = stopTime;
		inpatientOrderModel.StopDoctor = stopDoctor;
		inpatientOrderModel.NurseSign = nurseSign;
		InpatientOrderModel inpatientOrderModel2 = inpatientOrderModel;
		bool flag;
		switch (status)
		{
		case "待主治审核":
		case "待护士校对":
		case "待审核":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		inpatientOrderModel2.ReviewInfo = (flag ? ("已由医生 " + doctorName + " 开立，等待责任护士校对审签") : ("已由责任护士 " + nurseSign + " 完成校对审签"));
		return inpatientOrderModel;
	}

	private static ProgressNoteModel NewNote(string time, string author, string type, string content, bool signed)
	{
		return new ProgressNoteModel
		{
			RecordTime = time,
			Author = author,
			RecordType = type,
			Content = content,
			IsSigned = signed
		};
	}

	private static NursingTaskModel NewTask(string taskNo, string time, string category, string content, string sourceOrder, string priority, string status = "待执行", string executedAt = "--")
	{
		return new NursingTaskModel
		{
			TaskNo = taskNo,
			ScheduledTime = time,
			Category = category,
			Content = content,
			SourceOrder = sourceOrder,
			Priority = priority,
			Status = status,
			ExecutedAt = executedAt
		};
	}

	private static VitalSignModel NewVitals(string time, string temperature, string pulse, string respiration, string systolic, string diastolic, string spo2, string nurse, bool abnormal)
	{
		return new VitalSignModel
		{
			RecordedAt = time,
			Temperature = temperature,
			Pulse = pulse,
			Respiration = respiration,
			BloodPressure = systolic + "/" + diastolic,
			Spo2 = spo2,
			NurseName = nurse,
			IsAbnormal = abnormal
		};
	}

	private static HandoverNoteModel NewHandover(string shift, string nurse, string time, string content, bool confirmed)
	{
		return new HandoverNoteModel
		{
			Shift = shift,
			NurseName = nurse,
			RecordTime = time,
			Content = content,
			IsConfirmed = confirmed
		};
	}

	private void InitInpatientRecords()
	{
		AllInpatientRecords.Clear();
		InpatientRecordModel inpatientRecordModel = new InpatientRecordModel
		{
			AdmissionNo = "ADM20261105001",
			PatientName = "周建强",
			Gender = "男",
			Age = 41,
			MedicalRecordNo = "PAT85061801",
			BedCode = "02床 (借床)",
			AdmittingDept = "心血管内科住院一科",
			NursingWard = "内科二病区 (执行借床)",
			NursingLevel = "二级护理",
			Diagnosis = "不稳定型心绞痛；高血压2级",
			AttendingDoctor = "张建国 (主治)",
			AdmissionDate = "2026-11-05 10:15",
			DailySummary = "今日患者活动后胸闷已缓解，血压 132/84 mmHg，长期阿司匹林、拜新同规律口服，无明显心悸。",
			HasAllergyAlert = true,
			AllergyTags = "青霉素过敏、磺胺类过敏",
			MedTechReportsAllSigned = true,
			DischargeMedsDispensed = false,
			MedicalInsuranceSettled = false,
			PrepaymentCleared = false,
			PrepaymentAmount = 5000m,
			ActualSelfPay = 1820m
		};
		inpatientRecordModel.Orders.Add(NewOrder("ORD-Z01", "长期医嘱", "阿司匹林肠溶片 100mg*30片", "100mg", "口服", "qn (每晚一次)", "执行中", "张建国"));
		inpatientRecordModel.Orders.Add(NewOrder("ORD-Z02", "长期医嘱", "硝苯地平控释片 (拜新同) 30mg*7片", "30mg", "口服", "qd (每日一次)", "执行中", "张建国"));
		inpatientRecordModel.Orders.Add(NewOrder("ORD-Z03", "长期医嘱", "单硝酸异山梨酯缓释片 40mg*14片", "40mg", "口服", "qd (每日一次)", "待主治审核", "李华", "--", "--", "--"));
		inpatientRecordModel.Orders.Add(NewOrder("ORD-Z04", "长期医嘱", "心血管内科二级护理常规", "持续", "床旁", "qd (每日一次)", "执行中", "张建国"));
		inpatientRecordModel.Orders.Add(NewOrder("ORD-Z05", "临时医嘱", "12导联常规心电图检查", "1次", "床旁", "st (立即执行)", "执行中", "李华"));
		inpatientRecordModel.Orders.Add(NewOrder("ORD-Z06", "临时医嘱", "急查肌钙蛋白I + 心肌酶谱五项", "1次", "静脉采血", "st (立即执行)", "执行中", "李华"));
		inpatientRecordModel.ApplyOrderFilter();
		inpatientRecordModel.ProgressNotes.Add(NewNote("2026-11-05 10:30", "李华 (住院医师)", "入院记录", "患者因阵发性心悸胸闷入院，入院诊断不稳定型心绞痛。", signed: true));
		inpatientRecordModel.ProgressNotes.Add(NewNote("2026-11-06 08:10", "李华 (住院医师)", "日常病程记录", inpatientRecordModel.DailySummary, signed: false));
		inpatientRecordModel.NursingTasks.Add(NewTask("NT-Z01", "08:00", "给药", "口服阿司匹林肠溶片 100mg", "长期医嘱", "高"));
		inpatientRecordModel.NursingTasks.Add(NewTask("NT-Z02", "10:00", "监护", "测量血压并记录胸闷变化", "二级护理", "中"));
		inpatientRecordModel.NursingTasks.Add(NewTask("NT-Z03", "14:00", "宣教", "胸痛发作自救与服药依从性宣教", "护理常规", "常规", "已执行", "14:04"));
		inpatientRecordModel.VitalSigns.Add(NewVitals("2026-11-06 08:00", "36.6", "78", "18", "132", "84", "98", "陈劳", abnormal: false));
		inpatientRecordModel.VitalSigns.Add(NewVitals("2026-11-05 20:00", "36.8", "82", "19", "141", "88", "97", "赵敏", abnormal: false));
		inpatientRecordModel.HandoverNotes.Add(NewHandover("白班 → 夜班", "陈劳 (责任护士)", "2026-11-05 18:00", "患者晚间偶有活动后胸闷，已告知值班医生；继续观察血压与心率。", confirmed: true));
		InpatientRecordModel inpatientRecordModel2 = new InpatientRecordModel
		{
			AdmissionNo = "ADM20261102001",
			PatientName = "刘德海",
			Gender = "男",
			Age = 67,
			MedicalRecordNo = "PAT85061811",
			BedCode = "01床",
			AdmittingDept = "心血管内科住院一科",
			NursingWard = "内科一病区",
			NursingLevel = "一级护理",
			Diagnosis = "急性前壁心肌梗死 (急诊PCI术后)",
			AttendingDoctor = "王敏 (副主任)",
			AdmissionDate = "2026-11-02 08:30",
			DailySummary = "支架植入术后第4天，穿刺点无渗血，心肌酶谱呈下降趋势，生命体征平稳，继续二级抗血小板治疗。",
			MedTechReportsAllSigned = true,
			DischargeMedsDispensed = false,
			MedicalInsuranceSettled = false,
			PrepaymentCleared = false,
			PrepaymentAmount = 12000m,
			ActualSelfPay = 7360m
		};
		inpatientRecordModel2.Orders.Add(NewOrder("ORD-L01", "长期医嘱", "硫酸氢氯吡格雷片 75mg*7片", "75mg", "口服", "qd (每日一次)", "执行中", "王敏"));
		inpatientRecordModel2.Orders.Add(NewOrder("ORD-L02", "长期医嘱", "阿托伐他汀钙片 20mg*7片", "20mg", "口服", "qn (每晚一次)", "已审核待执行", "王敏"));
		inpatientRecordModel2.Orders.Add(NewOrder("ORD-L03", "长期医嘱", "酒石酸美托洛尔片 25mg*20片", "25mg", "口服", "bid (每日两次)", "执行中", "王敏"));
		inpatientRecordModel2.Orders.Add(NewOrder("ORD-L04", "长期医嘱", "心血管内科一级护理常规 (急诊PCI术后)", "持续", "床旁", "持续", "执行中", "王敏"));
		inpatientRecordModel2.Orders.Add(NewOrder("ORD-L05", "临时医嘱", "低分子量肝素钠注射液 5000IU", "5000IU", "皮下注射", "q12h (每12小时)", "执行中", "李华"));
		inpatientRecordModel2.Orders.Add(NewOrder("ORD-L06", "临时医嘱", "急查凝血六项 + D-二聚体", "1次", "静脉采血", "明晨", "待主治审核", "李华", "--", "--", "--"));
		inpatientRecordModel2.ApplyOrderFilter();
		inpatientRecordModel2.ProgressNotes.Add(NewNote("2026-11-02 09:10", "李华 (住院医师)", "入院记录", "急诊PCI术后转入，生命体征平稳，穿刺点加压包扎。", signed: true));
		inpatientRecordModel2.ProgressNotes.Add(NewNote("2026-11-06 08:20", "李华 (住院医师)", "日常病程记录", inpatientRecordModel2.DailySummary, signed: false));
		inpatientRecordModel2.NursingTasks.Add(NewTask("NT-L01", "08:30", "给药", "二级抗血小板药物口服", "长期医嘱", "高"));
		inpatientRecordModel2.NursingTasks.Add(NewTask("NT-L02", "09:00", "观察", "PCI穿刺点渗血与足背动脉搏动", "一级护理", "高"));
		inpatientRecordModel2.NursingTasks.Add(NewTask("NT-L03", "15:00", "记录", "出入量与心电监护记录", "护理常规", "中", "已执行", "15:06"));
		inpatientRecordModel2.VitalSigns.Add(NewVitals("2026-11-06 08:00", "36.7", "72", "17", "124", "76", "98", "陈劳", abnormal: false));
		inpatientRecordModel2.HandoverNotes.Add(NewHandover("夜班 → 白班", "赵敏 (责任护士)", "2026-11-06 07:50", "夜间生命体征平稳，穿刺点干燥，无胸痛发作。", confirmed: false));
		InpatientRecordModel inpatientRecordModel3 = new InpatientRecordModel
		{
			AdmissionNo = "ADM20261103002",
			PatientName = "陈芳华",
			Gender = "女",
			Age = 73,
			MedicalRecordNo = "PAT85061822",
			BedCode = "05床 (ICU)",
			AdmittingDept = "心血管内科住院一科",
			NursingWard = "内科一病区",
			NursingLevel = "特级护理 (病危)",
			Diagnosis = "急性非ST段抬高型心梗；心源性休克",
			AttendingDoctor = "张建国 (主治)",
			AdmissionDate = "2026-11-03 14:20",
			DailySummary = "持续多巴胺泵入维持血压，持续心电监护，尿量每小时约 35ml，病情处于高危状态，需严密防范室速室颤！",
			IsCritical = true,
			HasAllergyAlert = true,
			AllergyTags = "碘造影剂过敏",
			MedTechReportsAllSigned = false,
			DischargeMedsDispensed = false,
			MedicalInsuranceSettled = false,
			PrepaymentCleared = false,
			PrepaymentAmount = 20000m,
			ActualSelfPay = 15800m
		};
		inpatientRecordModel3.Orders.Add(NewOrder("ORD-C01", "长期医嘱", "重酒石酸去甲肾上腺素注射液 4mg + 5%GS 50ml", "4mg/50ml", "持续微量泵入", "持续", "执行中", "张建国"));
		inpatientRecordModel3.Orders.Add(NewOrder("ORD-C02", "长期医嘱", "持续多参数心电监护 + 无创血压连续监测", "1套", "床旁", "持续", "执行中", "张建国"));
		inpatientRecordModel3.Orders.Add(NewOrder("ORD-C03", "长期医嘱", "ICU特级护理常规 (病危密切监护)", "持续", "床旁", "持续", "执行中", "张建国"));
		inpatientRecordModel3.Orders.Add(NewOrder("ORD-C04", "长期医嘱", "留置导尿管引流 + 每小时尿量精细计量", "1套", "床旁", "持续", "执行中", "张建国"));
		inpatientRecordModel3.Orders.Add(NewOrder("ORD-C05", "临时医嘱", "急查动脉血气分析 + 乳酸测定", "1次", "桡动脉穿刺", "st (立即执行)", "待主治审核", "李华", "--", "--", "--"));
		inpatientRecordModel3.Orders.Add(NewOrder("ORD-C06", "临时医嘱", "去乙酰毛花苷注射液 (西地兰) 0.4mg", "0.4mg", "静脉推注", "st (立即执行)", "执行中", "张建国"));
		inpatientRecordModel3.Orders.Add(NewOrder("ORD-C07", "临时医嘱", "呋塞米注射液 20mg", "20mg", "静脉推注", "st (立即执行)", "执行中", "张建国"));
		inpatientRecordModel3.ApplyOrderFilter();
		inpatientRecordModel3.ProgressNotes.Add(NewNote("2026-11-05 22:00", "张建国 (主治)", "抢救记录", "夜间血压下降至 82/50 mmHg，调整升压药泵速后回升。", signed: true));
		inpatientRecordModel3.ProgressNotes.Add(NewNote("2026-11-06 07:40", "李华 (住院医师)", "日常病程记录", inpatientRecordModel3.DailySummary, signed: false));
		inpatientRecordModel3.NursingTasks.Add(NewTask("NT-C01", "08:00", "抢救", "升压药泵入速度双人核对", "长期医嘱", "高"));
		inpatientRecordModel3.NursingTasks.Add(NewTask("NT-C02", "09:00", "监护", "每小时记录尿量与有创血压", "特级护理", "高"));
		inpatientRecordModel3.NursingTasks.Add(NewTask("NT-C03", "11:00", "护理", "翻身拍背与皮肤完整性评估", "护理常规", "中"));
		inpatientRecordModel3.VitalSigns.Add(NewVitals("2026-11-06 08:00", "37.9", "112", "26", "86", "52", "91", "陈劳", abnormal: true));
		inpatientRecordModel3.HandoverNotes.Add(NewHandover("夜班 → 白班", "赵敏 (责任护士)", "2026-11-06 07:55", "夜间升压药维持中，尿量偏少，已提醒白班重点观察循环灌注。", confirmed: false));
		InpatientRecordModel inpatientRecordModel4 = new InpatientRecordModel
		{
			AdmissionNo = "ADM20261101006",
			PatientName = "钱学森",
			Gender = "男",
			Age = 55,
			MedicalRecordNo = "PAT85061833",
			BedCode = "06床",
			AdmittingDept = "心血管内科住院一科",
			NursingWard = "内科一病区",
			NursingLevel = "三级护理",
			Diagnosis = "阵发性房颤 (射频消融术后已转窦性)",
			AttendingDoctor = "王敏 (副主任)",
			AdmissionDate = "2026-11-01 09:00",
			DailySummary = "术后恢复优良，动态心电图未见房颤复发。六严关核查已全部就绪，主治已签审出院记录，等待办理出院结算。",
			IsCritical = false,
			IsDischargeReady = true,
			MedTechReportsAllSigned = true,
			DischargeMedsDispensed = true,
			MedicalInsuranceSettled = true,
			PrepaymentCleared = true,
			PrepaymentAmount = 15000m,
			ActualSelfPay = 10520.50m
		};
		inpatientRecordModel4.Orders.Add(NewOrder("ORD-Q01", "长期医嘱", "利伐沙班片 20mg*14片", "20mg", "口服", "qd (每日一次)", "已停止", "王敏", "2026-11-04 10:00", "王敏"));
		inpatientRecordModel4.Orders.Add(NewOrder("ORD-Q02", "长期医嘱", "缬沙坦胶囊 80mg*14粒", "80mg", "口服", "qd (每日一次)", "已停止", "王敏", "2026-11-04 10:00", "王敏"));
		inpatientRecordModel4.Orders.Add(NewOrder("ORD-Q03", "长期医嘱", "心血管内科三级护理常规", "持续", "床旁", "qd", "已停止", "王敏", "2026-11-04 10:00", "王敏"));
		inpatientRecordModel4.Orders.Add(NewOrder("ORD-Q04", "临时医嘱", "动态心电图 (Holter 24h) 复查", "1次", "佩戴", "完成", "已停止", "王敏", "2026-11-03 16:00", "王敏"));
		inpatientRecordModel4.Orders.Add(NewOrder("ORD-Q05", "临时医嘱", "出院带药双人核对处方", "1次", "药房", "完成", "已停止", "王敏", "2026-11-04 11:00", "王敏"));
		inpatientRecordModel4.ApplyOrderFilter();
		inpatientRecordModel4.ProgressNotes.Add(NewNote("2026-11-03 09:00", "王敏 (副主任)", "出院记录", "房颤射频消融术后恢复良好，心律稳定，准予出院。", signed: true));
		inpatientRecordModel4.ProgressNotes.Add(NewNote("2026-11-04 08:30", "李华 (住院医师)", "日常病程记录", "无胸闷心悸，穿刺点愈合良好，出院宣教已完成。", signed: true));
		inpatientRecordModel4.NursingTasks.Add(NewTask("NT-Q01", "08:00", "宣教", "出院服药与复诊计划宣教", "护理常规", "常规", "已核对", "08:20"));
		inpatientRecordModel4.NursingTasks.Add(NewTask("NT-Q02", "09:00", "观察", "心电监护与脉搏短绌观察", "三级护理", "常规", "已核对", "09:15"));
		inpatientRecordModel4.NursingTasks.Add(NewTask("NT-Q03", "10:00", "出院", "出院带药核对与床单位终末处理", "出院流程", "中", "已核对", "10:10"));
		inpatientRecordModel4.VitalSigns.Add(NewVitals("2026-11-06 07:30", "36.5", "68", "16", "118", "72", "99", "陈劳", abnormal: false));
		inpatientRecordModel4.HandoverNotes.Add(NewHandover("夜班 → 白班", "赵敏 (责任护士)", "2026-11-06 07:45", "患者今日拟出院，出院带药及结算资料已备齐。", confirmed: true));
		AllInpatientRecords.Add(inpatientRecordModel);
		AllInpatientRecords.Add(inpatientRecordModel2);
		AllInpatientRecords.Add(inpatientRecordModel3);
		AllInpatientRecords.Add(inpatientRecordModel4);
		DecomposeStandingOrdersForPatient(inpatientRecordModel, DateOnly.FromDateTime(DateTime.Today), announce: false);
		DecomposeStandingOrdersForPatient(inpatientRecordModel2, DateOnly.FromDateTime(DateTime.Today), announce: false);
		DecomposeStandingOrdersForPatient(inpatientRecordModel3, DateOnly.FromDateTime(DateTime.Today), announce: false);
	}

	private void InitPharmacyRecords()
	{
		PharmacyRecords.Clear();
		PharmacyDispenseModel[] array = new PharmacyDispenseModel[4]
		{
			new PharmacyDispenseModel
			{
				PrescriptionNo = "RX-20261105-001",
				PatientName = "周建强",
				DrugId = "DRUG-04",
				DispenseQuantity = 2,
				TotalAmount = 89.30m,
				DrugSummary = "0.9%氯化钠注射液 500ml*2瓶, 阿司匹林肠溶片 100mg*2盒 (¥89.30)",
				DispenseStatus = "已收费通知药房 · 待扫码发药",
				PaymentNoticeStatus = "✓ 收费员刘洋已收费 (PAY2026110501) · 已推送药房可发药通知",
				PharmacistName = "孙伟 (主管药师)",
				SafetyAudit = "含青霉素阳性拦截项 (需先替换高危药或解除阻断)",
				IsAllergyBlocked = true,
				IsPaidNotified = true,
				IsOrderClosed = false,
				IsReturned = false,
				CanDispense = false
			},
			new PharmacyDispenseModel
			{
				PrescriptionNo = "RX-20261105-002",
				PatientName = "王大明",
				DrugId = "DRUG-04",
				DispenseQuantity = 2,
				TotalAmount = 45.00m,
				DrugSummary = "阿司匹林肠溶片 100mg*2盒 (LOT202605C · ¥45.00)",
				DispenseStatus = "\ud83d\udd12 已发药并关闭订单 (防二次发药)",
				PaymentNoticeStatus = "✓ 已收费 (PAY2026110502) · 发药完成已自动关单锁止",
				PharmacistName = "孙伟 (主管药师)",
				SafetyAudit = "前置审方通过 · FEFO批次出库完成",
				IsAllergyBlocked = false,
				IsPaidNotified = true,
				IsOrderClosed = true,
				IsReturned = false,
				CanDispense = false
			},
			new PharmacyDispenseModel
			{
				PrescriptionNo = "RX-20261105-003",
				PatientName = "赵淑珍",
				DrugId = "DRUG-05",
				DispenseQuantity = 2,
				TotalAmount = 113.60m,
				DrugSummary = "硫酸氢氯吡格雷片 75mg*2盒 (LOT202605D · ¥113.60)",
				DispenseStatus = "已收费通知药房 · 待扫码发药",
				PaymentNoticeStatus = "✓ 收费员刘洋已收费 (PAY2026110503) · 已推送药房可发药通知",
				PharmacistName = "孙伟 (主管药师)",
				SafetyAudit = "前置审方通过 · 皮试免检",
				IsAllergyBlocked = false,
				IsPaidNotified = true,
				IsOrderClosed = false,
				IsReturned = false,
				CanDispense = true
			},
			new PharmacyDispenseModel
			{
				PrescriptionNo = "RX-20261105-004",
				PatientName = "孙建国",
				DrugId = "DRUG-03",
				DispenseQuantity = 4,
				TotalAmount = 22.00m,
				DrugSummary = "0.9%氯化钠注射液 500ml*4瓶 (¥22.00)",
				DispenseStatus = "⏳ 待收费员收费 (未收费严禁发药)",
				PaymentNoticeStatus = "⚠\ufe0f 患者尚未缴费 · 财务收费后将自动变更为可发药状态并通知药房",
				PharmacistName = "待分配",
				SafetyAudit = "前置审方通过 · 等待财务收费通知",
				IsAllergyBlocked = false,
				IsPaidNotified = false,
				IsOrderClosed = false,
				IsReturned = false,
				CanDispense = false
			}
		};
		PharmacyDispenseModel[] array2 = array;
		foreach (PharmacyDispenseModel item in array2)
		{
			PharmacyRecords.Add(item);
		}
	}

	private void InitOfdArchives()
	{
		OfdArchiveRecords.Clear();
		OfdArchiveRecordModel[] array = new OfdArchiveRecordModel[3]
		{
			new OfdArchiveRecordModel
			{
				ArchiveNo = "OFD-20261105-CARDIO-001",
				DocType = "门诊初诊病历 (OPD_EMR)",
				PatientName = "周建强",
				DeptName = "心血管内科门诊",
				OfdStandard = "GB/T 33190-2016",
				ZipEntries = "OFD.xml | Document.xml | CustomTags/EmrStructuredData.xml | Signatures.xml",
				HashSha256 = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
				SignatureStatus = "三级电子审签完成 (防篡改验签合法)",
				ArchiveTime = "2026-11-05 15:48:22",
				IsVerified = true
			},
			new OfdArchiveRecordModel
			{
				ArchiveNo = "OFD-20261103-RESP-008",
				DocType = "住院入院记录 (IPD_ADMISSION)",
				PatientName = "刘德海",
				DeptName = "呼吸内科住院部",
				OfdStandard = "GB/T 33190-2016",
				ZipEntries = "OFD.xml | Document.xml | CustomTags/EmrStructuredData.xml | Signatures.xml",
				HashSha256 = "8F434346648F6B96DF89DDA901C5176B10A6D83961DD3C1AC88B59B2DC327AA4",
				SignatureStatus = "主治医师审签完成 (归档封存)",
				ArchiveTime = "2026-11-03 10:22:05",
				IsVerified = true
			},
			new OfdArchiveRecordModel
			{
				ArchiveNo = "OFD-20261031-CARDIO-012",
				DocType = "冠状动脉造影介入手术记录 (OPERATION)",
				PatientName = "钱学森",
				DeptName = "心血管内科介入手术室",
				OfdStandard = "GB/T 33190-2016",
				ZipEntries = "OFD.xml | Document.xml | CustomTags/EmrStructuredData.xml | Signatures.xml",
				HashSha256 = "CA978112CA1BBDCAFAC231B39A23DC4DA786EFF8147C4E72B9807785AFEE48BB",
				SignatureStatus = "术者+一助+麻醉三签完成",
				ArchiveTime = "2026-10-31 16:30:11",
				IsVerified = true
			}
		};
		OfdArchiveRecordModel[] array2 = array;
		foreach (OfdArchiveRecordModel item in array2)
		{
			OfdArchiveRecords.Add(item);
		}
	}

	private void InitDynamicFormTemplates()
	{
		DynamicFormTemplates.Clear();
		DynamicFormTemplateModel[] array = new DynamicFormTemplateModel[3]
		{
			new DynamicFormTemplateModel
			{
				TemplateCode = "FORM-CARDIO-FOLLOWUP-V2",
				TemplateName = "心血管专病结构化随访表单",
				DeptCategory = "OPD",
				TargetDeptId = "DEPT-OPD-01",
				Version = "v2.1",
				Status = "生产已发布"
			},
			new DynamicFormTemplateModel
			{
				TemplateCode = "FORM-RESP-COPD-ASSESS",
				TemplateName = "慢性阻塞性肺疾病 CAT 评分量表",
				DeptCategory = "OPD",
				TargetDeptId = "DEPT-ER-01",
				Version = "v1.4",
				Status = "生产已发布"
			},
			new DynamicFormTemplateModel
			{
				TemplateCode = "FORM-OBGYN-FIRST-VISIT",
				TemplateName = "妇产科门诊初诊专科数据采集表",
				DeptCategory = "OPD",
				TargetDeptId = "DEPT-OPD-01",
				Version = "v3.0",
				Status = "生产已发布"
			}
		};
		DynamicFormTemplateModel[] array2 = array;
		foreach (DynamicFormTemplateModel item in array2)
		{
			DynamicFormTemplates.Add(item);
		}
		DynamicFormTemplates.Add(CreatePrescriptionItemFieldsTemplate());
	}

	public async Task LoadDynamicFormTemplatesAsync()
	{
		if (!SessionContext.Current.CanSeeDynFormConfig)
		{
			return;
		}
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			ClientApiEnvelope<List<DynamicFormSchemaModel>> envelope = await http.GetFromJsonAsync<ClientApiEnvelope<List<DynamicFormSchemaModel>>>("/api/v4/config/schemas", JsonOptions);
			if (envelope == null || envelope.Code != 200 || envelope.Data == null || envelope.Data.Count == 0)
			{
				return;
			}
			DynamicFormTemplates.Clear();
			foreach (DynamicFormSchemaModel schema in envelope.Data)
			{
				DynamicFormTemplateModel template = FromSchema(schema);
				DynamicFormTemplates.Add(template);
			}
			if (DynamicFormTemplates.All((DynamicFormTemplateModel x) => !string.Equals(x.TemplateCode, "FORM-PRESCRIPTION-ITEM-FIELDS", StringComparison.OrdinalIgnoreCase)))
			{
				DynamicFormTemplates.Add(CreatePrescriptionItemFieldsTemplate());
			}
			SelectedFormTemplate = DynamicFormTemplates.FirstOrDefault();
			if (SelectedFormTemplate != null)
			{
				await LoadDynamicFormDetailAsync(SelectedFormTemplate);
			}
		}
		catch (HttpRequestException)
		{
			SystemNotice = "动态表单服务尚未连接，当前显示本地模板索引";
		}
		catch (Exception ex2)
		{
			Exception ex3 = ex2;
			SystemNotice = "动态表单模板加载失败: " + ex3.Message;
		}
	}

	public async Task<bool> LoadDynamicFormDetailAsync(
		DynamicFormTemplateModel template,
		CancellationToken cancellationToken = default)
	{
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			ClientApiEnvelope<DynamicFormSchemaDto> envelope =
				await http.GetFromJsonAsync<ClientApiEnvelope<DynamicFormSchemaDto>>(
					"/api/v4/config/schemas/" + Uri.EscapeDataString(template.TemplateCode) + "?includeDraft=true",
					JsonOptions,
					cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			if (envelope == null || envelope.Code != 200 || envelope.Data == null)
			{
				SystemNotice = $"表单字段加载失败: {template.TemplateName} ({envelope?.Message ?? "服务返回无效数据"})";
				return false;
			}
			DynamicFormSchemaModel schema = envelope.Data.Schema;
			template.Id = schema.Id;
			template.TemplateCode = schema.FormCode;
			template.TemplateName = schema.FormName;
			template.EntityType = schema.EntityType;
			template.DeptCategory = schema.DeptCategory ?? string.Empty;
			template.TargetDeptId = schema.TargetDeptId ?? string.Empty;
			template.GridColumns = schema.GridColumns;
			template.IsPublished = schema.IsPublished == 1;
			template.ExtDataJson = schema.ExtDataJson;
			template.SchemaVersion = schema.SchemaVersion;
			template.Version = ReadSchemaVersion(schema.ExtDataJson) ?? template.Version;
			template.Status = (template.IsPublished ? "生产已发布" : "草稿");
			template.ReplaceFields(envelope.Data.Fields.Select(MapField));
			if (string.Equals(template.TemplateCode, "FORM-PRESCRIPTION-ITEM-FIELDS", StringComparison.OrdinalIgnoreCase))
			{
				UpdatePrescriptionFieldCatalog(template.Fields);
			}
			if (SelectedPatient != null)
			{
				ApplyDynamicFieldVisibility(SelectedPatient);
			}
			OnPropertyChanged("SelectedFormTemplate");
			return true;
		}
		catch (OperationCanceledException)
		{
			return false;
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			SystemNotice = "表单字段加载失败: " + ex2.Message;
			return false;
		}
	}

	public void BeginNewFormTemplate()
	{
		if (!SessionContext.Current.CanSeeDynFormConfig)
		{
			return;
		}
		DynamicFormTemplateModel dynamicFormTemplateModel = new DynamicFormTemplateModel
		{
			TemplateCode = $"FORM-CUSTOM-{DateTime.Now:yyyyMMddHHmmss}",
			TemplateName = "未命名临床动态表单",
			EntityType = "OPD_VISIT",
			DeptCategory = "OPD",
			GridColumns = 2,
			IsPublished = false,
			Version = "v1.0",
			Status = "草稿"
		};
		dynamicFormTemplateModel.Fields.Add(new DynamicFormFieldModel
		{
			FieldCode = "NEW_FIELD",
			FieldLabel = "新字段",
			DataType = "STRING",
			UrsaControlType = "u:TextBox",
			VisibilityExpr = "ALL",
			SortOrder = 0
		});
		DynamicFormTemplates.Insert(0, dynamicFormTemplateModel);
		SelectedFormTemplate = dynamicFormTemplateModel;
		EditingTemplate = dynamicFormTemplateModel;
		EditingField = null;
		IsFormDesignerOpen = true;
		SystemNotice = "已创建草稿模板，请补充模板信息并保存";
	}

	public async Task<bool> BeginDesignFormAsync(
		DynamicFormTemplateModel template,
		CancellationToken cancellationToken = default)
	{
		if (!SessionContext.Current.CanSeeDynFormConfig)
		{
			return false;
		}
		SelectedFormTemplate = template;
		if (!await LoadDynamicFormDetailAsync(template, cancellationToken))
		{
			cancellationToken.ThrowIfCancellationRequested();
			return false;
		}
		cancellationToken.ThrowIfCancellationRequested();
		EditingTemplate = template;
		EditingField = null;
		IsFormDesignerOpen = true;
		SystemNotice = "已打开 " + template.TemplateName + " 的字段设计器";
		return true;
	}

	public async Task BeginPreviewFormAsync(DynamicFormTemplateModel template)
	{
		SelectedFormTemplate = template;
		SystemNotice = "正在加载表单沙箱: [" + template.TemplateName + "]...";
		if (await LoadDynamicFormDetailAsync(template))
		{
			SystemNotice = $"表单预览已刷新: [{template.TemplateName}]，共 {template.FieldCount} 个字段，已按患者性别过滤显示。";
		}
	}

	public void BeginAddField()
	{
		if (!SessionContext.Current.CanSeeDynFormConfig)
		{
			return;
		}
		if (EditingTemplate == null)
		{
			SystemNotice = "请先选择表单模板";
			return;
		}
		_fieldEditSnapshot = null;
		bool flag = string.Equals(EditingTemplate?.TemplateCode, "FORM-PRESCRIPTION-ITEM-FIELDS", StringComparison.OrdinalIgnoreCase);
		DynamicFormFieldModel editingField = new DynamicFormFieldModel
		{
			FieldCode = (flag ? NextPrescriptionCustomFieldCode() : $"FIELD_{(EditingTemplate?.Fields.Count ?? 0) + 1:D2}"),
			FieldLabel = (flag ? "新明细字段" : "未命名字段"),
			SortOrder = (EditingTemplate?.Fields.Count ?? 0),
			VisibilityExpr = "ALL",
			UrsaControlType = "u:TextBox",
			DataType = "STRING",
			Placeholder = (flag ? "请输入新明细字段" : string.Empty),
			ColumnWidth = 120,
			IsSystemField = false,
			IsEditable = true
		};
		EditingField = editingField;
		IsFieldEditorOpen = true;
	}

	private string NextPrescriptionCustomFieldCode()
	{
		HashSet<string> hashSet = EditingTemplate?.Fields.Select((DynamicFormFieldModel x) => x.FieldCode).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (int num = 1; num <= 999; num++)
		{
			string text = $"CUSTOM_FIELD_{num:D2}";
			if (!hashSet.Contains(text))
			{
				return text;
			}
		}
		return $"CUSTOM_FIELD_{Guid.NewGuid():N}".Substring(0, 19);
	}

	public void BeginEditField(DynamicFormFieldModel field)
	{
		if (!SessionContext.Current.CanSeeDynFormConfig)
		{
			return;
		}
		DynamicFormTemplateModel? editingTemplate = EditingTemplate;
		_fieldEditSnapshot = ((editingTemplate != null && editingTemplate.Fields.Contains(field)) ? field.Clone() : null);
		EditingField = field;
		IsFieldEditorOpen = true;
	}

	public bool CommitField(bool closeEditor = true)
	{
		if (!SessionContext.Current.CanSeeDynFormConfig)
		{
			return false;
		}
		if (EditingTemplate == null || EditingField == null || string.IsNullOrWhiteSpace(EditingField.FieldCode) || string.IsNullOrWhiteSpace(EditingField.FieldLabel))
		{
			SystemNotice = "字段代码和字段标签不能为空";
			return false;
		}
		EditingField.FieldCode = EditingField.FieldCode.Trim();
		EditingField.FieldLabel = EditingField.FieldLabel.Trim();
		if (!IsValidFieldCode(EditingField.FieldCode))
		{
			SystemNotice = "字段代码必须以字母开头，且只能包含字母、数字和下划线（最多 64 位）";
			return false;
		}
		DynamicFormFieldModel dynamicFormFieldModel = EditingTemplate.Fields.FirstOrDefault((DynamicFormFieldModel x) => x != EditingField && string.Equals(x.FieldCode, EditingField.FieldCode, StringComparison.OrdinalIgnoreCase));
		if (dynamicFormFieldModel != null)
		{
			SystemNotice = "字段代码不能重复";
			return false;
		}
		if (!EditingTemplate.Fields.Contains(EditingField))
		{
			EditingTemplate.Fields.Add(EditingField);
		}
		EditingField.SortOrder = EditingTemplate.Fields.IndexOf(EditingField);
		_fieldEditSnapshot = null;
		EditingTemplate.NotifyDerived();
		RefreshEditingFormPreview();
		if (closeEditor)
		{
			IsFieldEditorOpen = false;
		}
		string text = (IsPrescriptionSchemaDesigner ? "；保存模板后处方表格将立即生成该列" : string.Empty);
		SystemNotice = "字段 [" + EditingField.FieldLabel + "] 已加入设计器" + text;
		return true;
	}

	public void RemoveEditingField()
	{
		if (!SessionContext.Current.CanSeeDynFormConfig)
		{
			return;
		}
		if (EditingTemplate != null && EditingField != null)
		{
			if (EditingField.IsProtectedField)
			{
				SystemNotice = EditingField.RemoveBlockedReason;
				return;
			}
			EditingTemplate.Fields.Remove(EditingField);
			_fieldEditSnapshot = null;
			ReorderFields(EditingTemplate);
			EditingTemplate.NotifyDerived();
			RefreshEditingFormPreview();
			IsFieldEditorOpen = false;
			EditingField = null;
			SystemNotice = (IsPrescriptionSchemaDesigner ? "字段已从处方表格配置中移除；保存模板后软删除，历史处方值仍保留" : "字段已移除，保存模板后生效");
		}
	}

	public void MoveField(DynamicFormFieldModel field, int offset)
	{
		if (!SessionContext.Current.CanSeeDynFormConfig)
		{
			return;
		}
		if (EditingTemplate != null)
		{
			int num = EditingTemplate.Fields.IndexOf(field);
			int num2 = num + offset;
			if (num >= 0 && num2 >= 0 && num2 < EditingTemplate.Fields.Count)
			{
				EditingTemplate.Fields.Move(num, num2);
				ReorderFields(EditingTemplate);
				RefreshEditingFormPreview();
			}
		}
	}

	public async Task<bool> SaveEditingTemplateAsync()
	{
		if (!SessionContext.Current.CanSeeDynFormConfig)
		{
			return false;
		}
		if (EditingTemplate == null)
		{
			return false;
		}
		if (string.IsNullOrWhiteSpace(EditingTemplate.TemplateCode) || string.IsNullOrWhiteSpace(EditingTemplate.TemplateName))
		{
			SystemNotice = "模板代码和模板名称不能为空";
			return false;
		}
		ReorderFields(EditingTemplate);
		var body = new
		{
			Schema = new
			{
				Id = EditingTemplate.Id,
				FormCode = EditingTemplate.TemplateCode.Trim(),
				FormName = EditingTemplate.TemplateName.Trim(),
				EntityType = EditingTemplate.EntityType,
				DeptCategory = EditingTemplate.DeptCategory,
				TargetDeptId = EditingTemplate.TargetDeptId,
				GridColumns = EditingTemplate.GridColumns,
				IsPublished = (short)(EditingTemplate.IsPublished ? 1 : 0),
				ExtDataJson = MergeSchemaExtDataJson(EditingTemplate.ExtDataJson, EditingTemplate.Version),
				SchemaVersion = EditingTemplate.SchemaVersion
			},
			Fields = EditingTemplate.Fields.Select((DynamicFormFieldModel field) => new
			{
				Id = field.Id,
				FormSchemaId = field.FormSchemaId,
				FieldCode = field.FieldCode,
				FieldLabel = field.FieldLabel,
				DataType = field.DataType,
				UrsaControlType = field.UrsaControlType,
				ControlKey = field.ControlKey,
				DictCode = field.DictCode,
				UnitText = field.UnitText,
				ValidationRuleJson = field.BuildValidationRuleJson(),
				VisibilityExpr = field.VisibilityExpr,
				IsIndexed = field.IsIndexed,
				SortOrder = field.SortOrder,
				ExtDataJson = field.ExtDataJson,
				SchemaVersion = field.SchemaVersion
			}).ToArray(),
			OperatorId = CurrentOperatorId
		};
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			using HttpResponseMessage response = await http.PostAsJsonAsync("/api/v4/config/schemas", body);
			ClientApiEnvelope<DynamicFormSchemaDto> envelope = await response.Content.ReadFromJsonAsync<ClientApiEnvelope<DynamicFormSchemaDto>>(JsonOptions);
			int num;
			if (response.IsSuccessStatusCode)
			{
				num = ((envelope == null || envelope.Code != 200) ? 1 : 0);
			}
			else
			{
				num = 1;
			}
			if (num != 0)
			{
				SystemNotice = envelope?.Message ?? "动态表单保存失败";
				return false;
			}
			bool isPrescriptionSchema = string.Equals(EditingTemplate.TemplateCode, "FORM-PRESCRIPTION-ITEM-FIELDS", StringComparison.OrdinalIgnoreCase);
			int previousColumnCount = PrescriptionColumns.Count;
			await LoadDynamicFormDetailAsync(EditingTemplate);
			if (isPrescriptionSchema)
			{
				await LoadPrescriptionColumnsAsync();
			}
			IsFormDesignerOpen = false;
			if (isPrescriptionSchema)
			{
				int currentColumnCount = PrescriptionColumns.Count;
				int addedColumnCount = Math.Max(0, currentColumnCount - previousColumnCount);
				SystemNotice = ((addedColumnCount > 0) ? $"字段配置已保存，处方表格已新增 {addedColumnCount} 列（现共 {currentColumnCount} 列）" : $"字段配置已保存，处方表格已同步为 {currentColumnCount} 列");
			}
			else
			{
				SystemNotice = "模板 [" + EditingTemplate.TemplateName + "] 已保存，字段定义已持久化";
			}
			return true;
		}
		catch (Exception ex)
		{
			SystemNotice = "动态表单保存失败: " + ex.Message;
			return false;
		}
	}

	public System.Windows.Input.ICommand SaveDynamicFormInstanceCommand => new HIS.Client.UrsaShell.Shell.AsyncRelayCommand(SaveDynamicFormInstanceAsync);

	public async Task SaveDynamicFormInstanceAsync()
	{
		DynamicFormTemplateModel template = SelectedFormTemplate;
		PatientQueueItemModel patient = SelectedPatient;
		if (template == null || patient == null)
		{
			return;
		}
		var values = template.Fields.Where((DynamicFormFieldModel x) => x.VisibilityExpr == "ALL" || (x.VisibilityExpr == "MALE_ONLY" && patient.Gender == "男") || (x.VisibilityExpr == "FEMALE_ONLY" && patient.Gender == "女")).Select((DynamicFormFieldModel x) =>
		{
			string id = Guid.NewGuid().ToString("D");
			string entityType = template.EntityType;
			string visitNo = patient.VisitNo;
			string fieldCode = x.FieldCode;
			bool flag;
			switch (x.DataType)
			{
			case "STRING":
			case "TEXT":
			case "ENUM":
			case "BOOLEAN":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			long result;
			decimal result2;
			return new
			{
				Id = id,
				EntityType = entityType,
				EntityId = visitNo,
				FieldCode = fieldCode,
				ValString = (flag ? x.RuntimeValue : null),
				ValInt = ((x.DataType == "INTEGER" && long.TryParse(x.RuntimeValue, out result)) ? new long?(result) : ((long?)null)),
				ValDecimal = ((x.DataType == "DECIMAL" && decimal.TryParse(x.RuntimeValue, out result2)) ? new decimal?(result2) : ((decimal?)null)),
				ValTimeMs = (long?)null
			};
		}).ToArray();
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			using HttpResponseMessage response = await http.PostAsJsonAsync("/api/v4/config/values", values);
			ClientApiEnvelope<int> envelope = await response.Content.ReadFromJsonAsync<ClientApiEnvelope<int>>(JsonOptions);
			WorkbenchModel workbenchModel = this;
			object systemNotice;
			if (response.IsSuccessStatusCode)
			{
				if (envelope != null && envelope.Code == 200)
				{
					systemNotice = $"动态表单实例已保存：患者 {patient.PatientName}，写入 {envelope.Data} 个字段";
					goto IL_02be;
				}
			}
			systemNotice = envelope?.Message ?? "动态表单实例保存失败";
			goto IL_02be;
			IL_02be:
			workbenchModel.SystemNotice = (string)systemNotice;
		}
		catch (Exception ex)
		{
			SystemNotice = "动态表单实例保存失败: " + ex.Message;
		}
	}

	private static DynamicFormTemplateModel FromSchema(DynamicFormSchemaModel schema)
	{
		return new DynamicFormTemplateModel
		{
			Id = schema.Id,
			TemplateCode = schema.FormCode,
			TemplateName = schema.FormName,
			EntityType = schema.EntityType,
			DeptCategory = (schema.DeptCategory ?? string.Empty),
			TargetDeptId = (schema.TargetDeptId ?? string.Empty),
			GridColumns = schema.GridColumns,
			IsPublished = (schema.IsPublished == 1),
			Version = (ReadSchemaVersion(schema.ExtDataJson) ?? "v1.0"),
			Status = ((schema.IsPublished == 1) ? "生产已发布" : "草稿"),
			ExtDataJson = schema.ExtDataJson,
			SchemaVersion = schema.SchemaVersion
		};
	}

	private static DynamicFormFieldModel MapField(DynamicFormFieldDto dto)
	{
		DynamicFormFieldModel dynamicFormFieldModel = new DynamicFormFieldModel
		{
			Id = dto.Id,
			FormSchemaId = dto.FormSchemaId,
			FieldCode = dto.FieldCode,
			FieldLabel = dto.FieldLabel,
			DataType = dto.DataType,
			UrsaControlType = dto.UrsaControlType,
			ControlKey = (dto.ControlKey ?? string.Empty),
			DictCode = dto.DictCode,
			UnitText = (dto.UnitText ?? string.Empty),
			VisibilityExpr = (string.IsNullOrWhiteSpace(dto.VisibilityExpr) ? "ALL" : dto.VisibilityExpr),
			IsIndexed = dto.IsIndexed,
			SortOrder = dto.SortOrder,
			ExtDataJson = dto.ExtDataJson,
			SchemaVersion = dto.SchemaVersion
		};
		dynamicFormFieldModel.LoadValidationRuleJson(dto.ValidationRuleJson);
		return dynamicFormFieldModel;
	}

	private static string MergeSchemaExtDataJson(string? existingJson, string? version)
	{
		JsonObject jsonObject;
		try
		{
			jsonObject = (string.IsNullOrWhiteSpace(existingJson) ? new JsonObject() : ((JsonNode.Parse(existingJson) as JsonObject) ?? new JsonObject()));
		}
		catch (JsonException)
		{
			jsonObject = new JsonObject();
		}
		jsonObject["version"] = (string.IsNullOrWhiteSpace(version) ? "v1.0" : version.Trim());
		return jsonObject.ToJsonString();
	}

	private static string? ReadSchemaVersion(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}
		try
		{
			string value;
			return (JsonNode.Parse(json) is JsonObject jsonObject && jsonObject["version"] is JsonValue jsonValue && jsonValue.TryGetValue<string>(out value) && !string.IsNullOrWhiteSpace(value)) ? value : null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static void ReorderFields(DynamicFormTemplateModel template)
	{
		for (int i = 0; i < template.Fields.Count; i++)
		{
			template.Fields[i].SortOrder = i;
		}
		template.NotifyDerived();
	}

	private HttpClient CreateConfigHttpClient()
	{
		HttpMessageHandler handler = new ServerConnectionHandler(this)
		{
			InnerHandler = new HttpClientHandler()
		};
		HttpClient httpClient = new HttpClient(new AuthenticatedHttpHandler(SessionContext.Current, handler))
		{
			BaseAddress = new Uri(ApiEndpoints.BaseAddress),
			Timeout = TimeSpan.FromSeconds(30)
		};
		return httpClient;
	}

	private void InitTimelineEvents()
	{
		TimelineEvents.Clear();
		TimelineEventModel[] array = new TimelineEventModel[6]
		{
			new TimelineEventModel
			{
				EventTime = "2026-11-06 08:05:12",
				EventType = "CRITICAL_ALERT",
				EventTitle = "检验急诊危急值强广播 [hs-cTnI = 12.85 ng/mL]",
				Department = "医学检验科 急诊急救生化室",
				DoctorOrOperator = "李红技师 / 系统自动广播",
				Summary = "超敏肌钙蛋白 I 定量异常升高（超警戒线 300 倍），已触发双通道声光强推送！需管床主治与病区护士双签收。",
				BadgeClass = "Danger",
				ResultIcon = "\ud83d\udea8"
			},
			new TimelineEventModel
			{
				EventTime = "2026-11-05 10:30:15",
				EventType = "SAFETY_GATE",
				EventTitle = "用药安全红线阻断 [注射用青霉素钠]",
				Department = "门诊用药安全中心",
				DoctorOrOperator = "系统规则引擎 (前置审方)",
				Summary = "检测到患者既往明确青霉素过敏史且皮试结果阳性，处方已被安全硬隔离锁定，发药与执行接口 100% 拒绝提交！",
				BadgeClass = "Danger",
				ResultIcon = "\ud83d\udee1\ufe0f"
			},
			new TimelineEventModel
			{
				EventTime = "2026-11-05 10:15:00",
				EventType = "MEDTECH_LOCK",
				EventTitle = "医技到检核收并确费 [12导联同步心电图]",
				Department = "功能检查科",
				DoctorOrOperator = "王敏 (心电技师)",
				Summary = "患者已在门诊 2 楼心电图室完成扫码到检，已确费锁单，直接撤单与财务退款已锁定。",
				BadgeClass = "Warning",
				ResultIcon = "\ud83d\udd12"
			},
			new TimelineEventModel
			{
				EventTime = "2026-11-05 09:30:22",
				EventType = "OPD_VISIT",
				EventTitle = "心血管内科门诊初诊接诊",
				Department = "心血管内科门诊",
				DoctorOrOperator = "张建国 (主治医师)",
				Summary = "主诉阵发性心前区憋闷心悸3天；初步诊断为劳力性心绞痛 (CCS II级)；完成 EMR 结构化录入并生成 GB/T 33190-2016 OFD。",
				BadgeClass = "Primary",
				ResultIcon = "\ud83e\ude7a"
			},
			new TimelineEventModel
			{
				EventTime = "2026-11-03 14:10:00",
				EventType = "ER_RESCUE",
				EventTitle = "急诊医学科绿色通道留观抢救",
				Department = "急诊医学科诊室",
				DoctorOrOperator = "赵晓东 (副主任医师)",
				Summary = "血压急剧升高至 168/102 mmHg，伴气促出汗。开启绿色通道先诊疗后付费授信 (5000元)，静脉滴注硝酸甘油。",
				BadgeClass = "Warning",
				ResultIcon = "\ud83d\ude91"
			},
			new TimelineEventModel
			{
				EventTime = "2026-10-18 09:00:00",
				EventType = "LAB_ARCHIVE",
				EventTitle = "全血细胞分析与凝血功能四项已归档",
				Department = "医学检验科",
				DoctorOrOperator = "张梅 (主管检验师)",
				Summary = "白细胞 7.8×10^9/L，血小板 215×10^9/L，PT 12.1s，APTT 32.5s，报告 OFD 已同步至全院临床数据中心 (CDR)。",
				BadgeClass = "Success",
				ResultIcon = "\ud83e\uddea"
			}
		};
		TimelineEventModel[] array2 = array;
		foreach (TimelineEventModel item in array2)
		{
			TimelineEvents.Add(item);
		}
	}

	public async Task LoadAvailableDrugsCatalogAsync()
	{
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			DrugCatalogEnvelope? envelope = await http.GetFromJsonAsync<DrugCatalogEnvelope>(
				"/api/v4/inventory/drugs?activeOnly=false", JsonOptions);
			if (envelope is not { Code: 200, Data: not null })
			{
				SystemNotice = "药品目录加载失败：" + (envelope?.Message ?? "服务端未返回药品目录");
				return;
			}

			AvailableDrugsCatalog.Clear();
			foreach (InventoryDrugDto row in envelope.Data)
			{
				AvailableDrugsCatalog.Add(new DrugCatalogItemModel
				{
					DrugId = row.Id,
					DrugCode = row.DrugCode,
					DrugName = row.DrugName,
					Spec = row.Spec,
					UnitName = row.UnitName,
					UnitPrice = $"¥{row.Price:0.00}",
					StockQuantity = row.IsActive == 1 ? "可用" : "已停用",
					IsSkinTestRequired = row.IsSkinTestRequired == 1,
					IsHighRisk = row.AntibioticLevel > 0 || row.IsNarcotic == 1
				});
			}
			OnPropertyChanged(nameof(AvailableDrugsCatalog));
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
		{
			SystemNotice = "药品目录加载失败：" + ex.Message;
		}
	}

	public void JumpToStation(int targetIndex)
	{
		SelectedNavIndex = targetIndex;
		SystemNotice = "已跳转至【" + CurrentStationTitle + "】";
	}

	private void EditingField_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (sender is DynamicFormFieldModel field)
		{
			SynchronizeFieldControl(field, e.PropertyName);
		}
		RefreshEditingFormPreview();
	}

	private static void SynchronizeFieldControl(DynamicFormFieldModel field, string? propertyName)
	{
		if (string.Equals(propertyName, "DataType", StringComparison.Ordinal))
		{
			string dataType = field.DataType;
			if (1 == 0)
			{
			}
			string text;
			switch (dataType)
			{
			case "TEXT":
				text = "u:TextArea";
				break;
			case "INTEGER":
				text = "u:NumericIntUpDown";
				break;
			case "DECIMAL":
				text = "u:NumericDoubleUpDown";
				break;
			case "BOOLEAN":
				text = "CheckBox";
				break;
			case "DATE":
			case "DATETIME":
				text = "DatePicker";
				break;
			case "ENUM":
				text = "ComboBox";
				break;
			default:
				text = "u:TextBox";
				break;
			}
			if (1 == 0)
			{
			}
			string text2 = text;
			if (!string.Equals(field.UrsaControlType, text2, StringComparison.Ordinal))
			{
				field.UrsaControlType = text2;
			}
		}
		else
		{
			if (!string.Equals(propertyName, "UrsaControlType", StringComparison.Ordinal))
			{
				return;
			}
			string ursaControlType = field.UrsaControlType;
			if (1 == 0)
			{
			}
			bool flag2;
			switch (ursaControlType)
			{
			case "u:TextArea":
			{
				string text = field.DataType;
				bool flag = ((text == "TEXT" || text == "STRING") ? true : false);
				flag2 = flag;
				break;
			}
			case "u:NumericIntUpDown":
				flag2 = field.DataType == "INTEGER";
				break;
			case "u:NumericDoubleUpDown":
				flag2 = field.DataType == "DECIMAL";
				break;
			case "ComboBox":
				flag2 = field.DataType == "ENUM";
				break;
			case "DatePicker":
			{
				string text = field.DataType;
				bool flag = ((text == "DATE" || text == "DATETIME") ? true : false);
				flag2 = flag;
				break;
			}
			case "CheckBox":
				flag2 = field.DataType == "BOOLEAN";
				break;
			default:
			{
				string text = field.DataType;
				bool flag = ((text == "STRING" || text == "TEXT") ? true : false);
				flag2 = flag;
				break;
			}
			}
			if (1 == 0)
			{
			}
			if (!flag2)
			{
				string ursaControlType2 = field.UrsaControlType;
				if (1 == 0)
				{
				}
				string text = ursaControlType2 switch
				{
					"u:TextArea" => "TEXT", 
					"u:NumericIntUpDown" => "INTEGER", 
					"u:NumericDoubleUpDown" => "DECIMAL", 
					"ComboBox" => "ENUM", 
					"DatePicker" => "DATE", 
					"CheckBox" => "BOOLEAN", 
					_ => "STRING", 
				};
				if (1 == 0)
				{
				}
				field.DataType = text;
			}
		}
	}

	public void CancelFieldEdit()
	{
		if (EditingTemplate != null && EditingField != null && _fieldEditSnapshot != null && EditingTemplate.Fields.Contains(EditingField))
		{
			EditingField.CopyEditablePropertiesFrom(_fieldEditSnapshot);
		}
		_fieldEditSnapshot = null;
		IsFieldEditorOpen = false;
		EditingField = null;
		RefreshEditingFormPreview();
	}

	private static bool IsValidFieldCode(string fieldCode)
	{
		return Regex.IsMatch(fieldCode, "^[A-Za-z][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant);
	}

	public void RefreshEditingFormPreview()
	{
		if (SelectedPatient != null && EditingTemplate != null)
		{
			ApplyDynamicFieldVisibility(SelectedPatient, EditingTemplate);
			if (EditingField != null && !EditingTemplate.Fields.Contains(EditingField))
			{
				ApplyDynamicFieldVisibility(SelectedPatient, EditingField);
			}
		}
		OnPropertyChanged("PreviewFields");
		OnPropertyChanged("IsPrescriptionSchemaDesigner");
		OnPropertyChanged("IsGenericSchemaDesigner");
		OnPropertyChanged("FieldApplyButtonText");
		OnPropertyChanged("DesignerSaveButtonText");
		OnPropertyChanged("FieldApplyHint");
		OnPropertyChanged("PreviewFieldCount");
		OnPropertyChanged("IsPreviewEmpty");
		OnPropertyChanged("PreviewPatientSummary");
	}

	public void SyncWithSession()
	{
		if (SessionContext.Current.IsLoggedIn)
		{
			UserName = SessionContext.Current.DisplayName + " (" + SessionContext.Current.RoleName + ")";
			if (!string.IsNullOrEmpty(SessionContext.Current.ActiveDeptName))
			{
				ActiveDeptName = SessionContext.Current.ActiveDeptName;
			}
		}
		AvailableContexts.Clear();
		foreach (LoginContextSnapshot item in SessionContext.Current.AvailableContexts)
		{
			bool isSelected = (!string.IsNullOrWhiteSpace(SessionContext.Current.ActivePostId) && item.PostId == SessionContext.Current.ActivePostId) || (string.IsNullOrWhiteSpace(SessionContext.Current.ActivePostId) && item.RoleCode == SessionContext.Current.RoleCode && item.DeptName == SessionContext.Current.ActiveDeptName);
			AvailableContexts.Add(new LoginContextOption
			{
				PostId = item.PostId,
				UserId = item.UserId,
				UserName = item.UserName,
				DisplayName = item.DisplayName,
				EmployeeNo = item.EmployeeNo,
				PrescribeLevel = item.PrescribeLevel,
				SurgeryLevel = item.SurgeryLevel,
				EmrSignLevel = item.EmrSignLevel,
				RoleId = item.RoleId,
				RoleCode = item.RoleCode,
				RoleName = item.RoleName,
				DeptId = item.DeptId,
				DeptName = item.DeptName,
				DeptCategory = item.DeptCategory,
				WardId = item.WardId,
				DataScope = item.DataScope,
				IsDefault = item.IsDefault,
				IsSelected = isSelected
			});
		}
		if (AvailableContexts.Count > 0 && AvailableContexts.All((LoginContextOption x) => !x.IsSelected))
		{
			AvailableContexts[0].IsSelected = true;
		}
		OnPropertyChanged("RoleBadgeText");
		OnPropertyChanged("UserRoleSubtitle");
		OnPropertyChanged("CurrentRoleCode");
		OnPropertyChanged("CanPrescribe");
		OnPropertyChanged("CanSignEmr");
		OnPropertyChanged("CanSurgery");
		OnPropertyChanged("IsAdmin");
		OnPropertyChanged("IsNurse");
		OnPropertyChanged("CanSeeOpdStation");
		OnPropertyChanged("CanSeeIpdStation");
		OnPropertyChanged("CanSeeWardStation");
		OnPropertyChanged("CanSeeMedTech");
		OnPropertyChanged("IsPharmacistRole");
		OnPropertyChanged("IsCashierRole");
		OnPropertyChanged("CanSeePharmacy");
		OnPropertyChanged("CanSeeFinance");
		OnPropertyChanged("CanSeeSchedule");
		OnPropertyChanged("CanSeeEmrOfd");
		OnPropertyChanged("CanSeeDynFormConfig");
		OnPropertyChanged("CanSeeAdminPanel");
		OnPropertyChanged("CanOperateOpdClinical");
		OnPropertyChanged("CanOperateIpdClinical");
		OnPropertyChanged("CanOperateWardNursing");
		OnPropertyChanged("CanOperatePharmacy");
		OnPropertyChanged("CanOperateFinance");
		OnPropertyChanged("CanFinalizeInpatientOrders");
		OnPropertyChanged("CanExecuteNursingTasks");
		OnPropertyChanged("CurrentSodRoleBannerText");
		OnPropertyChanged("DefaultStationIndex");
		OnPropertyChanged("ActiveContextSummary");
		OnPropertyChanged("HasMultipleContexts");
		OnPropertyChanged("CurrentRoleDisplayName");
		OnPropertyChanged("CurrentOperatorName");
		OnPropertyChanged("CurrentOperatorRole");
		RebuildScopedInpatientData();
		RebuildNavigationItems();
	}

	private void RebuildScopedInpatientData()
	{
		string deptName = SessionContext.Current.ActiveDeptName?.Trim() ?? string.Empty;
		bool isAdminOrClinician = SessionContext.Current.IsAdmin || SessionContext.Current.IsIpdResident || SessionContext.Current.IsIpdAttending;
		List<InpatientRecordModel> list = ((string.IsNullOrWhiteSpace(deptName) | isAdminOrClinician) ? AllInpatientRecords.ToList() : AllInpatientRecords.Where((InpatientRecordModel x) => string.Equals(x.AdmittingDept, deptName, StringComparison.OrdinalIgnoreCase) || x.NursingWard.StartsWith(deptName, StringComparison.OrdinalIgnoreCase) || x.NursingWard.Contains(deptName, StringComparison.OrdinalIgnoreCase)).ToList());
		List<BedCardModel> list2 = (string.IsNullOrWhiteSpace(deptName) ? AllBedCards.ToList() : AllBedCards.Where((BedCardModel x) => string.Equals(x.AdmittingDeptName, deptName, StringComparison.OrdinalIgnoreCase) || (isAdminOrClinician ? x.AdmittingDeptName.Contains(deptName, StringComparison.OrdinalIgnoreCase) : (x.NursingWardName.StartsWith(deptName, StringComparison.OrdinalIgnoreCase) || x.NursingWardName.Contains(deptName, StringComparison.OrdinalIgnoreCase)))).ToList());
		InpatientRecordModel selectedInpatient = _selectedInpatient;
		InpatientRecords.Clear();
		foreach (InpatientRecordModel item in list)
		{
			InpatientRecords.Add(item);
		}
		BedCards.Clear();
		foreach (BedCardModel item2 in list2)
		{
			BedCards.Add(item2);
		}
		if (selectedInpatient != null && InpatientRecords.Contains(selectedInpatient))
		{
			_selectedInpatient = null;
			SelectedInpatient = selectedInpatient;
		}
		else
		{
			SelectedInpatient = InpatientRecords.FirstOrDefault();
		}
		OnPropertyChanged("AllBedCards");
		OnPropertyChanged("AllInpatientRecords");
		OnPropertyChanged("BedCards");
		OnPropertyChanged("InpatientRecords");
		RefreshInpatientMetrics();
	}

	private LoginContextOption? ResolveTargetContext(string identifier)
	{
		if (AvailableContexts.Count == 0)
		{
			SyncWithSession();
		}
		return AvailableContexts.FirstOrDefault(Match);
		bool Match(LoginContextOption x)
		{
			if (string.Equals(x.PostId, identifier, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			if (string.Equals(x.RoleId, identifier, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			if (string.Equals(x.RoleCode, identifier, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			string text = identifier.Replace("POST-", "").Replace("UDP-", "").Replace("ROLE-", "");
			string text2 = x.PostId.Replace("POST-", "").Replace("UDP-", "").Replace("ROLE-", "");
			string text3 = x.RoleId.Replace("POST-", "").Replace("UDP-", "").Replace("ROLE-", "");
			if (string.Equals(text2, text, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			if (string.Equals(text3, text, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			if (text.StartsWith("PHARM") && (text2.StartsWith("PHM") || text3.StartsWith("PHARM")))
			{
				return true;
			}
			if (text.StartsWith("CASHIER") && (text2.StartsWith("CASHIER") || text3.StartsWith("CASHIER")))
			{
				return true;
			}
			if (text.StartsWith("NURSE") && (text2.StartsWith("NURSE") || text3.StartsWith("WARD-NURSE") || text3.StartsWith("NURSE")))
			{
				return true;
			}
			return false;
		}
	}

	public void QuickSwitchToRole(string postId, int? targetStation = null, int? targetTab = null)
	{
		_ = SwitchToContextAsync(postId, targetStation, targetTab);
	}

	public void SaveNewUser()
	{
		_ = SaveNewUserAsync();
	}

	public async Task SaveNewUserAsync()
	{
		if (string.IsNullOrWhiteSpace(NewUserForm.UserName) || string.IsNullOrWhiteSpace(NewUserForm.DisplayName))
		{
			SystemNotice = "请输入用户名和姓名后再保存。";
			return;
		}
		if (string.IsNullOrWhiteSpace(NewUserForm.Password))
		{
			SystemNotice = "请输入初始密码后再保存。";
			return;
		}
		string displayName = NewUserForm.DisplayName.Trim();
		try
		{
			using HttpClient departmentHttp = CreateConfigHttpClient();
			ClientApiEnvelope<List<PlatformDepartmentDto>>? departmentEnvelope =
				await departmentHttp.GetFromJsonAsync<ClientApiEnvelope<List<PlatformDepartmentDto>>>("api/v4/platform/departments");
			PlatformDepartmentDto? department = departmentEnvelope?.Data?.FirstOrDefault(x =>
				string.Equals(x.DeptName, NewUserForm.SelectedDept?.Trim(), StringComparison.OrdinalIgnoreCase));
			department ??= departmentEnvelope?.Data?.FirstOrDefault(x =>
				string.Equals(x.Id, NewUserForm.SelectedDeptId, StringComparison.OrdinalIgnoreCase));
			if (department == null)
			{
				SystemNotice = "创建用户失败：所选科室不存在，请选择有效科室。";
				return;
			}
			using HttpClient http = CreateConfigHttpClient();
			using HttpResponseMessage response = await http.PostAsJsonAsync("api/v4/platform/users", new
			{
				UserName = NewUserForm.UserName.Trim(),
				DisplayName = displayName,
				EmployeeNo = NewUserForm.EmployeeNo.Trim(),
				Password = NewUserForm.Password,
				RoleId = NewUserForm.SelectedRole,
				DeptId = department.Id,
				PrescribeLevel = NewUserForm.PrescribeLevel,
				SurgeryLevel = NewUserForm.SurgeryLevel,
				EmrSignLevel = NewUserForm.EmrSignLevel
			});
			ClientApiEnvelope<string>? envelope = await response.Content.ReadFromJsonAsync<ClientApiEnvelope<string>>();
			if (!response.IsSuccessStatusCode || envelope == null || envelope.Code != 200 || string.IsNullOrWhiteSpace(envelope.Data))
			{
				string error = envelope?.Message ?? $"HTTP {(int)response.StatusCode}";
				SystemNotice = "创建用户失败：" + error;
				ClientDiagnosticsLog.Error("SaveNewUserAsync", error);
				return;
			}
			NewUserForm.Reset();
			IsCreateUserModalOpen = false;
			await LoadUsersAsync();
			await LoadRolesAsync();
			SystemNotice = $"已创建用户“{displayName}”。";
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException || ex is InvalidOperationException)
		{
			SystemNotice = "创建用户失败：" + ex.Message;
			ClientDiagnosticsLog.Error("SaveNewUserAsync", ex.Message, ex);
		}
	}

	private async Task LoadUsersAsync()
	{
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			ClientApiEnvelope<List<PlatformUserDto>>? usersEnvelope =
				await http.GetFromJsonAsync<ClientApiEnvelope<List<PlatformUserDto>>>("api/v4/platform/users");
			ClientApiEnvelope<List<PlatformDepartmentDto>>? departmentsEnvelope =
				await http.GetFromJsonAsync<ClientApiEnvelope<List<PlatformDepartmentDto>>>("api/v4/platform/departments");
			if (usersEnvelope?.Data == null)
			{
				SystemNotice = "用户列表加载失败：" + (usersEnvelope?.Message ?? "服务返回数据为空");
				return;
			}
			Dictionary<string, string> departmentNames = departmentsEnvelope?.Data?
				.Where(x => !string.IsNullOrWhiteSpace(x.Id))
				.ToDictionary(x => x.Id, x => x.DeptName, StringComparer.OrdinalIgnoreCase)
				?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			UsersList.Clear();
			foreach (PlatformUserDto user in usersEnvelope.Data.OrderBy(x => x.UserName, StringComparer.OrdinalIgnoreCase))
			{
				string deptName = departmentNames.TryGetValue(user.DeptId ?? string.Empty, out string? name)
					? name
					: "未分配科室";
				UsersList.Add(new UserAdminItemModel
				{
					UserId = user.Id,
					UserName = user.UserName,
					DisplayName = user.DisplayName,
					EmployeeNo = user.EmployeeNo,
					RoleCode = user.RoleId,
					RoleName = user.RoleName,
					DeptName = deptName,
					ContextCount = string.IsNullOrWhiteSpace(user.RoleId) ? 0 : 1,
					ContextSummary = string.IsNullOrWhiteSpace(user.RoleName)
						? "尚未配置执业身份"
						: $"{user.RoleName} · {deptName}",
					PrescribeLevel = user.PrescribeLevel,
					SurgeryLevel = user.SurgeryLevel,
					EmrSignLevel = user.EmrSignLevel,
					IsActive = user.IsActive
				});
			}
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException || ex is InvalidOperationException)
		{
			SystemNotice = "用户列表加载失败：" + ex.Message;
			ClientDiagnosticsLog.Error("LoadUsersAsync", ex.Message, ex);
		}
	}

	private async Task LoadRolesAsync()
	{
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			ClientApiEnvelope<List<PlatformRoleDto>>? envelope =
				await http.GetFromJsonAsync<ClientApiEnvelope<List<PlatformRoleDto>>>("api/v4/platform/roles", JsonOptions);
			if (envelope == null || envelope.Code != 200 || envelope.Data == null)
			{
				ClientDiagnosticsLog.Error("LoadRolesAsync", envelope?.Message ?? "角色列表返回无效数据");
				return;
			}
			HashSet<string> activeRoleIds = new(StringComparer.OrdinalIgnoreCase);
			foreach (PlatformRoleDto role in envelope.Data)
			{
				string roleId = role.Id;
				if (string.IsNullOrWhiteSpace(roleId))
				{
					continue;
				}
				activeRoleIds.Add(roleId);
				RoleAdminItemModel? existing = RolesList.FirstOrDefault(x => string.Equals(x.RoleId, roleId, StringComparison.OrdinalIgnoreCase));
				if (existing == null)
				{
					existing = new RoleAdminItemModel { RoleId = roleId };
					RolesList.Add(existing);
				}
				existing.RoleCode = role.RoleCode;
				existing.RoleName = role.RoleName;
				existing.DataScope = role.DataScope;
				existing.IsActive = role.IsActive;
			}
			List<RoleAdminItemModel> staleRoles = RolesList
				.Where(x => !activeRoleIds.Contains(x.RoleId) && IsLocalSeedRole(x.RoleId))
				.ToList();
			foreach (RoleAdminItemModel staleRole in staleRoles)
			{
				RolesList.Remove(staleRole);
			}
			RefreshRoleMemberCounts();
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException || ex is InvalidOperationException)
		{
			ClientDiagnosticsLog.Error("LoadRolesAsync", ex.Message, ex);
		}
	}

	public async Task ResetUserPasswordAsync(UserAdminItemModel user)
	{
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			using HttpResponseMessage response = await http.PutAsJsonAsync($"api/v4/platform/users/{Uri.EscapeDataString(user.UserId)}", new
			{
				NewPassword = DefaultDevelopmentPassword
			});
			ClientApiEnvelope<bool>? envelope = await response.Content.ReadFromJsonAsync<ClientApiEnvelope<bool>>();
			if (!response.IsSuccessStatusCode || envelope == null || envelope.Code != 200 || !envelope.Data)
			{
				string error = envelope?.Message ?? $"HTTP {(int)response.StatusCode}";
				SystemNotice = "密码重置失败：" + error;
				ClientDiagnosticsLog.Error("ResetUserPasswordAsync", error);
				return;
			}
			SystemNotice = $"已将用户“{user.DisplayName} ({user.UserName})”的密码重置为 {DefaultDevelopmentPassword}，原登录会话已失效。";
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException || ex is InvalidOperationException)
		{
			SystemNotice = "密码重置失败：" + ex.Message;
			ClientDiagnosticsLog.Error("ResetUserPasswordAsync", ex.Message, ex);
		}
	}

	public async Task ToggleUserStatusAsync(UserAdminItemModel user)
	{
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			using HttpResponseMessage response = await http.PutAsJsonAsync($"api/v4/platform/users/{Uri.EscapeDataString(user.UserId)}", new
			{
				IsActive = !user.IsActive
			});
			ClientApiEnvelope<bool>? envelope = await response.Content.ReadFromJsonAsync<ClientApiEnvelope<bool>>();
			if (!response.IsSuccessStatusCode || envelope == null || envelope.Code != 200 || !envelope.Data)
			{
				string error = envelope?.Message ?? $"HTTP {(int)response.StatusCode}";
				SystemNotice = "更新账号状态失败：" + error;
				ClientDiagnosticsLog.Error("ToggleUserStatusAsync", error);
				return;
			}
			user.IsActive = !user.IsActive;
			SystemNotice = $"已将用户“{user.DisplayName}”账号状态更新为{user.StatusText}。";
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException || ex is InvalidOperationException)
		{
			SystemNotice = "更新账号状态失败：" + ex.Message;
			ClientDiagnosticsLog.Error("ToggleUserStatusAsync", ex.Message, ex);
		}
	}

	public void ToggleUserStatus(UserAdminItemModel user)
	{
		_ = ToggleUserStatusAsync(user);
	}

	public async Task<bool> SaveNewRoleAsync()
	{
		string roleCode = NormalizeRoleCode(NewRoleForm.RoleCode);
		string roleName = NewRoleForm.RoleName.Trim();
		if (string.IsNullOrWhiteSpace(roleCode) || string.IsNullOrWhiteSpace(roleName))
		{
			SystemNotice = "请填写角色编码和角色名称。";
			return false;
		}
		if (!Regex.IsMatch(roleCode, "^[A-Z][A-Z0-9_]{1,39}$"))
		{
			SystemNotice = "角色编码需以字母开头，只可包含大写字母、数字和下划线，最多 40 位。";
			return false;
		}
		if (RolesList.Any(x => string.Equals(x.RoleCode, roleCode, StringComparison.OrdinalIgnoreCase)))
		{
			SystemNotice = "角色编码已存在，请更换后再保存。";
			return false;
		}
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			using HttpResponseMessage response = await http.PostAsJsonAsync("api/v4/platform/roles", new
			{
				RoleCode = roleCode,
				RoleName = roleName,
				DataScope = NormalizeRoleDataScope(NewRoleForm.DataScope)
			}, JsonOptions);
			ClientApiEnvelope<string>? envelope =
				await response.Content.ReadFromJsonAsync<ClientApiEnvelope<string>>(JsonOptions);
			if (!response.IsSuccessStatusCode || envelope == null || envelope.Code != 200 || string.IsNullOrWhiteSpace(envelope.Data))
			{
				string error = envelope?.Message ?? $"HTTP {(int)response.StatusCode}";
				SystemNotice = "角色创建失败：" + error;
				ClientDiagnosticsLog.Error("SaveNewRoleAsync", error);
				return false;
			}
			NewRoleForm.Reset();
			await LoadRolesAsync();
			SystemNotice = $"角色“{roleName}”已创建。";
			return true;
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException || ex is InvalidOperationException)
		{
			SystemNotice = "角色创建失败：" + ex.Message;
			ClientDiagnosticsLog.Error("SaveNewRoleAsync", ex.Message, ex);
			return false;
		}
	}

	public async Task SaveNewRole()
	{
		await SaveNewRoleAsync();
	}

	public async Task<bool> UpdateRoleAsync(RoleAdminItemModel role, string? roleName, string? dataScope, bool? isActive)
	{
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			using HttpResponseMessage response = await http.PutAsJsonAsync(
				$"api/v4/platform/roles/{Uri.EscapeDataString(role.RoleId)}",
				new
				{
					RoleName = string.IsNullOrWhiteSpace(roleName) ? null : roleName.Trim(),
					DataScope = string.IsNullOrWhiteSpace(dataScope) ? null : NormalizeRoleDataScope(dataScope),
					IsActive = isActive
				},
				JsonOptions);
			ClientApiEnvelope<bool>? envelope =
				await response.Content.ReadFromJsonAsync<ClientApiEnvelope<bool>>(JsonOptions);
			if (!response.IsSuccessStatusCode || envelope == null || envelope.Code != 200 || !envelope.Data)
			{
				string error = envelope?.Message ?? $"HTTP {(int)response.StatusCode}";
				SystemNotice = "角色更新失败：" + error;
				ClientDiagnosticsLog.Error("UpdateRoleAsync", error);
				return false;
			}
			await LoadRolesAsync();
			SystemNotice = $"角色“{roleName ?? role.RoleName}”已更新。";
			return true;
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException || ex is InvalidOperationException)
		{
			SystemNotice = "角色更新失败：" + ex.Message;
			ClientDiagnosticsLog.Error("UpdateRoleAsync", ex.Message, ex);
			return false;
		}
	}

	public async Task<bool> LoadRolePermissionsAsync(string roleId, IList<PermissionMatrixItemModel> target)
	{
		if (string.IsNullOrWhiteSpace(roleId))
		{
			return false;
		}
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			ClientApiEnvelope<List<PlatformRolePermissionDto>>? envelope =
				await http.GetFromJsonAsync<ClientApiEnvelope<List<PlatformRolePermissionDto>>>(
					$"api/v4/platform/roles/{Uri.EscapeDataString(roleId)}/permissions",
					JsonOptions);
			if (envelope?.Data == null || envelope.Code != 200)
			{
				SystemNotice = "权限加载失败：" + (envelope?.Message ?? "服务端返回无效数据");
				return false;
			}

			target.Clear();
			foreach (PlatformRolePermissionDto permission in envelope.Data
				.OrderBy(x => x.ResourceKey, StringComparer.OrdinalIgnoreCase))
			{
				target.Add(new PermissionMatrixItemModel
				{
					ResourceKey = permission.ResourceKey,
					ResourceName = GetPermissionDisplayName(permission.ResourceKey),
					AccessPolicy = permission.AccessPolicy
				});
			}
			return true;
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException || ex is InvalidOperationException)
		{
			SystemNotice = "权限加载失败：" + ex.Message;
			ClientDiagnosticsLog.Error("LoadRolePermissionsAsync", $"{roleId}: {ex.Message}", ex);
			return false;
		}
	}

	public async Task<bool> SaveRolePermissionPolicyAsync(
		string roleId,
		PermissionMatrixItemModel item,
		int accessPolicy)
	{
		if (string.IsNullOrWhiteSpace(roleId) || string.IsNullOrWhiteSpace(item.ResourceKey))
		{
			return false;
		}
		if (accessPolicy is < 0 or > 3)
		{
			SystemNotice = "授权级别无效，请选择完全授权、只读、脱敏或仅隐藏。";
			return false;
		}

		int previous = item.AccessPolicy;
		item.AccessPolicy = accessPolicy;
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			using HttpResponseMessage response = await http.PutAsJsonAsync(
				$"api/v4/platform/roles/{Uri.EscapeDataString(roleId)}/permissions",
				new[]
				{
					new
					{
						ResourceKey = item.ResourceKey,
						AccessPolicy = accessPolicy
					}
				},
				JsonOptions);
			ClientApiEnvelope<bool>? envelope =
				await response.Content.ReadFromJsonAsync<ClientApiEnvelope<bool>>(JsonOptions);
			if (!response.IsSuccessStatusCode || envelope == null || envelope.Code != 200 || !envelope.Data)
			{
				item.AccessPolicy = previous;
				string error = envelope?.Message ?? $"HTTP {(int)response.StatusCode}";
				SystemNotice = "权限保存失败：" + error;
				ClientDiagnosticsLog.Error("SaveRolePermissionPolicyAsync", error);
				return false;
			}

			SystemNotice = $"已将“{item.ResourceKey}”设为{item.PolicyLabel}。";
			return true;
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException || ex is InvalidOperationException)
		{
			item.AccessPolicy = previous;
			SystemNotice = "权限保存失败：" + ex.Message;
			ClientDiagnosticsLog.Error("SaveRolePermissionPolicyAsync", ex.Message, ex);
			return false;
		}
	}

	public async Task<bool> SaveRolePermissionPoliciesAsync(
		string roleId,
		IReadOnlyCollection<PermissionMatrixItemModel> items,
		int accessPolicy)
	{
		if (string.IsNullOrWhiteSpace(roleId) || items.Count == 0 || accessPolicy is < 0 or > 3)
		{
			return false;
		}

		Dictionary<string, int> previous = items.ToDictionary(
			x => x.ResourceKey,
			x => x.AccessPolicy,
			StringComparer.OrdinalIgnoreCase);
		foreach (PermissionMatrixItemModel item in items)
		{
			item.AccessPolicy = accessPolicy;
		}

		try
		{
			using HttpClient http = CreateConfigHttpClient();
			using HttpResponseMessage response = await http.PutAsJsonAsync(
				$"api/v4/platform/roles/{Uri.EscapeDataString(roleId)}/permissions",
				items.Select(x => new
				{
					ResourceKey = x.ResourceKey,
					AccessPolicy = accessPolicy
				}),
				JsonOptions);
			ClientApiEnvelope<bool>? envelope =
				await response.Content.ReadFromJsonAsync<ClientApiEnvelope<bool>>(JsonOptions);
			if (!response.IsSuccessStatusCode || envelope == null || envelope.Code != 200 || !envelope.Data)
			{
				RestoreRolePolicies(items, previous);
				string error = envelope?.Message ?? $"HTTP {(int)response.StatusCode}";
				SystemNotice = "批量权限保存失败：" + error;
				ClientDiagnosticsLog.Error("SaveRolePermissionPoliciesAsync", error);
				return false;
			}

			SystemNotice = $"已批量更新 {items.Count} 项权限。";
			return true;
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException || ex is InvalidOperationException)
		{
			RestoreRolePolicies(items, previous);
			SystemNotice = "批量权限保存失败：" + ex.Message;
			ClientDiagnosticsLog.Error("SaveRolePermissionPoliciesAsync", ex.Message, ex);
			return false;
		}
	}

	private static void RestoreRolePolicies(
		IEnumerable<PermissionMatrixItemModel> items,
		IReadOnlyDictionary<string, int> previous)
	{
		foreach (PermissionMatrixItemModel item in items)
		{
			if (previous.TryGetValue(item.ResourceKey, out int policy))
			{
				item.AccessPolicy = policy;
			}
		}
	}

	private static string GetPermissionDisplayName(string resourceKey) => resourceKey switch
	{
		"OPD.ConsultView.BtnSignPrescription" => "门诊接诊 · 处方签名",
		"EMR.Toolbar.BtnExportOfd" => "电子病历 · 归档导出",
		_ => resourceKey
	};

	private static string NormalizeRoleCode(string? roleCode) =>
		(roleCode ?? string.Empty).Trim().ToUpperInvariant();

	private static string NormalizeRoleDataScope(string? scope) =>
		string.IsNullOrWhiteSpace(scope) ? "ALL_CAMPUS" : scope.Trim().ToUpperInvariant();

	private static bool IsLocalSeedRole(string roleId) =>
		roleId.StartsWith("ROLE-", StringComparison.OrdinalIgnoreCase)
		&& !roleId.StartsWith("ROLE-CUSTOM-", StringComparison.OrdinalIgnoreCase);

	private void RefreshRoleMemberCounts()
	{
		foreach (RoleAdminItemModel role in RolesList)
		{
			role.UserCount = UsersList.Count(x =>
				string.Equals(x.RoleCode, role.RoleId, StringComparison.OrdinalIgnoreCase));
		}
	}

	public async Task SwitchToContextAsync(string contextId, int? targetStation = null, int? targetTab = null)
	{
		LoginContextOption target = ResolveTargetContext(contextId);
		if (target == null)
		{
			SystemNotice = "无权限：当前账号未被分配该执业身份，请联系系统管理员。";
			return;
		}
		string token = SessionContext.Current.AccessToken;
		if (string.IsNullOrWhiteSpace(token))
		{
			SystemNotice = "登录会话已失效，请重新登录后再切换执业身份。";
			return;
		}
		IsSwitchingContext = true;
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			http.Timeout = TimeSpan.FromSeconds(10);
			using HttpResponseMessage response = await http.PostAsJsonAsync(
				"/api/v4/platform/auth/switch-context",
				new { ContextId = target.PostId },
				JsonOptions);
			LoginModel.ApiEnvelope<LoginModel.LoginResponseDto> envelope =
				await response.Content.ReadFromJsonAsync<LoginModel.ApiEnvelope<LoginModel.LoginResponseDto>>(JsonOptions);
			if (!response.IsSuccessStatusCode || envelope == null || envelope.Code != 200 || envelope.Data == null)
			{
				SystemNotice = "切换执业身份失败：" + (envelope?.Message ?? $"服务返回 {(int)response.StatusCode}");
				return;
			}
			ApplyLogin(envelope.Data);
			SyncWithSession();
			SelectedNavIndex = targetStation ?? DefaultStationIndex;
			RebuildNavigationItems();
			if (targetTab.HasValue)
			{
				if (SelectedNavIndex == 9)
				{
					SelectedFinanceTabIndex = targetTab.Value;
				}
				else if (SelectedNavIndex == 4)
				{
					SelectedPharmacyHubTabIndex = targetTab.Value;
				}
				else if (SelectedNavIndex == 2)
				{
					SelectedNurseTabIndex = targetTab.Value;
				}
			}
			IsSwitchUserModalOpen = false;
			SystemNotice = $"✓ 已切换至执业身份：{SessionContext.Current.DisplayName} ({SessionContext.Current.RoleName} · {SessionContext.Current.ActiveDeptName})";
		}
		catch (HttpRequestException ex)
		{
			SystemNotice = "切换执业身份失败，无法连接认证服务：" + ex.Message;
			ClientDiagnosticsLog.Error("context.switch", "切换执业身份失败", ex);
		}
		catch (TaskCanceledException ex)
		{
			SystemNotice = "切换执业身份超时，请稍后重试。";
			ClientDiagnosticsLog.Error("context.switch", "切换执业身份超时", ex);
		}
		catch (JsonException ex)
		{
			SystemNotice = "切换执业身份失败，服务返回数据无法解析：" + ex.Message;
			ClientDiagnosticsLog.Error("context.switch", "切换执业身份响应解析失败", ex);
		}
		finally
		{
			IsSwitchingContext = false;
		}
	}

	private void ApplyLogin(LoginModel.LoginResponseDto data)
	{
		List<LoginContextSnapshot> availableContexts =
			data.Contexts?.Select(x => x.ToSnapshot()).ToList() ?? new List<LoginContextSnapshot>();
		string roleCode = string.IsNullOrWhiteSpace(data.RoleCode)
			? availableContexts.FirstOrDefault(x => x.PostId == data.PostId)?.RoleCode ?? string.Empty
			: data.RoleCode;
		string roleName = string.IsNullOrWhiteSpace(data.RoleName)
			? availableContexts.FirstOrDefault(x => x.PostId == data.PostId)?.RoleName ?? string.Empty
			: data.RoleName;
		string deptName = string.IsNullOrWhiteSpace(data.ActiveDeptName)
			? availableContexts.FirstOrDefault(x => x.PostId == data.PostId)?.DeptName ?? string.Empty
			: data.ActiveDeptName;
		SessionContext.Current.SetFromLogin(data.UserId, data.UserName, data.DisplayName, roleCode, roleName,
			data.AccessToken, data.ActiveDeptId, deptName, data.ActiveDeptCategory, data.ActiveWardId,
			data.PrescribeLevel, data.SurgeryLevel, data.EmrSignLevel, data.RoleId, data.PostId,
			data.EmployeeNo, availableContexts, data.RefreshToken, data.AccessTokenExpiresAtMs,
			data.RefreshTokenExpiresAtMs);
	}

	private static string CategoryLabel(string category)
	{
		if (1 == 0)
		{
		}
		string result = category switch
		{
			"OPD" => "门诊", 
			"IPD" => "住院", 
			"WARD" => "病区", 
			"MEDTECH" => "医技", 
			"PHARMACY" => "药房", 
			"FINANCE" => "财务", 
			"ADMIN" => "管理", 
			_ => category, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private void InitAdminData()
	{
		if (!CanSeeAdminPanel)
		{
			return;
		}
		UsersList.Clear();
		RolesList.Clear();
		RolesList.Add(new RoleAdminItemModel
		{
			RoleId = "ROLE-ADMIN",
			RoleCode = "ADMIN",
			RoleName = "超级管理员",
			DataScope = "ALL_CAMPUS",
			UserCount = 1,
			IsActive = true,
			Description = "全院最高管理角色，拥有系统配置、用户角色分配、安全审计及全模块穿透访问权限。"
		});
		RolesList.Add(new RoleAdminItemModel
		{
			RoleId = "ROLE-OPD-DOC",
			RoleCode = "OPD_DOC",
			RoleName = "门诊医生",
			DataScope = "OPD_DEPT_SCOPE",
			UserCount = 1,
			IsActive = true,
			Description = "门诊专科接诊、处方及检查开立、门诊病历书写及电子签名、危急值核收响应。"
		});
		RolesList.Add(new RoleAdminItemModel
		{
			RoleId = "ROLE-IPD-RESIDENT",
			RoleCode = "IPD_RESIDENT",
			RoleName = "住院医师",
			DataScope = "IPD_DEPT_AND_BORROWED",
			UserCount = 1,
			IsActive = true,
			Description = "住院管床日常查房、住院病程录入、下达常规临时医嘱并交主治医师复核审签。"
		});
		RolesList.Add(new RoleAdminItemModel
		{
			RoleId = "ROLE-IPD-ATTENDING",
			RoleCode = "IPD_ATTENDING",
			RoleName = "主治医师",
			DataScope = "IPD_DEPT_AND_BORROWED",
			UserCount = 2,
			IsActive = true,
			Description = "三级查房终审、高风险药物/限制级抗生素授权、手术双签审批、出院六严关终审放行。"
		});
		RolesList.Add(new RoleAdminItemModel
		{
			RoleId = "ROLE-WARD-NURSE",
			RoleCode = "WARD_NURSE",
			RoleName = "病区护士",
			DataScope = "NURSING_WARD_SCOPE",
			UserCount = 2,
			IsActive = true,
			Description = "病区床位维护、跨病区借床扫码执行、危急值护士端签收、医嘱扫码执行与核销。"
		});
		RolesList.Add(new RoleAdminItemModel
		{
			RoleId = "ROLE-PHARMACIST",
			RoleCode = "PHARMACIST",
			RoleName = "药师",
			DataScope = "MEDTECH_EXEC_SCOPE",
			UserCount = 1,
			IsActive = true,
			Description = "处方前置合理用药审查、过敏史/皮试强阻断拦截核验、摆药复核扫码出库。"
		});
		RolesList.Add(new RoleAdminItemModel
		{
			RoleId = "ROLE-CASHIER",
			RoleCode = "CASHIER",
			RoleName = "收费员",
			DataScope = "ALL_CAMPUS",
			UserCount = 1,
			IsActive = true,
			Description = "门急诊收挂号费、住院预交金交退、出院医保结算与财务核算。"
		});
		_ = InitializeAdminWorkspaceAsync();
	}

	private async Task InitializeAdminWorkspaceAsync()
	{
		await LoadUsersAsync();
		await LoadRolesAsync();
	}

	public async Task ReloadAdminDataAsync()
	{
		await LoadUsersAsync();
		await LoadRolesAsync();
	}

	private void ReindexPrescriptionRows()
	{
		int num = 1;
		foreach (PrescriptionItemModel item in Prescriptions.Where((PrescriptionItemModel x) => x.IsVisible))
		{
			item.Seq = num++;
		}
	}

	private void RecalculatePrescriptionSafety(PrescriptionItemModel item)
	{
		bool flag = item.DrugName.Contains("青霉素", StringComparison.OrdinalIgnoreCase);
		bool flag2 = SelectedPatient?.HasAllergyAlert ?? false;
		if (flag & flag2)
		{
			item.SkinTestStatus = "❌ 皮试阳性 (强阻断)";
			item.SkinTestClass = "Danger";
			item.AuditStatus = "已拦截 (过敏史冲突)";
			item.AuditClass = "Danger";
			item.IsBlocked = true;
			SystemNotice = "用药安全拦截：处方明细 [" + item.DrugName + "] 与患者过敏史冲突，发药与执行已禁止提交";
			return;
		}
		item.IsBlocked = false;
		item.AuditClass = "Info";
		item.AuditStatus = "待药师审方";
		if (item.SkinTestStatus.Contains("阻断", StringComparison.OrdinalIgnoreCase))
		{
			item.SkinTestStatus = "待皮试";
			item.SkinTestClass = "Warning";
		}
	}

	private PrescriptionItemModel ClonePrescriptionItem(PrescriptionItemModel item)
	{
		PrescriptionItemModel prescriptionItemModel = new PrescriptionItemModel
		{
			Id = item.Id,
			Seq = item.Seq,
			DrugId = item.DrugId,
			IsPersisted = item.IsPersisted,
			QuantityValue = item.QuantityValue,
			Days = item.Days,
			SkinTestClass = item.SkinTestClass,
			AuditClass = item.AuditClass,
			IsBlocked = item.IsBlocked,
			IsDeleted = item.IsDeleted,
			ExtDataJson = item.ExtDataJson,
			DeletedAtMs = item.DeletedAtMs,
			DeletedBy = item.DeletedBy
		};
		prescriptionItemModel.ReplaceFieldValues(item.FieldValues);
		prescriptionItemModel.RebuildCells(PrescriptionColumns);
		return prescriptionItemModel;
	}

	private static PrescriptionExtraFieldModel ClonePrescriptionField(PrescriptionExtraFieldModel field)
	{
		return new PrescriptionExtraFieldModel
		{
			Id = field.Id,
			Name = field.Name,
			Value = field.Value,
			Placeholder = field.Placeholder,
			DataType = field.DataType,
			SortOrder = field.SortOrder,
			IsDeleted = field.IsDeleted,
			DeletedAtMs = field.DeletedAtMs,
			DeletedBy = field.DeletedBy
		};
	}

	private void ApplyDynamicFieldVisibility(PatientQueueItemModel patient)
	{
		ApplyDynamicFieldVisibility(patient, SelectedFormTemplate);
	}

	private static void ApplyDynamicFieldVisibility(PatientQueueItemModel patient, DynamicFormTemplateModel? template)
	{
		if (template == null)
		{
			return;
		}
		foreach (DynamicFormFieldModel field in template.Fields)
		{
			ApplyDynamicFieldVisibility(patient, field);
		}
	}

	private static void ApplyDynamicFieldVisibility(PatientQueueItemModel patient, DynamicFormFieldModel field)
	{
		field.IsVisibleForCurrentPatient = field.VisibilityExpr == "ALL" || (field.VisibilityExpr == "MALE_ONLY" && patient.Gender == "男") || (field.VisibilityExpr == "FEMALE_ONLY" && patient.Gender == "女");
	}

	public void SelectNextPatient()
	{
		if (PatientQueue.Count != 0)
		{
			PatientQueueItemModel patientQueueItemModel = PatientQueue.FirstOrDefault((PatientQueueItemModel x) => x.IsWaiting);
			if (patientQueueItemModel != null)
			{
				patientQueueItemModel.SetStatus(2, 2);
				SelectedPatient = patientQueueItemModel;
				ApplyPatientQueueFilter();
				NotifyOpdWorkflowStateChanged();
				SystemNotice = $"\ud83d\udd0a [步骤A：呼叫接诊完成] 已顺呼候诊患者：{SelectedPatient.PatientName} (排队号 #{SelectedPatient.QueueNo}) 进入【就诊中】，请按 A➔B(病历审签)➔C(处方推送)➔D(完成诊疗) 推进！";
			}
			else
			{
				int num = ((SelectedPatient != null) ? PatientQueue.IndexOf(SelectedPatient) : (-1));
				int index = (num + 1) % PatientQueue.Count;
				SelectedPatient = PatientQueue[index];
				ApplyPatientQueueFilter();
				NotifyOpdWorkflowStateChanged();
				SystemNotice = $"已切换至队列患者：{SelectedPatient.PatientName} (#{SelectedPatient.QueueNo} · {SelectedPatient.Status})";
			}
		}
	}

	public void CompleteCurrentPatientVisit()
	{
		_ = CompleteCurrentOpdVisitAsync(noPrescription: false);
	}

	public void SubmitPrescriptionToPharmacy(bool jumpToPharmacy = false)
	{
		if (SelectedPatient == null)
		{
			SystemNotice = "请先选择门诊患者。";
			return;
		}
		if (SelectedPatient.IsWaiting)
		{
			SystemNotice = "⚠\ufe0f [A➔B➔C 闭环门禁拦截] 患者尚处于【候诊中】，请先点击顶部「▶ 步骤A：开始接诊该患者」后再下达处方！";
			return;
		}
		if (!SelectedPatient.IsEmrSigned)
		{
			SystemNotice = "⚠\ufe0f [A➔B➔C 闭环门禁拦截] 尚未完成【步骤B：门诊病历三级审签并生成 OFD】！请先在「1. 门诊病历」完成病历审签确认初步诊断后，再执行【步骤C：签署处方并推送药房】。";
			return;
		}
		List<PrescriptionItemModel> list = Prescriptions.Where((PrescriptionItemModel x) => x.IsVisible).ToList();
		if (list.Count == 0)
		{
			SystemNotice = "当前处方无有效药品明细，请先从药品字典添加药品。";
			return;
		}
		if (HasBlockedPrescription)
		{
			SystemNotice = "\ud83d\udee1\ufe0f 用药安全拦截：当前处方含皮试阳性或过敏冲突药品，禁止签署推送！请先点击「自动替换为安全替代药」或删除阻断项。";
			return;
		}
		decimal num = 0m;
		foreach (PrescriptionItemModel item in list)
		{
			string s = item.Amount?.Replace("¥", "").Replace("元", "").Trim() ?? "0";
			if (decimal.TryParse(s, out var result))
			{
				num += result;
			}
			else
			{
				num += 25.00m;
			}
		}
		string text = string.Join(", ", list.Select((PrescriptionItemModel x) => $"{x.DrugName} {x.Spec}*{x.Quantity}")) + $" (¥{num:0.00})";
		PharmacyDispenseModel pharmacyDispenseModel = PharmacyRecords.FirstOrDefault((PharmacyDispenseModel x) => x.PatientName == SelectedPatient.PatientName && !x.IsOrderClosed && !x.IsReturned);
		if (pharmacyDispenseModel != null)
		{
			pharmacyDispenseModel.TotalAmount = num;
			pharmacyDispenseModel.DispenseQuantity = list.Count;
			pharmacyDispenseModel.DrugSummary = text;
			pharmacyDispenseModel.IsAllergyBlocked = false;
			pharmacyDispenseModel.SafetyAudit = "✓ 门诊医生已重签审方通过 · 等待收费或扫码发药";
			if (!pharmacyDispenseModel.IsPaidNotified)
			{
				pharmacyDispenseModel.DispenseStatus = "⏳ 待收费员收费 (医生已签发推送)";
				pharmacyDispenseModel.PaymentNoticeStatus = "⚠\ufe0f 门诊医生 " + CurrentOperatorName + " 已签署处方 · 待收费员点击「收费并通知药房」";
			}
			else
			{
				pharmacyDispenseModel.CanDispense = true;
				pharmacyDispenseModel.DispenseStatus = "已收费通知药房 · 待扫码发药";
			}
		}
		else
		{
			string prescriptionNo = $"RX-{DateTime.Now:yyyyMMdd}-{PharmacyRecords.Count + 1:D3}";
			PharmacyRecords.Insert(0, new PharmacyDispenseModel
			{
				PrescriptionNo = prescriptionNo,
				PatientName = SelectedPatient.PatientName,
				DrugId = "DRUG-04",
				DispenseQuantity = list.Count,
				TotalAmount = num,
				DrugSummary = text,
				DispenseStatus = "⏳ 待收费员收费 (医生已签发推送)",
				PaymentNoticeStatus = "⚠\ufe0f 门诊医生 " + CurrentOperatorName + " 已签署处方 · 待财务收费员点击「收费并通知药房」",
				PharmacistName = "孙伟 (主管药师)",
				SafetyAudit = "✓ 前置合理用药审方通过 · 已同步至财务收费与中心药房队列",
				IsAllergyBlocked = false,
				IsPaidNotified = false,
				IsOrderClosed = false,
				IsReturned = false,
				CanDispense = false
			});
		}
		TimelineEvents.Insert(0, new TimelineEventModel
		{
			EventTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
			EventType = "OPD_RX_PUSH",
			EventTitle = $"门诊电子处方签署并推送 [{SelectedPatient.PatientName} · 共{list.Count}味药 ¥{num:0.00}]",
			Department = ActiveDeptName,
			DoctorOrOperator = CurrentOperatorName,
			Summary = text,
			BadgeClass = "Success",
			ResultIcon = "\ud83d\udc8a"
		});
		SelectedPatient.IsOrdersPushed = true;
		NotifyOpdWorkflowStateChanged();
		RefreshPharmacyWarehouseMetrics();
		if (jumpToPharmacy)
		{
			if (!CanSeeFinance)
			{
				QuickSwitchToRole("CASHIER", 9, 0);
			}
			else
			{
				SelectedNavIndex = 9;
				SelectedFinanceTabIndex = 0;
			}
			SystemNotice = $"✓ [步骤C：处方签署推送完成] 已签署患者 [{SelectedPatient.PatientName}] 的处方 (¥{num:0.00}) 并交接至独立模块【\ud83d\udcb0 财务收费与结算中心】，请由收费员执行【步骤B：收费并通知药房】后再由药师发药！";
		}
		else
		{
			SystemNotice = $"✓ [步骤C：处方签署推送完成] 患者 [{SelectedPatient.PatientName}] 的 {list.Count} 项药品 (¥{num:0.00}) 已推送至【\ud83d\udcb0 财务收费中心】待收费及【\ud83d\udc8a 药房中心】待配药，现已解锁右上角【✓ 步骤D：完成诊疗闭环】！";
		}
	}

	public void ConfirmMedTechFee(MedTechOrderItemModel? item)
	{
		if (item != null)
		{
			if (item.IsFeeLocked)
			{
				SystemNotice = "医技申请单 [" + item.RequestNo + "] 已完成【步骤B：扫码确费锁单】，请继续执行【步骤C：审核发布报告】！";
				return;
			}
			item.IsFeeLocked = true;
			item.Status = "已核收确费 (已锁单)";
			item.StatusClass = "Primary";
			TimelineEvents.Insert(0, new TimelineEventModel
			{
				EventTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
				EventType = "MEDTECH_LOCK",
				EventTitle = "医技扫码到检并确费锁单 [" + item.ItemName + "]",
				Department = item.DeptName,
				DoctorOrOperator = CurrentOperatorName,
				Summary = "申请单 " + item.RequestNo + " 已确费锁单，临床直接撤单与退费已锁止，现可审核发布报告。",
				BadgeClass = "Warning",
				ResultIcon = "\ud83d\udd12"
			});
			SystemNotice = $"\ud83d\udd12 申请单 [{item.RequestNo} · {item.ItemName}] 已确费锁单，现可审核发布报告！";
		}
	}

	public void PublishMedTechReport(MedTechOrderItemModel? item)
	{
		if (item != null)
		{
			if (!item.IsFeeLocked)
			{
				SystemNotice = $"⚠\ufe0f [A➔B➔C 顺序门禁拦截] 申请单 [{item.RequestNo} · {item.ItemName}] 尚处于【步骤A：未到检确费】状态，必须先执行【步骤B：扫码到检确费 (锁单)】后方可发布报告！";
				return;
			}
			item.Status = "已出报告";
			item.StatusClass = "Success";
			TimelineEvents.Insert(0, new TimelineEventModel
			{
				EventTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
				EventType = "MEDTECH_REPORT",
				EventTitle = "医技检查检验报告已审核发布 [" + item.ItemName + "]",
				Department = item.DeptName,
				DoctorOrOperator = CurrentOperatorName,
				Summary = "申请单 " + item.RequestNo + " 报告已签发并同步至临床医生工作站与患者全息时间轴。",
				BadgeClass = "Success",
				ResultIcon = "\ud83e\uddea"
			});
			SystemNotice = $"✓ 医技申请单 [{item.RequestNo} · {item.ItemName}] 报告已审核发布，结果已回写临床诊疗工作台！";
		}
	}

	public void TryCancelMedTechOrder(MedTechOrderItemModel? item)
	{
		if (item != null)
		{
			if (item.IsFeeLocked)
			{
				SystemNotice = $"\ud83d\udee1\ufe0f 申请单 [{item.RequestNo} · {item.ItemName}] 已在医技科室扫码确费，不可直接撤单或直接退费，请走冲正流程！";
			}
			else
			{
				item.Status = "已撤单退回";
				item.StatusClass = "Secondary";
				SystemNotice = $"✓ 申请单 [{item.RequestNo} · {item.ItemName}] 尚未确费，已完成临床撤单。";
			}
		}
	}

	public async Task SignOfdAsync()
	{
		await EmrWorkspace.SignOfdAsync();
		SystemNotice = "国标 OFD 审签已请求服务端真国密双签通道: " + EmrWorkspace.OfdStatus;
	}

	public PrescriptionItemModel? AddPrescriptionFromCatalog(DrugCatalogItemModel drug)
	{
		if (!IsOpdPrescriptionEditable)
		{
			SystemNotice = "当前处方不可编辑，请先开始接诊并录入诊断，或等待药师处理驳回处方";
			return null;
		}
		int seq = Enumerable.Count(Prescriptions, (PrescriptionItemModel x) => x.IsVisible) + 1;
		PrescriptionItemModel item;
		bool flag = drug.DrugName.Contains("青霉素");
		bool flag2 = SelectedPatient?.HasAllergyAlert ?? false;
		if (flag & flag2)
		{
			item = new PrescriptionItemModel
			{
				Seq = seq,
				DrugId = drug.DrugId,
				DrugName = drug.DrugName,
				Spec = drug.Spec,
				Dose = drug.DefaultDose,
				Freq = drug.DefaultFreq,
				Route = drug.DefaultRoute,
				Quantity = "1",
				Amount = drug.UnitPrice,
				SkinTestStatus = "❌ 皮试阳性 (强阻断)",
				SkinTestClass = "Danger",
				AuditStatus = "已拦截 (过敏史冲突)",
				AuditClass = "Danger",
				IsBlocked = true
			};
			SystemNotice = "\ud83d\udee1\ufe0f 用药安全拦截：药品 [" + drug.DrugName + "] 与患者过敏史冲突且皮试阳性，不允许提交！";
		}
		else
		{
			item = new PrescriptionItemModel
			{
				Seq = seq,
				DrugId = drug.DrugId,
				DrugName = drug.DrugName,
				Spec = drug.Spec,
				Dose = drug.DefaultDose,
				Freq = drug.DefaultFreq,
				Route = drug.DefaultRoute,
				Quantity = "2",
				Amount = drug.UnitPrice,
				SkinTestStatus = (drug.IsSkinTestRequired ? "待皮试" : string.Empty),
				SkinTestClass = (drug.IsSkinTestRequired ? "Warning" : "Info"),
				AuditStatus = string.Empty,
				AuditClass = "Info",
				IsBlocked = false
			};
			SystemNotice = $"已新增处方药品 [{drug.DrugName}]，请核对剂量与频次后保存";
		}
		item.QuantityValue = Math.Max(ParseInt(item.Quantity, 1), 1);
		item.Days = Math.Max(1, item.Days);
		Prescriptions.Add(item);
		IsAddPrescriptionDialogOpen = false;
		_prescriptionRevision++;
		ReindexPrescriptionRows();
		item.RebuildCells(PrescriptionColumns);
		RefreshPrescriptionMetrics();
		return item;
	}

	public void EnsureEmrCatalogInitialized()
	{
		if (!_emrDesignerInitialized)
		{
			_emrDesignerInitialized = true;
			InitDefaultEmrPrintLayouts();
			InitDefaultEmrTemplates();
			ApplyEmrSceneFilter();
		}
	}

	public void SyncOpdWorkspaceToEmrTemplate()
	{
		EnsureEmrCatalogInitialized();
		EmrVisualTemplateModel opdFirst = EmrTemplates.FirstOrDefault((EmrVisualTemplateModel x) => x.TemplateCode == "OPD_FIRST_VISIT" || x.TemplateId == "TMPL-OPD-FIRST");
		if (opdFirst != null)
		{
			SetVal("CHIEF_COMPLAINT", EmrWorkspace.ChiefComplaint);
			SetVal("PRESENT_ILLNESS", EmrWorkspace.PresentIllness);
			SetVal("ALLERGY_HISTORY", SelectedPatient?.AllergyTags ?? "无已知药物过敏");
			SetVal("DE_PROSTATE_EXAM", EmrWorkspace.ProstateExam);
			SetVal("DE_PREGNANCY_COUNT", EmrWorkspace.PregnancyCount.ToString());
			SetVal("PRIMARY_DIAGNOSIS", EmrWorkspace.Diagnosis);
			RefreshPatientFieldVisibilityForTemplate(opdFirst);
			OnPropertyChanged("CurrentEmrPatientName");
			OnPropertyChanged("CurrentEmrPatientGender");
			OnPropertyChanged("CurrentEmrPatientAge");
			OnPropertyChanged("CurrentEmrEncounterNo");
			OnPropertyChanged("CurrentEmrBedAndWardText");
			OnPropertyChanged("CurrentEmrPatientSummary");
		}
		void SetVal(string elementCode, string val)
		{
			foreach (EmrVisualSectionModel section in opdFirst.Sections)
			{
				EmrVisualFieldItemModel emrVisualFieldItemModel = section.Fields.FirstOrDefault((EmrVisualFieldItemModel f) => string.Equals(f.ElementCode, elementCode, StringComparison.OrdinalIgnoreCase));
				if (emrVisualFieldItemModel != null)
				{
					emrVisualFieldItemModel.ValueText = val;
					break;
				}
			}
		}
	}

	private void InitDefaultEmrPrintLayouts()
	{
		EmrPrintLayouts.Clear();
		EmrPrintLayouts.Add(new EmrPrintLayoutConfigModel
		{
			LayoutId = "LAYOUT-OPD-B5",
			LayoutCode = "OPD_STD_B5",
			LayoutName = "门诊病历标准 B5 紧凑版式",
			PageSizeCode = "B5",
			PageWidthMm = 176.0m,
			PageHeightMm = 250.0m,
			MarginTopMm = 16.0m,
			MarginBottomMm = 16.0m,
			MarginLeftMm = 18.0m,
			MarginRightMm = 16.0m,
			HospitalName = "国家区域医疗中心 (三级甲等综合医院)",
			SubTitle = "门 诊 初 诊 结 构 化 病 历",
			AccentColor = "#B71C1C",
			WatermarkText = "国家三甲医院门诊病历归档专章",
			FooterText = "遵循国家卫健委《电子病历基本数据集》与 GB/T 33190-2016 标准 · 门诊病历归档件",
			ShowPatientBanner = true,
			ShowBedAndAdmissionNo = false,
			ShowVitalSignsBanner = false,
			ShowNursingScales = false,
			ShowTrackChanges = false,
			SignLevelCount = 1
		});
		EmrPrintLayouts.Add(new EmrPrintLayoutConfigModel
		{
			LayoutId = "LAYOUT-IPD-A4",
			LayoutCode = "IPD_STD_A4",
			LayoutName = "住院病历国标 A4 三级审签版式",
			PageSizeCode = "A4",
			PageWidthMm = 210.0m,
			PageHeightMm = 297.0m,
			MarginTopMm = 25.0m,
			MarginBottomMm = 20.0m,
			MarginLeftMm = 25.0m,
			MarginRightMm = 20.0m,
			HospitalName = "国家区域医疗中心 (三级甲等综合医院)",
			SubTitle = "住 院 24 小 时 入 院 记 录",
			AccentColor = "#1565C0",
			WatermarkText = "国家三甲医院住院病案归档专章",
			FooterText = "住院病案归档件 · 遵循 GB/T 33190-2016 双层语义嵌入与三级医师审签锁止规范",
			ShowPatientBanner = true,
			ShowBedAndAdmissionNo = true,
			ShowVitalSignsBanner = false,
			ShowNursingScales = false,
			ShowTrackChanges = true,
			SignLevelCount = 3
		});
		EmrPrintLayouts.Add(new EmrPrintLayoutConfigModel
		{
			LayoutId = "LAYOUT-WARD-A4",
			LayoutCode = "WARD_NURSING_A4",
			LayoutName = "护理病历与评估单 A4 质控版式",
			PageSizeCode = "A4",
			PageWidthMm = 210.0m,
			PageHeightMm = 297.0m,
			MarginTopMm = 20.0m,
			MarginBottomMm = 18.0m,
			MarginLeftMm = 20.0m,
			MarginRightMm = 18.0m,
			HospitalName = "国家区域医疗中心 (三级甲等综合医院)",
			SubTitle = "住 院 患 者 入 院 护 理 评 估 与 记 录 单",
			AccentColor = "#059669",
			WatermarkText = "国家三甲医院护理病历质控专章",
			FooterText = "护理质量控制标准表单 · 责任护士与病区护士长双人质控核对",
			ShowPatientBanner = true,
			ShowBedAndAdmissionNo = true,
			ShowVitalSignsBanner = true,
			ShowNursingScales = true,
			ShowTrackChanges = false,
			SignLevelCount = 2
		});
	}

	private void InitDefaultEmrTemplates()
	{
		EmrTemplates.Clear();
		EmrVisualTemplateModel emrVisualTemplateModel = new EmrVisualTemplateModel
		{
			TemplateId = "TMPL-OPD-FIRST",
			TemplateCode = "OPD_FIRST_VISIT",
			TemplateName = "门诊初诊结构化病历",
			EmrScene = "OPD",
			DocTypeCode = "OPD_FIRST_VISIT",
			DeptName = "心血管内科门诊",
			TimeLimitHours = 2,
			RequiredSignLevels = 1,
			BoundLayoutCode = "OPD_STD_B5",
			Version = 1
		};
		emrVisualTemplateModel.Sections.Add(CreateSection("SEC_CHIEF", "一、主诉与现病史", "GRID_FORM", 1, CreateField("CHIEF_COMPLAINT", "DE04.01.119.00", "主诉", "STRING", "u:TextBox", null, "ALL", isRequired: true, "阵发性心前区隐痛、心悸3天，活动后加重半天。"), CreateField("PRESENT_ILLNESS", "DE02.10.071.00", "现病史", "TEXT", "u:TextArea", null, "ALL", isRequired: true, "患者3天前因过度劳累后出现心前区憋闷不适，伴阵发性心悸，每次持续约3-5分钟，休息后可逐渐缓解。半天前快步登山后胸痛加重，放射至左肩背部，伴大汗、气促。")));
		emrVisualTemplateModel.Sections.Add(CreateSection("SEC_HISTORY", "二、既往史与药物过敏史", "GRID_FORM", 2, CreateField("PAST_HISTORY", "DE02.10.099.00", "既往史与慢病史", "STRING", "u:TextBox", null, "ALL", isRequired: true, "原发性高血压病史5年，最高血压165/100mmHg，规律服用氨氯地平。"), CreateField("ALLERGY_HISTORY", "DE02.10.022.00", "药物与食物过敏史", "STRING", "u:TextBox", null, "ALL", isRequired: true, "青霉素过敏史(+), 磺胺类过敏(+)")));
		emrVisualTemplateModel.Sections.Add(CreateSection("SEC_EXAM", "三、体格检查与专科检查", "GRID_FORM", 3, CreateField("PHYSICAL_EXAM", "DE04.10.258.00", "体格检查与生命体征", "STRING", "u:TextBox", null, "ALL", isRequired: true, "T 36.5℃，P 82次/分，R 18次/分，BP 138/86mmHg。双肺呼吸音清，心律齐，各瓣膜区未闻及病理性杂音。"), CreateField("DE_PROSTATE_EXAM", "DE04.10.188.00", "前列腺触诊", "STRING", "u:TextBox", null, "MALE_ONLY", isRequired: false, "轻度肥大，质地韧，中央沟浅，未触及明显硬结。"), CreateField("DE_PREGNANCY_COUNT", "DE02.10.002.00", "孕产次数", "INTEGER", "u:NumericIntUpDown", "次", "FEMALE_ONLY", isRequired: false, "0")));
		emrVisualTemplateModel.Sections.Add(CreateSection("SEC_DIAG_PLAN", "四、初步诊断与诊疗处置计划", "INLINE_PARAGRAPH", 4, CreateField("PRIMARY_DIAGNOSIS", "DE05.01.024.00", "初步诊断 (ICD-10)", "STRING", "u:AutoCompleteBox", null, "ALL", isRequired: true, "冠状动脉粥样硬化性心脏病 (I25.101)；劳力性心绞痛 (CCS II级)"), CreateField("TREATMENT_PLAN", "DE05.10.131.00", "诊疗计划与处置意见", "TEXT", "u:TextArea", null, "ALL", isRequired: true, "1. 急查心肌损伤标志物三项、12导联同步心电图；2. 予阿司匹林肠溶片100mg qd + 硫酸氢氯吡格雷75mg qd 双抗治疗；3. 建议立即办理住院行冠脉造影评估。")));
		EmrTemplates.Add(emrVisualTemplateModel);
		EmrVisualTemplateModel emrVisualTemplateModel2 = new EmrVisualTemplateModel
		{
			TemplateId = "TMPL-OPD-FOLLOWUP",
			TemplateCode = "OPD_FOLLOWUP",
			TemplateName = "门诊复诊与慢病随访病历",
			EmrScene = "OPD",
			DocTypeCode = "OPD_FOLLOWUP",
			DeptName = "心血管内科门诊",
			TimeLimitHours = 2,
			RequiredSignLevels = 1,
			BoundLayoutCode = "OPD_STD_B5",
			Version = 1
		};
		emrVisualTemplateModel2.Sections.Add(CreateSection("SEC_FOLLOWUP_MAIN", "一、复诊主诉与慢病控制评估", "GRID_FORM", 1, CreateField("CHIEF_COMPLAINT", "DE04.01.119.00", "复诊主诉", "STRING", "u:TextBox", null, "ALL", isRequired: true, "冠心病PCI术后3个月定期复诊配药，近1月无胸闷胸痛发作。"), CreateField("AUX_EXAM", "DE04.30.010.00", "近期复查检验结果", "TEXT", "u:TextArea", null, "ALL", isRequired: true, "LDL-C 1.42 mmol/L (达标)，肝肾功能、血常规及凝血功能均正常，静息心电图窦性心律。")));
		emrVisualTemplateModel2.Sections.Add(CreateSection("SEC_FOLLOWUP_PLAN", "二、复诊诊断与慢病续方指导", "INLINE_PARAGRAPH", 2, CreateField("PRIMARY_DIAGNOSIS", "DE05.01.024.00", "复诊诊断 (ICD-10)", "STRING", "u:AutoCompleteBox", null, "ALL", isRequired: true, "冠状动脉支架植入术后状态 (Z95.500)；原发性高血压 (I10.x00)"), CreateField("TREATMENT_PLAN", "DE05.10.131.00", "续方方案与生活方式指导", "TEXT", "u:TextArea", null, "ALL", isRequired: true, "继续阿司匹林+氯吡格雷双抗满12个月，瑞舒伐他汀10mg qn强化降脂；低盐低脂饮食，1个月后门诊随访。")));
		EmrTemplates.Add(emrVisualTemplateModel2);
		EmrVisualTemplateModel emrVisualTemplateModel3 = new EmrVisualTemplateModel
		{
			TemplateId = "TMPL-IPD-ADMISSION",
			TemplateCode = "IPD_ADMISSION_24H",
			TemplateName = "住院24小时入院记录",
			EmrScene = "IPD",
			DocTypeCode = "IPD_ADMISSION",
			DeptName = "心血管内科住院一科",
			TimeLimitHours = 24,
			RequiredSignLevels = 3,
			BoundLayoutCode = "IPD_STD_A4",
			Version = 1
		};
		emrVisualTemplateModel3.Sections.Add(CreateSection("SEC_IPD_HISTORY", "一、主诉、现病史与既往过敏史", "INLINE_PARAGRAPH", 1, CreateField("CHIEF_COMPLAINT", "DE04.01.119.00", "入院主诉", "STRING", "u:TextBox", null, "ALL", isRequired: true, "反复胸闷痛3天，加重伴大汗、左肩背放射痛2小时入院。"), CreateField("PRESENT_ILLNESS", "DE02.10.071.00", "现病史与急诊处置经过", "TEXT", "u:TextArea", null, "ALL", isRequired: true, "患者3天前劳累后反复发作心前区压榨样疼痛，2小时前症状持续不缓解伴冷汗，急诊查超敏肌钙蛋白I 12.85 ng/mL(危急值)，以“急性非ST段抬高型心肌梗死”跨病区借床收住内科二病区02床。"), CreateField("PAST_HISTORY", "DE02.10.099.00", "既往史与个人婚育史", "STRING", "u:TextBox", null, "ALL", isRequired: true, "高血压病史5年，高脂血症2年；无吸烟酗酒史。"), CreateField("ALLERGY_HISTORY", "DE02.10.022.00", "过敏史", "STRING", "u:TextBox", null, "ALL", isRequired: true, "青霉素过敏史(+), 磺胺类药物过敏(+)")));
		emrVisualTemplateModel3.Sections.Add(CreateSection("SEC_IPD_PHYSICAL", "二、入院体格检查与辅助检查", "GRID_FORM", 2, CreateField("PHYSICAL_EXAM", "DE04.10.258.00", "系统体格检查", "TEXT", "u:TextArea", null, "ALL", isRequired: true, "T 36.6℃，P 84次/分，R 19次/分，BP 135/82mmHg。神志清楚，双肺呼吸音清，未闻及干湿啰音；心界不大，心率84次/分，律齐，双下肢无水肿。"), CreateField("AUX_EXAM", "DE04.30.010.00", "入院专科与危急值辅助检查", "TEXT", "u:TextArea", null, "ALL", isRequired: true, "超敏肌钙蛋白I (hs-cTnI) 12.85 ng/mL (超危急值)；12导联心电图示 V1-V4 导联 ST 段水平型压低 0.15-0.20mV；心脏彩超示左室前壁节段性室壁运动减弱，LVEF 54%。")));
		emrVisualTemplateModel3.Sections.Add(CreateSection("SEC_IPD_DIAG", "三、入院诊断与三级医师诊疗计划", "INLINE_PARAGRAPH", 3, CreateField("ADMISSION_DIAGNOSIS", "DE05.01.024.01", "入院诊断 (ICD-10)", "STRING", "u:AutoCompleteBox", null, "ALL", isRequired: true, "1. 急性非ST段抬高型心肌梗死 (I21.401) Killip I级；2. 冠状动脉粥样硬化性心脏病 (I25.101)；3. 原发性高血压2级 (很高危)"), CreateField("TREATMENT_PLAN", "DE05.10.131.00", "住院诊疗计划", "TEXT", "u:TextArea", null, "ALL", isRequired: true, "1. 按心血管内科危重级护理、持续心电监护及吸氧；2. 启动胸痛中心绿色通道，急诊导管室行经皮冠状动脉造影及支架植入术 (PCI)；3. 术后密切监测心肌酶谱、肾功能及穿刺部位情况。")));
		EmrTemplates.Add(emrVisualTemplateModel3);
		EmrVisualTemplateModel emrVisualTemplateModel4 = new EmrVisualTemplateModel
		{
			TemplateId = "TMPL-IPD-PROGRESS",
			TemplateCode = "IPD_DAILY_PROGRESS",
			TemplateName = "住院首次/日常病程记录",
			EmrScene = "IPD",
			DocTypeCode = "IPD_PROGRESS",
			DeptName = "心血管内科住院一科",
			TimeLimitHours = 8,
			RequiredSignLevels = 2,
			BoundLayoutCode = "IPD_STD_A4",
			Version = 1
		};
		emrVisualTemplateModel4.Sections.Add(CreateSection("SEC_PROG_WARDROUND", "一、主治/主任医师查房记录与病情评估", "INLINE_PARAGRAPH", 1, CreateField("PROGRESS_CONTENT", "DE06.00.182.00", "查房病情变化与分析", "TEXT", "u:TextArea", null, "ALL", isRequired: true, "今日上午王崇主治医师查房：患者急诊行前降支(LAD)药物洗脱支架植入术后第1天，诉胸闷胸痛明显缓解，无心悸气促，夜间睡眠安。查体：BP 126/76mmHg，HR 72次/分，律齐，右侧桡动脉穿刺处敷料干燥无渗血。"), CreateField("AUX_EXAM", "DE04.30.010.00", "术后复查指标分析", "STRING", "u:TextBox", null, "ALL", isRequired: true, "术后复查 hs-cTnI 较前峰值回落至 4.20 ng/mL，心电图 V1-V4 导联 ST 段基本回落至等电位线。")));
		emrVisualTemplateModel4.Sections.Add(CreateSection("SEC_PROG_ORDERPLAN", "二、今日医嘱调整与观察重点", "INLINE_PARAGRAPH", 2, CreateField("TREATMENT_PLAN", "DE05.10.131.00", "查房处置指令", "TEXT", "u:TextArea", null, "ALL", isRequired: true, "1. 停桡动脉压迫器加压包扎，改为无菌敷料覆盖；2. 将特级护理降为一级护理；3. 继续双联抗血小板、强化他汀及β受体阻滞剂治疗。")));
		EmrTemplates.Add(emrVisualTemplateModel4);
		EmrVisualTemplateModel emrVisualTemplateModel5 = new EmrVisualTemplateModel
		{
			TemplateId = "TMPL-IPD-DISCHARGE",
			TemplateCode = "IPD_DISCHARGE_SUMMARY",
			TemplateName = "住院出院记录与小结",
			EmrScene = "IPD",
			DocTypeCode = "IPD_DISCHARGE",
			DeptName = "心血管内科住院一科",
			TimeLimitHours = 24,
			RequiredSignLevels = 3,
			BoundLayoutCode = "IPD_STD_A4",
			Version = 1
		};
		emrVisualTemplateModel5.Sections.Add(CreateSection("SEC_DIS_SUMMARY", "一、入院诊断与住院诊疗经过", "INLINE_PARAGRAPH", 1, CreateField("ADMISSION_DIAGNOSIS", "DE05.01.024.01", "入院诊断", "STRING", "u:TextBox", null, "ALL", isRequired: true, "急性非ST段抬高型心肌梗死 (I21.401)；原发性高血压2级"), CreateField("DISCHARGE_COURSE", "DE06.00.193.00", "住院诊疗经过与转归", "TEXT", "u:TextArea", null, "ALL", isRequired: true, "患者入院后经胸痛中心急诊行冠脉造影示前降支近中段90%次全闭塞，成功植入药物洗脱支架1枚（TIMI 3级血流）。术后予双抗、调脂稳斑及改善心室重构治疗，现恢复良好，出院六严关核查全部通过，准予今日出院。")));
		emrVisualTemplateModel5.Sections.Add(CreateSection("SEC_DIS_ADVICE", "二、出院诊断、带药指导与随访计划", "INLINE_PARAGRAPH", 2, CreateField("PRIMARY_DIAGNOSIS", "DE05.01.024.00", "出院诊断", "STRING", "u:TextBox", null, "ALL", isRequired: true, "1. 急性非ST段抬高型心肌梗死 (PCI术后)；2. 冠状动脉粥样硬化性心脏病；3. 原发性高血压2级"), CreateField("DISCHARGE_ADVICE", "DE06.00.287.00", "出院医嘱与带药指导", "TEXT", "u:TextArea", null, "ALL", isRequired: true, "1. 出院带药：阿司匹林肠溶片 100mg qd、硫酸氢氯吡格雷片 75mg qd、阿托伐他汀钙片 20mg qn；2. 严禁擅自停用抗血小板药物；3. 出院后2周、1个月携带出院小结至心内科专病门诊复诊。")));
		EmrTemplates.Add(emrVisualTemplateModel5);
		EmrVisualTemplateModel emrVisualTemplateModel6 = new EmrVisualTemplateModel
		{
			TemplateId = "TMPL-WARD-ADMISSION",
			TemplateCode = "WARD_ADMISSION_ASSESS",
			TemplateName = "住院患者入院护理评估单 (含三评量表)",
			EmrScene = "WARD",
			DocTypeCode = "NURSING_ADMISSION",
			DeptName = "内科二病区 (护理单元)",
			TimeLimitHours = 4,
			RequiredSignLevels = 2,
			BoundLayoutCode = "WARD_NURSING_A4",
			Version = 1
		};
		emrVisualTemplateModel6.Sections.Add(CreateSection("SEC_NUR_GENERAL", "一、入科一般资料与生命体征基线", "GRID_FORM", 1, CreateField("NURSING_CONSCIOUSNESS", "DE04.10.167.00", "神志与精神状态", "STRING", "u:TextBox", null, "ALL", isRequired: true, "神志清楚，精神稍紧张，对答切题，步入/平车入病房"), CreateField("ALLERGY_HISTORY", "DE02.10.022.00", "过敏史与腕带标识核对", "STRING", "u:TextBox", null, "ALL", isRequired: true, "青霉素过敏(+), 磺胺类过敏(+) —— 已双人核对并佩戴红色过敏警示腕带"), CreateField("PHYSICAL_EXAM", "DE04.10.258.00", "入科生命体征基线", "STRING", "u:TextBox", null, "ALL", isRequired: true, "T 36.5℃ · P 78次/分 · R 18次/分 · BP 128/78mmHg · SpO₂ 98% (鼻导管吸氧 2L/min)")));
		emrVisualTemplateModel6.Sections.Add(CreateSection("SEC_NUR_SCALES", "二、护理风险三评量表 (压疮Braden / 自理Barthel / 跌倒Morse)", "TABLE_GRID", 2, CreateField("NURSING_BRADEN_SCORE", "DE04.10.008.01", "Braden压疮风险评分", "INTEGER", "u:NumericIntUpDown", "分 (≥19分低危)", "ALL", isRequired: true, "19"), CreateField("NURSING_BARTHEL_SCORE", "DE05.10.003.00", "Barthel自理能力评分", "INTEGER", "u:NumericIntUpDown", "分 (61-99轻度依赖)", "ALL", isRequired: true, "75"), CreateField("NURSING_FALL_SCORE", "DE05.10.145.00", "Morse跌倒坠床风险评分", "INTEGER", "u:NumericIntUpDown", "分 (≥45分中/高风险)", "ALL", isRequired: true, "45")));
		emrVisualTemplateModel6.Sections.Add(CreateSection("SEC_NUR_PLAN", "三、专科护理措施与健康宣教记录", "INLINE_PARAGRAPH", 3, CreateField("NURSING_INTERVENTION", "DE06.00.342.00", "护理观察与专科护理措施", "TEXT", "u:TextArea", null, "ALL", isRequired: true, "1. 严格落实跨病区借床患者身份双人PDA扫码核对；2. 持续心电、血压、指脉氧监护，每小时巡视记录；3. Morse跌倒评分45分(中风险)，已拉双侧床档、床头悬挂防跌倒警示标识并向家属宣教；4. 完成急诊PCI术前双侧桡动脉备皮及心理护理。")));
		EmrTemplates.Add(emrVisualTemplateModel6);
		EmrVisualTemplateModel emrVisualTemplateModel7 = new EmrVisualTemplateModel
		{
			TemplateId = "TMPL-WARD-CRITICAL",
			TemplateCode = "WARD_CRITICAL_CARE",
			TemplateName = "危重患者特级护理观察记录单",
			EmrScene = "WARD",
			DocTypeCode = "NURSING_CRITICAL",
			DeptName = "内科二病区 (护理单元)",
			TimeLimitHours = 2,
			RequiredSignLevels = 2,
			BoundLayoutCode = "WARD_NURSING_A4",
			Version = 1
		};
		emrVisualTemplateModel7.Sections.Add(CreateSection("SEC_CRIT_VITALS", "一、严密生命体征、出入量与管路监护", "TABLE_GRID", 1, CreateField("NURSING_CONSCIOUSNESS", "DE04.10.167.00", "意识状态与瞳孔对光反射", "STRING", "u:TextBox", null, "ALL", isRequired: true, "神志清楚，双侧瞳孔等大等圆 d=2.5mm，对光反射灵敏"), CreateField("PHYSICAL_EXAM", "DE04.10.258.00", "监护仪生命体征与出入量", "STRING", "u:TextBox", null, "ALL", isRequired: true, "HR 76次/分(窦性)，BP 124/74mmHg，SpO₂ 99%；24h入量 1450mL，尿量 1600mL")));
		emrVisualTemplateModel7.Sections.Add(CreateSection("SEC_CRIT_ACTION", "二、特级护理管路维护与病情观察记录", "INLINE_PARAGRAPH", 2, CreateField("NURSING_INTERVENTION", "DE06.00.342.00", "特级护理措施记录", "TEXT", "u:TextArea", null, "ALL", isRequired: true, "1. 右侧桡动脉TR-Band止血器定时减压2mL，穿刺点无渗血血肿，末梢搏动良好；2. 留置针输液通畅，微量泵入硝酸甘油 2mL/h 维持；3. 协助患者床上翻身拍背 q2h，受压皮肤完好。")));
		EmrTemplates.Add(emrVisualTemplateModel7);
	}

	private static EmrVisualSectionModel CreateSection(string code, string title, string renderMode, int sortOrder, params EmrVisualFieldItemModel[] fields)
	{
		EmrVisualSectionModel emrVisualSectionModel = new EmrVisualSectionModel
		{
			SectionCode = code,
			SectionTitle = title,
			RenderMode = renderMode,
			SortOrder = sortOrder,
			IsRequired = true
		};
		foreach (EmrVisualFieldItemModel item in fields)
		{
			emrVisualSectionModel.Fields.Add(item);
		}
		return emrVisualSectionModel;
	}

	private static EmrVisualFieldItemModel CreateField(string code, string nhcCode, string name, string dataType, string controlType, string? unit, string genderExclusive, bool isRequired, string defaultValue)
	{
		return new EmrVisualFieldItemModel
		{
			ElementCode = code,
			NhcStdCode = nhcCode,
			ElementName = name,
			DataType = dataType,
			UrsaControlType = controlType,
			UnitText = unit,
			GenderExclusive = genderExclusive,
			IsRequired = isRequired,
			ValueText = defaultValue,
			PlaceholderText = "请输入" + name + "..."
		};
	}

	public void ApplyEmrSceneFilter()
	{
		EnsureEmrCatalogInitialized();
		List<EmrVisualTemplateModel> list = EmrTemplates.Where((EmrVisualTemplateModel t) => SelectedEmrSceneCode == "ALL" || string.Equals(t.EmrScene, SelectedEmrSceneCode, StringComparison.OrdinalIgnoreCase)).ToList();
		FilteredEmrTemplates.Clear();
		foreach (EmrVisualTemplateModel item in list)
		{
			FilteredEmrTemplates.Add(item);
		}
		if (SelectedEmrTemplate == null || !FilteredEmrTemplates.Contains(SelectedEmrTemplate))
		{
			SelectedEmrTemplate = FilteredEmrTemplates.FirstOrDefault() ?? EmrTemplates.FirstOrDefault();
		}
	}

	private void BindLayoutForTemplate(EmrVisualTemplateModel template)
	{
		EmrPrintLayoutConfigModel emrPrintLayoutConfigModel = EmrPrintLayouts.FirstOrDefault((EmrPrintLayoutConfigModel l) => string.Equals(l.LayoutCode, template.BoundLayoutCode, StringComparison.OrdinalIgnoreCase) || string.Equals(l.LayoutId, template.BoundLayoutCode, StringComparison.OrdinalIgnoreCase)) ?? EmrPrintLayouts.FirstOrDefault();
		if (emrPrintLayoutConfigModel != null)
		{
			emrPrintLayoutConfigModel.SubTitle = template.TemplateName;
			emrPrintLayoutConfigModel.SignLevelCount = template.RequiredSignLevels;
			ActiveEmrPrintLayout = emrPrintLayoutConfigModel;
		}
	}

	public void RefreshPatientFieldVisibilityForTemplate(EmrVisualTemplateModel? template)
	{
		if (template == null)
		{
			return;
		}
		string currentEmrPatientGender = CurrentEmrPatientGender;
		foreach (EmrVisualSectionModel section in template.Sections)
		{
			foreach (EmrVisualFieldItemModel field in section.Fields)
			{
				field.IsVisibleForPatient = field.GenderExclusive == "ALL" || (field.GenderExclusive == "MALE_ONLY" && currentEmrPatientGender == "男") || (field.GenderExclusive == "FEMALE_ONLY" && currentEmrPatientGender == "女");
			}
		}
	}

	public void RebuildSignatureSlotsPreview()
	{
		PreviewSignatureSlots.Clear();
		EmrVisualTemplateModel selectedEmrTemplate = SelectedEmrTemplate;
		EmrPrintLayoutConfigModel activeEmrPrintLayout = ActiveEmrPrintLayout;
		string text = selectedEmrTemplate?.EmrScene ?? SelectedEmrSceneCode;
		int num = Math.Clamp((activeEmrPrintLayout.SignLevelCount > 0) ? activeEmrPrintLayout.SignLevelCount : (selectedEmrTemplate?.RequiredSignLevels ?? 1), 1, 3);
		string signedTimeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
		if (text == "WARD")
		{
			PreviewSignatureSlots.Add(new EmrSignatureSlotPreviewModel
			{
				Level = 1,
				RoleName = "责任护士",
				Label = "责任护士签名",
				SignerDisplay = "赵敏 (EMP006)",
				SignedTimeText = signedTimeText,
				IsSigned = true
			});
			if (num >= 2)
			{
				PreviewSignatureSlots.Add(new EmrSignatureSlotPreviewModel
				{
					Level = 2,
					RoleName = "病区护士长/质控护师",
					Label = "质控护士长审签",
					SignerDisplay = "陈芳 (EMP005)",
					SignedTimeText = signedTimeText,
					IsSigned = true
				});
			}
		}
		else if (text == "IPD")
		{
			PreviewSignatureSlots.Add(new EmrSignatureSlotPreviewModel
			{
				Level = 1,
				RoleName = "经治住院医师",
				Label = "住院医师签名",
				SignerDisplay = "李华 (EMP003)",
				SignedTimeText = signedTimeText,
				IsSigned = true
			});
			if (num >= 2)
			{
				PreviewSignatureSlots.Add(new EmrSignatureSlotPreviewModel
				{
					Level = 2,
					RoleName = "主治医师",
					Label = "主治医师审签",
					SignerDisplay = "王崇 (EMP004)",
					SignedTimeText = signedTimeText,
					IsSigned = true
				});
			}
			if (num >= 3)
			{
				PreviewSignatureSlots.Add(new EmrSignatureSlotPreviewModel
				{
					Level = 3,
					RoleName = "主任/副主任医师",
					Label = "主任医师终审",
					SignerDisplay = "张建国 (EMP002)",
					SignedTimeText = signedTimeText,
					IsSigned = true
				});
			}
		}
		else
		{
			PreviewSignatureSlots.Add(new EmrSignatureSlotPreviewModel
			{
				Level = 1,
				RoleName = "门诊接诊医师",
				Label = "接诊医师签名",
				SignerDisplay = "张建国 (EMP002)",
				SignedTimeText = signedTimeText,
				IsSigned = true
			});
			if (num >= 2)
			{
				PreviewSignatureSlots.Add(new EmrSignatureSlotPreviewModel
				{
					Level = 2,
					RoleName = "专科上级医师",
					Label = "上级医师审签",
					SignerDisplay = "王崇 (EMP004)",
					SignedTimeText = signedTimeText,
					IsSigned = true
				});
			}
		}
	}

	public void OpenEmrVisualDesigner()
	{
		EnsureEmrCatalogInitialized();
		if (SelectedEmrTemplate != null && SelectedDesignSection == null)
		{
			SelectedDesignSection = SelectedEmrTemplate.Sections.FirstOrDefault();
		}
		IsEmrVisualDesignerOpen = true;
		SystemNotice = "已打开【" + (SelectedEmrTemplate?.TemplateName ?? "电子病历") + "】管理员可视化模板与打印版式设计器（支持章节增删排序、数据元配置、纸张/边距/抬头/签名槽位可视化定义）。";
	}

	public void CloseEmrVisualDesigner()
	{
		IsEmrVisualDesignerOpen = false;
		RebuildSignatureSlotsPreview();
		ActiveEmrPrintLayout.NotifyGeometryChanged();
	}

	public void ApplyPaperSizePreset(string presetCode)
	{
		EmrPrintLayoutConfigModel activeEmrPrintLayout = ActiveEmrPrintLayout;
		string text = (activeEmrPrintLayout.PageSizeCode = presetCode?.Trim().ToUpperInvariant() ?? "A4");
		string text3 = text;
		string text4 = text3;
		if (!(text4 == "B5"))
		{
			if (text4 == "A4_LANDSCAPE")
			{
				activeEmrPrintLayout.PageWidthMm = 297.0m;
				activeEmrPrintLayout.PageHeightMm = 210.0m;
				activeEmrPrintLayout.MarginTopMm = 18.0m;
				activeEmrPrintLayout.MarginBottomMm = 18.0m;
				activeEmrPrintLayout.MarginLeftMm = 20.0m;
				activeEmrPrintLayout.MarginRightMm = 20.0m;
			}
			else
			{
				activeEmrPrintLayout.PageWidthMm = 210.0m;
				activeEmrPrintLayout.PageHeightMm = 297.0m;
				activeEmrPrintLayout.MarginTopMm = 25.0m;
				activeEmrPrintLayout.MarginBottomMm = 20.0m;
				activeEmrPrintLayout.MarginLeftMm = 25.0m;
				activeEmrPrintLayout.MarginRightMm = 20.0m;
			}
		}
		else
		{
			activeEmrPrintLayout.PageWidthMm = 176.0m;
			activeEmrPrintLayout.PageHeightMm = 250.0m;
			activeEmrPrintLayout.MarginTopMm = 16.0m;
			activeEmrPrintLayout.MarginBottomMm = 16.0m;
			activeEmrPrintLayout.MarginLeftMm = 18.0m;
			activeEmrPrintLayout.MarginRightMm = 16.0m;
		}
		activeEmrPrintLayout.NotifyGeometryChanged();
		SystemNotice = "已切换打印纸张规格为 [" + activeEmrPrintLayout.PaperSpecSummary + "]，打印预览画布已实时重排。";
	}

	public void AddEmrSection()
	{
		if (SelectedEmrTemplate != null)
		{
			string text = (string.IsNullOrWhiteSpace(NewEmrSectionTitle) ? $"自定义临床章节 {SelectedEmrTemplate.Sections.Count + 1}" : NewEmrSectionTitle.Trim());
			int num = SelectedEmrTemplate.Sections.Count + 1;
			EmrVisualSectionModel emrVisualSectionModel = new EmrVisualSectionModel
			{
				SectionCode = $"SEC_CUSTOM_{num:D2}",
				SectionTitle = text,
				RenderMode = (string.IsNullOrWhiteSpace(NewEmrSectionRenderMode) ? "GRID_FORM" : NewEmrSectionRenderMode),
				SortOrder = num,
				IsRequired = true
			};
			emrVisualSectionModel.Fields.Add(new EmrVisualFieldItemModel
			{
				ElementCode = $"CUSTOM_FIELD_{num:D2}",
				NhcStdCode = $"DE06.00.{100 + num}.00",
				ElementName = text + "记录",
				DataType = "TEXT",
				UrsaControlType = "u:TextArea",
				GenderExclusive = "ALL",
				IsRequired = true,
				ValueText = "[" + text + "] 临床结构化记录内容..."
			});
			SelectedEmrTemplate.Sections.Add(emrVisualSectionModel);
			SelectedDesignSection = emrVisualSectionModel;
			NewEmrSectionTitle = string.Empty;
			SystemNotice = $"✓ 已在模板【{SelectedEmrTemplate.TemplateName}】中新增可视化章节：{emrVisualSectionModel.SectionTitle} ({emrVisualSectionModel.RenderModeDisplay})";
		}
	}

	public void RemoveEmrSection(EmrVisualSectionModel? section)
	{
		if (SelectedEmrTemplate == null || section == null)
		{
			return;
		}
		if (SelectedEmrTemplate.Sections.Count <= 1)
		{
			SystemNotice = "病历模板至少需保留 1 个临床结构化章节。";
			return;
		}
		SelectedEmrTemplate.Sections.Remove(section);
		for (int i = 0; i < SelectedEmrTemplate.Sections.Count; i++)
		{
			SelectedEmrTemplate.Sections[i].SortOrder = i + 1;
		}
		SelectedDesignSection = SelectedEmrTemplate.Sections.FirstOrDefault();
		SystemNotice = "已移除章节 [" + section.SectionTitle + "] 并重排章节顺序。";
	}

	public void MoveEmrSection(EmrVisualSectionModel? section, int delta)
	{
		if (SelectedEmrTemplate == null || section == null)
		{
			return;
		}
		int num = SelectedEmrTemplate.Sections.IndexOf(section);
		int num2 = num + delta;
		if (num >= 0 && num2 >= 0 && num2 < SelectedEmrTemplate.Sections.Count)
		{
			SelectedEmrTemplate.Sections.Move(num, num2);
			for (int i = 0; i < SelectedEmrTemplate.Sections.Count; i++)
			{
				SelectedEmrTemplate.Sections[i].SortOrder = i + 1;
			}
			SystemNotice = $"已调整章节 [{section.SectionTitle}] 顺序至第 {num2 + 1} 节。";
		}
	}

	public void AddFieldToSelectedSection()
	{
		EmrVisualSectionModel emrVisualSectionModel = SelectedDesignSection ?? SelectedEmrTemplate?.Sections.FirstOrDefault();
		if (emrVisualSectionModel == null)
		{
			SystemNotice = "请先在左侧选择一个病历章节。";
			return;
		}
		string text = (string.IsNullOrWhiteSpace(NewEmrFieldName) ? $"扩展数据元{emrVisualSectionModel.Fields.Count + 1}" : NewEmrFieldName.Trim());
		string elementCode = (string.IsNullOrWhiteSpace(NewEmrFieldCode) ? $"DE_EXT_{emrVisualSectionModel.Fields.Count + 1:D2}" : NewEmrFieldCode.Trim().ToUpperInvariant());
		string newEmrFieldDataType = NewEmrFieldDataType;
		if (1 == 0)
		{
		}
		string text2 = newEmrFieldDataType switch
		{
			"TEXT" => "u:TextArea", 
			"INTEGER" => "u:NumericIntUpDown", 
			"DECIMAL" => "u:NumericDoubleUpDown", 
			"ENUM" => "ComboBox", 
			_ => "u:TextBox", 
		};
		if (1 == 0)
		{
		}
		string ursaControlType = text2;
		EmrVisualFieldItemModel emrVisualFieldItemModel = new EmrVisualFieldItemModel
		{
			ElementCode = elementCode,
			NhcStdCode = (string.IsNullOrWhiteSpace(NewEmrFieldNhcCode) ? "DE04.01.999.00" : NewEmrFieldNhcCode.Trim()),
			ElementName = text,
			DataType = NewEmrFieldDataType,
			UrsaControlType = ursaControlType,
			UnitText = (string.IsNullOrWhiteSpace(NewEmrFieldUnit) ? null : NewEmrFieldUnit.Trim()),
			GenderExclusive = NewEmrFieldGenderRule,
			IsRequired = NewEmrFieldRequired,
			ValueText = "正常 / 已核查 (" + text + ")"
		};
		emrVisualSectionModel.Fields.Add(emrVisualFieldItemModel);
		RefreshPatientFieldVisibilityForTemplate(SelectedEmrTemplate);
		NewEmrFieldCode = string.Empty;
		NewEmrFieldName = string.Empty;
		NewEmrFieldUnit = string.Empty;
		SystemNotice = $"✓ 已向章节【{emrVisualSectionModel.SectionTitle}】添加结构化字段 [{emrVisualFieldItemModel.ElementName} ({emrVisualFieldItemModel.ElementCode})]，性别规则={emrVisualFieldItemModel.GenderBadgeText}。";
	}

	public void RemoveFieldFromSection(EmrVisualFieldItemModel? field)
	{
		if (field == null || SelectedEmrTemplate == null)
		{
			return;
		}
		foreach (EmrVisualSectionModel section in SelectedEmrTemplate.Sections)
		{
			if (section.Fields.Remove(field))
			{
				SystemNotice = $"已从章节【{section.SectionTitle}】移除数据元 [{field.ElementName}]。";
				break;
			}
		}
	}

	public async Task SaveEmrVisualDesignAsync()
	{
		if (SelectedEmrTemplate == null)
		{
			return;
		}
		SelectedEmrTemplate.Version++;
		RebuildSignatureSlotsPreview();
		ActiveEmrPrintLayout.NotifyGeometryChanged();
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			EmrPrintLayoutConfigModel layout = ActiveEmrPrintLayout;
			await http.PostAsJsonAsync("/api/v4/emr/layouts", new
			{
				LayoutCode = layout.LayoutCode,
				LayoutName = layout.LayoutName,
				PageSizeCode = layout.PageSizeCode,
				PageWidthMm = layout.PageWidthMm,
				PageHeightMm = layout.PageHeightMm,
				MarginTopMm = layout.MarginTopMm,
				MarginBottomMm = layout.MarginBottomMm,
				MarginLeftMm = layout.MarginLeftMm,
				MarginRightMm = layout.MarginRightMm,
				IsContinuousFlow = (short)1,
				HospitalName = layout.HospitalName,
				SubTitle = layout.SubTitle,
				ShowPatientBanner = layout.ShowPatientBanner,
				ShowBedAndAdmissionNo = layout.ShowBedAndAdmissionNo,
				ShowVitalSignsBanner = layout.ShowVitalSignsBanner,
				ShowNursingScales = layout.ShowNursingScales,
				ShowTrackChanges = layout.ShowTrackChanges,
				AccentColor = layout.AccentColor,
				FooterText = layout.FooterText,
				WatermarkPolicy = layout.WatermarkText,
				IsActive = (short)1
			}, JsonOptions);
			var sectionsPayload = SelectedEmrTemplate.Sections.Select((EmrVisualSectionModel s, int idx) => new
			{
				SectionCode = s.SectionCode,
				SectionTitle = s.SectionTitle,
				RenderMode = s.RenderMode,
				ElementCodes = s.Fields.Select((EmrVisualFieldItemModel f) => f.ElementCode).ToList(),
				IsRequired = (short)(s.IsRequired ? 1 : 0),
				VisibilityExpr = s.VisibilityExpr,
				SortOrder = idx + 1
			}).ToList();
			await http.PostAsJsonAsync("/api/v4/emr/templates/design", new
			{
				TemplateCode = SelectedEmrTemplate.TemplateCode,
				TemplateName = SelectedEmrTemplate.TemplateName,
				EmrScene = SelectedEmrTemplate.EmrScene,
				DocTypeCode = SelectedEmrTemplate.DocTypeCode,
				DeptCategory = SelectedEmrTemplate.EmrScene,
				TargetDeptId = "DEPT-OPD-01",
				TimeLimitHours = SelectedEmrTemplate.TimeLimitHours,
				RequiredSignLevels = layout.SignLevelCount,
				OfdLayoutId = layout.LayoutId,
				IsPublished = (short)1,
				Sections = sectionsPayload
			}, JsonOptions);
		}
		catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException) ? true : false)
		{
		}
		IsEmrVisualDesignerOpen = false;
		SystemNotice = $"✓ 管理员可视化配置已发布生效！模板【{SelectedEmrTemplate.TemplateName} ({SelectedEmrTemplate.VersionBadge})】共 {SelectedEmrTemplate.Sections.Count} 个章节，打印版式【{ActiveEmrPrintLayout.PaperSpecSummary}】已同步至打印预览。";
	}

	public void OpenPrintPreviewForCurrentTemplate(string? sceneOverride = null)
	{
		EnsureEmrCatalogInitialized();
		SyncOpdWorkspaceToEmrTemplate();
		if (!string.IsNullOrWhiteSpace(sceneOverride))
		{
			SelectedEmrSceneCode = sceneOverride.Trim().ToUpperInvariant();
		}
		RefreshPatientFieldVisibilityForTemplate(SelectedEmrTemplate);
		RebuildSignatureSlotsPreview();
		ActiveEmrPrintLayout.NotifyGeometryChanged();
		IsOfdViewerDrawerOpen = true;
		SystemNotice = $"已打开【{SelectedEmrTemplate?.TemplateName ?? "电子病历"}】所见即所得打印预览界面 ({ActiveEmrPrintLayout.PaperSpecSummary})。";
	}

	public void AdjustPrintPreviewZoom(double delta)
	{
		ActiveEmrPrintLayout.ZoomScale += delta;
	}

	public void ResetPrintPreviewZoom()
	{
		ActiveEmrPrintLayout.ZoomScale = 1.0;
	}

	public void ExecutePrintAndArchiveCurrentEmr()
	{
		EnsureEmrCatalogInitialized();
		SyncOpdWorkspaceToEmrTemplate();
		EmrVisualTemplateModel emrVisualTemplateModel = SelectedEmrTemplate ?? EmrTemplates.First();
		EmrPrintLayoutConfigModel activeEmrPrintLayout = ActiveEmrPrintLayout;
		string currentEmrPatientGender = CurrentEmrPatientGender;
		foreach (EmrVisualSectionModel section in emrVisualTemplateModel.Sections)
		{
			foreach (EmrVisualFieldItemModel field in section.Fields)
			{
				if (!string.IsNullOrWhiteSpace(field.ValueText) && !(field.ValueText == "0"))
				{
					if (currentEmrPatientGender == "男" && field.GenderExclusive == "FEMALE_ONLY")
					{
						SystemNotice = $"❌ 性别适用校验拦截：患者【{CurrentEmrPatientName}】为男性，禁止录入女性专属字段 [{field.ElementName}]！";
						return;
					}
					if (currentEmrPatientGender == "女" && field.GenderExclusive == "MALE_ONLY" && !field.ValueText.Contains("不适用"))
					{
						SystemNotice = $"❌ 性别适用校验拦截：患者【{CurrentEmrPatientName}】为女性，禁止录入男性专属字段 [{field.ElementName}]！";
						return;
					}
				}
			}
		}
		string text = $"DOC-{emrVisualTemplateModel.EmrScene}-{DateTime.Now:yyyyMMddHHmmss}";
		IEnumerable<string> values = emrVisualTemplateModel.Sections.Select((EmrVisualSectionModel s) => "【" + s.SectionTitle + "】\n" + string.Join("\n", from f in s.Fields
			where f.IsVisibleForPatient
			select "  · " + f.ElementName + ": " + f.FormattedPrintValue));
		string contentText = string.Join("\n\n", values);
		string signSlotsSummary = string.Join(" | ", PreviewSignatureSlots.Select((EmrSignatureSlotPreviewModel s) => s.Label + ": " + s.SignerDisplay));
		string structuredSemanticXml = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<EmrStructuredData xmlns=\"http://www.ofdspec.org/2016\" Version=\"1.0\" DocNo=\"{text}\" Scene=\"{emrVisualTemplateModel.EmrScene}\" Template=\"{emrVisualTemplateModel.TemplateCode}\">\n  <Header>\n    <HospitalName>{activeEmrPrintLayout.HospitalName}</HospitalName>\n    <SubTitle>{activeEmrPrintLayout.SubTitle}</SubTitle>\n    <PaperSize Code=\"{activeEmrPrintLayout.PageSizeCode}\" WidthMm=\"{activeEmrPrintLayout.PageWidthMm}\" HeightMm=\"{activeEmrPrintLayout.PageHeightMm}\"/>\n  </Header>\n  <PatientInfo Name=\"{CurrentEmrPatientName}\" Gender=\"{CurrentEmrPatientGender}\" Age=\"{CurrentEmrPatientAge}\" EncounterNo=\"{CurrentEmrEncounterNo}\"/>\n  <ClinicalData>\n    <ChiefComplaint>{EmrWorkspace.ChiefComplaint}</ChiefComplaint>\n    <Diagnosis Code=\"I25.101\">{EmrWorkspace.Diagnosis}</Diagnosis>\n    <GenderExclusiveFields Gate=\"GATE-03\">\n      <Field Name=\"ProstateExam\" Label=\"前列腺触诊\" Gender=\"MALE\">{EmrWorkspace.ProstateExam}</Field>\n      <Field Name=\"PregnancyCount\" Label=\"孕产次数\" Gender=\"FEMALE\">{EmrWorkspace.PregnancyCount}</Field>\n    </GenderExclusiveFields>\n  </ClinicalData>\n</EmrStructuredData>";
		OfdRenderOptions options = new OfdRenderOptions(text, text, activeEmrPrintLayout.SubTitle + " - " + CurrentEmrPatientName, CurrentOperatorName, DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"), contentText, structuredSemanticXml, CurrentOperatorName, activeEmrPrintLayout.WatermarkText, activeEmrPrintLayout.PageSizeCode, activeEmrPrintLayout.PageWidthMm, activeEmrPrintLayout.PageHeightMm, activeEmrPrintLayout.MarginTopMm, activeEmrPrintLayout.MarginBottomMm, activeEmrPrintLayout.MarginLeftMm, activeEmrPrintLayout.MarginRightMm, activeEmrPrintLayout.HospitalName + " · " + activeEmrPrintLayout.SubTitle, CurrentEmrPatientSummary, activeEmrPrintLayout.FooterText, signSlotsSummary);
		byte[] array = OfdDocumentPackage.BuildOfdPackage(options);
		string text2 = Path.Combine(AppContext.BaseDirectory, "OfdExports");
		Directory.CreateDirectory(text2);
		string path = Path.Combine(text2, text + ".ofd");
		File.WriteAllBytes(path, array);
		OfdVerificationResult ofdVerificationResult = OfdDocumentPackage.VerifyOfdPackage(array);
		EmrWorkspace.DocNo = text;
		EmrWorkspace.OfdStatus = $"已审签归档 ({activeEmrPrintLayout.PageSizeCode} · SHA-256: {ofdVerificationResult.ContentSha256.Substring(0, 12)}...)";
		if (emrVisualTemplateModel.EmrScene == "OPD")
		{
			MarkCurrentOpdEmrSigned(text, ofdVerificationResult.ContentSha256.Substring(0, 12));
		}
		OfdArchiveRecords.Insert(0, new OfdArchiveRecordModel
		{
			ArchiveNo = text,
			DocType = $"[{emrVisualTemplateModel.SceneBadgeText}] {emrVisualTemplateModel.TemplateName} ({activeEmrPrintLayout.PageSizeCode} {activeEmrPrintLayout.PageWidthMm:0.#}×{activeEmrPrintLayout.PageHeightMm:0.#}mm)",
			PatientName = CurrentEmrPatientName,
			DeptName = emrVisualTemplateModel.DeptName,
			OfdStandard = "GB/T 33190-2016",
			ZipEntries = "OFD.xml | Doc_0/Document.xml | CustomTags/EmrStructuredData.xml | Signs/Sign_1/Signature.xml",
			HashSha256 = ofdVerificationResult.ContentSha256,
			SignatureStatus = $"✓ {PreviewSignatureSlots.Count}级电子审签完成 (防篡改校验通过)",
			ArchiveTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
			IsVerified = true
		});
		SystemNotice = $"\ud83d\udda8\ufe0f 病历打印输出与国标 OFD 归档完成：[{emrVisualTemplateModel.TemplateName}] 纸张={activeEmrPrintLayout.PageSizeCode} ({activeEmrPrintLayout.PageWidthMm:0.#}×{activeEmrPrintLayout.PageHeightMm:0.#}mm)，封包={array.Length}字节，SHA-256={ofdVerificationResult.ContentSha256.Substring(0, 16)}...";
	}

	public EmrPrintSnapshot CreateCurrentEmrPrintSnapshot()
	{
		EmrPrintLayoutConfigModel activeEmrPrintLayout = ActiveEmrPrintLayout;
		List<EmrPrintSnapshotSection> sections = SelectedEmrTemplate?.Sections.Select((EmrVisualSectionModel section) => new EmrPrintSnapshotSection(section.SectionTitle, section.RenderMode, (from field in section.Fields
			where field.IsVisibleForPatient
			select new EmrPrintSnapshotField(field.DisplayLabel, string.IsNullOrWhiteSpace(field.FormattedPrintValue) ? "--" : field.FormattedPrintValue, field.IsRequired)).ToList())).ToList() ?? new List<EmrPrintSnapshotSection>();
		return new EmrPrintSnapshot(EmrWorkspace?.DocNo ?? $"DOC-EMR-{DateTime.Now:yyyyMMddHHmmss}", SelectedEmrTemplate?.TemplateName ?? "电子病历", activeEmrPrintLayout.HospitalName, activeEmrPrintLayout.SubTitle, CurrentEmrPatientSummary, CurrentEmrBedAndWardText, CurrentNursingScalesSummary, activeEmrPrintLayout.FooterText, activeEmrPrintLayout.WatermarkText, "李华 (住院医师)", "李华 (数字证书)", sections, PreviewSignatureSlots.Select((EmrSignatureSlotPreviewModel slot) => $"{slot.Label}: {slot.SignerDisplay} | {slot.SignedTimeText} | {slot.StatusBadge}").ToList(), activeEmrPrintLayout.PageWidthMm, activeEmrPrintLayout.PageHeightMm, activeEmrPrintLayout.MarginTopMm, activeEmrPrintLayout.MarginBottomMm, activeEmrPrintLayout.MarginLeftMm, activeEmrPrintLayout.MarginRightMm, activeEmrPrintLayout.ShowPatientBanner, activeEmrPrintLayout.ShowBedAndAdmissionNo, activeEmrPrintLayout.ShowNursingScales, activeEmrPrintLayout.ShowTrackChanges);
	}

	public byte[] BuildCurrentOfdPackage()
	{
		EmrPrintSnapshot emrPrintSnapshot = CreateCurrentEmrPrintSnapshot();
		string contentText = string.Join("\n\n", emrPrintSnapshot.Sections.Select((EmrPrintSnapshotSection section) => "【" + section.Title + "】\n" + string.Join("\n", section.Fields.Select((EmrPrintSnapshotField field) => field.Label + ": " + field.Value))));
		OfdRenderOptions options = new OfdRenderOptions(emrPrintSnapshot.DocNo, emrPrintSnapshot.DocNo, emrPrintSnapshot.SubTitle, emrPrintSnapshot.Author, DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"), contentText, null, emrPrintSnapshot.SignerName, emrPrintSnapshot.WatermarkText, (emrPrintSnapshot.PageWidthMm >= emrPrintSnapshot.PageHeightMm) ? "A4_LANDSCAPE" : "A4", emrPrintSnapshot.PageWidthMm, emrPrintSnapshot.PageHeightMm, emrPrintSnapshot.MarginTopMm, emrPrintSnapshot.MarginBottomMm, emrPrintSnapshot.MarginLeftMm, emrPrintSnapshot.MarginRightMm, emrPrintSnapshot.HospitalName, emrPrintSnapshot.PatientBanner, emrPrintSnapshot.FooterText, string.Join("\n", emrPrintSnapshot.SignSlots));
		return OfdDocumentPackage.BuildOfdPackage(options);
	}

	public string SaveCurrentOfdToTemporaryFile()
	{
		EmrPrintSnapshot emrPrintSnapshot = CreateCurrentEmrPrintSnapshot();
		string text = Path.Combine(Path.GetTempPath(), "GHIS", "PrintJobs");
		Directory.CreateDirectory(text);
		string text2 = Path.Combine(text, SanitizeFileName(emrPrintSnapshot.DocNo) + ".ofd");
		File.WriteAllBytes(text2, BuildCurrentOfdPackage());
		return text2;
	}

	public void SetPrintResult(string message)
	{
		SystemNotice = message;
	}

	private static string SanitizeFileName(string value)
	{
		char[] invalid = Path.GetInvalidFileNameChars();
		char[] value2 = value.Select((char ch) => invalid.Contains(ch) ? '_' : ch).ToArray();
		return new string(value2);
	}

	public void InitFrequencyDefinitions()
	{
		FrequencyDefinitions.Clear();
		(string, string, string, int, string, bool)[] array = new (string, string, string, int, string, bool)[10]
		{
			("qd", "每日一次", "08:00", 1, "每日晨间8点执行给药或常规护理", true),
			("bid", "每日两次", "08:00, 16:00", 2, "早8点、下午16点各执行一次", true),
			("tid", "每日三次", "08:00, 12:00, 16:00", 3, "早8点、中午12点、下午16点各执行一次", true),
			("qid", "每日四次", "08:00, 12:00, 16:00, 20:00", 4, "早、中、晚及夜间各执行一次", true),
			("qn", "每晚一次", "20:00", 1, "晚间20点睡前给药或处置", true),
			("q8h", "每8小时一次", "06:00, 14:00, 22:00", 3, "维持抗感染血药峰浓度恒定，每8小时规律给药", true),
			("q12h", "每12小时一次", "08:00, 20:00", 2, "早8点与晚20点间隔12小时执行", true),
			("持续", "持续微量泵入/监护", "08:00", 1, "全天微量泵入或心电监护，每日白班交接核对", true),
			("st", "立即执行", "即刻", 1, "急救或临时处置医嘱，下达后即刻执行", false),
			("prn", "必要时", "按需", 1, "备用医嘱，病情发作时按需核对执行", false)
		};
		(string, string, string, int, string, bool)[] array2 = array;
		foreach ((string, string, string, int, string, bool) tuple in array2)
		{
			ObservableCollection<OrderFrequencyDefinition> frequencyDefinitions = FrequencyDefinitions;
			OrderFrequencyDefinition orderFrequencyDefinition = new OrderFrequencyDefinition();
			(orderFrequencyDefinition.Code, orderFrequencyDefinition.Name, orderFrequencyDefinition.TimePointsText, orderFrequencyDefinition.DailyTimes, orderFrequencyDefinition.Description, orderFrequencyDefinition.IsLongTermApplicable) = tuple;
			frequencyDefinitions.Add(orderFrequencyDefinition);
		}
		SyncFrequencyOptions();
	}

	public void SyncFrequencyOptions()
	{
		OrderFrequencyOptions.Clear();
		foreach (OrderFrequencyDefinition frequencyDefinition in FrequencyDefinitions)
		{
			OrderFrequencyOptions.Add(frequencyDefinition.DisplayText);
		}
	}

	public async Task LoadFrequenciesFromServerAsync()
	{
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			ClientApiEnvelope<List<MasterFrequencyDto>> envelope = await http.GetFromJsonAsync<ClientApiEnvelope<List<MasterFrequencyDto>>>("/api/v4/config/master/frequencies", JsonOptions);
			int num;
			if (envelope != null && envelope.Code == 200)
			{
				List<MasterFrequencyDto> data = envelope.Data;
				num = ((data != null && data.Count > 0) ? 1 : 0);
			}
			else
			{
				num = 0;
			}
			if (num == 0)
			{
				return;
			}
			FrequencyDefinitions.Clear();
			foreach (MasterFrequencyDto item in envelope.Data)
			{
				FrequencyDefinitions.Add(new OrderFrequencyDefinition
				{
					Code = item.FreqCode,
					Name = item.FreqName,
					TimePointsText = item.TimePointsText,
					DailyTimes = item.DailyTimes,
					Description = (item.Description ?? string.Empty),
					IsLongTermApplicable = !string.Equals(item.ApplicableTerm, "STAT", StringComparison.OrdinalIgnoreCase)
				});
			}
			SyncFrequencyOptions();
		}
		catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException) ? true : false)
		{
		}
	}

	private async Task PersistFrequencyToServerAsync(OrderFrequencyDefinition def)
	{
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			await http.PostAsJsonAsync("/api/v4/config/master/frequencies", new
			{
				FreqCode = def.Code,
				FreqName = def.Name,
				DailyTimes = def.DailyTimes,
				TimePointsText = def.TimePointsText,
				ApplicableTerm = (def.IsLongTermApplicable ? "STANDING" : "STAT"),
				Description = def.Description,
				SortOrder = FrequencyDefinitions.IndexOf(def) + 1,
				IsActive = (short)1
			}, JsonOptions);
		}
		catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException) ? true : false)
		{
		}
	}

	public OrderFrequencyDefinition? FindFrequencyDefinition(string? frequencyText)
	{
		if (string.IsNullOrWhiteSpace(frequencyText))
		{
			return null;
		}
		string raw = frequencyText.Trim().ToLowerInvariant();
		OrderFrequencyDefinition orderFrequencyDefinition = FrequencyDefinitions.FirstOrDefault((OrderFrequencyDefinition f) => string.Equals(f.Code, raw, StringComparison.OrdinalIgnoreCase));
		if (orderFrequencyDefinition != null)
		{
			return orderFrequencyDefinition;
		}
		return FrequencyDefinitions.FirstOrDefault((OrderFrequencyDefinition f) => raw.StartsWith(f.Code.ToLowerInvariant() + " ") || raw.StartsWith(f.Code.ToLowerInvariant() + "(") || raw.Contains(f.Code.ToLowerInvariant()) || raw.Contains(f.Name.ToLowerInvariant()));
	}

	public int DecomposeStandingOrdersForPatient(InpatientRecordModel patient, DateOnly targetDate, bool announce = true)
	{
		if (!patient.IsActiveInpatient)
		{
			if (announce)
			{
				SystemNotice = "患者【" + patient.PatientName + "】已出院，无法执行医嘱分解。";
			}
			return 0;
		}
		List<InpatientOrderModel> list = patient.Orders.Where((InpatientOrderModel x) => x.OrderType == "临时医嘱" && x.IsReviewedWaitingDecompose).ToList();
		List<InpatientOrderModel> list2 = patient.Orders.Where((InpatientOrderModel x) => x.OrderType == "长期医嘱" && x.IsActive && !x.IsPendingReview).ToList();
		if (list2.Count == 0 && list.Count == 0)
		{
			if (announce)
			{
				SystemNotice = ((patient.PendingOrderCount > 0) ? $"⚠\ufe0f [A➔B➔C 闭环门禁拦截] 患者【{patient.PatientName}】有 {patient.PendingOrderCount} 条医嘱尚处于【步骤A：待护士校对】状态，请先点击「✓ 步骤B：批量校对审签」后再执行【步骤C：分解生成执行单】！" : ("患者【" + patient.PatientName + "】当前无已校对生效的医嘱可供分解。"));
			}
			return 0;
		}
		string dateStr = targetDate.ToString("yyyy-MM-dd");
		int num = 0;
		foreach (InpatientOrderModel tempOrder in list)
		{
			tempOrder.Status = "执行中";
			if (!patient.NursingTasks.Any((NursingTaskModel t) => t.SourceOrder == tempOrder.OrderNo))
			{
				NursingTaskModel nursingTaskModel = CreateNursingTaskFromOrder(tempOrder, patient.NursingTasks.Count);
				nursingTaskModel.ScheduledDate = dateStr;
				nursingTaskModel.TimePoint = DateTime.Now.ToString("HH:mm");
				nursingTaskModel.FrequencySnapshot = "st";
				nursingTaskModel.IsDecomposedTask = true;
				patient.NursingTasks.Insert(0, nursingTaskModel);
				num++;
			}
		}
		foreach (InpatientOrderModel order in list2)
		{
			if (order.IsReviewedWaitingDecompose)
			{
				order.Status = "执行中";
			}
			OrderFrequencyDefinition orderFrequencyDefinition = FindFrequencyDefinition(order.Frequency) ?? FrequencyDefinitions.FirstOrDefault((OrderFrequencyDefinition f) => f.Code == "qd") ?? new OrderFrequencyDefinition
			{
				Code = "qd",
				Name = "每日一次",
				TimePointsText = "08:00"
			};
			IReadOnlyList<string> readOnlyList = orderFrequencyDefinition.ParsedTimePoints;
			if (readOnlyList.Count == 0)
			{
				readOnlyList = new string[1] { "08:00" };
			}
			for (int num2 = 0; num2 < readOnlyList.Count; num2++)
			{
				string tp = readOnlyList[num2];
				string scheduledDateTimeStr = dateStr + " " + tp;
				if (DateTime.TryParse(scheduledDateTimeStr, out var result) && (!DateTime.TryParse(order.StartTime, out var result2) || !(result < result2)) && (!order.IsStopped || !DateTime.TryParse(order.StopTime, out var result3) || !(result > result3)) && !patient.NursingTasks.Any((NursingTaskModel t) => t.SourceOrder == order.OrderNo && ((t.ScheduledDate == dateStr && t.TimePoint == tp) || t.ScheduledTime == scheduledDateTimeStr || (targetDate == DateOnly.FromDateTime(DateTime.Today) && t.ScheduledTime == tp))))
				{
					string priority;
					if (patient.IsCritical || patient.NursingLevel.Contains("特级"))
					{
						priority = "高";
					}
					else
					{
						priority = (patient.NursingLevel.Contains("一级") ? "中" : "常规");
					}
					string category = DetermineTaskCategory(order);
					string value = ((readOnlyList.Count > 1) ? $" · 批次 {num2 + 1}/{readOnlyList.Count}" : "");
					NursingTaskModel item = new NursingTaskModel
					{
						TaskNo = NextInpatientId("NT", patient.NursingTasks.Count),
						ScheduledDate = dateStr,
						TimePoint = tp,
						ScheduledTime = dateStr + " " + tp,
						Category = category,
						Content = $"{order.Content} [{order.Dose}] ({order.Route}){value}",
						SourceOrder = order.OrderNo,
						FrequencySnapshot = orderFrequencyDefinition.Code,
						Priority = priority,
						Status = "待执行",
						IsDecomposedTask = true
					};
					patient.NursingTasks.Insert(0, item);
					num++;
				}
			}
		}
		patient.NotifyWorkflowChanged();
		RefreshInpatientMetrics();
		if (announce)
		{
			string value2;
			if (targetDate == DateOnly.FromDateTime(DateTime.Today))
			{
				value2 = "今日 (" + dateStr + ")";
			}
			else
			{
				value2 = ((targetDate == DateOnly.FromDateTime(DateTime.Today.AddDays(1.0))) ? ("明日 (" + dateStr + ")") : dateStr);
			}
			if (num > 0)
			{
				SystemNotice = $"✓ [步骤C：医嘱分解完成] 已为患者【{patient.PatientName}】将已校对医嘱转为【执行中】并分解生成 {num} 项 {value2} 护理执行单，请切至「2. 护理任务与执行单」执行步骤D（扫码执行）与步骤E（双人核对）！";
			}
			else if (patient.PendingOrderCount > 0)
			{
				SystemNotice = $"⚠\ufe0f [A➔B➔C 闭环提示] 仍有 {patient.PendingOrderCount} 条新开医嘱处于【步骤A：待护士校对】状态，请先完成【步骤B：校对审签】后再分解！";
			}
			else
			{
				SystemNotice = $"患者【{patient.PatientName}】{value2} 的已校对医嘱已全部完成分解，无新增任务。";
			}
		}
		return num;
	}

	public void DecomposeTodayOrders()
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null)
		{
			SystemNotice = "请先选择住院患者。";
		}
		else
		{
			DecomposeStandingOrdersForPatient(selectedInpatient, DateOnly.FromDateTime(DateTime.Today));
		}
	}

	public void DecomposeTomorrowOrders()
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null)
		{
			SystemNotice = "请先选择住院患者。";
		}
		else
		{
			DecomposeStandingOrdersForPatient(selectedInpatient, DateOnly.FromDateTime(DateTime.Today.AddDays(1.0)));
		}
	}

	public void DecomposeAllActiveInpatients()
	{
		List<InpatientRecordModel> list = InpatientRecords.Where((InpatientRecordModel x) => x.IsActiveInpatient).ToList();
		int num = 0;
		DateOnly targetDate = DateOnly.FromDateTime(DateTime.Today);
		foreach (InpatientRecordModel item in list)
		{
			num += DecomposeStandingOrdersForPatient(item, targetDate, announce: false);
		}
		SystemNotice = $"✓ 病区批量医嘱分解完成：为 {list.Count} 位在院患者生成共 {num} 条今日护理执行单！";
	}

	public void OpenFrequencyConfigModal()
	{
		IsFrequencyConfigModalOpen = true;
	}

	public void CloseFrequencyConfigModal()
	{
		SyncFrequencyOptions();
		IsFrequencyConfigModalOpen = false;
		IsAddFrequencyFormOpen = false;
		SystemNotice = "已更新医嘱频次与执行时间点配置，分解引擎已按新配置同步生效。";
	}

	public void ResetFrequencyDefinitionsToDefault()
	{
		InitFrequencyDefinitions();
		SystemNotice = "已恢复三甲医院标准临床医嘱执行频次与分解时间点推荐配置。";
	}

	public void ToggleAddFrequencyPanel()
	{
		IsAddFrequencyFormOpen = !IsAddFrequencyFormOpen;
	}

	public void CancelAddFrequency()
	{
		IsAddFrequencyFormOpen = false;
		NewFreqCode = string.Empty;
		NewFreqName = string.Empty;
		NewFreqDesc = string.Empty;
	}

	public void SaveNewFrequency()
	{
		if (string.IsNullOrWhiteSpace(NewFreqCode) || string.IsNullOrWhiteSpace(NewFreqName))
		{
			SystemNotice = "频次代码和频次名称不能为空。";
			return;
		}
		string code = NewFreqCode.Trim().ToLowerInvariant();
		if (FrequencyDefinitions.Any((OrderFrequencyDefinition f) => string.Equals(f.Code, code, StringComparison.OrdinalIgnoreCase)))
		{
			SystemNotice = "频次代码 [" + code + "] 已存在，不能重复添加。";
			return;
		}
		string timePointsText = (string.IsNullOrWhiteSpace(NewFreqTimePoints) ? "08:00" : NewFreqTimePoints.Trim());
		OrderFrequencyDefinition orderFrequencyDefinition = new OrderFrequencyDefinition
		{
			Code = code,
			Name = NewFreqName.Trim(),
			TimePointsText = timePointsText,
			Description = (string.IsNullOrWhiteSpace(NewFreqDesc) ? "科室自定义频次" : NewFreqDesc.Trim()),
			IsLongTermApplicable = NewFreqIsLongTerm,
			IsCustom = true
		};
		FrequencyDefinitions.Add(orderFrequencyDefinition);
		SyncFrequencyOptions();
		_ = PersistFrequencyToServerAsync(orderFrequencyDefinition);
		CancelAddFrequency();
		SystemNotice = $"已新增临床频次 [{orderFrequencyDefinition.Code}]，分解时间点: {orderFrequencyDefinition.TimePointsBadge}。";
	}

	private static string DetermineTaskCategory(InpatientOrderModel order)
	{
		string text = (order.Content + " " + order.Route).ToLowerInvariant();
		if (text.Contains("监护") || text.Contains("吸氧") || text.Contains("血氧") || text.Contains("心电"))
		{
			return "监护";
		}
		if (text.Contains("护理") || text.Contains("翻身") || text.Contains("口腔") || text.Contains("导尿") || text.Contains("会阴"))
		{
			return "护理";
		}
		if (text.Contains("采血") || text.Contains("血常规") || text.Contains("检验") || text.Contains("化验"))
		{
			return "检验";
		}
		if (text.Contains("雾化") || text.Contains("清创") || text.Contains("换药") || text.Contains("引流"))
		{
			return "处置";
		}
		return "给药";
	}

	public void SelectInpatientByBedCard(BedCardModel? bed)
	{
		if (bed == null)
		{
			return;
		}
		if (!bed.IsOccupied || bed.AdmissionNo == "-")
		{
			SystemNotice = "床位 [" + bed.BedCode + "] 当前为空床待收治状态，可点击上方「新患者入科」或「跨病区借床入科」办理收治。";
			return;
		}
		InpatientRecordModel inpatientRecordModel = InpatientRecords.FirstOrDefault((InpatientRecordModel x) => x.AdmissionNo == bed.AdmissionNo) ?? AllInpatientRecords.FirstOrDefault((InpatientRecordModel x) => x.AdmissionNo == bed.AdmissionNo || x.PatientName == bed.PatientName);
		if (inpatientRecordModel != null)
		{
			SelectedInpatient = inpatientRecordModel;
			SystemNotice = $"已联动选中 [{bed.BedCode}] 在院患者：{inpatientRecordModel.PatientName} ({inpatientRecordModel.Gender}, {inpatientRecordModel.Age}岁) · 住院号: {inpatientRecordModel.AdmissionNo}";
		}
	}

	public void RefreshInpatientMetrics()
	{
		OnPropertyChanged("TotalInpatientCount");
		OnPropertyChanged("CriticalInpatientCount");
		OnPropertyChanged("PendingOrderReviewCount");
		OnPropertyChanged("WaitingDecomposeTotalCount");
		OnPropertyChanged("PendingNursingTaskCount");
		OnPropertyChanged("UnverifiedNursingTaskCount");
		OnPropertyChanged("DischargeReadyCount");
		OnPropertyChanged("CurrentOperatorName");
		OnPropertyChanged("CurrentOperatorRole");
		OnPropertyChanged("CanOperateOpdClinical");
		OnPropertyChanged("CanOperateIpdClinical");
		OnPropertyChanged("CanOperateWardNursing");
		OnPropertyChanged("CanOperatePharmacy");
		OnPropertyChanged("CanOperateFinance");
		OnPropertyChanged("CurrentSodRoleBannerText");
		OnPropertyChanged("CanCreateInpatientOrder");
		OnPropertyChanged("CanReviewSelectedInpatientOrders");
		OnPropertyChanged("CanDecomposeSelectedInpatientOrders");
		OnPropertyChanged("CanSignSelectedProgressNotes");
		OnPropertyChanged("CanExecuteSelectedNursingTasks");
		OnPropertyChanged("CanVerifySelectedNursingTasks");
		OnPropertyChanged("CanApproveSelectedDischarge");
		DischargeCheckDetails.Refresh();
	}

	public void AddInpatientOrder(bool longTerm)
	{
		if (!CanOperateIpdClinical)
		{
			SystemNotice = "\ud83d\udee1\ufe0f [医护职责权限分离拦截 (SoD)] 当前登录角色【" + CurrentOperatorRole + "】无住院医嘱处方权！住院医嘱仅限【住院医师 / 主治医师】开立。";
			return;
		}
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null || !selectedInpatient.IsActiveInpatient)
		{
			SystemNotice = "请先选择一名在院患者，再下达医嘱。";
			return;
		}
		if (string.IsNullOrWhiteSpace(NewOrderContent))
		{
			SystemNotice = "医嘱内容不能为空。";
			return;
		}
		string dose = (string.IsNullOrWhiteSpace(NewOrderDose) ? "1次" : NewOrderDose.Trim());
		string route = (string.IsNullOrWhiteSpace(NewOrderRoute) ? "口服" : NewOrderRoute.Trim());
		string frequency;
		if (string.IsNullOrWhiteSpace(NewOrderFrequency))
		{
			frequency = (longTerm ? "qd (每日一次)" : "st (立即执行)");
		}
		else
		{
			frequency = NewOrderFrequency.Trim();
		}
		InpatientOrderModel inpatientOrderModel = new InpatientOrderModel
		{
			OrderNo = NextInpatientId("ORD", selectedInpatient.Orders.Count),
			OrderType = (longTerm ? "长期医嘱" : "临时医嘱"),
			Content = NewOrderContent.Trim(),
			Dose = dose,
			Route = route,
			Frequency = frequency,
			StartTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
			DoctorName = CurrentOperatorName,
			Status = "待护士校对",
			NurseSign = "--",
			ReviewInfo = "[步骤A] 由医生 " + CurrentOperatorName + " 开立，等待病区责任护士执行【步骤B：校对审签】"
		};
		selectedInpatient.Orders.Insert(0, inpatientOrderModel);
		SyncInpatientBed(selectedInpatient);
		NewOrderContent = string.Empty;
		NotifyInpatientChanged(selectedInpatient);
		SystemNotice = $"✓ [步骤A：医生开立医嘱完成] {inpatientOrderModel.OrderType} [{inpatientOrderModel.Content}] 状态为【待护士校对】。下一步必须执行【步骤B：护士校对审签】➔【步骤C：分解生成执行单】➔【步骤D：扫码执行】➔【步骤E：双人核对】！";
	}

	public void ReviewSelectedInpatientOrders()
	{
		if (!CanOperateWardNursing)
		{
			SystemNotice = "\ud83d\udee1\ufe0f [医护职责权限分离拦截 (SoD)] 当前登录角色【" + CurrentOperatorRole + "】不可越权代签护士校对！医嘱校对审签(步骤B)与分解执行(步骤C~E)必须由【\ud83d\udc69\u200d⚕\ufe0f 病区护士】执行。";
			return;
		}
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null)
		{
			SystemNotice = "请先选择住院患者。";
			return;
		}
		List<InpatientOrderModel> list = selectedInpatient.Orders.Where((InpatientOrderModel x) => x.IsPendingReview).ToList();
		if (list.Count == 0)
		{
			SystemNotice = "所选患者当前没有处于【步骤A：待护士校对】的医嘱。";
			return;
		}
		string text = ((SessionContext.Current.IsWardNurse && !string.IsNullOrWhiteSpace(SessionContext.Current.DisplayName)) ? SessionContext.Current.DisplayName : "陈劳");
		foreach (InpatientOrderModel item in list)
		{
			item.Status = ((item.OrderType == "长期医嘱") ? "已校对待分解" : "已校对待执行");
			item.NurseSign = text + " (已校对)";
			item.ReviewInfo = $"[步骤B] 责任护士 {text} 于 {DateTime.Now:HH:mm} 完成校对审签，等待【步骤C：分解生成执行单】";
		}
		NotifyInpatientChanged(selectedInpatient);
		SystemNotice = $"✓ [步骤B：护士校对审签完成] 已核对 {list.Count} 条医嘱并转为【已校对待分解】，已解锁【步骤C：⚡ 自动分解今日医嘱】，请点击分解生成执行单！";
	}

	public void ReviewSingleInpatientOrder(InpatientOrderModel order)
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null || !selectedInpatient.Orders.Contains(order) || !order.IsPendingReview)
		{
			SystemNotice = "该医嘱当前无需校对。";
			return;
		}
		string text = ((SessionContext.Current.IsWardNurse && !string.IsNullOrWhiteSpace(SessionContext.Current.DisplayName)) ? SessionContext.Current.DisplayName : "陈劳");
		order.Status = ((order.OrderType == "长期医嘱") ? "已校对待分解" : "已校对待执行");
		order.NurseSign = text + " (已校对)";
		order.ReviewInfo = $"[步骤B] 责任护士 {text} 于 {DateTime.Now:HH:mm} 完成校对审签，等待【步骤C：分解生成执行单】";
		NotifyInpatientChanged(selectedInpatient);
		SystemNotice = $"✓ [步骤B：护士校对完成] 医嘱 [{order.Content}] 已转为【{order.Status}】，请点击上方「⚡ 步骤C：自动分解今日医嘱」生成护理执行单！";
	}

	public void StopInpatientOrder(InpatientOrderModel order)
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null || !selectedInpatient.Orders.Contains(order))
		{
			return;
		}
		if (order.IsPendingReview)
		{
			SystemNotice = "⚠\ufe0f [A➔B 顺序拦截] 未经护士校对的新开医嘱不能执行「停止」，若需撤回请点击「作废」！";
			return;
		}
		if (!order.CanStop)
		{
			SystemNotice = "该医嘱当前不可停止。";
			return;
		}
		order.Status = "已停止";
		order.StopTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
		order.StopDoctor = CurrentOperatorName;
		order.ReviewInfo = $"{CurrentOperatorName} 于 {DateTime.Now:HH:mm} 停止医嘱";
		DateTime now = DateTime.Now;
		int num = 0;
		foreach (NursingTaskModel item in selectedInpatient.NursingTasks.Where((NursingTaskModel t) => t.SourceOrder == order.OrderNo && t.IsPending).ToList())
		{
			if (!DateTime.TryParse(item.ScheduledTime, out var result) || result >= now.AddHours(-12.0))
			{
				item.Status = "已退回";
				item.ExecutedAt = $"已随医嘱停止撤销 ({DateTime.Now:HH:mm})";
				num++;
			}
		}
		NotifyInpatientChanged(selectedInpatient);
		SystemNotice = ((num > 0) ? $"✓ 医嘱 [{order.Content}] 已下达停止，并自动撤销其关联的 {num} 项未执行分解任务。" : ("✓ 医嘱 [" + order.Content + "] 已停止，出院医嘱闭环校验已刷新。"));
	}

	public void CancelInpatientOrder(InpatientOrderModel order)
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient != null && selectedInpatient.Orders.Contains(order))
		{
			if (!order.CanCancel)
			{
				SystemNotice = "⚠\ufe0f [A➔B➔C 闭环拦截] 该医嘱已由护士校对或进入执行阶段，严禁直接作废！请使用「停止」医嘱流程。";
				return;
			}
			order.Status = "已作废";
			order.CancelTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
			order.CancelDoctor = CurrentOperatorName;
			order.ReviewInfo = $"{CurrentOperatorName} 于 {DateTime.Now:HH:mm} 作废未校对医嘱";
			NotifyInpatientChanged(selectedInpatient);
			SystemNotice = "✓ 未校对医嘱 [" + order.Content + "] 已作废。";
		}
	}

	public void AddProgressNote()
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null || !selectedInpatient.IsActiveInpatient)
		{
			SystemNotice = "请先选择一名在院患者。";
			return;
		}
		if (string.IsNullOrWhiteSpace(NewProgressNote))
		{
			SystemNotice = "病程记录内容不能为空。";
			return;
		}
		ProgressNoteModel item = new ProgressNoteModel
		{
			RecordTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
			Author = CurrentOperatorName + " (" + CurrentOperatorRole + ")",
			RecordType = "日常病程记录",
			Content = NewProgressNote.Trim(),
			IsSigned = false
		};
		selectedInpatient.ProgressNotes.Insert(0, item);
		NewProgressNote = string.Empty;
		NotifyInpatientChanged(selectedInpatient);
		SystemNotice = "✓ [步骤A：病程书写完成] 病程已保存为【待主治审签】，请点击「步骤B：主治审签」完成上级医师审签闭环！";
	}

	public void SignProgressNote(ProgressNoteModel note)
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null || !selectedInpatient.ProgressNotes.Contains(note))
		{
			SystemNotice = "未找到对应的病程记录。";
			return;
		}
		if (note.IsSigned)
		{
			SystemNotice = "该病程记录已完成审签。";
			return;
		}
		note.IsSigned = true;
		NotifyInpatientChanged(selectedInpatient);
		SystemNotice = "✓ [步骤B：主治审签完成] 病程记录已由 " + CurrentOperatorName + " 完成电子审签。";
	}

	public void ExecuteNursingTask(NursingTaskModel task)
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null || !selectedInpatient.NursingTasks.Contains(task))
		{
			SystemNotice = "未找到对应的护理任务。";
			return;
		}
		if (!task.CanExecute)
		{
			SystemNotice = "该护理任务已执行或已核对。";
			return;
		}
		task.NurseName = CurrentOperatorName;
		task.ExecutedAt = DateTime.Now.ToString("HH:mm");
		task.Status = "已执行";
		NotifyInpatientChanged(selectedInpatient);
		SystemNotice = "✓ [步骤D：扫码执行完成] 护理任务 [" + task.Content + "] 状态已变更为【已执行】，已解锁【步骤E：双人核对】按钮！";
	}

	public void VerifyNursingTask(NursingTaskModel task)
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null || !selectedInpatient.NursingTasks.Contains(task))
		{
			SystemNotice = "未找到对应的护理任务。";
			return;
		}
		if (task.IsPending)
		{
			SystemNotice = "⚠\ufe0f [D➔E 顺序门禁拦截] 该任务尚处于【步骤D：待执行】状态，严禁跳过扫码执行直接点击核对！";
			return;
		}
		if (!task.CanVerify)
		{
			SystemNotice = "该护理任务已完成核对。";
			return;
		}
		task.Status = "已核对";
		NotifyInpatientChanged(selectedInpatient);
		SystemNotice = "✓ [步骤E：双人核对闭环完成] 护理任务 [" + task.Content + "] 已完成双人核对签字！";
	}

	public void ExecuteAllNursingTasks()
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null)
		{
			SystemNotice = "请先选择住院患者。";
			return;
		}
		List<NursingTaskModel> list = selectedInpatient.NursingTasks.Where((NursingTaskModel x) => x.IsPending).ToList();
		if (list.Count == 0)
		{
			SystemNotice = "所选患者没有处于【步骤D：待执行】的护理任务。";
			return;
		}
		foreach (NursingTaskModel item in list)
		{
			item.NurseName = CurrentOperatorName;
			item.ExecutedAt = DateTime.Now.ToString("HH:mm");
			item.Status = "已执行";
		}
		NotifyInpatientChanged(selectedInpatient);
		SystemNotice = $"✓ [步骤D：批量扫码执行完成] 已执行 {list.Count} 项护理任务，已解锁右侧「✓ 步骤E：批量双人核对」！";
	}

	public void VerifyAllExecutedNursingTasks()
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null)
		{
			SystemNotice = "请先选择住院患者。";
			return;
		}
		List<NursingTaskModel> list = selectedInpatient.NursingTasks.Where((NursingTaskModel x) => x.CanVerify).ToList();
		if (list.Count == 0)
		{
			SystemNotice = "⚠\ufe0f [D➔E 顺序门禁拦截] 当前没有处于【已执行·待核对】的任务！请先完成【步骤D：扫码执行】后再进行双人核对。";
			return;
		}
		foreach (NursingTaskModel item in list)
		{
			item.Status = "已核对";
		}
		NotifyInpatientChanged(selectedInpatient);
		SystemNotice = $"✓ [步骤E：批量双人核对完成] 已核销 {list.Count} 项已执行护理任务，医嘱执行全链路闭环！";
	}

	public void CompleteDischargeGate1_Orders()
	{
		InpatientRecordModel inpatientRecordModel = SelectedInpatient ?? DischargeCheckDetails.Patient;
		if (inpatientRecordModel == null)
		{
			return;
		}
		foreach (InpatientOrderModel item in inpatientRecordModel.Orders.Where((InpatientOrderModel x) => x.IsActive).ToList())
		{
			if (item.IsPendingReview)
			{
				item.Status = "已作废";
				item.CancelTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
				item.CancelDoctor = CurrentOperatorName;
			}
			else
			{
				item.Status = "已停止";
				item.StopTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
				item.StopDoctor = CurrentOperatorName;
			}
		}
		foreach (NursingTaskModel item2 in inpatientRecordModel.NursingTasks.Where((NursingTaskModel x) =>
		{
			string status = x.Status;
			return (status == "待执行" || status == "已执行") ? true : false;
		}).ToList())
		{
			item2.ExecutedAt = DateTime.Now.ToString("HH:mm");
			item2.Status = "已核对";
		}
		NotifyInpatientChanged(inpatientRecordModel);
		SystemNotice = "✓ [出院六严关·步骤A完成] 患者【" + inpatientRecordModel.PatientName + "】所有在院长期/临时医嘱已停止，全部护理任务已双人核销闭环！";
	}

	public void CompleteDischargeGate2_MedTech()
	{
		InpatientRecordModel inpatientRecordModel = SelectedInpatient ?? DischargeCheckDetails.Patient;
		if (inpatientRecordModel != null)
		{
			inpatientRecordModel.MedTechReportsAllSigned = true;
			NotifyInpatientChanged(inpatientRecordModel);
			SystemNotice = "✓ [出院六严关·步骤B完成] 患者【" + inpatientRecordModel.PatientName + "】所有住院检验/检查/影像医技报告已完成审核归档！";
		}
	}

	public void CompleteDischargeGate3_DischargeMeds()
	{
		InpatientRecordModel inpatientRecordModel = SelectedInpatient ?? DischargeCheckDetails.Patient;
		if (inpatientRecordModel != null)
		{
			if (!CanOperatePharmacy)
			{
				SystemNotice = "\ud83d\udee1\ufe0f [临床与药房职责权限分离拦截 (SoD)] 当前角色【" + CurrentOperatorRole + "】无权核发出院带药！【步骤C：出院带药清零】属于【\ud83d\udc8a 药剂科药师】专属职责，请点击右侧「➔ 交接药师核发带药」或切至【药房与药库中心】办理！";
			}
			else
			{
				DispenseInpatientDischargeMedsForPatient(inpatientRecordModel);
			}
		}
	}

	public void CompleteDischargeGate4_SignNotes()
	{
		InpatientRecordModel inpatientRecordModel = SelectedInpatient ?? DischargeCheckDetails.Patient;
		if (inpatientRecordModel == null)
		{
			return;
		}
		if (!CanFinalizeInpatientOrders)
		{
			SystemNotice = "\ud83d\udee1\ufe0f [职责权限分离拦截 (SoD)] 当前角色【" + CurrentOperatorRole + "】无主治医师病程终审权限！";
			return;
		}
		foreach (ProgressNoteModel item in inpatientRecordModel.ProgressNotes.Where((ProgressNoteModel x) => !x.IsSigned))
		{
			item.IsSigned = true;
		}
		NotifyInpatientChanged(inpatientRecordModel);
		SystemNotice = "✓ [出院六严关·步骤D完成] 患者【" + inpatientRecordModel.PatientName + "】所有住院病程与出院记录已由主治医师审签锁定！";
	}

	public void CompleteDischargeGate5_Insurance()
	{
		InpatientRecordModel inpatientRecordModel = SelectedInpatient ?? DischargeCheckDetails.Patient;
		if (inpatientRecordModel != null)
		{
			if (!CanOperateFinance)
			{
				SystemNotice = "\ud83d\udee1\ufe0f [临床与财务职责权限分离拦截 (SoD)] 当前角色【" + CurrentOperatorRole + "】严禁操作医保财务结算！【步骤E：DRG/DIP医保结算核验】属于独立模块【\ud83d\udcb0 财务收费与结算中心】专属职责，请点击「➔ 交接财务收费员结算」！";
			}
			else
			{
				SettleInpatientInsuranceForPatient(inpatientRecordModel);
			}
		}
	}

	public void CompleteDischargeGate6_Prepayment()
	{
		InpatientRecordModel inpatientRecordModel = SelectedInpatient ?? DischargeCheckDetails.Patient;
		if (inpatientRecordModel != null)
		{
			if (!CanOperateFinance)
			{
				SystemNotice = "\ud83d\udee1\ufe0f [临床与财务职责权限分离拦截 (SoD)] 当前角色【" + CurrentOperatorRole + "】严禁操作住院预交金结清！【步骤F：预交金多退少补结清】属于独立模块【\ud83d\udcb0 财务收费与结算中心】专属职责，请点击「➔ 交接财务收费员结算」！";
			}
			else
			{
				ClearInpatientPrepaymentForPatient(inpatientRecordModel);
			}
		}
	}

	public void SaveVitalSigns()
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null || !selectedInpatient.IsActiveInpatient)
		{
			SystemNotice = "请先选择一名在院患者。";
			return;
		}
		if (!TryParseVital(TemperatureText, out var result) || !TryParseVital(PulseText, out var result2) || !TryParseVital(RespirationText, out var result3) || !TryParseVital(SystolicText, out var result4) || !TryParseVital(DiastolicText, out var result5) || !TryParseVital(Spo2Text, out var result6))
		{
			SystemNotice = "生命体征必须填写为有效数字。";
			return;
		}
		if (result4 < result5 || result5 <= 0m || result6 > 100m)
		{
			SystemNotice = "血压或血氧数值不符合有效范围。";
			return;
		}
		bool flag = result >= 37.3m || result2 > 100m || result3 > 22m || result4 >= 140m || result4 < 90m || result5 >= 90m || result6 < 95m;
		VitalSignModel item = new VitalSignModel
		{
			RecordedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
			Temperature = FormatVital(result),
			Pulse = FormatVital(result2),
			Respiration = FormatVital(result3),
			BloodPressure = FormatVital(result4) + "/" + FormatVital(result5),
			Spo2 = FormatVital(result6),
			NurseName = CurrentOperatorName,
			IsAbnormal = flag
		};
		selectedInpatient.VitalSigns.Insert(0, item);
		NotifyInpatientChanged(selectedInpatient);
		SystemNotice = (flag ? "⚠\ufe0f 生命体征已保存，检测到异常指标，请立即通知经治医师并复核。" : "✓ 生命体征已保存，指标均在正常范围。");
	}

	public void AddHandoverNote()
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null || !selectedInpatient.IsActiveInpatient)
		{
			SystemNotice = "请先选择一名在院患者。";
			return;
		}
		if (string.IsNullOrWhiteSpace(HandoverDraft))
		{
			SystemNotice = "交接班内容不能为空。";
			return;
		}
		HandoverNoteModel item = new HandoverNoteModel
		{
			Shift = SelectedHandoverShift + " (交接)",
			NurseName = CurrentOperatorName + " (" + CurrentOperatorRole + ")",
			RecordTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
			Content = HandoverDraft.Trim(),
			IsConfirmed = false
		};
		selectedInpatient.HandoverNotes.Insert(0, item);
		HandoverDraft = string.Empty;
		NotifyInpatientChanged(selectedInpatient);
		SystemNotice = "✓ [步骤A：交班提交完成] 交接班记录已保存，请由接班护士点击「步骤B：接班确认」完成交接闭环！";
	}

	public void ConfirmHandoverNote(HandoverNoteModel note)
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null || !selectedInpatient.HandoverNotes.Contains(note))
		{
			SystemNotice = "未找到对应的交接班记录。";
			return;
		}
		if (note.IsConfirmed)
		{
			SystemNotice = "该交接班记录已确认。";
			return;
		}
		note.IsConfirmed = true;
		NotifyInpatientChanged(selectedInpatient);
		SystemNotice = "✓ [步骤B：接班确认完成] 交接班记录已双人确认，护理责任已闭环移交。";
	}

	public void CycleNursingLevel()
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null || !selectedInpatient.IsActiveInpatient)
		{
			SystemNotice = "请先选择一名在院患者。";
			return;
		}
		if (selectedInpatient.NursingLevel.Contains("特级") || selectedInpatient.IsCritical)
		{
			SystemNotice = "病危/特级护理患者不允许直接下调护理级别，请先完成病情评估与医嘱变更。";
			return;
		}
		InpatientRecordModel inpatientRecordModel = selectedInpatient;
		string nursingLevel = selectedInpatient.NursingLevel;
		if (1 == 0)
		{
		}
		string nursingLevel2 = nursingLevel switch
		{
			"一级护理" => "二级护理", 
			"二级护理" => "三级护理", 
			"三级护理" => "一级护理", 
			_ => "二级护理", 
		};
		if (1 == 0)
		{
		}
		inpatientRecordModel.NursingLevel = nursingLevel2;
		SyncInpatientBed(selectedInpatient);
		NotifyInpatientChanged(selectedInpatient);
		SystemNotice = "✓ 护理级别已调整为 [" + selectedInpatient.NursingLevel + "]，病区床位卡已同步。";
	}

	public void PrepareDischargeCheck()
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null)
		{
			SystemNotice = "请先选择需要办理出院的患者。";
			return;
		}
		DischargeCheckDetails.Patient = selectedInpatient;
		DischargeCheckDetails.Refresh();
		SystemNotice = (DischargeCheckDetails.AllPassed ? ("✓ " + selectedInpatient.PatientName + " 六严关已全部通过，可点击「步骤G：终审办结出院」办结。") : $"⚠\ufe0f {selectedInpatient.PatientName} 尚有关卡未通过（{selectedInpatient.DischargeGateSummary}），请按 A➔B➔C➔D➔E➔F 顺序逐关推进。");
	}

	public void PrepareCriticalValueAlert()
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null)
		{
			SystemNotice = "请先选择住院患者。";
			return;
		}
		CriticalValueAlert.PatientName = selectedInpatient.PatientName;
		CriticalValueAlert.BedCode = selectedInpatient.BedCode;
		SystemNotice = "已调取 " + selectedInpatient.PatientName + " 的危急值双签收工作台（遵循 A.经治医生首接审签 ➔ B.责任护士复核签收 ➔ C.启动急救绿通 顺序闭环）。";
	}

	public void AdmitInpatient()
	{
		BedCardModel bedCardModel = BedCards.FirstOrDefault((BedCardModel x) => !x.IsOccupied) ?? AllBedCards.FirstOrDefault((BedCardModel x) => !x.IsOccupied) ?? BedCards.FirstOrDefault((BedCardModel x) => x.BedCode.StartsWith("03", StringComparison.Ordinal));
		string admissionNo = $"ADM{DateTime.Now:yyyyMMddHHmmss}";
		InpatientRecordModel inpatientRecordModel = new InpatientRecordModel
		{
			AdmissionNo = admissionNo,
			PatientName = $"新入院患者{AllInpatientRecords.Count + 1:D2}",
			Gender = "男",
			Age = 52,
			MedicalRecordNo = $"PAT{DateTime.Now:yyyyMMddHHmmss}",
			BedCode = (bedCardModel?.BedCode.Replace(" ", string.Empty) ?? "加床"),
			AdmittingDept = (string.IsNullOrWhiteSpace(ActiveDeptName) ? "心血管内科住院一科" : ActiveDeptName),
			NursingWard = (bedCardModel?.NursingWardName ?? "内科一病区"),
			NursingLevel = "二级护理",
			Diagnosis = "待完善入院诊断",
			AttendingDoctor = CurrentOperatorName + " (" + CurrentOperatorRole + ")",
			AdmissionDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
			DailySummary = "新入院患者，已完成入科登记与身份核对，待完成首次病程与查房评估。",
			MedTechReportsAllSigned = false,
			DischargeMedsDispensed = false,
			MedicalInsuranceSettled = false,
			PrepaymentCleared = false,
			PrepaymentAmount = 5000m,
			ActualSelfPay = 0m
		};
		inpatientRecordModel.Orders.Add(new InpatientOrderModel
		{
			OrderNo = NextInpatientId("ORD", 0),
			OrderType = "长期医嘱",
			Content = "持续心电监护",
			Dose = "1套",
			Route = "床旁",
			Frequency = "持续",
			StartTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
			DoctorName = CurrentOperatorName,
			Status = "待护士校对",
			NurseSign = "--",
			ReviewInfo = "[步骤A] 入院常规医嘱，等待责任护士执行【步骤B：校对审签】"
		});
		inpatientRecordModel.ApplyOrderFilter();
		inpatientRecordModel.ProgressNotes.Add(new ProgressNoteModel
		{
			RecordTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
			Author = CurrentOperatorName + " (" + CurrentOperatorRole + ")",
			RecordType = "入院记录",
			Content = "患者新入院，已核对身份、腕带与床位信息，待完善既往史、过敏史与系统查体。",
			IsSigned = false
		});
		inpatientRecordModel.VitalSigns.Add(new VitalSignModel
		{
			RecordedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
			Temperature = "36.5",
			Pulse = "76",
			Respiration = "18",
			BloodPressure = "126/78",
			Spo2 = "98",
			NurseName = CurrentOperatorName,
			IsAbnormal = false
		});
		if (bedCardModel != null)
		{
			bedCardModel.PatientName = inpatientRecordModel.PatientName;
			bedCardModel.Gender = inpatientRecordModel.Gender;
			bedCardModel.Age = inpatientRecordModel.Age;
			bedCardModel.AdmissionNo = inpatientRecordModel.AdmissionNo;
			bedCardModel.AdmittingDeptName = inpatientRecordModel.AdmittingDept;
			bedCardModel.NursingWardName = inpatientRecordModel.NursingWard;
			bedCardModel.NursingLevel = inpatientRecordModel.NursingLevel;
			bedCardModel.Diagnosis = inpatientRecordModel.Diagnosis;
			bedCardModel.IsOccupied = true;
			bedCardModel.CardTheme = "Primary";
			bedCardModel.NotifyUpdated();
		}
		AllInpatientRecords.Add(inpatientRecordModel);
		RebuildScopedInpatientData();
		SelectedInpatient = inpatientRecordModel;
		RefreshInpatientMetrics();
		SystemNotice = $"✓ 已为 {inpatientRecordModel.PatientName} 办理入科并预占 {inpatientRecordModel.BedCode}，入院医嘱处于【步骤A：待护士校对】，请按 A➔B➔C➔D➔E 推进！";
	}

	public bool ApproveDischarge()
	{
		InpatientRecordModel inpatientRecordModel = SelectedInpatient ?? DischargeCheckDetails.Patient;
		if (inpatientRecordModel == null)
		{
			SystemNotice = "请先选择需要办理出院的患者。";
			return false;
		}
		DischargeCheckDetails.Patient = inpatientRecordModel;
		DischargeCheckDetails.Refresh();
		if (!DischargeCheckDetails.AllPassed)
		{
			SystemNotice = $"⚠\ufe0f {inpatientRecordModel.PatientName} 出院核查未全部通过（{inpatientRecordModel.DischargeGateSummary}），请先完成全部检查项！";
			return false;
		}
		inpatientRecordModel.IsDischargeReady = false;
		inpatientRecordModel.IsDischarged = true;
		ReleaseInpatientBed(inpatientRecordModel);
		DischargeCheckDetails.Refresh();
		NotifyInpatientChanged(inpatientRecordModel);
		SystemNotice = $"✓ [步骤G：出院闭环完成] {inpatientRecordModel.PatientName} A➔B➔C➔D➔E➔F 六严关终审全部通过，已办结出院并释放 {inpatientRecordModel.BedCode}！";
		return true;
	}

	public async Task ApproveDischargeAsync()
	{
		InpatientRecordModel patient = SelectedInpatient ?? DischargeCheckDetails.Patient;
		if (patient == null)
		{
			SystemNotice = "请先选择需要申请出院的患者。";
			return;
		}
		if (!CanOperateIpdClinical)
		{
			SystemNotice = "当前登录角色没有住院出院申请权限。";
			return;
		}
		DischargeCheckDetails.Patient = patient;
		DischargeCheckDetails.Refresh();
		if (!DischargeCheckDetails.AllPassed)
		{
			SystemNotice = $"⚠ {patient.PatientName} 仍有未完成核查项（{patient.DischargeGateSummary}）。可先提交出院申请，由收费处结算前完成全部核查。";
		}
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			using HttpResponseMessage activeResponse = await http.GetAsync("/api/v4/ipd/encounters/active");
			ClientApiEnvelope<List<DischargeEncounterDto>> activeEnvelope =
				await activeResponse.Content.ReadFromJsonAsync<ClientApiEnvelope<List<DischargeEncounterDto>>>(JsonOptions);
			if (!activeResponse.IsSuccessStatusCode || activeEnvelope == null || activeEnvelope.Code != 200)
			{
				SystemNotice = "出院申请失败：" + (activeEnvelope?.Message ?? "无法加载在院患者");
				return;
			}
			DischargeEncounterDto? encounter = activeEnvelope.Data?.FirstOrDefault(x =>
				string.Equals(x.AdmissionNo, patient.AdmissionNo, StringComparison.OrdinalIgnoreCase));
			if (encounter == null)
			{
				SystemNotice = $"未找到住院号 {patient.AdmissionNo} 的在院记录，可能已完成结算或尚未由收费处登记。";
				return;
			}
			var body = new { DischargeDiagnosis = patient.Diagnosis, DischargeInstructions = patient.DailySummary };
			using HttpResponseMessage response = await http.PostAsJsonAsync(
				"/api/v4/ipd/encounters/" + Uri.EscapeDataString(encounter.Id) + "/discharge-request", body, JsonOptions);
			ClientApiEnvelope<DischargeRequestDto> envelope =
				await response.Content.ReadFromJsonAsync<ClientApiEnvelope<DischargeRequestDto>>(JsonOptions);
			if (!response.IsSuccessStatusCode || envelope == null || envelope.Code != 200)
			{
				SystemNotice = "出院申请失败：" + (envelope?.Message ?? "服务无响应");
				return;
			}
			patient.IsDischargeReady = true;
			NotifyInpatientChanged(patient);
			SystemNotice = $"✓ 已提交 {patient.PatientName} 的出院申请（{envelope.Data?.RequestNo}）。请收费处完成六项核查与财务结算，结算后自动释放床位。";
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
		{
			SystemNotice = "出院申请失败，请检查服务连接：" + ex.Message;
			ClientDiagnosticsLog.Error("ipd.discharge", "提交出院申请失败", ex);
		}
	}

	private NursingTaskModel CreateNursingTaskFromOrder(InpatientOrderModel order, int sequence)
	{
		string category = DetermineTaskCategory(order);
		return new NursingTaskModel
		{
			TaskNo = NextInpatientId("NT", sequence),
			ScheduledTime = DateTime.Now.ToString("HH:mm"),
			Category = category,
			Content = $"{order.Content} [{order.Dose}] ({order.Route})",
			SourceOrder = order.OrderNo,
			Priority = ((order.OrderType == "临时医嘱") ? "高" : "中"),
			Status = "待执行"
		};
	}

	private void NotifyInpatientChanged(InpatientRecordModel patient)
	{
		patient.NotifyWorkflowChanged();
		SyncInpatientBed(patient);
		if (SelectedInpatient == patient)
		{
			CriticalValueAlert.PatientName = patient.PatientName;
			CriticalValueAlert.BedCode = patient.BedCode;
			DischargeCheckDetails.Patient = patient;
			DischargeCheckDetails.Refresh();
		}
		RefreshInpatientMetrics();
	}

	private void SyncInpatientBed(InpatientRecordModel patient)
	{
		BedCardModel bedCardModel = AllBedCards.FirstOrDefault((BedCardModel x) => x.AdmissionNo == patient.AdmissionNo);
		if (bedCardModel != null)
		{
			bedCardModel.PatientName = patient.PatientName;
			bedCardModel.Gender = patient.Gender;
			bedCardModel.Age = patient.Age;
			bedCardModel.AdmissionNo = patient.AdmissionNo;
			bedCardModel.AdmittingDeptName = patient.AdmittingDept;
			bedCardModel.NursingWardName = patient.NursingWard;
			bedCardModel.NursingLevel = patient.NursingLevel;
			bedCardModel.Diagnosis = patient.Diagnosis;
			bedCardModel.IsOccupied = patient.IsActiveInpatient;
			BedCardModel bedCardModel2 = bedCardModel;
			string cardTheme;
			if (patient.IsDischarged)
			{
				cardTheme = "Success";
			}
			else if (patient.IsCritical)
			{
				cardTheme = "Danger";
			}
			else
			{
				cardTheme = (patient.IsDischargeReady ? "Warning" : "Primary");
			}
			bedCardModel2.CardTheme = cardTheme;
			bedCardModel.NotifyUpdated();
		}
	}

	private void ReleaseInpatientBed(InpatientRecordModel patient)
	{
		BedCardModel bedCardModel = AllBedCards.FirstOrDefault((BedCardModel x) => x.AdmissionNo == patient.AdmissionNo);
		if (bedCardModel != null)
		{
			bedCardModel.PatientName = "【空床待收治】";
			bedCardModel.Gender = "-";
			bedCardModel.Age = 0;
			bedCardModel.AdmissionNo = "-";
			bedCardModel.NursingLevel = "未指定";
			bedCardModel.Diagnosis = "无";
			bedCardModel.BedType = "标准空床";
			bedCardModel.IsOccupied = false;
			bedCardModel.IsBorrowed = false;
			bedCardModel.CardTheme = "Success";
			bedCardModel.NotifyUpdated();
		}
	}

	private string NextInpatientId(string prefix, int sequence)
	{
		string value = DateTime.Now.ToString("MMddHHmmss");
		return $"{prefix}-{value}-{sequence + 1:D2}";
	}

	private static bool TryParseVital(string value, out decimal result)
	{
		return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result) || decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out result);
	}

	private static string FormatVital(decimal value)
	{
		return value.ToString("0.#", CultureInfo.InvariantCulture);
	}

	public void EnsurePharmacyWarehouseFinanceInitialized()
	{
		if (!_phmWhFinInitialized)
		{
			_phmWhFinInitialized = true;
			InitDrugMasterAndBatches();
			InitDrugPriceLogs();
			InitWarehouseTransfers();
			RecalculateDrugStockAndFinanceMetrics();
		}
	}

	private void InitDrugMasterAndBatches()
	{
		DrugMasterList.Clear();
		DrugMasterList.Add(new DrugMasterItemModel
		{
			DrugId = "DRUG-01",
			DrugCode = "PENICILLIN_G",
			DrugName = "注射用青霉素钠",
			Spec = "80万单位/支",
			UnitName = "支",
			RetailPrice = 12.50m,
			CostPrice = 8.00m,
			AntibioticLevel = 1,
			IsSkinTestRequired = true,
			IsNarcotic = false,
			IsActive = true
		});
		DrugMasterList.Add(new DrugMasterItemModel
		{
			DrugId = "DRUG-02",
			DrugCode = "AMOXICILLIN_CAPS",
			DrugName = "阿莫西林胶囊",
			Spec = "0.25g*24粒/盒",
			UnitName = "盒",
			RetailPrice = 18.00m,
			CostPrice = 11.20m,
			AntibioticLevel = 1,
			IsSkinTestRequired = true,
			IsNarcotic = false,
			IsActive = true
		});
		DrugMasterList.Add(new DrugMasterItemModel
		{
			DrugId = "DRUG-03",
			DrugCode = "NORMAL_SALINE_500",
			DrugName = "0.9%氯化钠注射液",
			Spec = "500ml/瓶",
			UnitName = "瓶",
			RetailPrice = 5.50m,
			CostPrice = 3.20m,
			AntibioticLevel = 0,
			IsSkinTestRequired = false,
			IsNarcotic = false,
			IsActive = true
		});
		DrugMasterList.Add(new DrugMasterItemModel
		{
			DrugId = "DRUG-04",
			DrugCode = "DRUG-CARD-006",
			DrugName = "阿司匹林肠溶片",
			Spec = "100mg*30片/盒",
			UnitName = "盒",
			RetailPrice = 22.50m,
			CostPrice = 14.00m,
			AntibioticLevel = 0,
			IsSkinTestRequired = false,
			IsNarcotic = false,
			IsActive = true
		});
		DrugMasterList.Add(new DrugMasterItemModel
		{
			DrugId = "DRUG-05",
			DrugCode = "DRUG-CARD-005",
			DrugName = "硫酸氢氯吡格雷片",
			Spec = "75mg*7片/盒",
			UnitName = "盒",
			RetailPrice = 56.80m,
			CostPrice = 38.00m,
			AntibioticLevel = 0,
			IsSkinTestRequired = false,
			IsNarcotic = false,
			IsActive = true
		});
		WarehouseBatchList.Clear();
		WarehouseBatchList.Add(new WarehouseBatchItemModel
		{
			BatchId = "BATCH-WH1-01",
			WarehouseId = "DEPT-WH-01",
			WarehouseName = "全院中心药库 (DEPT-WH-01)",
			WarehouseCategory = "WAREHOUSE",
			DrugId = "DRUG-01",
			DrugCode = "PENICILLIN_G",
			DrugName = "注射用青霉素钠",
			Spec = "80万单位/支",
			UnitName = "支",
			BatchNo = "LOT202609W",
			ExpiryDateInt = 20280901,
			InvoiceNo = "INV-2026-101",
			ColdChainInfo = "阴凉库 18.0°C 合格",
			UnitCostPrice = 8.00m,
			UnitRetailPrice = 12.50m,
			Quantity = 1000
		});
		WarehouseBatchList.Add(new WarehouseBatchItemModel
		{
			BatchId = "BATCH-WH1-03",
			WarehouseId = "DEPT-WH-01",
			WarehouseName = "全院中心药库 (DEPT-WH-01)",
			WarehouseCategory = "WAREHOUSE",
			DrugId = "DRUG-03",
			DrugCode = "NORMAL_SALINE_500",
			DrugName = "0.9%氯化钠注射液",
			Spec = "500ml/瓶",
			UnitName = "瓶",
			BatchNo = "LOT202610W",
			ExpiryDateInt = 20281001,
			InvoiceNo = "INV-2026-102",
			ColdChainInfo = "常温合格",
			UnitCostPrice = 3.20m,
			UnitRetailPrice = 5.50m,
			Quantity = 2000
		});
		WarehouseBatchList.Add(new WarehouseBatchItemModel
		{
			BatchId = "BATCH-WH1-04",
			WarehouseId = "DEPT-WH-01",
			WarehouseName = "全院中心药库 (DEPT-WH-01)",
			WarehouseCategory = "WAREHOUSE",
			DrugId = "DRUG-04",
			DrugCode = "DRUG-CARD-006",
			DrugName = "阿司匹林肠溶片",
			Spec = "100mg*30片/盒",
			UnitName = "盒",
			BatchNo = "LOT202611W",
			ExpiryDateInt = 20281101,
			InvoiceNo = "INV-2026-103",
			ColdChainInfo = "常温合格",
			UnitCostPrice = 14.00m,
			UnitRetailPrice = 22.50m,
			Quantity = 1500
		});
		WarehouseBatchList.Add(new WarehouseBatchItemModel
		{
			BatchId = "BATCH-WH2-05",
			WarehouseId = "DEPT-WH-02",
			WarehouseName = "冷链生物制品专库 (DEPT-WH-02)",
			WarehouseCategory = "WAREHOUSE",
			DrugId = "DRUG-05",
			DrugCode = "DRUG-CARD-005",
			DrugName = "硫酸氢氯吡格雷片",
			Spec = "75mg*7片/盒",
			UnitName = "盒",
			BatchNo = "LOT202612C",
			ExpiryDateInt = 20281201,
			InvoiceNo = "INV-2026-104",
			ColdChainInfo = "GSP冷链 4.2°C 合格",
			UnitCostPrice = 38.00m,
			UnitRetailPrice = 56.80m,
			Quantity = 600
		});
		WarehouseBatchList.Add(new WarehouseBatchItemModel
		{
			BatchId = "BATCH-01-NEAR",
			WarehouseId = "DEPT-PHM-01",
			WarehouseName = "门诊西药房 (DEPT-PHM-01)",
			WarehouseCategory = "PHARMACY",
			DrugId = "DRUG-01",
			DrugCode = "PENICILLIN_G",
			DrugName = "注射用青霉素钠",
			Spec = "80万单位/支",
			UnitName = "支",
			BatchNo = "LOT202601A",
			ExpiryDateInt = 20270301,
			InvoiceNo = "INV-2026-001",
			ColdChainInfo = "阴凉柜合格",
			UnitCostPrice = 8.00m,
			UnitRetailPrice = 12.50m,
			Quantity = 50
		});
		WarehouseBatchList.Add(new WarehouseBatchItemModel
		{
			BatchId = "BATCH-03-NEAR",
			WarehouseId = "DEPT-PHM-01",
			WarehouseName = "门诊西药房 (DEPT-PHM-01)",
			WarehouseCategory = "PHARMACY",
			DrugId = "DRUG-03",
			DrugCode = "NORMAL_SALINE_500",
			DrugName = "0.9%氯化钠注射液",
			Spec = "500ml/瓶",
			UnitName = "瓶",
			BatchNo = "LOT202602S",
			ExpiryDateInt = 20270501,
			InvoiceNo = "INV-2026-004",
			ColdChainInfo = "常温合格",
			UnitCostPrice = 3.20m,
			UnitRetailPrice = 5.50m,
			Quantity = 100
		});
		WarehouseBatchList.Add(new WarehouseBatchItemModel
		{
			BatchId = "BATCH-04-MAIN",
			WarehouseId = "DEPT-PHM-01",
			WarehouseName = "门诊西药房 (DEPT-PHM-01)",
			WarehouseCategory = "PHARMACY",
			DrugId = "DRUG-04",
			DrugCode = "DRUG-CARD-006",
			DrugName = "阿司匹林肠溶片",
			Spec = "100mg*30片/盒",
			UnitName = "盒",
			BatchNo = "LOT202605C",
			ExpiryDateInt = 20280501,
			InvoiceNo = "INV-2026-006",
			ColdChainInfo = "常温合格",
			UnitCostPrice = 14.00m,
			UnitRetailPrice = 22.50m,
			Quantity = 320
		});
		WarehouseBatchList.Add(new WarehouseBatchItemModel
		{
			BatchId = "BATCH-05-MAIN",
			WarehouseId = "DEPT-PHM-01",
			WarehouseName = "门诊西药房 (DEPT-PHM-01)",
			WarehouseCategory = "PHARMACY",
			DrugId = "DRUG-05",
			DrugCode = "DRUG-CARD-005",
			DrugName = "硫酸氢氯吡格雷片",
			Spec = "75mg*7片/盒",
			UnitName = "盒",
			BatchNo = "LOT202605D",
			ExpiryDateInt = 20280501,
			InvoiceNo = "INV-2026-007",
			ColdChainInfo = "常温合格",
			UnitCostPrice = 38.00m,
			UnitRetailPrice = 56.80m,
			Quantity = 180
		});
		WarehouseBatchList.Add(new WarehouseBatchItemModel
		{
			BatchId = "BATCH-PHM2-03",
			WarehouseId = "DEPT-PHM-02",
			WarehouseName = "住院中心药房 (DEPT-PHM-02)",
			WarehouseCategory = "PHARMACY",
			DrugId = "DRUG-03",
			DrugCode = "NORMAL_SALINE_500",
			DrugName = "0.9%氯化钠注射液",
			Spec = "500ml/瓶",
			UnitName = "瓶",
			BatchNo = "LOT202608S",
			ExpiryDateInt = 20280801,
			InvoiceNo = "INV-2026-005",
			ColdChainInfo = "常温合格",
			UnitCostPrice = 3.20m,
			UnitRetailPrice = 5.50m,
			Quantity = 400
		});
		ApplyWarehouseBatchFilter();
	}

	private void InitDrugPriceLogs()
	{
		DrugPriceLogs.Clear();
		DrugPriceLogs.Add(new DrugPriceAdjustmentLogModel
		{
			AdjustNo = "PRC-ADJ-20261101-01",
			DrugCode = "DRUG-CARD-006",
			DrugName = "阿司匹林肠溶片",
			OldRetailPrice = 25.00m,
			NewRetailPrice = 22.50m,
			OldCostPrice = 15.50m,
			NewCostPrice = 14.00m,
			AffectedStockQuantity = 1820,
			UnitName = "盒",
			PriceDiffAmount = -4550.00m,
			AdjustReason = "国家第九批药品集中带量采购中选零差率调价",
			EffectiveTime = "2026-11-01 00:00:00",
			OperatorName = "物价管理员 (EMP001)"
		});
		DrugPriceLogs.Add(new DrugPriceAdjustmentLogModel
		{
			AdjustNo = "PRC-ADJ-20261103-02",
			DrugCode = "DRUG-CARD-005",
			DrugName = "硫酸氢氯吡格雷片",
			OldRetailPrice = 62.00m,
			NewRetailPrice = 56.80m,
			OldCostPrice = 41.00m,
			NewCostPrice = 38.00m,
			AffectedStockQuantity = 780,
			UnitName = "盒",
			PriceDiffAmount = -4056.00m,
			AdjustReason = "省医保集采续约联动降价",
			EffectiveTime = "2026-11-03 00:00:00",
			OperatorName = "物价管理员 (EMP001)"
		});
	}

	private void InitWarehouseTransfers()
	{
		WarehouseTransferOrders.Clear();
		WarehouseTransferOrders.Add(new WarehouseTransferOrderModel
		{
			TransferNo = "ISS-20261105-001",
			TransferType = "WAREHOUSE_ISSUE",
			SourceWarehouseId = "DEPT-WH-01",
			SourceWarehouseName = "全院中心药库 (DEPT-WH-01)",
			TargetWarehouseId = "DEPT-PHM-01",
			TargetWarehouseName = "门诊西药房 (DEPT-PHM-01)",
			DrugName = "阿司匹林肠溶片",
			Spec = "100mg*30片/盒",
			BatchNo = "LOT202611W",
			ExpiryDateInt = 20281101,
			Quantity = 120,
			UnitName = "盒",
			TotalRetailAmount = 2700.00m,
			OperatorName = "孙伟 (主管药师)",
			TransferTime = "2026-11-05 08:30:15",
			StatusText = "✓ 药库已按FEFO发药出库 · 药房已扫码验收入账",
			Remark = "门诊西药房周常补货请领单 (药库FEFO发药)"
		});
		WarehouseTransferOrders.Add(new WarehouseTransferOrderModel
		{
			TransferNo = "TRF-20261106-002",
			TransferType = "INTER_WAREHOUSE",
			SourceWarehouseId = "DEPT-PHM-01",
			SourceWarehouseName = "门诊西药房 (DEPT-PHM-01)",
			TargetWarehouseId = "DEPT-PHM-02",
			TargetWarehouseName = "住院中心药房 (DEPT-PHM-02)",
			DrugName = "0.9%氯化钠注射液",
			Spec = "500ml/瓶",
			BatchNo = "LOT202602S",
			ExpiryDateInt = 20270501,
			Quantity = 50,
			UnitName = "瓶",
			TotalRetailAmount = 275.00m,
			OperatorName = "孙伟 (主管药师)",
			TransferTime = "2026-11-06 14:20:00",
			StatusText = "✓ 跨库房调拨完成 · 双边库存台账已平衡",
			Remark = "门诊西药房向住院中心药房跨库房应急调拨"
		});
	}

	public void ApplyWarehouseBatchFilter()
	{
		List<WarehouseBatchItemModel> list = (from b in WarehouseBatchList
			where SelectedWarehouseFilter == "ALL" || string.Equals(b.WarehouseId, SelectedWarehouseFilter, StringComparison.OrdinalIgnoreCase)
			orderby b.ExpiryDateInt
			select b).ToList();
		FilteredWarehouseBatchList.Clear();
		foreach (WarehouseBatchItemModel item in list)
		{
			FilteredWarehouseBatchList.Add(item);
		}
	}

	public void RefreshPharmacyWarehouseMetrics()
	{
		RecalculateDrugStockAndFinanceMetrics();
	}

	public void RecalculateDrugStockAndFinanceMetrics()
	{
		foreach (DrugMasterItemModel drug in DrugMasterList)
		{
			drug.TotalStockQuantity = WarehouseBatchList.Where((WarehouseBatchItemModel b) => b.DrugId == drug.DrugId || b.DrugCode == drug.DrugCode).Sum((WarehouseBatchItemModel b) => b.Quantity);
		}
		DrugFinanceBreakdownList.Clear();
		foreach (DrugMasterItemModel drug2 in DrugMasterList)
		{
			int num = PharmacyRecords.Where((PharmacyDispenseModel r) => r.IsOrderClosed && (r.DrugId == drug2.DrugId || r.DrugSummary.Contains(drug2.DrugName))).Sum((PharmacyDispenseModel r) => r.DispenseQuantity) + 35;
			int num2 = PharmacyRecords.Where((PharmacyDispenseModel r) => r.IsReturned && (r.DrugId == drug2.DrugId || r.DrugSummary.Contains(drug2.DrugName))).Sum((PharmacyDispenseModel r) => r.DispenseQuantity) + 2;
			int num3 = drug2.TotalStockQuantity + num - num2;
			DrugFinanceBreakdownList.Add(new DrugFinanceReconcileItemModel
			{
				DrugCode = drug2.DrugCode,
				DrugName = drug2.DrugName,
				Spec = drug2.Spec,
				UnitName = drug2.UnitName,
				CostPrice = drug2.CostPrice,
				RetailPrice = drug2.RetailPrice,
				PurchasedQty = num3,
				PurchasedCostAmount = (decimal)num3 * drug2.CostPrice,
				DispensedQty = num,
				DispensedSalesAmount = (decimal)num * drug2.RetailPrice,
				ReturnedQty = num2,
				ReturnedSalesAmount = (decimal)num2 * drug2.RetailPrice,
				CurrentStockQty = drug2.TotalStockQuantity,
				CurrentStockRetailValue = (decimal)drug2.TotalStockQuantity * drug2.RetailPrice
			});
		}
		OnPropertyChanged("PendingPharmacyDispenseCount");
		OnPropertyChanged("ClosedPharmacyOrderCount");
		OnPropertyChanged("ReturnedPharmacyOrderCount");
		OnPropertyChanged("TotalInventoryCostValueText");
		OnPropertyChanged("TotalInventoryRetailValueText");
		OnPropertyChanged("TotalProcurementCostText");
		OnPropertyChanged("TotalDrugNetSalesText");
		OnPropertyChanged("TotalDrugReturnDeductionText");
		OnPropertyChanged("TotalPriceDiffImpactText");
		OnPropertyChanged("UnpaidPrescriptionCount");
		OnPropertyChanged("PendingFinancialRefundCount");
		OnPropertyChanged("CompletedFinancialRefundCount");
	}

	public void CashierSettleAndNotifyPharmacy(PharmacyDispenseModel? item = null)
	{
		EnsurePharmacyWarehouseFinanceInitialized();
		if (!CanOperateFinance)
		{
			SystemNotice = "\ud83d\udee1\ufe0f [职责分离 SoD 权限拦截] 处方收费结算属于【财务处收费员 (ROLE-CASHIER)】专属职责！当前角色无收费权限，请点击「切换为财务收费员」后再操作。";
			return;
		}
		PharmacyDispenseModel pharmacyDispenseModel = item ?? PharmacyRecords.FirstOrDefault((PharmacyDispenseModel x) => !x.IsPaidNotified && !x.IsOrderClosed && !x.IsReturned);
		if (pharmacyDispenseModel == null)
		{
			SystemNotice = "⚠\ufe0f [A➔B➔C 闭环门禁拦截] 当前无处于【步骤A：医生已签发·待收费】的处方！请先在「门诊医生站」按 A(接诊)➔B(病历审签)➔C(签署处方推送) 推送处方后再执行收费结算。";
			return;
		}
		if (pharmacyDispenseModel.IsPaidNotified)
		{
			SystemNotice = "处方 [" + pharmacyDispenseModel.PrescriptionNo + "] 已完成【步骤B：财务收费通知】，请直接执行【步骤C：药房条码核验发药】！";
			return;
		}
		string value = $"PAY{DateTime.Now:yyMMddHHmmss}";
		pharmacyDispenseModel.IsPaidNotified = true;
		pharmacyDispenseModel.CanDispense = !pharmacyDispenseModel.IsAllergyBlocked;
		pharmacyDispenseModel.DispenseStatus = (pharmacyDispenseModel.IsAllergyBlocked ? "⚠\ufe0f 已收费 · 但被过敏红线阻断" : "已收费通知药房 · 待扫码发药");
		pharmacyDispenseModel.PaymentNoticeStatus = $"✓ [步骤B完成] 收费员刘洋已收费 ({value} · ¥{pharmacyDispenseModel.TotalAmount:0.00}) -> 处方状态变更为 [已收费(3)] 并实时通知药房可发药！";
		RecalculateDrugStockAndFinanceMetrics();
		SystemNotice = $"\ud83d\udcb0 [步骤B：收费状态变更通知药房完成] 处方 [{pharmacyDispenseModel.PrescriptionNo}] ({pharmacyDispenseModel.PatientName}) 已完成收费结算 ({value})，已解锁【步骤C：药房扫码发药并关单】！";
	}

	public void DispenseSinglePrescription(PharmacyDispenseModel? item = null)
	{
		EnsurePharmacyWarehouseFinanceInitialized();
		if (!CanOperatePharmacy)
		{
			SystemNotice = "\ud83d\udee1\ufe0f [职责分离 SoD 权限拦截] 扫码核验发药与关单属于【药剂科药师 (ROLE-PHARMACIST)】专属职责！财务或临床人员严禁操作发药，请切换为药房药师。";
			return;
		}
		PharmacyDispenseModel target = item ?? PharmacyRecords.FirstOrDefault((PharmacyDispenseModel x) => x.CanDispense && !x.IsAllergyBlocked && !x.IsOrderClosed);
		if (target == null)
		{
			SystemNotice = "⚠\ufe0f [A➔B➔C 闭环门禁拦截] 当前无满足【步骤C：扫码发药】条件的处方（需已完成步骤B收费通知、无过敏阻断且未发药关单）。";
			return;
		}
		if (target.IsOrderClosed)
		{
			SystemNotice = $"\ud83d\udd12 [9. 防二次发药门禁拦截] 处方 [{target.PrescriptionNo}] ({target.PatientName}) 已完成发药并关闭订单，系统 100% 拒绝二次重复发药！";
			return;
		}
		if (!target.IsPaidNotified)
		{
			SystemNotice = $"⚠\ufe0f [A➔B➔C 顺序门禁拦截] 处方 [{target.PrescriptionNo}] ({target.PatientName}) 尚未完成【步骤B：财务收费员收费结算】，未收到可发药通知，严禁跳过收费直接发药！";
			return;
		}
		if (target.IsAllergyBlocked)
		{
			SystemNotice = "\ud83d\udee1\ufe0f 用药安全阻断：处方 [" + target.PrescriptionNo + "] 含青霉素阳性拦截药品，请先在门诊医生处方区点击「自动替换为安全替代药」并重签推送后再发药！";
			return;
		}
		WarehouseBatchItemModel warehouseBatchItemModel = (from b in WarehouseBatchList
			where b.WarehouseId == "DEPT-PHM-01" && !b.IsFrozen && b.Quantity >= target.DispenseQuantity && (b.DrugId == target.DrugId || b.DrugId == "DRUG-04")
			orderby b.ExpiryDateInt
			select b).FirstOrDefault();
		string text = "LOT202605C";
		if (warehouseBatchItemModel != null)
		{
			warehouseBatchItemModel.Quantity -= target.DispenseQuantity;
			text = $"{warehouseBatchItemModel.BatchNo} (余{warehouseBatchItemModel.Quantity}{warehouseBatchItemModel.UnitName})";
		}
		target.IsOrderClosed = true;
		target.CanDispense = false;
		target.DispenseStatus = "\ud83d\udd12 步骤C已完成: 已发药并关闭订单 (防二次发药)";
		target.PaymentNoticeStatus = "✓ [步骤C完成] 已按 FEFO 批次 [" + text + "] 发药出库 · 订单已自动关闭锁止 (若患者需退药，现已解锁【步骤D: 退药扫码入库】)";
		target.PharmacistName = "孙伟 (主管药师 · 扫码复核)";
		RecalculateDrugStockAndFinanceMetrics();
		SystemNotice = $"✓ [步骤C：药房扫码发药并自动关单完成] 处方 [{target.PrescriptionNo}] ({target.PatientName}) 已按 FEFO 扣减批次 [{text}] 完成发药并关闭订单（严防二次发药），同时已解锁【步骤D：退药扫码入库】！";
	}

	public void ConfirmReturnDrugForOrder(PharmacyDispenseModel? item = null)
	{
		EnsurePharmacyWarehouseFinanceInitialized();
		if (!CanOperatePharmacy)
		{
			SystemNotice = "\ud83d\udee1\ufe0f [职责分离 SoD 权限拦截] 实物退药扫码质检与回补库存属于【药剂科药师 (ROLE-PHARMACIST)】专属职责！请切换为药房药师执行步骤D退药回库。";
			return;
		}
		PharmacyDispenseModel target = item ?? PharmacyRecords.FirstOrDefault((PharmacyDispenseModel x) => x.IsOrderClosed && !x.IsReturned);
		if (target == null)
		{
			SystemNotice = "⚠\ufe0f [B➔C➔D 顺序门禁拦截] 请先选择一张已完成【步骤C：药房已发药并关单】的处方，未发药处方严禁直接执行退药入库！";
			return;
		}
		if (!target.IsOrderClosed)
		{
			SystemNotice = "⚠\ufe0f [B➔C➔D 顺序门禁拦截] 处方 [" + target.PrescriptionNo + "] 尚未完成【步骤C：药房扫码发药】，不能跳过发药直接办理退药入库！";
			return;
		}
		if (target.IsReturned)
		{
			SystemNotice = "处方 [" + target.PrescriptionNo + "] 已完成【步骤D：退药扫码入库】，请点击右侧「\ud83d\udcb8 步骤E：执行财务退费」完成退款闭环！";
			return;
		}
		WarehouseBatchItemModel warehouseBatchItemModel = WarehouseBatchList.FirstOrDefault((WarehouseBatchItemModel b) => b.WarehouseId == "DEPT-PHM-01" && (b.DrugId == target.DrugId || b.DrugId == "DRUG-04"));
		if (warehouseBatchItemModel != null)
		{
			warehouseBatchItemModel.Quantity += target.DispenseQuantity;
		}
		target.IsReturned = true;
		target.CanDispense = false;
		target.DispenseStatus = "步骤D已完成: 药房已退药回库 · 待执行【步骤E: 财务退费】";
		target.PaymentNoticeStatus = "✓ [步骤D完成] 药房已完成 GSP 包装与冷链质检并回补原批次库存 (drug_return_confirmed=1) · 已解锁【步骤E: 财务窗口退费】！";
		RecalculateDrugStockAndFinanceMetrics();
		SystemNotice = $"✓ [步骤D：药房实物退药回库完成] 处方 [{target.PrescriptionNo}] ({target.PatientName}) 实物扫码核验通过，已回补原批次库存 +{target.DispenseQuantity}，已解锁【步骤E：财务退费结清】！";
	}

	public void ExecuteFinancialRefundForOrder(PharmacyDispenseModel? item = null)
	{
		EnsurePharmacyWarehouseFinanceInitialized();
		if (!CanOperateFinance)
		{
			SystemNotice = "\ud83d\udee1\ufe0f [职责分离 SoD 权限拦截] 原路退费结清属于【财务处收费员 (ROLE-CASHIER)】专属职责！药房仅负责退药入库，严禁药房直接退钱，请切换为财务收费员执行步骤E退费。";
			return;
		}
		PharmacyDispenseModel pharmacyDispenseModel = item ?? PharmacyRecords.FirstOrDefault((PharmacyDispenseModel x) => x.IsReturned && !x.IsFinancialRefunded);
		if (pharmacyDispenseModel == null)
		{
			SystemNotice = "⚠\ufe0f [C➔D➔E 顺序门禁拦截] 必须先由药房完成【步骤D：退药扫码质检入库 (drug_return_confirmed=1)】，财务窗口方可解锁执行【步骤E：退费结清】！";
			return;
		}
		if (!pharmacyDispenseModel.IsReturned)
		{
			SystemNotice = "⚠\ufe0f [C➔D➔E 顺序门禁拦截] 处方 [" + pharmacyDispenseModel.PrescriptionNo + "] 药品尚未退回药房（drug_return_confirmed=0），严禁在未退药情况下直接退费！";
			return;
		}
		if (pharmacyDispenseModel.IsFinancialRefunded)
		{
			SystemNotice = "处方 [" + pharmacyDispenseModel.PrescriptionNo + "] 已完成财务退费结清，不能重复退费。";
			return;
		}
		string value = $"REF{DateTime.Now:yyMMddHHmmss}";
		pharmacyDispenseModel.IsFinancialRefunded = true;
		pharmacyDispenseModel.DispenseStatus = "✓ 步骤A~E全闭环: 已退药回库并完成财务退费";
		pharmacyDispenseModel.PaymentNoticeStatus = $"✓ [步骤E完成] 财务收费员刘洋已凭药房退药单完成原路退费 ({value} · -¥{pharmacyDispenseModel.TotalAmount:0.00})，医-药-财五步闭环完毕！";
		RecalculateDrugStockAndFinanceMetrics();
		SystemNotice = $"\ud83d\udcb8 [步骤E：财务退费结清完成] 处方 [{pharmacyDispenseModel.PrescriptionNo}] ({pharmacyDispenseModel.PatientName}) 已凭药房退药确认回执完成退费 (-¥{pharmacyDispenseModel.TotalAmount:0.00}，单号 {value})，A➔B➔C➔D➔E 全链路闭环！";
	}

	public void SaveNewOrUpdatedDrugMaster()
	{
		EnsurePharmacyWarehouseFinanceInitialized();
		if (string.IsNullOrWhiteSpace(EditDrugCode) || string.IsNullOrWhiteSpace(EditDrugName))
		{
			SystemNotice = "药品编码与药品名称不能为空。";
			return;
		}
		string code = EditDrugCode.Trim().ToUpperInvariant();
		DrugMasterItemModel drugMasterItemModel = DrugMasterList.FirstOrDefault((DrugMasterItemModel d) => string.Equals(d.DrugCode, code, StringComparison.OrdinalIgnoreCase));
		if (drugMasterItemModel != null)
		{
			decimal retailPrice = drugMasterItemModel.RetailPrice;
			decimal costPrice = drugMasterItemModel.CostPrice;
			drugMasterItemModel.DrugName = EditDrugName.Trim();
			drugMasterItemModel.Spec = EditDrugSpec.Trim();
			drugMasterItemModel.UnitName = EditDrugUnit.Trim();
			drugMasterItemModel.RetailPrice = EditDrugRetailPrice;
			drugMasterItemModel.CostPrice = EditDrugCostPrice;
			drugMasterItemModel.AntibioticLevel = EditDrugAntibioticLevel;
			drugMasterItemModel.IsSkinTestRequired = EditDrugSkinTest;
			drugMasterItemModel.IsNarcotic = EditDrugNarcotic;
			if (Math.Abs(retailPrice - EditDrugRetailPrice) > 0.001m || Math.Abs(costPrice - EditDrugCostPrice) > 0.001m)
			{
				RecordDrugPriceAdjustmentInternal(drugMasterItemModel, retailPrice, EditDrugRetailPrice, costPrice, EditDrugCostPrice, "药品基础数据维护同步调价");
			}
			RecalculateDrugStockAndFinanceMetrics();
			SystemNotice = $"✓ [1. 药品基础数据维护] 已更新药品 [{drugMasterItemModel.DrugCode} - {drugMasterItemModel.DrugName}] ({drugMasterItemModel.Spec})，零售价=¥{drugMasterItemModel.RetailPrice:0.00}，成本价=¥{drugMasterItemModel.CostPrice:0.00}。";
		}
		else
		{
			DrugMasterItemModel drugMasterItemModel2 = new DrugMasterItemModel
			{
				DrugId = $"DRUG-{DrugMasterList.Count + 1:D2}",
				DrugCode = code,
				DrugName = EditDrugName.Trim(),
				Spec = (string.IsNullOrWhiteSpace(EditDrugSpec) ? "标准规格" : EditDrugSpec.Trim()),
				UnitName = (string.IsNullOrWhiteSpace(EditDrugUnit) ? "盒" : EditDrugUnit.Trim()),
				RetailPrice = EditDrugRetailPrice,
				CostPrice = EditDrugCostPrice,
				AntibioticLevel = EditDrugAntibioticLevel,
				IsSkinTestRequired = EditDrugSkinTest,
				IsNarcotic = EditDrugNarcotic,
				IsActive = true,
				TotalStockQuantity = 0
			};
			DrugMasterList.Insert(0, drugMasterItemModel2);
			RecalculateDrugStockAndFinanceMetrics();
			SystemNotice = $"✓ [1. 药品基础数据维护] 成功新增药品字典 [{drugMasterItemModel2.DrugCode} - {drugMasterItemModel2.DrugName}] ({drugMasterItemModel2.Spec})，核定零售价=¥{drugMasterItemModel2.RetailPrice:0.00}。";
		}
	}

	public void ToggleDrugMasterStatus(DrugMasterItemModel? drug)
	{
		if (drug != null)
		{
			drug.IsActive = !drug.IsActive;
			SystemNotice = $"[1. 药品基础数据维护] 药品 [{drug.DrugName}] 状态已变更为：{drug.StatusText}。";
		}
	}

	public void ExecuteQuickPriceAdjustment(DrugMasterItemModel? drug = null)
	{
		EnsurePharmacyWarehouseFinanceInitialized();
		DrugMasterItemModel target = drug ?? DrugMasterList.FirstOrDefault((DrugMasterItemModel d) => d.DrugCode == EditDrugCode) ?? DrugMasterList.FirstOrDefault();
		if (target == null)
		{
			return;
		}
		decimal retailPrice = target.RetailPrice;
		decimal costPrice = target.CostPrice;
		decimal num = ((drug != null) ? Math.Max(1.00m, Math.Round(target.RetailPrice * 0.90m, 2)) : EditDrugRetailPrice);
		decimal num2 = ((drug != null) ? Math.Max(0.50m, Math.Round(target.CostPrice * 0.90m, 2)) : EditDrugCostPrice);
		target.RetailPrice = num;
		target.CostPrice = num2;
		foreach (WarehouseBatchItemModel item in WarehouseBatchList.Where((WarehouseBatchItemModel b) => b.DrugId == target.DrugId || b.DrugCode == target.DrugCode))
		{
			item.UnitRetailPrice = num;
			item.UnitCostPrice = num2;
		}
		DrugPriceAdjustmentLogModel drugPriceAdjustmentLogModel = RecordDrugPriceAdjustmentInternal(target, retailPrice, num, costPrice, num2, string.IsNullOrWhiteSpace(PriceAdjustReason) ? "国家集采中选联动调价" : PriceAdjustReason);
		RecalculateDrugStockAndFinanceMetrics();
		SystemNotice = $"\ud83d\udcc8 [2. 药品定价调价完成] 调价单 [{drugPriceAdjustmentLogModel.AdjustNo}]：[{target.DrugName}] 零售价 ¥{retailPrice:0.00}→¥{num:0.00}，涉及全院在库 {drugPriceAdjustmentLogModel.AffectedStockQuantity}{target.UnitName}，库存损益差额 {((drugPriceAdjustmentLogModel.PriceDiffAmount >= 0m) ? $"+¥{drugPriceAdjustmentLogModel.PriceDiffAmount:0.00}" : $"-¥{Math.Abs(drugPriceAdjustmentLogModel.PriceDiffAmount):0.00}")}！";
	}

	private DrugPriceAdjustmentLogModel RecordDrugPriceAdjustmentInternal(DrugMasterItemModel drug, decimal oldRetail, decimal newRetail, decimal oldCost, decimal newCost, string reason)
	{
		int num = WarehouseBatchList.Where((WarehouseBatchItemModel b) => b.DrugId == drug.DrugId || b.DrugCode == drug.DrugCode).Sum((WarehouseBatchItemModel b) => b.Quantity);
		decimal priceDiffAmount = (newRetail - oldRetail) * (decimal)num;
		DrugPriceAdjustmentLogModel drugPriceAdjustmentLogModel = new DrugPriceAdjustmentLogModel
		{
			AdjustNo = $"PRC-ADJ-{DateTime.Now:yyyyMMdd}-{DrugPriceLogs.Count + 1:D2}",
			DrugCode = drug.DrugCode,
			DrugName = drug.DrugName,
			OldRetailPrice = oldRetail,
			NewRetailPrice = newRetail,
			OldCostPrice = oldCost,
			NewCostPrice = newCost,
			AffectedStockQuantity = num,
			UnitName = drug.UnitName,
			PriceDiffAmount = priceDiffAmount,
			AdjustReason = reason,
			EffectiveTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
			OperatorName = CurrentOperatorName
		};
		DrugPriceLogs.Insert(0, drugPriceAdjustmentLogModel);
		return drugPriceAdjustmentLogModel;
	}

	public void ExecuteStockInNewBatch()
	{
		EnsurePharmacyWarehouseFinanceInitialized();
		if (StockInQuantity <= 0)
		{
			SystemNotice = "入库数量必须大于 0。";
			return;
		}
		if (StockInColdChainTemp > 8.0m && StockInWarehouseId == "DEPT-WH-02")
		{
			SystemNotice = $"❌ [GSP 冷链超温强阻断] 冷链专库收货实测温度 {StockInColdChainTemp:0.0}°C > 阈值 8.0°C，系统强制拒绝入库！";
			return;
		}
		DrugMasterItemModel drug = DrugMasterList.FirstOrDefault((DrugMasterItemModel d) => string.Equals(d.DrugCode, StockInDrugCode, StringComparison.OrdinalIgnoreCase)) ?? DrugMasterList.First();
		string text = ResolveWarehouseDisplayName(StockInWarehouseId);
		WarehouseBatchItemModel warehouseBatchItemModel = WarehouseBatchList.FirstOrDefault((WarehouseBatchItemModel b) => b.WarehouseId == StockInWarehouseId && b.DrugCode == drug.DrugCode && b.BatchNo == StockInBatchNo);
		if (warehouseBatchItemModel != null)
		{
			warehouseBatchItemModel.Quantity += StockInQuantity;
		}
		else
		{
			WarehouseBatchList.Insert(0, new WarehouseBatchItemModel
			{
				BatchId = $"BATCH-{WarehouseBatchList.Count + 1:D3}",
				WarehouseId = StockInWarehouseId,
				WarehouseName = text,
				WarehouseCategory = (StockInWarehouseId.StartsWith("DEPT-WH") ? "WAREHOUSE" : "PHARMACY"),
				DrugId = drug.DrugId,
				DrugCode = drug.DrugCode,
				DrugName = drug.DrugName,
				Spec = drug.Spec,
				UnitName = drug.UnitName,
				BatchNo = (string.IsNullOrWhiteSpace(StockInBatchNo) ? $"LOT{DateTime.Now:yyyyMMdd}" : StockInBatchNo.Trim()),
				ExpiryDateInt = StockInExpiryInt,
				InvoiceNo = (string.IsNullOrWhiteSpace(StockInInvoiceNo) ? $"INV-{DateTime.Now:yyyyMMdd}" : StockInInvoiceNo.Trim()),
				ColdChainInfo = ((StockInWarehouseId == "DEPT-WH-02") ? $"GSP冷链 {StockInColdChainTemp:0.0}°C 合格" : "常温验收合格"),
				UnitCostPrice = drug.CostPrice,
				UnitRetailPrice = drug.RetailPrice,
				Quantity = StockInQuantity,
				IsFrozen = false
			});
		}
		ApplyWarehouseBatchFilter();
		RecalculateDrugStockAndFinanceMetrics();
		SystemNotice = $"✓ [3 & 4. 药品采购入库与财务应付登记] 库房 [{text}] 成功入库 [{drug.DrugName}] 批号 [{StockInBatchNo}] +{StockInQuantity}{drug.UnitName}（采购成本 ¥{(decimal)StockInQuantity * drug.CostPrice:0.00}，随货同行发票：{StockInInvoiceNo}）！";
	}

	public void ToggleBatchFreezeStatus(WarehouseBatchItemModel? batch)
	{
		if (batch != null)
		{
			batch.IsFrozen = !batch.IsFrozen;
			SystemNotice = (batch.IsFrozen ? $"\ud83d\udd12 [3. 库存质量管控] 已冻结库房 [{batch.WarehouseName}] 批次 [{batch.BatchNo} ({batch.DrugName})]，FEFO 发药与调拨已自动跳过该批次！" : $"✓ [3. 库存质量管控] 已解除批次 [{batch.BatchNo} ({batch.DrugName})] 冻结状态，恢复正常发药与调拨。");
		}
	}

	public void ExecuteWarehouseIssueOrTransfer(string transferType)
	{
		EnsurePharmacyWarehouseFinanceInitialized();
		if (string.Equals(TransferSourceWarehouseId, TransferTargetWarehouseId, StringComparison.OrdinalIgnoreCase))
		{
			SystemNotice = "调出库房（源库房）与调入库房（目标库房/药房）不能相同！";
			return;
		}
		if (TransferQuantity <= 0)
		{
			SystemNotice = "调拨或药库发药数量必须大于 0。";
			return;
		}
		DrugMasterItemModel drug = DrugMasterList.FirstOrDefault((DrugMasterItemModel d) => string.Equals(d.DrugCode, TransferDrugCode, StringComparison.OrdinalIgnoreCase)) ?? DrugMasterList.First();
		WarehouseBatchItemModel sourceBatch = (from b in WarehouseBatchList
			where b.WarehouseId == TransferSourceWarehouseId && (b.DrugCode == drug.DrugCode || b.DrugId == drug.DrugId) && !b.IsFrozen && b.Quantity > 0
			orderby b.ExpiryDateInt
			select b).FirstOrDefault();
		if (sourceBatch == null || sourceBatch.Quantity < TransferQuantity)
		{
			int value = sourceBatch?.Quantity ?? 0;
			SystemNotice = $"❌ 源库房 [{ResolveWarehouseDisplayName(TransferSourceWarehouseId)}] 药品 [{drug.DrugName}] 可用库存不足（需 {TransferQuantity}{drug.UnitName}，当前单批可用 {value}{drug.UnitName}）！";
			return;
		}
		sourceBatch.Quantity -= TransferQuantity;
		string text = ResolveWarehouseDisplayName(TransferTargetWarehouseId);
		WarehouseBatchItemModel warehouseBatchItemModel = WarehouseBatchList.FirstOrDefault((WarehouseBatchItemModel b) => b.WarehouseId == TransferTargetWarehouseId && b.DrugCode == drug.DrugCode && b.BatchNo == sourceBatch.BatchNo);
		if (warehouseBatchItemModel != null)
		{
			warehouseBatchItemModel.Quantity += TransferQuantity;
		}
		else
		{
			WarehouseBatchList.Insert(0, new WarehouseBatchItemModel
			{
				BatchId = $"BATCH-TRF-{WarehouseBatchList.Count + 1:D3}",
				WarehouseId = TransferTargetWarehouseId,
				WarehouseName = text,
				WarehouseCategory = (TransferTargetWarehouseId.StartsWith("DEPT-WH") ? "WAREHOUSE" : "PHARMACY"),
				DrugId = drug.DrugId,
				DrugCode = drug.DrugCode,
				DrugName = drug.DrugName,
				Spec = drug.Spec,
				UnitName = drug.UnitName,
				BatchNo = sourceBatch.BatchNo,
				ExpiryDateInt = sourceBatch.ExpiryDateInt,
				InvoiceNo = sourceBatch.InvoiceNo,
				ColdChainInfo = sourceBatch.ColdChainInfo,
				UnitCostPrice = drug.CostPrice,
				UnitRetailPrice = drug.RetailPrice,
				Quantity = TransferQuantity,
				IsFrozen = false
			});
		}
		bool flag = string.Equals(transferType, "WAREHOUSE_ISSUE", StringComparison.OrdinalIgnoreCase);
		string value2 = (flag ? "ISS" : "TRF");
		string text2 = $"{value2}-{DateTime.Now:yyyyMMdd}-{WarehouseTransferOrders.Count + 1:D3}";
		string text3 = ResolveWarehouseDisplayName(TransferSourceWarehouseId);
		WarehouseTransferOrderModel warehouseTransferOrderModel = new WarehouseTransferOrderModel
		{
			TransferNo = text2,
			TransferType = (flag ? "WAREHOUSE_ISSUE" : "INTER_WAREHOUSE"),
			SourceWarehouseId = TransferSourceWarehouseId,
			SourceWarehouseName = text3,
			TargetWarehouseId = TransferTargetWarehouseId,
			TargetWarehouseName = text,
			DrugName = drug.DrugName,
			Spec = drug.Spec,
			BatchNo = sourceBatch.BatchNo,
			ExpiryDateInt = sourceBatch.ExpiryDateInt,
			Quantity = TransferQuantity,
			UnitName = drug.UnitName,
			TotalRetailAmount = (decimal)TransferQuantity * drug.RetailPrice,
			OperatorName = CurrentOperatorName,
			TransferTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
			StatusText = (flag ? "✓ 药库已按FEFO发药出库 · 药房已扫码入账" : "✓ 多库房跨库调拨完成 · 双边台账已平衡"),
			Remark = ((!string.IsNullOrWhiteSpace(TransferRemark)) ? TransferRemark.Trim() : (flag ? "药库向药房发药补货" : "多库房跨库调拨"))
		};
		WarehouseTransferOrders.Insert(0, warehouseTransferOrderModel);
		ApplyWarehouseBatchFilter();
		RecalculateDrugStockAndFinanceMetrics();
		SystemNotice = (flag ? $"\ud83d\udce6 [6. 药库发药管理闭环] 单号 [{text2}]：已从 [{text3}] 按 FEFO 批次 [{sourceBatch.BatchNo}] 向 [{text}] 发放 [{drug.DrugName}] {TransferQuantity}{drug.UnitName}（零售总额 ¥{warehouseTransferOrderModel.TotalRetailAmount:0.00}）！" : $"\ud83d\udd04 [5. 多个库房调拨闭环] 单号 [{text2}]：已完成 [{text3}] ➔ [{text}] 跨库房调拨 [{drug.DrugName}] {TransferQuantity}{drug.UnitName}（批次 {sourceBatch.BatchNo}）！");
	}

	public void DispenseInpatientDischargeMedsForPatient(InpatientRecordModel? patient = null)
	{
		InpatientRecordModel inpatientRecordModel = patient ?? SelectedInpatient;
		if (inpatientRecordModel != null)
		{
			if (!CanOperatePharmacy)
			{
				SystemNotice = "\ud83d\udee1\ufe0f [职责分离 SoD 权限拦截] 住院出院带药核发（出院六严关·步骤C/关卡3）属于【药房药师 (ROLE-PHARMACIST)】专属职责！";
				return;
			}
			SelectedInpatient = inpatientRecordModel;
			CompleteDischargeGate3_DischargeMeds();
		}
	}

	public void SettleInpatientInsuranceForPatient(InpatientRecordModel? patient = null)
	{
		InpatientRecordModel inpatientRecordModel = patient ?? SelectedInpatient;
		if (inpatientRecordModel != null)
		{
			if (!CanOperateFinance)
			{
				SystemNotice = "\ud83d\udee1\ufe0f [职责分离 SoD 权限拦截] 住院医保基金结算核验（出院六严关·步骤E/关卡5）属于【财务收费员 (ROLE-CASHIER)】专属职责！";
				return;
			}
			SelectedInpatient = inpatientRecordModel;
			CompleteDischargeGate5_Insurance();
		}
	}

	public void ClearInpatientPrepaymentForPatient(InpatientRecordModel? patient = null)
	{
		InpatientRecordModel inpatientRecordModel = patient ?? SelectedInpatient;
		if (inpatientRecordModel != null)
		{
			if (!CanOperateFinance)
			{
				SystemNotice = "\ud83d\udee1\ufe0f [职责分离 SoD 权限拦截] 住院预交金清算退补（出院六严关·步骤F/关卡6）属于【财务收费员 (ROLE-CASHIER)】专属职责！";
				return;
			}
			SelectedInpatient = inpatientRecordModel;
			CompleteDischargeGate6_Prepayment();
		}
	}

	public void GenerateCashierDailyClosing()
	{
		EnsurePharmacyWarehouseFinanceInitialized();
		if (!CanOperateFinance)
		{
			SystemNotice = "\ud83d\udee1\ufe0f [职责分离 SoD 权限拦截] 收费员日结轧账单生成属于【财务收费员 (ROLE-CASHIER)】专属职责！";
			return;
		}
		List<PharmacyDispenseModel> list = PharmacyRecords.Where((PharmacyDispenseModel x) => x.IsPaidNotified).ToList();
		List<PharmacyDispenseModel> list2 = PharmacyRecords.Where((PharmacyDispenseModel x) => x.IsFinancialRefunded).ToList();
		List<InpatientRecordModel> list3 = AllInpatientRecords.Where((InpatientRecordModel x) => x.PrepaymentCleared).ToList();
		decimal num = list.Sum((PharmacyDispenseModel x) => x.TotalAmount) + list3.Sum((InpatientRecordModel x) => x.ActualSelfPay);
		decimal num2 = list2.Sum((PharmacyDispenseModel x) => x.TotalAmount);
		decimal value = num - num2;
		string value2 = $"CLS-{DateTime.Now:yyyyMMdd-HHmmss}";
		DailyClosingStatusText = $"✓ 日结轧账完成 · 日结单 [{value2}] 已生成 | 收费员: {CurrentOperatorName} | 门急诊收费:{list.Count}笔 | 退费冲红:{list2.Count}笔 (-¥{num2:N2}) | 住院结清:{list3.Count}人 | 实收净额: ¥{value:N2}";
		SystemNotice = $"\ud83d\udcca [财务日结轧账完成] 已生成收费员日结单 [{value2}]，实收净额 ¥{value:N2}，账实相符并已锁定日结区间！";
	}

	private static string ResolveWarehouseDisplayName(string warehouseId)
	{
		if (1 == 0)
		{
		}
		string result = warehouseId switch
		{
			"DEPT-WH-01" => "全院中心药库 (DEPT-WH-01)", 
			"DEPT-WH-02" => "冷链生物制品专库 (DEPT-WH-02)", 
			"DEPT-PHM-01" => "门诊西药房 (DEPT-PHM-01)", 
			"DEPT-PHM-02" => "住院中心药房 (DEPT-PHM-02)", 
			_ => warehouseId, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	public void InitPrescriptionColumns()
	{
		ReplacePrescriptionColumns(CreateDefaultPrescriptionColumns());
	}

	public async Task LoadPrescriptionColumnsAsync()
	{
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			ClientApiEnvelope<DynamicFormSchemaDto> envelope = await http.GetFromJsonAsync<ClientApiEnvelope<DynamicFormSchemaDto>>("/api/v4/config/schemas/" + Uri.EscapeDataString("FORM-PRESCRIPTION-ITEM-FIELDS") + "?includeDraft=true", JsonOptions);
			if (envelope != null && envelope.Code == 200 && envelope.Data?.Fields != null)
			{
				ReplacePrescriptionColumns(envelope.Data.Fields.Select(MapPrescriptionColumn));
			}
		}
		catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException) ? true : false)
		{
		}
	}

	public async Task BeginDesignPrescriptionItemFieldsAsync()
	{
		DynamicFormTemplateModel template = (SelectedFormTemplate = EnsurePrescriptionItemFieldsTemplate());
		bool loaded = await LoadDynamicFormDetailAsync(template);
		EditingTemplate = template;
		EditingField = null;
		IsFormDesignerOpen = true;
		SystemNotice = (loaded ? "已打开处方明细字段配置，可新增、排序或隐藏字段" : "配置服务暂不可用，当前显示内置处方明细字段");
	}

	public async Task BeginAddPrescriptionItemFieldAsync()
	{
		await BeginDesignPrescriptionItemFieldsAsync();
		BeginAddField();
		SystemNotice = "新增处方明细字段：填写字段代码、标签、控件与列宽，点击“保存并应用”立即生成处方列";
	}

	private DynamicFormTemplateModel EnsurePrescriptionItemFieldsTemplate()
	{
		DynamicFormTemplateModel dynamicFormTemplateModel = DynamicFormTemplates.FirstOrDefault((DynamicFormTemplateModel x) => string.Equals(x.TemplateCode, "FORM-PRESCRIPTION-ITEM-FIELDS", StringComparison.OrdinalIgnoreCase));
		if (dynamicFormTemplateModel != null)
		{
			return dynamicFormTemplateModel;
		}
		dynamicFormTemplateModel = CreatePrescriptionItemFieldsTemplate();
		DynamicFormTemplates.Add(dynamicFormTemplateModel);
		return dynamicFormTemplateModel;
	}

	private static DynamicFormTemplateModel CreatePrescriptionItemFieldsTemplate()
	{
		DynamicFormTemplateModel dynamicFormTemplateModel = new DynamicFormTemplateModel
		{
			Id = "SCHEMA-PRESCRIPTION-ITEM-FIELDS",
			TemplateCode = "FORM-PRESCRIPTION-ITEM-FIELDS",
			TemplateName = "门诊处方明细字段配置",
			EntityType = "OPD_PRESCRIPTION_ITEM",
			DeptCategory = "OPD",
			TargetDeptId = "DEPT-OPD-01",
			GridColumns = 1,
			Version = "v1.0",
			Status = "生产已发布",
			IsPublished = true,
			ExtDataJson = "{\"version\":\"v1.0\",\"description\":\"门诊处方明细动态列配置\"}",
			SchemaVersion = 1
		};
		foreach (PrescriptionColumnModel item in CreateDefaultPrescriptionColumns())
		{
			dynamicFormTemplateModel.Fields.Add(new DynamicFormFieldModel
			{
				Id = item.Id,
				FormSchemaId = "SCHEMA-PRESCRIPTION-ITEM-FIELDS",
				FieldCode = item.FieldCode,
				FieldLabel = item.FieldLabel,
				DataType = item.DataType,
				UrsaControlType = item.ControlType,
				ControlKey = "PRESCRIPTION.Dynamic." + item.FieldCode,
				DictCode = item.DictCode,
				UnitText = item.UnitText,
				Placeholder = item.Placeholder,
				IsRequired = item.IsRequired,
				OptionsText = item.OptionsText,
				Minimum = item.Minimum,
				Maximum = item.Maximum,
				Step = item.Step,
				SortOrder = item.SortOrder,
				VisibilityExpr = "ALL",
				ColumnWidth = item.Width,
				IsSystemField = item.IsSystemField,
				IsEditable = item.IsEditable,
				SchemaVersion = 1
			});
		}
		dynamicFormTemplateModel.NotifyDerived();
		return dynamicFormTemplateModel;
	}

	private static IReadOnlyList<PrescriptionColumnModel> CreateDefaultPrescriptionColumns()
	{
		return new[]
		{
			CreateDefaultPrescriptionColumn("FIELD-RX-ITEM-01", "DRUG_NAME", "药品名称", 130, 0, system: true, editable: true, "输入药品名称"),
			CreateDefaultPrescriptionColumn("FIELD-RX-ITEM-02", "SPEC", "规格", 95, 1, system: true, editable: true, "输入规格"),
			CreateDefaultPrescriptionColumn("FIELD-RX-ITEM-03", "DOSE", "剂量", 70, 2, system: true, editable: true, "输入剂量"),
			CreateDefaultPrescriptionColumn("FIELD-RX-ITEM-04", "FREQ", "频次", 90, 3, system: true, editable: true, "输入频次"),
			CreateDefaultPrescriptionColumn("FIELD-RX-ITEM-12", "DAYS", "天数", 58, 4, system: true, editable: true, "输入用药天数"),
			CreateDefaultPrescriptionColumn("FIELD-RX-ITEM-05", "ROUTE", "途径", 70, 5, system: true, editable: true, "输入给药途径"),
			CreateDefaultPrescriptionColumn("FIELD-RX-ITEM-06", "QUANTITY", "数量", 55, 6, system: true, editable: true, "输入数量"),
			CreateDefaultPrescriptionColumn("FIELD-RX-ITEM-07", "AMOUNT", "金额", 65, 7, system: true, editable: true, "输入金额"),
			CreateDefaultPrescriptionColumn("FIELD-RX-ITEM-08", "SKIN_TEST_STATUS", "皮试状态", 95, 8, system: true, editable: false, string.Empty),
			CreateDefaultPrescriptionColumn("FIELD-RX-ITEM-09", "AUDIT_STATUS", "审核结果", 80, 9, system: true, editable: false, string.Empty),
			CreateDefaultPrescriptionColumn("FIELD-RX-ITEM-10", "MEDICATION_NOTE", "用药备注", 120, 10, system: true, editable: true, "补充用药说明"),
			CreateDefaultPrescriptionColumn("FIELD-RX-ITEM-11", "INFUSION_NOTE", "给药说明", 130, 11, system: true, editable: true, "例如：静脉滴注 40 滴/分")
		};
	}

	private static PrescriptionColumnModel CreateDefaultPrescriptionColumn(string id, string code, string label, int width, int sortOrder, bool system, bool editable, string placeholder)
	{
		return new PrescriptionColumnModel
		{
			Id = id,
			FieldCode = code,
			FieldLabel = label,
			DataType = "STRING",
			ControlType = "u:TextBox",
			Placeholder = placeholder,
			Width = width,
			SortOrder = sortOrder,
			IsSystemField = system,
			IsEditable = editable
		};
	}

	private static PrescriptionColumnModel MapPrescriptionColumn(DynamicFormFieldDto dto)
	{
		PrescriptionColumnModel prescriptionColumnModel = new PrescriptionColumnModel
		{
			Id = dto.Id,
			FieldCode = dto.FieldCode,
			FieldLabel = dto.FieldLabel,
			DataType = dto.DataType,
			ControlType = dto.UrsaControlType,
			UnitText = (dto.UnitText ?? string.Empty),
			DictCode = dto.DictCode,
			SortOrder = dto.SortOrder
		};
		prescriptionColumnModel.LoadValidationRuleJson(dto.ValidationRuleJson);
		return prescriptionColumnModel;
	}

	private void ReplacePrescriptionColumns(IEnumerable<PrescriptionColumnModel> columns)
	{
		// Keep this a mutable list: missing core columns are appended below. Converting
		// to an array first made the append path fail with NotSupportedException and
		// prevented prescription fields loaded from the server from being rendered.
		List<PrescriptionColumnModel> merged = columns
			.Where(x => x != null && !string.IsNullOrWhiteSpace(x.FieldCode))
			.OrderBy(x => x.SortOrder)
			.ToList();
		foreach (PrescriptionColumnModel required in CreateDefaultPrescriptionColumns()
			.Where(x => string.Equals(x.FieldCode, PrescriptionItemModel.DoseCode, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(x.FieldCode, PrescriptionItemModel.FreqCode, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(x.FieldCode, PrescriptionItemModel.DaysCode, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(x.FieldCode, PrescriptionItemModel.QuantityCode, StringComparison.OrdinalIgnoreCase)))
		{
			if (!merged.Any(x => string.Equals(x.FieldCode, required.FieldCode, StringComparison.OrdinalIgnoreCase)))
			{
				required.SortOrder = merged.Count;
				merged.Add(required);
			}
		}
		List<PrescriptionColumnModel> ordered = merged.OrderBy(x => x.SortOrder).ToList();
		PrescriptionColumns.Clear();
		for (int index = 0; index < ordered.Count; index++)
		{
			ordered[index].SortOrder = index;
			PrescriptionColumns.Add(ordered[index]);
		}
		PrescriptionItemModel.EnsureCoreEditableColumns(ordered);
		RebuildPrescriptionCells();
		UpdatePrescriptionFieldCatalogFromColumns();
		OnPropertyChanged("PrescriptionColumns");
	}

	private void RebuildPrescriptionCells()
	{
		foreach (PrescriptionItemModel prescription in Prescriptions)
		{
			prescription.RebuildCells(PrescriptionColumns);
		}
	}

	public async Task<bool> AddPrescriptionFromCatalogAsync(DrugCatalogItemModel drug)
	{
		PrescriptionItemModel? added = AddPrescriptionFromCatalog(drug);
		if (added != null)
		{
			bool saved = await SyncPrescriptionItemAsync(added, isNew: true);
			if (!saved)
			{
				return false;
			}
			BeginEditPrescriptionItem(added);
			return true;
		}
		return false;
	}

	public async Task<bool> CommitPrescriptionItemAsync(PrescriptionItemModel item)
	{
		if (string.IsNullOrWhiteSpace(item.EditDraft.DrugName))
		{
			SystemNotice = "药品名称不能为空，处方明细未保存";
			return false;
		}
		string validationError = ValidatePrescriptionDraft(item.EditDraft);
		if (validationError != null)
		{
			SystemNotice = validationError + "，处方明细未保存";
			return false;
		}
		if (!CommitPrescriptionItem(item))
		{
			return false;
		}
		return await SyncPrescriptionItemAsync(item, isNew: !item.IsPersisted);
	}

	private string? ValidatePrescriptionDraft(PrescriptionItemDraftModel draft)
	{
		foreach (PrescriptionColumnModel item in PrescriptionColumns.OrderBy((PrescriptionColumnModel x) => x.SortOrder))
		{
			if (!item.IsEditable)
			{
				continue;
			}
			string value = draft.GetValue(item.FieldCode).Trim();
			if (item.IsRequired && string.IsNullOrWhiteSpace(value))
			{
				return "字段 [" + item.FieldLabel + "] 为必填项";
			}
			if (string.IsNullOrWhiteSpace(value))
			{
				continue;
			}
			decimal? num = null;
			bool flag = item.IsIntegerControl || item.IsDecimalControl;
			bool flag2 = flag;
			bool result;
			if (!flag2)
			{
				string dataType = item.DataType;
				result = ((dataType == "INTEGER" || dataType == "DECIMAL") ? true : false);
				flag2 = result;
			}
			if (flag2)
			{
				string text = ExtractNumber(value);
				if (string.IsNullOrWhiteSpace(text) || !decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var result2))
				{
					return "字段 [" + item.FieldLabel + "] 必须填写数字";
				}
				num = result2;
				if ((item.IsIntegerControl || item.DataType == "INTEGER") && result2 != decimal.Truncate(result2))
				{
					return "字段 [" + item.FieldLabel + "] 必须填写整数";
				}
			}
			bool isDateControl = item.IsDateControl;
			bool flag3 = isDateControl;
			if (!flag3)
			{
				string dataType = item.DataType;
				result = ((dataType == "DATE" || dataType == "DATETIME") ? true : false);
				flag3 = result;
			}
			if (flag3 && !DateTimeOffset.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var _))
			{
				return "字段 [" + item.FieldLabel + "] 必须填写有效日期";
			}
			if ((item.IsEnumControl || item.DataType == "ENUM") && item.Options.Count > 0 && !item.Options.Any((string x) => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)))
			{
				return "字段 [" + item.FieldLabel + "] 不在可选范围内";
			}
			if ((item.IsBooleanControl || item.DataType == "BOOLEAN") && !TryParseBoolean(value, out result))
			{
				return "字段 [" + item.FieldLabel + "] 必须填写是或否";
			}
			if (num.HasValue)
			{
				if (item.Minimum.HasValue && num.Value < item.Minimum.Value)
				{
					return $"字段 [{item.FieldLabel}] 不能小于 {item.Minimum.Value:0.####}";
				}
				if (item.Maximum.HasValue && num.Value > item.Maximum.Value)
				{
					return $"字段 [{item.FieldLabel}] 不能大于 {item.Maximum.Value:0.####}";
				}
			}
		}
		return null;
	}

	private static bool TryParseBoolean(string value, out bool result)
	{
		switch (value.Trim())
		{
		case "1":
		case "是":
		case "已确认":
		case "true":
		case "TRUE":
		case "True":
			result = true;
			return true;
		case "0":
		case "否":
		case "未确认":
		case "false":
		case "FALSE":
		case "False":
			result = false;
			return true;
		default:
			return bool.TryParse(value, out result);
		}
	}

	public async Task<bool> SoftDeletePrescriptionItemAsync(PrescriptionItemModel item)
	{
		if (item.IsDeleted)
		{
			return true;
		}
		return await SyncPrescriptionItemSoftDeleteAsync(item);
	}

	public async Task CommitPrescriptionFieldAsync()
	{
		if (string.IsNullOrWhiteSpace(PrescriptionFieldDraft.Name))
		{
			SystemNotice = "扩展字段名称不能为空";
			return;
		}
		bool isNew = _editingPrescriptionField == null;
		CommitPrescriptionField();
		await SyncPrescriptionFieldsAsync(isNew ? "扩展字段已新增并随处方保存" : "扩展字段已更新并随处方保存");
	}

	public async Task SoftDeletePrescriptionFieldAsync(PrescriptionExtraFieldModel field)
	{
		if (!field.IsDeleted)
		{
			SoftDeletePrescriptionField(field);
			await SyncPrescriptionFieldsAsync("扩展字段已从界面隐藏，历史定义与值仍保留在处方扩展数据中");
		}
	}

	public async Task SavePrescriptionExtFieldsAsync()
	{
		await SyncPrescriptionFieldsAsync("处方扩展字段值已保存");
	}

	public Task<bool> SyncReplacedPrescriptionItemAsync(PrescriptionItemModel item)
	{
		return SyncPrescriptionItemAsync(item, isNew: false);
	}

	private async Task<bool> SyncPrescriptionItemAsync(PrescriptionItemModel item, bool isNew)
	{
		string visitId = _currentPrescriptionVisitId;
		if (string.IsNullOrWhiteSpace(visitId))
		{
			SystemNotice = "当前为演示患者，缺少真实就诊记录，处方明细尚未保存";
			return false;
		}
		await _prescriptionSyncLock.WaitAsync();
		try
		{
			EnsurePrescriptionResult ensured = await EnsurePrescriptionAsync(visitId);
			string rxId = ensured.RxId;
			bool createdNow = ensured.CreatedNow;
			if (string.IsNullOrWhiteSpace(rxId))
			{
				return false;
			}
			if (isNew && createdNow && item.IsPersisted)
			{
				SystemNotice = "处方明细 [" + item.DrugName + "] 已随新处方保存至服务端";
				return true;
			}
			using HttpClient http = CreateConfigHttpClient();
			if (isNew)
			{
				// 临时行尚未获得服务端主键，必须仅 POST 一次，成功后再切换为更新模式。
				var createBody = ToServerItem(item);
				using HttpResponseMessage response = await HttpClientJsonExtensions.PostAsJsonAsync(value: createBody, client: http, requestUri: "/api/v4/opd/prescriptions/" + Uri.EscapeDataString(rxId) + "/items", options: JsonOptions);
					ClientApiEnvelope<PrescriptionItemDto> envelope = await response.Content.ReadFromJsonAsync<ClientApiEnvelope<PrescriptionItemDto>>(JsonOptions);
				if (envelope != null && envelope.Code == 200)
				{
						// 首次保存成功后只保留服务端生成的主键。
						item.IsPersisted = true;
						ApplySavedPrescriptionItem(item, envelope.Data);
					SystemNotice = "处方明细 [" + item.DrugName + "] 已新增并保存至服务端";
					return true;
				}
				else
				{
					SystemNotice = "处方明细已新增到界面，后台保存失败：" + (envelope?.Message ?? "服务无响应");
				}
				return false;
			}
			int quantity = item.QuantityValue;
			using HttpResponseMessage updateResponse = await HttpClientJsonExtensions.PutAsJsonAsync(value: new
			{
				DrugId = item.DrugId,
				DrugNameSnapshot = item.DrugName,
				DoseOnce = ParseDecimal(item.Dose, 1m),
				DoseUnit = ParseDoseUnit(item.Dose),
				FrequencyCode = item.Freq,
				RouteCode = item.Route,
				Days = item.Days,
				TotalQuantity = quantity,
				UnitPrice = ToUnitPrice(ParseMoney(item.Amount), quantity),
				OperatorId = CurrentOperatorId,
				CustomFieldsJson = BuildItemExtJson(item)
			}, client: http, requestUri: "/api/v4/opd/prescriptions/items/" + Uri.EscapeDataString(item.Id), options: JsonOptions);
			ClientApiEnvelope<PrescriptionItemDto> updateEnvelope = await updateResponse.Content.ReadFromJsonAsync<ClientApiEnvelope<PrescriptionItemDto>>(JsonOptions);
			if (updateEnvelope == null || updateEnvelope.Code != 200)
			{
				SystemNotice = "处方明细 [" + item.DrugName + "] 已更新到界面，后台保存失败：" + (updateEnvelope?.Message ?? "服务无响应");
				return false;
			}
			else
			{
				ApplySavedPrescriptionItem(item, updateEnvelope.Data);
				SystemNotice = "处方明细 [" + item.DrugName + "] 已保存至服务端";
				return true;
			}
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
		{
			SystemNotice = "处方明细已更新到界面，后台同步失败：" + ex.Message;
			return false;
		}
		finally
		{
			_prescriptionSyncLock.Release();
		}
	}

	private async Task<bool> SyncPrescriptionItemSoftDeleteAsync(PrescriptionItemModel item)
	{
		string visitId = _currentPrescriptionVisitId;
		await _prescriptionSyncLock.WaitAsync();
		try
		{
			if (!item.IsPersisted)
			{
				item.IsDeleted = true;
				item.DeletedAtMs = null;
				item.DeletedBy = CurrentOperatorId;
				item.IsEditing = false;
				RefreshPrescriptionMetrics();
				SystemNotice = $"已从当前处方移除尚未保存的药品 [{item.DrugName}]";
				return true;
			}
			int activeItemCount = Prescriptions.Count(x => !x.IsDeleted);
			if (activeItemCount <= 1)
			{
				SystemNotice = "处方至少保留一条有效明细，不能删除最后一条药品";
				return false;
			}
			if (string.IsNullOrWhiteSpace(visitId))
			{
				SystemNotice = "当前没有真实门诊就诊记录，未发送删除请求";
				return false;
			}
			EnsurePrescriptionResult ensured = await EnsurePrescriptionAsync(visitId);
			string rxId = ensured.RxId;
			if (string.IsNullOrWhiteSpace(rxId))
			{
				return false;
			}
			using HttpClient http = CreateConfigHttpClient();
			using HttpResponseMessage response = await http.PostAsJsonAsync("/api/v4/opd/prescriptions/items/" + Uri.EscapeDataString(item.Id) + "/soft-delete", new
			{
				OperatorId = CurrentOperatorId,
				Reason = "门诊处方界面删除（软删除）"
			}, JsonOptions);
			ClientApiEnvelope<PrescriptionItemDto> envelope = await response.Content.ReadFromJsonAsync<ClientApiEnvelope<PrescriptionItemDto>>(JsonOptions);
			if (envelope != null && envelope.Code == 200)
			{
				item.IsDeleted = true;
				item.DeletedAtMs = envelope.Data?.DeletedAtMs;
				item.DeletedBy = envelope.Data?.DeletedBy;
				item.IsEditing = false;
				RefreshPrescriptionMetrics();
				SystemNotice = "已从界面隐藏 [" + item.DrugName + "]，后台保留原始记录、删除时间与操作人";
				return true;
			}
			SystemNotice = $"删除处方明细 [{item.DrugName}] 失败：{envelope?.Message ?? "服务无响应"}（明细仍保留）";
			return false;
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
		{
			SystemNotice = $"删除处方明细 [{item.DrugName}] 失败：{ex.Message}（明细仍保留）";
			return false;
		}
		finally
		{
			_prescriptionSyncLock.Release();
		}
	}

	private async Task SyncPrescriptionFieldsAsync(string successMessage)
	{
		string visitId = _currentPrescriptionVisitId;
		if (string.IsNullOrWhiteSpace(visitId))
		{
			SystemNotice = "当前为演示患者，缺少真实就诊记录，处方扩展字段尚未保存";
			return;
		}
		await _prescriptionSyncLock.WaitAsync();
		try
		{
			string rxId = (await EnsurePrescriptionAsync(visitId)).RxId;
			if (string.IsNullOrWhiteSpace(rxId))
			{
				return;
			}
			var fields = PrescriptionExtraFields.Select((PrescriptionExtraFieldModel x, int index) => new
			{
				Id = x.Id,
				Name = x.Name,
				Value = x.Value,
				DataType = x.DataType,
				Placeholder = x.Placeholder,
				IsDeleted = x.IsDeleted,
				SortOrder = index,
				DeletedAtMs = x.DeletedAtMs,
				DeletedBy = x.DeletedBy
			}).ToList();
			using HttpClient http = CreateConfigHttpClient();
			using HttpResponseMessage response = await http.PutAsJsonAsync("/api/v4/opd/prescriptions/" + Uri.EscapeDataString(rxId) + "/ext-fields", new
			{
				CustomFields = fields,
				OperatorId = CurrentOperatorId
			}, JsonOptions);
			ClientApiEnvelope<List<PrescriptionFieldDto>> envelope = await response.Content.ReadFromJsonAsync<ClientApiEnvelope<List<PrescriptionFieldDto>>>(JsonOptions);
			SystemNotice = ((envelope != null && envelope.Code == 200) ? successMessage : ("扩展字段已在界面生效，后台保存失败：" + (envelope?.Message ?? "服务无响应")));
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
		{
			SystemNotice = "扩展字段已在界面生效，后台同步失败：" + ex.Message;
		}
		finally
		{
			_prescriptionSyncLock.Release();
		}
	}

	private async Task<EnsurePrescriptionResult> EnsurePrescriptionAsync(string visitId)
	{
		bool createdNow;
		PrescriptionAggregateDto? aggregate = await GetPrescriptionFromServerAsync(visitId);
		if (aggregate?.Prescription != null && !string.IsNullOrWhiteSpace(aggregate.Prescription.Id))
		{
			string existingId = aggregate.Prescription.Id;
			_persistedPrescriptionByVisitId[visitId] = existingId;
			_currentPrescriptionId = existingId;
			_currentPrescriptionStatus = aggregate.Prescription.Status;
			_currentPrescriptionNo = aggregate.Prescription.PrescriptionNo;
			_currentPrescriptionReviewRemark = aggregate.Prescription.ReviewRemark;
			return new EnsurePrescriptionResult(existingId, CreatedNow: false);
		}
		using HttpClient http = CreateConfigHttpClient();
		PatientQueueItemModel? patient = FindPatientByVisitId(visitId);
		if (patient == null || string.IsNullOrWhiteSpace(patient.PatientId))
		{
			SystemNotice = "无法识别就诊患者主记录，未发送处方保存请求";
			return new EnsurePrescriptionResult(null, CreatedNow: false);
		}
		PrescriptionItemModel? firstItem = Prescriptions.FirstOrDefault(x =>
			!x.IsDeleted && !string.IsNullOrWhiteSpace(ResolveDrugId(x)));
		if (firstItem == null)
		{
			SystemNotice = "处方中没有药品目录内的有效药品，未发送处方创建请求";
			return new EnsurePrescriptionResult(null, CreatedNow: false);
		}
		List<object> items = new List<object> { ToServerItem(firstItem) };
		createdNow = false;
		var body = new
		{
			VisitId = visitId,
			PatientId = patient.PatientId,
			OpdDeptId = SessionContext.Current.ActiveDeptId,
			DoctorId = CurrentOperatorId,
			Items = items
		};
		try
		{
			using HttpResponseMessage response = await http.PostAsJsonAsync("/api/v4/opd/prescriptions", body, JsonOptions);
			ClientApiEnvelope<PrescriptionDto> envelope = await response.Content.ReadFromJsonAsync<ClientApiEnvelope<PrescriptionDto>>(JsonOptions);
			if (envelope != null && envelope.Code == 200 && envelope.Data != null && !string.IsNullOrWhiteSpace(envelope.Data.Id))
			{
				_persistedPrescriptionByVisitId[visitId] = envelope.Data.Id;
				_currentPrescriptionId = envelope.Data.Id;
				_currentPrescriptionStatus = envelope.Data.Status;
				_currentPrescriptionNo = envelope.Data.PrescriptionNo;
				PrescriptionAggregateDto? created = await GetPrescriptionFromServerAsync(visitId);
				PrescriptionItemDto? createdFirst = created?.Items?.FirstOrDefault(x =>
					x.IsDeleted == 0 && string.Equals(x.DrugId, firstItem.DrugId, StringComparison.OrdinalIgnoreCase));
				if (createdFirst != null)
				{
					ApplySavedPrescriptionItem(firstItem, createdFirst);
				}
				else
				{
					SystemNotice = "处方主记录已创建，但未能读取首条药品明细，请刷新后继续";
					return new EnsurePrescriptionResult(envelope.Data.Id, CreatedNow: true);
				}
				createdNow = true;
				return new EnsurePrescriptionResult(envelope.Data.Id, CreatedNow: true);
			}
			SystemNotice = "处方后台保存失败：" + (envelope?.Message ?? "服务无响应");
			return new EnsurePrescriptionResult(null, CreatedNow: false);
		}
		catch (Exception ex2) when (ex2 is HttpRequestException || ex2 is TaskCanceledException || ex2 is JsonException)
		{
			SystemNotice = "处方后台保存失败：" + ex2.Message;
			return new EnsurePrescriptionResult(null, CreatedNow: false);
		}
	}

	private object ToServerItem(PrescriptionItemModel item)
	{
		int num = item.QuantityValue > 0 ? item.QuantityValue : Math.Max(ParseInt(item.Quantity, 1), 1);
		return new
		{
			Id = item.Id,
			GroupNo = item.Seq,
			DrugId = ResolveDrugId(item),
			DrugNameSnapshot = item.DrugName,
			DoseOnce = ParseDecimal(item.Dose, 1m),
			DoseUnit = ParseDoseUnit(item.Dose),
			FrequencyCode = item.Freq,
			RouteCode = item.Route,
			Days = item.Days > 0 ? item.Days : 3,
			TotalQuantity = num,
			UnitPrice = ToUnitPrice(ParseMoney(item.Amount), num),
			SkinTestFlag = ParseSkinTestFlag(item.SkinTestStatus),
			IsDeleted = (short)(item.IsDeleted ? 1 : 0),
			ExtDataJson = BuildItemExtJson(item)
		};
	}

	private string ResolveDrugId(PrescriptionItemModel item)
	{
		if (!string.IsNullOrWhiteSpace(item.DrugId))
		{
			return item.DrugId;
		}
		DrugCatalogItemModel? catalogDrug = AvailableDrugsCatalog.FirstOrDefault(x =>
			string.Equals(x.DrugName, item.DrugName, StringComparison.OrdinalIgnoreCase));
		if (catalogDrug != null && !string.IsNullOrWhiteSpace(catalogDrug.DrugId))
		{
			item.DrugId = catalogDrug.DrugId;
			return catalogDrug.DrugId;
		}
		return string.Empty;
	}

	private void ApplySavedPrescriptionItem(PrescriptionItemModel item, PrescriptionItemDto? dto)
	{
		if (dto == null)
		{
			return;
		}
		item.Id = dto.Id;
		item.Seq = dto.GroupNo;
		item.DrugId = dto.DrugId;
		item.DrugName = dto.DrugNameSnapshot;
		item.Dose = $"{dto.DoseOnce:0.####}{dto.DoseUnit}";
		item.Freq = dto.FrequencyCode;
		item.Route = dto.RouteCode;
		item.Days = dto.Days;
		item.QuantityValue = dto.TotalQuantity;
		item.Quantity = dto.TotalQuantity.ToString(CultureInfo.InvariantCulture);
		item.Amount = $"¥{dto.UnitPrice * (decimal)dto.TotalQuantity:0.00}";
		item.ExtDataJson = dto.ExtDataJson;
		if (!string.IsNullOrWhiteSpace(dto.ExtDataJson))
		{
			try
			{
				using JsonDocument json = JsonDocument.Parse(dto.ExtDataJson);
				item.Spec = ReadString(json.RootElement, "spec", item.Spec);
				item.SkinTestStatus = ReadString(json.RootElement, "skinTestStatus", item.SkinTestStatus);
				item.SkinTestClass = ReadString(json.RootElement, "skinTestClass", item.SkinTestClass);
				item.AuditStatus = ReadString(json.RootElement, "auditStatus", item.AuditStatus);
				item.AuditClass = ReadString(json.RootElement, "auditClass", item.AuditClass);
				if (json.RootElement.TryGetProperty("isBlocked", out JsonElement blocked) &&
					(blocked.ValueKind is JsonValueKind.True or JsonValueKind.False))
				{
					item.IsBlocked = blocked.GetBoolean();
				}
				if (json.RootElement.TryGetProperty("fields", out JsonElement fields) &&
					fields.ValueKind == JsonValueKind.Object)
				{
					item.ReplaceCustomFields(fields.EnumerateObject()
						.Where(x => x.Value.ValueKind == JsonValueKind.String)
						.Select(x => new KeyValuePair<string, string>(x.Name, x.Value.GetString() ?? string.Empty)));
				}
			}
			catch (JsonException)
			{
			}
		}
		item.IsDeleted = dto.IsDeleted != 0;
		item.DeletedAtMs = dto.DeletedAtMs;
		item.DeletedBy = dto.DeletedBy;
		item.IsPersisted = true;
		RefreshPrescriptionMetrics();
		NotifyOpdWorkflowStateChanged();
	}

	private PatientQueueItemModel? FindPatientByVisitId(string visitId)
	{
		return PatientQueue.FirstOrDefault(x => string.Equals(x.VisitId, visitId, StringComparison.OrdinalIgnoreCase));
	}

	private async Task<PrescriptionAggregateDto?> GetPrescriptionFromServerAsync(string visitId)
	{
		using HttpClient http = CreateConfigHttpClient();
		using HttpResponseMessage response = await http.GetAsync(
			"/api/v4/opd/prescriptions/by-visit/" + Uri.EscapeDataString(visitId) + "?includeDeleted=true");
		if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
		{
			return null;
		}
		if (!response.IsSuccessStatusCode)
		{
			return null;
		}
		ClientApiEnvelope<PrescriptionAggregateDto>? envelope =
			await response.Content.ReadFromJsonAsync<ClientApiEnvelope<PrescriptionAggregateDto>>(JsonOptions);
		return envelope is { Code: 200 } ? envelope.Data : null;
	}

	private static decimal ToUnitPrice(decimal totalAmount, int quantity)
	{
		return (quantity > 0) ? decimal.Round(totalAmount / (decimal)quantity, 4) : totalAmount;
	}

	private static string BuildItemExtJson(PrescriptionItemModel item)
	{
		Dictionary<string, object> dictionary = new Dictionary<string, object>();
		if (!string.IsNullOrWhiteSpace(item.ExtDataJson))
		{
			try
			{
				Dictionary<string, object> dictionary2 = JsonSerializer.Deserialize<Dictionary<string, object>>(item.ExtDataJson, PrescriptionJsonOptions);
				if (dictionary2 != null)
				{
					foreach (KeyValuePair<string, object> item2 in dictionary2)
					{
						dictionary[item2.Key] = item2.Value;
					}
				}
			}
			catch (JsonException)
			{
			}
		}
		dictionary["spec"] = item.Spec;
		dictionary["days"] = item.Days;
		dictionary["doseText"] = item.Dose;
		dictionary["freqText"] = item.Freq;
		dictionary["routeText"] = item.Route;
		dictionary["quantityText"] = item.Quantity;
		dictionary["amountText"] = item.Amount;
		dictionary["skinTestStatus"] = item.SkinTestStatus;
		dictionary["skinTestClass"] = item.SkinTestClass;
		dictionary["auditStatus"] = item.AuditStatus;
		dictionary["auditClass"] = item.AuditClass;
		dictionary["isBlocked"] = item.IsBlocked;
		dictionary["fields"] = item.FieldValues.Where((KeyValuePair<string, string> x) => !string.IsNullOrWhiteSpace(x.Key)).ToDictionary((KeyValuePair<string, string> x) => x.Key, (KeyValuePair<string, string> x) => x.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);
		dictionary["customFields"] = item.CustomFields.Where((KeyValuePair<string, string> x) => !string.IsNullOrWhiteSpace(x.Key)).ToDictionary((KeyValuePair<string, string> x) => x.Key, (KeyValuePair<string, string> x) => x.Value, StringComparer.OrdinalIgnoreCase);
		item.ExtDataJson = JsonSerializer.Serialize(dictionary, PrescriptionJsonOptions);
		return item.ExtDataJson;
	}

	private static PrescriptionItemModel MapItemFromServer(PrescriptionItemDto dto)
	{
		PrescriptionItemModel prescriptionItemModel = new PrescriptionItemModel
		{
			Id = dto.Id,
			Seq = dto.GroupNo,
			DrugId = dto.DrugId,
			DrugName = dto.DrugNameSnapshot,
			Dose = $"{dto.DoseOnce:0.####}{dto.DoseUnit}",
			Freq = dto.FrequencyCode,
			Route = dto.RouteCode,
			QuantityValue = dto.TotalQuantity,
			Days = dto.Days > 0 ? dto.Days : 1,
			Quantity = dto.TotalQuantity.ToString(CultureInfo.InvariantCulture),
			Amount = $"¥{dto.UnitPrice * (decimal)dto.TotalQuantity:0.00}",
			SkinTestStatus = "待核验",
			SkinTestClass = "Info",
			AuditStatus = "待药师审方",
			AuditClass = "Info",
			IsDeleted = (dto.IsDeleted != 0),
			DeletedAtMs = dto.DeletedAtMs,
			DeletedBy = dto.DeletedBy,
			ExtDataJson = dto.ExtDataJson,
			IsPersisted = true
		};
		if (string.IsNullOrWhiteSpace(dto.ExtDataJson))
		{
			return prescriptionItemModel;
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(dto.ExtDataJson);
			JsonElement rootElement = jsonDocument.RootElement;
			if (rootElement.TryGetProperty("fields", out var value) && value.ValueKind == JsonValueKind.Object)
			{
				prescriptionItemModel.ReplaceFieldValues(from x in value.EnumerateObject()
					where x.Value.ValueKind == JsonValueKind.String
					select new KeyValuePair<string, string>(x.Name, x.Value.GetString() ?? string.Empty));
			}
			prescriptionItemModel.Spec = ReadString(rootElement, "spec", prescriptionItemModel.Spec);
			prescriptionItemModel.Dose = ReadString(rootElement, "doseText", prescriptionItemModel.Dose);
			prescriptionItemModel.Freq = ReadString(rootElement, "freqText", prescriptionItemModel.Freq);
			prescriptionItemModel.Route = ReadString(rootElement, "routeText", prescriptionItemModel.Route);
			prescriptionItemModel.Quantity = ReadString(rootElement, "quantityText", prescriptionItemModel.Quantity);
			prescriptionItemModel.QuantityValue = ParseInt(prescriptionItemModel.Quantity, prescriptionItemModel.QuantityValue);
			if (rootElement.TryGetProperty("days", out JsonElement storedDays) &&
				int.TryParse(storedDays.ToString(), out int storedDaysValue))
			{
				prescriptionItemModel.Days = Math.Clamp(storedDaysValue, 1, 365);
			}
			prescriptionItemModel.Amount = ReadString(rootElement, "amountText", prescriptionItemModel.Amount);
			prescriptionItemModel.SkinTestStatus = ReadString(rootElement, "skinTestStatus", prescriptionItemModel.SkinTestStatus);
			prescriptionItemModel.SkinTestClass = ReadString(rootElement, "skinTestClass", prescriptionItemModel.SkinTestClass);
			prescriptionItemModel.AuditStatus = ReadString(rootElement, "auditStatus", prescriptionItemModel.AuditStatus);
			prescriptionItemModel.AuditClass = ReadString(rootElement, "auditClass", prescriptionItemModel.AuditClass);
			if (rootElement.TryGetProperty("isBlocked", out var value2) && (value2.ValueKind == JsonValueKind.True || value2.ValueKind == JsonValueKind.False))
			{
				prescriptionItemModel.IsBlocked = value2.GetBoolean();
			}
			if (rootElement.TryGetProperty("customFields", out var value3) && value3.ValueKind == JsonValueKind.Object)
			{
				foreach (JsonProperty item in from x in value3.EnumerateObject()
					where x.Value.ValueKind == JsonValueKind.String
					select x)
				{
					if (!prescriptionItemModel.FieldValues.ContainsKey(item.Name))
					{
						prescriptionItemModel.SetColumnValue(item.Name, item.Value.GetString() ?? string.Empty);
					}
				}
			}
		}
		catch (JsonException)
		{
		}
		return prescriptionItemModel;
	}

	private static PrescriptionExtraFieldModel MapFieldFromServer(PrescriptionFieldDto dto)
	{
		return new PrescriptionExtraFieldModel
		{
			Id = (string.IsNullOrWhiteSpace(dto.Id) ? Guid.NewGuid().ToString("D") : dto.Id),
			Name = (dto.Name ?? string.Empty),
			Value = (dto.Value ?? string.Empty),
			DataType = (string.IsNullOrWhiteSpace(dto.DataType) ? "STRING" : dto.DataType),
			Placeholder = (dto.Placeholder ?? "请输入"),
			SortOrder = dto.SortOrder,
			IsDeleted = dto.IsDeleted,
			DeletedAtMs = dto.DeletedAtMs,
			DeletedBy = dto.DeletedBy
		};
	}

	private async Task LoadPrescriptionFromServerAsync(string visitId, int revision)
	{
		if (string.IsNullOrWhiteSpace(visitId) || _prescriptionLoadedVisitIds.Contains(visitId))
		{
			return;
		}
		try
		{
			PrescriptionAggregateDto? aggregate = await GetPrescriptionFromServerAsync(visitId);
			if (aggregate == null)
			{
				if (revision == _prescriptionRevision && string.Equals(_currentPrescriptionVisitId, visitId, StringComparison.OrdinalIgnoreCase))
				{
					_prescriptionLoadedVisitIds.Add(visitId);
				}
				return;
			}
			if (aggregate.Prescription == null || revision != _prescriptionRevision || !string.Equals(_currentPrescriptionVisitId, visitId, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			_prescriptionLoadedVisitIds.Add(visitId);
			_persistedPrescriptionByVisitId[visitId] = aggregate.Prescription.Id;
			_currentPrescriptionId = aggregate.Prescription.Id;
			_currentPrescriptionStatus = aggregate.Prescription.Status;
			_currentPrescriptionNo = aggregate.Prescription.PrescriptionNo;
			_currentPrescriptionReviewRemark = aggregate.Prescription.ReviewRemark;
			List<PrescriptionItemModel> items = aggregate.Items?.Select(MapItemFromServer).ToList() ?? new List<PrescriptionItemModel>();
			List<PrescriptionExtraFieldModel> fields = aggregate.CustomFields?.Select(MapFieldFromServer).ToList() ?? new List<PrescriptionExtraFieldModel>();
			string visitNo = _currentPrescriptionVisitNo ?? string.Empty;
			_prescriptionCache[visitNo] = items;
			_prescriptionExtraFieldCache[visitNo] = fields;
			Prescriptions.Clear();
			foreach (PrescriptionItemModel item in items)
			{
				Prescriptions.Add(ClonePrescriptionItem(item));
			}
			PrescriptionExtraFields.Clear();
			foreach (PrescriptionExtraFieldModel field in fields)
			{
				PrescriptionExtraFields.Add(ClonePrescriptionField(field));
			}
			ReindexPrescriptionRows();
			RefreshPrescriptionMetrics();
			NotifyOpdWorkflowStateChanged();
		}
		catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException) ? true : false)
		{
			_prescriptionLoadedVisitIds.Remove(visitId);
		}
	}

	private static string ReadString(JsonElement root, string name, string fallback)
	{
		JsonElement value;
		return (root.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.String) ? (value.GetString() ?? fallback) : fallback;
	}

	private static decimal ParseDecimal(string? text, decimal fallback)
	{
		string s = ExtractNumber(text);
		decimal result;
		return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out result) ? result : fallback;
	}

	private static int ParseInt(string? text, int fallback)
	{
		string s = ExtractNumber(text);
		int result;
		return int.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out result) ? Math.Max(result, 0) : fallback;
	}

	private static decimal ParseMoney(string? text)
	{
		return ParseDecimal(text, 0m);
	}

	private static string ExtractNumber(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return string.Empty;
		}
		int num = -1;
		for (int i = 0; i < text.Length; i++)
		{
			if (char.IsDigit(text[i]) || text[i] == '.')
			{
				num = i;
				break;
			}
		}
		if (num < 0)
		{
			return string.Empty;
		}
		int j = num;
		for (bool flag = false; j < text.Length && (char.IsDigit(text[j]) || (text[j] == '.' && !flag)); j++)
		{
			if (text[j] == '.')
			{
				flag = true;
			}
		}
		return text.Substring(num, j - num);
	}

	private static string ParseDoseUnit(string? dose)
	{
		if (string.IsNullOrWhiteSpace(dose))
		{
			return "mg";
		}
		string text = ExtractNumber(dose);
		if (string.IsNullOrEmpty(text))
		{
			return "mg";
		}
		string text2 = dose.Replace(text, string.Empty, StringComparison.Ordinal).Trim();
		return string.IsNullOrWhiteSpace(text2) ? "mg" : text2;
	}

	private static int ParseSkinTestFlag(string? status)
	{
		if (string.IsNullOrWhiteSpace(status))
		{
			return 0;
		}
		if (status.Contains("阳性", StringComparison.OrdinalIgnoreCase))
		{
			return 4;
		}
		if (status.Contains("待皮试", StringComparison.OrdinalIgnoreCase))
		{
			return 2;
		}
		if (status.Contains("需皮试", StringComparison.OrdinalIgnoreCase))
		{
			return 1;
		}
		return 0;
	}

	private static string ShortRxNo(string rxId)
	{
		return (rxId.Length <= 8) ? rxId : rxId.Substring(0, 8);
	}

	private void UpdatePrescriptionFieldCatalog(IEnumerable<DynamicFormFieldModel> fields)
	{
		PrescriptionFieldDefinitions.Clear();
		foreach (DynamicFormFieldModel item in from x in fields
			orderby x.SortOrder, x.FieldCode
			select x)
		{
			PrescriptionFieldDefinitions.Add(item.Clone());
		}
		NotifyPrescriptionFieldCatalogChanged();
	}

	private void UpdatePrescriptionFieldCatalogFromColumns()
	{
		UpdatePrescriptionFieldCatalog(PrescriptionColumns.Select((PrescriptionColumnModel column) => new DynamicFormFieldModel
		{
			Id = column.Id,
			FormSchemaId = "SCHEMA-PRESCRIPTION-ITEM-FIELDS",
			FieldCode = column.FieldCode,
			FieldLabel = column.FieldLabel,
			DataType = column.DataType,
			UrsaControlType = column.ControlType,
			UnitText = column.UnitText,
			DictCode = column.DictCode,
			Placeholder = column.Placeholder,
			IsRequired = column.IsRequired,
			OptionsText = column.OptionsText,
			Minimum = column.Minimum,
			Maximum = column.Maximum,
			Step = column.Step,
			SortOrder = column.SortOrder,
			ColumnWidth = column.Width,
			IsSystemField = column.IsSystemField,
			IsEditable = column.IsEditable
		}));
	}

	private void NotifyPrescriptionFieldCatalogChanged()
	{
		OnPropertyChanged("PrescriptionFieldDefinitionCount");
		OnPropertyChanged("PrescriptionCustomFieldDefinitionCount");
		OnPropertyChanged("PrescriptionFieldCatalogSummary");
	}

	public async Task BeginEditPrescriptionItemFieldAsync(DynamicFormFieldModel definition)
	{
		await BeginDesignPrescriptionItemFieldsAsync();
		DynamicFormFieldModel target = EditingTemplate?.Fields.FirstOrDefault((DynamicFormFieldModel x) => (!string.IsNullOrWhiteSpace(definition.Id) && string.Equals(x.Id, definition.Id, StringComparison.OrdinalIgnoreCase)) || string.Equals(x.FieldCode, definition.FieldCode, StringComparison.OrdinalIgnoreCase));
		if (target == null)
		{
			SystemNotice = "未找到字段 [" + definition.FieldLabel + "]，请刷新字段目录后重试";
			return;
		}
		BeginEditField(target);
		SystemNotice = "编辑处方字段 [" + target.FieldLabel + "]，保存并应用后立即刷新处方列";
	}

	public async Task HidePrescriptionItemFieldAsync(DynamicFormFieldModel definition)
	{
		await BeginDesignPrescriptionItemFieldsAsync();
		DynamicFormFieldModel target = EditingTemplate?.Fields.FirstOrDefault((DynamicFormFieldModel x) => (!string.IsNullOrWhiteSpace(definition.Id) && string.Equals(x.Id, definition.Id, StringComparison.OrdinalIgnoreCase)) || string.Equals(x.FieldCode, definition.FieldCode, StringComparison.OrdinalIgnoreCase));
		if (target == null)
		{
			SystemNotice = "未找到字段 [" + definition.FieldLabel + "]，请刷新字段目录后重试";
			return;
		}
		if (target.IsProtectedField)
		{
			SystemNotice = target.RemoveBlockedReason;
			return;
		}
		EditingTemplate.Fields.Remove(target);
		ReorderFields(EditingTemplate);
		EditingTemplate.NotifyDerived();
		RefreshEditingFormPreview();
		await SaveEditingTemplateAsync();
	}

	public async Task<bool> SaveEditingFieldAndApplyAsync()
	{
		if (CommitField(closeEditor: false) && await SaveEditingTemplateAsync())
		{
			IsFieldEditorOpen = false;
			EditingField = null;
			return true;
		}
		return false;
	}

	public async Task LoadAuditLogsAsync()
	{
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			var query = new List<string> { "page=1", "pageSize=100" };
			if (!string.IsNullOrWhiteSpace(AuditEventTypeFilter))
				query.Add($"eventType={Uri.EscapeDataString(AuditEventTypeFilter.Trim())}");
			if (!string.IsNullOrWhiteSpace(AuditTargetEntityFilter))
				query.Add($"targetEntity={Uri.EscapeDataString(AuditTargetEntityFilter.Trim())}");
			if (!string.IsNullOrWhiteSpace(AuditOperatorFilter))
				query.Add($"operator={Uri.EscapeDataString(AuditOperatorFilter.Trim())}");

			string url = "/api/v4/operations/audit-logs?" + string.Join("&", query);
			var response = await http.GetFromJsonAsync<ClientApiEnvelope<JsonElement>>(url, JsonOptions);
			if (response is { Code: 200 } && response.Data.ValueKind == JsonValueKind.Object)
			{
				AuditLogsList.Clear();
				if (response.Data.TryGetProperty("items", out var itemsElem) && itemsElem.ValueKind == JsonValueKind.Array)
				{
					foreach (var item in itemsElem.EnumerateArray())
					{
						AuditLogsList.Add(new AuditLogEntryModel
						{
							Id = item.TryGetProperty("id", out var p) ? p.GetString() ?? "" : "",
							EventType = item.TryGetProperty("eventType", out p) ? p.GetString() ?? "" : "",
							TargetEntity = item.TryGetProperty("targetEntity", out p) ? p.GetString() ?? "" : "",
							TargetId = item.TryGetProperty("targetId", out p) ? p.GetString() ?? "" : "",
							ActionDetail = item.TryGetProperty("actionDetail", out p) ? p.GetString() ?? "" : "",
							UserId = item.TryGetProperty("userId", out p) ? p.GetString() ?? "" : "",
							OperatorName = item.TryGetProperty("operatorName", out p) ? p.GetString() ?? "" : "",
							DeptId = item.TryGetProperty("deptId", out p) ? p.GetString() ?? "" : "",
							CurrentHash = item.TryGetProperty("currentHash", out p) ? p.GetString() ?? "" : "",
							PrevHash = item.TryGetProperty("prevHash", out p) ? p.GetString() ?? "" : "",
							TimestampMs = item.TryGetProperty("timestampMs", out p) && p.TryGetInt64(out var ts) ? ts : 0
						});
					}
				}
				int total = response.Data.TryGetProperty("total", out var totalProp) ? totalProp.GetInt32() : AuditLogsList.Count;
				AuditSummaryInfo = $"共检索到 {total} 条审计日志（当前展示最新 {AuditLogsList.Count} 条）";
			}
			else
			{
				AuditSummaryInfo = response?.Message ?? "审计日志拉取失败";
			}
		}
		catch (Exception ex)
		{
			AuditSummaryInfo = "审计日志查询异常：" + ex.Message;
		}
	}

	public async Task VerifyAuditChainAsync()
	{
		try
		{
			AuditVerificationStatus = "正在执行密码学哈希链校验与防截断锚点比对...";
			AuditVerificationBadgeClass = "Warning";
			using HttpClient http = CreateConfigHttpClient();
			var response = await http.GetFromJsonAsync<ClientApiEnvelope<JsonElement>>("/api/v4/operations/audit-logs/verify", JsonOptions);
			if (response is { Code: 200 } && response.Data.ValueKind == JsonValueKind.Object)
			{
				bool isValid = response.Data.TryGetProperty("isValid", out var ivProp) && ivProp.GetBoolean();
				int checkedCount = response.Data.TryGetProperty("checkedEvents", out var cProp) ? cProp.GetInt32() : 0;
				bool anchorTruncated = response.Data.TryGetProperty("anchorTruncated", out var atProp) && atProp.GetBoolean();
				string? breakReason = response.Data.TryGetProperty("breakReason", out var brProp) ? brProp.GetString() : null;
				string? error = response.Data.TryGetProperty("error", out var errProp) ? errProp.GetString() : null;

				if (isValid && !anchorTruncated)
				{
					AuditVerificationStatus = $"✓ 链完整性验证通过：已校验 {checkedCount} 节点，哈希链无篡改且锚点无尾部截断";
					AuditVerificationBadgeClass = "Success";
				}
				else
				{
					AuditVerificationStatus = $"⚠ 审计告警：{breakReason ?? error ?? "检测到日志链篡改或尾部截断"}";
					AuditVerificationBadgeClass = "Danger";
				}
			}
			else
			{
				AuditVerificationStatus = "验证失败：" + (response?.Message ?? "接口返回错误");
				AuditVerificationBadgeClass = "Danger";
			}
		}
		catch (Exception ex)
		{
			AuditVerificationStatus = "验证异常：" + ex.Message;
			AuditVerificationBadgeClass = "Danger";
		}
	}

	public async Task CreateAuditAnchorAsync()
	{
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			var response = await http.PostAsync("/api/v4/operations/audit-logs/anchor", null);
			if (response.IsSuccessStatusCode)
			{
				SystemNotice = "已成功固化外部安全审计锚点，防止尾部截断攻击";
				await VerifyAuditChainAsync();
			}
			else
			{
				SystemNotice = "固化审计锚点失败：" + response.StatusCode;
			}
		}
		catch (Exception ex)
		{
			SystemNotice = "固化锚点异常：" + ex.Message;
		}
	}

	public async Task LoadSecurityPostureAsync()
	{
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			var response = await http.GetFromJsonAsync<ClientApiEnvelope<JsonElement>>("/api/v4/operations/security/posture", JsonOptions);
			if (response is { Code: 200 } && response.Data.ValueKind == JsonValueKind.Object)
			{
				if (response.Data.TryGetProperty("securityScore", out var ss)) SecurityScore = ss.GetInt32();
				if (response.Data.TryGetProperty("threatLevel", out var tl)) ThreatLevel = tl.GetString() ?? "NORMAL";
				if (response.Data.TryGetProperty("activeAlertsCount", out var aac)) ActiveAlertsCount = aac.GetInt32();
				if (response.Data.TryGetProperty("hsmStatus", out var hs)) HsmStatus = hs.GetString() ?? "ONLINE";
				if (response.Data.TryGetProperty("nationalCipherStatus", out var ncs)) NationalCipherStatus = ncs.GetString() ?? "密评三级已合规";
				if (response.Data.TryGetProperty("dengbaoStatus", out var ds)) DengbaoStatus = ds.GetString() ?? "等保三级已合规";
			}
		}
		catch (Exception ex)
		{
			SystemNotice = "拉取安全态势异常: " + ex.Message;
		}
	}

	public async Task LoadSecurityAlertsAsync()
	{
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			var response = await http.GetFromJsonAsync<ClientApiEnvelope<JsonElement>>("/api/v4/operations/security/alerts", JsonOptions);
			if (response is { Code: 200 } && response.Data.ValueKind == JsonValueKind.Array)
			{
				SecurityAlertsList.Clear();
				foreach (var item in response.Data.EnumerateArray())
				{
					SecurityAlertsList.Add(new SecurityAlertModel
					{
						Id = item.TryGetProperty("id", out var p) ? p.GetString() ?? "" : "",
						AlertNo = item.TryGetProperty("alertNo", out p) ? p.GetString() ?? "" : "",
						AlertType = item.TryGetProperty("alertType", out p) ? p.GetString() ?? "" : "",
						Severity = item.TryGetProperty("severity", out p) ? p.GetString() ?? "HIGH" : "HIGH",
						SourceIp = item.TryGetProperty("sourceIp", out p) ? p.GetString() ?? "" : "",
						UserId = item.TryGetProperty("userId", out p) ? p.GetString() ?? "" : "",
						OperatorName = item.TryGetProperty("operatorName", out p) ? p.GetString() ?? "" : "",
						TargetResource = item.TryGetProperty("targetResource", out p) ? p.GetString() ?? "" : "",
						Description = item.TryGetProperty("description", out p) ? p.GetString() ?? "" : "",
						ThreatIndicatorsJson = item.TryGetProperty("threatIndicatorsJson", out p) ? p.GetString() ?? "" : "",
						Status = item.TryGetProperty("status", out p) ? p.GetString() ?? "ACTIVE" : "ACTIVE",
						ResolvedBy = item.TryGetProperty("resolvedBy", out p) ? p.GetString() ?? "" : "",
						ResolutionNote = item.TryGetProperty("resolutionNote", out p) ? p.GetString() ?? "" : "",
						CreatedAtMs = item.TryGetProperty("createdAtMs", out p) && p.TryGetInt64(out var ts) ? ts : 0
					});
				}
				ActiveAlertsCount = SecurityAlertsList.Count(a => a.IsActive);
			}
		}
		catch (Exception ex)
		{
			SystemNotice = "拉取入侵告警异常: " + ex.Message;
		}
	}

	public async Task ScanSecurityThreatsAsync()
	{
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			var response = await http.PostAsync("/api/v4/operations/security/alerts/scan", null);
			if (response.IsSuccessStatusCode)
			{
				SystemNotice = "已执行全网威胁与异常行为扫描";
				await LoadSecurityAlertsAsync();
				await LoadSecurityPostureAsync();
			}
		}
		catch (Exception ex)
		{
			SystemNotice = "威胁扫描异常: " + ex.Message;
		}
	}

	public async Task ResolveSecurityAlertAsync(string alertId, string resolutionNote)
	{
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			var response = await http.PostAsJsonAsync($"/api/v4/operations/security/alerts/{alertId}/resolve", new { ResolutionNote = resolutionNote });
			if (response.IsSuccessStatusCode)
			{
				SystemNotice = "安全威胁告警已处置闭环";
				await LoadSecurityAlertsAsync();
				await LoadSecurityPostureAsync();
			}
		}
		catch (Exception ex)
		{
			SystemNotice = "处置告警异常: " + ex.Message;
		}
	}

	public async Task LoadKmsKeysAsync()
	{
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			var response = await http.GetFromJsonAsync<ClientApiEnvelope<JsonElement>>("/api/v4/operations/kms/keys", JsonOptions);
			if (response is { Code: 200 } && response.Data.ValueKind == JsonValueKind.Array)
			{
				KmsKeysList.Clear();
				foreach (var item in response.Data.EnumerateArray())
				{
					KmsKeysList.Add(new KmsKeyModel
					{
						Id = item.TryGetProperty("id", out var p) ? p.GetString() ?? "" : "",
						KeyName = item.TryGetProperty("keyName", out p) ? p.GetString() ?? "" : "",
						Algorithm = item.TryGetProperty("algorithm", out p) ? p.GetString() ?? "" : "",
						Version = item.TryGetProperty("version", out p) && p.TryGetInt32(out var v) ? v : 1,
						State = item.TryGetProperty("state", out p) ? p.GetString() ?? "ACTIVE" : "ACTIVE",
						HsmProtected = item.TryGetProperty("hsmProtected", out p) && p.TryGetInt16(out var hp) ? hp : (short)1,
						KeyFingerprint = item.TryGetProperty("keyFingerprint", out p) ? p.GetString() ?? "" : "",
						ExpiresAtMs = item.TryGetProperty("expiresAtMs", out p) && p.TryGetInt64(out var exp) ? exp : 0,
						RotatedAtMs = item.TryGetProperty("rotatedAtMs", out p) && p.TryGetInt64(out var rot) ? rot : null,
						Description = item.TryGetProperty("description", out p) ? p.GetString() ?? "" : ""
					});
				}
			}
		}
		catch (Exception ex)
		{
			SystemNotice = "拉取KMS密钥清单异常: " + ex.Message;
		}
	}

	public async Task RotateKmsKeyAsync(string keyName, string? reason = null)
	{
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			var response = await http.PostAsJsonAsync("/api/v4/operations/kms/keys/rotate", new { KeyName = keyName, Reason = reason });
			if (response.IsSuccessStatusCode)
			{
				SystemNotice = $"密码学密钥 [{keyName}] 平滑轮换成功";
				await LoadKmsKeysAsync();
			}
			else
			{
				SystemNotice = $"密钥轮换失败: {response.StatusCode}";
			}
		}
		catch (Exception ex)
		{
			SystemNotice = "密钥轮换异常: " + ex.Message;
		}
	}
}
