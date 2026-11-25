using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class PrescriptionItemModel : ClientModelBase
{
	public const string DrugNameCode = "DRUG_NAME";

	public const string SpecCode = "SPEC";

	public const string DoseCode = "DOSE";

	public const string FreqCode = "FREQ";

	public const string RouteCode = "ROUTE";

	public const string DaysCode = "DAYS";

	public const string QuantityCode = "QUANTITY";

	public const string AmountCode = "AMOUNT";

	public const string SkinTestStatusCode = "SKIN_TEST_STATUS";

	public const string AuditStatusCode = "AUDIT_STATUS";

	private static readonly HashSet<string> BuiltInFieldCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"DRUG_NAME", "SPEC", "DOSE", "FREQ", "ROUTE", "DAYS", "QUANTITY", "AMOUNT", "SKIN_TEST_STATUS", "AUDIT_STATUS",
		"ORDER_TYPE", "GROUP_NO", "SKIN_TEST", "TOTAL_QTY", "TOTAL_UNIT", "SINGLE_DOSE", "DOSE_UNIT", "PKG_RATIO",
		"PAYMENT_COUNT", "FREQ_CODE", "ROUTE_NAME", "HOSP_INJECT_CNT", "DOCTOR_NAME", "EXEC_DEPT", "IS_URGENT",
		"EXAM_PART", "SPECIMEN_TYPE", "DEDUCT_DEPT", "MEDICATION_NOTE", "OPERATOR", "OPD_DEPT", "CREATED_AT",
		"STOP_TIME", "STOP_DOCTOR"
	};

	private readonly Dictionary<string, string> _fieldValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private int _seq;
	private string _skinTestClass = "Info";
	private string _auditClass = "Info";
	private bool _isBlocked;
	private bool _isEditing;
	private bool _isDeleted;
	private bool _isEditable = true;
	private string _customFieldsSummary = string.Empty;
	private string? _extDataJson;

	private string _orderType = "西药";
	private string _groupNo = string.Empty;
	private string _groupNoText = "-";
	private string _skinTest = "免试";
	private string _totalUnit = "盒";
	private string _singleDose = "1";
	private string _doseUnit = "片";
	private string _packageRatio = "0.250 g";
	private int _paymentCount = 1;
	private string _freqCode = "tid";
	private string _routeName = "口服";
	private int _hospitalInjectionCount = 0;
	private string _doctorName = !string.IsNullOrWhiteSpace(SessionContext.Current.DisplayName) ? SessionContext.Current.DisplayName : "主治医师";
	private string _execDeptName = "门诊西药房";
	private bool _isUrgent = false;
	private string _examBodyPart = string.Empty;
	private string _specimenType = string.Empty;
	private string _deductStockDept = "门诊西药房";
	private string _medicationNote = string.Empty;
	private string _operatorName = !string.IsNullOrWhiteSpace(SessionContext.Current.DisplayName) ? SessionContext.Current.DisplayName : "录入人";
	private string _deptName = "心血管门诊";
	private string _createdAtText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
	private string _stopTimeText = "--";
	private string _stopDoctor = "--";
	private bool _isSelected = false;

	public string Id { get; set; } = Guid.NewGuid().ToString("D");

	public int Seq
	{
		get
		{
			return _seq;
		}
		set
		{
			SetField(ref _seq, value, "Seq");
		}
	}

	public string DrugName
	{
		get
		{
			return GetColumnValue("DRUG_NAME");
		}
		set
		{
			SetColumnValue("DRUG_NAME", value);
		}
	}

	public string Spec
	{
		get
		{
			return GetColumnValue("SPEC");
		}
		set
		{
			SetColumnValue("SPEC", value);
		}
	}

	public string Dose
	{
		get
		{
			return GetColumnValue("DOSE");
		}
		set
		{
			SetColumnValue("DOSE", value);
		}
	}

	public string Freq
	{
		get
		{
			return GetColumnValue("FREQ");
		}
		set
		{
			string frequency = value ?? string.Empty;
			SetColumnValue("FREQ", frequency);
			if (IsEditing)
			{
				EditDraft.SetValue("FREQ", frequency);
				RecalculateQuantityFromFrequency();
			}
		}
	}

	public string Route
	{
		get
		{
			return GetColumnValue("ROUTE");
		}
		set
		{
			SetColumnValue("ROUTE", value);
		}
	}

	public string DaysText
	{
		get
		{
			return DaysValue.ToString();
		}
		set
		{
			if (int.TryParse(value, out int parsed))
			{
				DaysValue = parsed;
			}
		}
	}

	public string Quantity
	{
		get
		{
			return GetColumnValue("QUANTITY");
		}
		set
		{
			SetColumnValue("QUANTITY", value);
		}
	}

	public string Amount
	{
		get
		{
			return GetColumnValue("AMOUNT");
		}
		set
		{
			SetColumnValue("AMOUNT", value);
			OnPropertyChanged(nameof(AmountNumeric));
		}
	}

	public decimal AmountNumeric
	{
		get
		{
			var raw = Amount?.Replace("¥", "").Trim();
			return decimal.TryParse(raw, out var v) ? v : 0m;
		}
	}

	private bool _isPaid;
	public bool IsPaid
	{
		get => _isPaid;
		set => SetField(ref _isPaid, value, nameof(IsPaid));
	}

	public string SkinTestStatus
	{
		get
		{
			return GetColumnValue("SKIN_TEST_STATUS");
		}
		set
		{
			SetColumnValue("SKIN_TEST_STATUS", value);
		}
	}

	public string SkinTestClass
	{
		get
		{
			return _skinTestClass;
		}
		set
		{
			SetField(ref _skinTestClass, value, "SkinTestClass");
		}
	}

	public string AuditStatus
	{
		get
		{
			return GetColumnValue("AUDIT_STATUS");
		}
		set
		{
			SetColumnValue("AUDIT_STATUS", value);
		}
	}

	public string AuditClass
	{
		get
		{
			return _auditClass;
		}
		set
		{
			SetField(ref _auditClass, value, "AuditClass");
		}
	}

	public bool IsBlocked
	{
		get
		{
			return _isBlocked;
		}
		set
		{
			SetField(ref _isBlocked, value, "IsBlocked");
		}
	}

	public string CustomFieldsSummary
	{
		get
		{
			return _customFieldsSummary;
		}
		set
		{
			SetField(ref _customFieldsSummary, value, "CustomFieldsSummary");
		}
	}

	public string? ExtDataJson
	{
		get
		{
			return _extDataJson;
		}
		set
		{
			SetField(ref _extDataJson, value, "ExtDataJson");
		}
	}

	public string OrderType
	{
		get => GetColumnValue("ORDER_TYPE") is { Length: > 0 } v ? v : _orderType;
		set { _orderType = value; SetColumnValue("ORDER_TYPE", value); }
	}

	public string GroupNo
	{
		get => GetColumnValue("GROUP_NO") is { Length: > 0 } v ? v : _groupNo;
		set { _groupNo = value; SetColumnValue("GROUP_NO", value); OnPropertyChanged(nameof(GroupNoDisplay)); OnPropertyChanged(nameof(GroupNoText)); }
	}

	public string GroupNoText
	{
		get => !string.IsNullOrWhiteSpace(_groupNoText) && _groupNoText != "-" ? _groupNoText : (string.IsNullOrWhiteSpace(GroupNo) ? "-" : $"组{GroupNo}");
		set => SetField(ref _groupNoText, value, nameof(GroupNoText));
	}

	public string GroupNoDisplay => GroupNoText;

	public string SkinTest
	{
		get => GetColumnValue("SKIN_TEST") is { Length: > 0 } v ? v : _skinTest;
		set { _skinTest = value; SetColumnValue("SKIN_TEST", value); }
	}

	public string TotalQuantity
	{
		get => Quantity;
		set => Quantity = value;
	}

	public string TotalUnit
	{
		get => GetColumnValue("TOTAL_UNIT") is { Length: > 0 } v ? v : _totalUnit;
		set { _totalUnit = value; SetColumnValue("TOTAL_UNIT", value); }
	}

	public string SingleDose
	{
		get => GetColumnValue("SINGLE_DOSE") is { Length: > 0 } v ? v : (string.IsNullOrWhiteSpace(Dose) ? _singleDose : Dose);
		set { _singleDose = value; SetColumnValue("SINGLE_DOSE", value); Dose = value; }
	}

	public string DoseUnit
	{
		get => GetColumnValue("DOSE_UNIT") is { Length: > 0 } v ? v : _doseUnit;
		set { _doseUnit = value; SetColumnValue("DOSE_UNIT", value); }
	}

	public string PackageRatio
	{
		get => GetColumnValue("PKG_RATIO") is { Length: > 0 } v ? v : _packageRatio;
		set { _packageRatio = value; SetColumnValue("PKG_RATIO", value); }
	}

	public int PaymentCount
	{
		get => int.TryParse(GetColumnValue("PAYMENT_COUNT"), out var c) ? c : _paymentCount;
		set { _paymentCount = value; SetColumnValue("PAYMENT_COUNT", value.ToString()); }
	}

	public string FrequencyCode
	{
		get => GetColumnValue("FREQ_CODE") is { Length: > 0 } v ? v : (string.IsNullOrWhiteSpace(Freq) ? _freqCode : Freq);
		set { _freqCode = value; SetColumnValue("FREQ_CODE", value); Freq = value; }
	}

	public string RouteName
	{
		get => GetColumnValue("ROUTE_NAME") is { Length: > 0 } v ? v : (string.IsNullOrWhiteSpace(Route) ? _routeName : Route);
		set { _routeName = value; SetColumnValue("ROUTE_NAME", value); Route = value; }
	}

	public int HospitalInjectionCount
	{
		get => int.TryParse(GetColumnValue("HOSP_INJECT_CNT"), out var c) ? c : _hospitalInjectionCount;
		set { _hospitalInjectionCount = value; SetColumnValue("HOSP_INJECT_CNT", value.ToString()); }
	}

	public string DoctorName
	{
		get => GetColumnValue("DOCTOR_NAME") is { Length: > 0 } v ? v : _doctorName;
		set { _doctorName = value; SetColumnValue("DOCTOR_NAME", value); }
	}

	public string ExecDeptName
	{
		get => GetColumnValue("EXEC_DEPT") is { Length: > 0 } v ? v : _execDeptName;
		set { _execDeptName = value; SetColumnValue("EXEC_DEPT", value); }
	}

	public bool IsUrgent
	{
		get => _isUrgent || string.Equals(GetColumnValue("IS_URGENT"), "1", StringComparison.OrdinalIgnoreCase);
		set { SetField(ref _isUrgent, value, nameof(IsUrgent)); SetColumnValue("IS_URGENT", value ? "1" : "0"); OnPropertyChanged(nameof(IsUrgentText)); }
	}

	public string IsUrgentText => IsUrgent ? "加急" : "常规";

	public string ExamBodyPart
	{
		get => GetColumnValue("EXAM_PART") is { Length: > 0 } v ? v : _examBodyPart;
		set { _examBodyPart = value; SetColumnValue("EXAM_PART", value); }
	}

	public string SpecimenType
	{
		get => GetColumnValue("SPECIMEN_TYPE") is { Length: > 0 } v ? v : _specimenType;
		set { _specimenType = value; SetColumnValue("SPECIMEN_TYPE", value); }
	}

	public string DeductStockDept
	{
		get => GetColumnValue("DEDUCT_DEPT") is { Length: > 0 } v ? v : _deductStockDept;
		set { _deductStockDept = value; SetColumnValue("DEDUCT_DEPT", value); }
	}

	public string MedicationNote
	{
		get => GetColumnValue("MEDICATION_NOTE") is { Length: > 0 } v ? v : _medicationNote;
		set { _medicationNote = value; SetColumnValue("MEDICATION_NOTE", value); }
	}

	public string OperatorName
	{
		get => GetColumnValue("OPERATOR") is { Length: > 0 } v ? v : _operatorName;
		set { _operatorName = value; SetColumnValue("OPERATOR", value); }
	}

	public string DeptName
	{
		get => GetColumnValue("OPD_DEPT") is { Length: > 0 } v ? v : _deptName;
		set { _deptName = value; SetColumnValue("OPD_DEPT", value); }
	}

	public string CreatedAtText
	{
		get => GetColumnValue("CREATED_AT") is { Length: > 0 } v ? v : _createdAtText;
		set { _createdAtText = value; SetColumnValue("CREATED_AT", value); }
	}

	public string StopTimeText
	{
		get => GetColumnValue("STOP_TIME") is { Length: > 0 } v ? v : _stopTimeText;
		set { _stopTimeText = value; SetColumnValue("STOP_TIME", value); }
	}

	public string StopDoctor
	{
		get => GetColumnValue("STOP_DOCTOR") is { Length: > 0 } v ? v : _stopDoctor;
		set { _stopDoctor = value; SetColumnValue("STOP_DOCTOR", value); }
	}

	public bool IsSelected
	{
		get => _isSelected;
		set => SetField(ref _isSelected, value, nameof(IsSelected));
	}

	public string StatusAlertIcon => IsBlocked ? "❌" : (SkinTestStatus.Contains("需") || SkinTestStatus.Contains("阳") || SkinTest.Contains("需") || SkinTest.Contains("阳") ? "⚠" : (string.IsNullOrWhiteSpace(AuditStatus) ? "✓" : "🛡"));

	// 临时行仅存在于编辑界面；只有服务端返回主键后才允许更新、删除或提交处方。
	public bool IsPersisted { get; set; }

	// 院内药品主键，服务端以此校验目录、价格和库存，不使用界面药品名称反查。
	public string DrugId { get; set; } = string.Empty;

	// 数量按药品最小包装单位保存，天数参与服务端处方校验。
	public int QuantityValue { get; set; } = 1;

	public int Days { get; set; } = 3;

	public int DaysValue
	{
		get => IsEditing ? Math.Max(ParseDraftInteger(EditDraft.GetValue("DAYS"), Days), 1) : Days;
		set
		{
			int normalized = Math.Clamp(value, 1, 365);
			Days = normalized;
			EditDraft.SetValue("DAYS", normalized.ToString());
			RecalculateQuantityFromFrequency(normalized);
			OnPropertyChanged("DaysValue");
			OnPropertyChanged("DaysText");
			OnPropertyChanged("QuantityValue");
			OnPropertyChanged("Quantity");
		}
	}

	public static int ParseDailyFrequencyCount(string? frequency)
	{
		string text = frequency?.Trim() ?? string.Empty;
		string code = text.Split(' ', 2)[0];
		if (code.StartsWith("q", StringComparison.OrdinalIgnoreCase) && code.Length > 2 &&
			code[1..].All(char.IsDigit) && int.TryParse(code[1..], out int interval) && interval > 0)
		{
			return Math.Max(1, 24 / interval);
		}
		if (code.StartsWith("bid", StringComparison.OrdinalIgnoreCase)) return 2;
		if (code.StartsWith("tid", StringComparison.OrdinalIgnoreCase)) return 3;
		if (code.StartsWith("qid", StringComparison.OrdinalIgnoreCase)) return 4;
		return 1;
	}

	private static int ParseDraftInteger(string? text, int fallback) =>
		int.TryParse(text, out int value) ? Math.Max(value, 1) : Math.Max(fallback, 1);

	public long? DeletedAtMs { get; set; }

	public string? DeletedBy { get; set; }

	public PrescriptionItemDraftModel EditDraft { get; } = new PrescriptionItemDraftModel();

	public ObservableCollection<PrescriptionCellModel> Cells { get; } = new ObservableCollection<PrescriptionCellModel>();

	public IReadOnlyDictionary<string, string> FieldValues => _fieldValues;

	public IReadOnlyDictionary<string, string> CustomFields => _fieldValues.Where((KeyValuePair<string, string> x) => !BuiltInFieldCodes.Contains(x.Key)).ToDictionary((KeyValuePair<string, string> x) => x.Key, (KeyValuePair<string, string> x) => x.Value, StringComparer.OrdinalIgnoreCase);

	public bool IsEditing
	{
		get
		{
			return _isEditing;
		}
		set
		{
			if (!SetField(ref _isEditing, value, "IsEditing"))
			{
				return;
			}
			OnPropertyChanged("IsReadOnly");
			foreach (PrescriptionCellModel cell in Cells)
			{
				cell.NotifyItemChanged();
			}
		}
	}

	public bool IsDeleted
	{
		get
		{
			return _isDeleted;
		}
		set
		{
			if (SetField(ref _isDeleted, value, "IsDeleted"))
			{
				OnPropertyChanged("IsVisible");
			}
		}
	}

	public bool IsVisible => !IsDeleted;

	public bool IsReadOnly => !IsEditing;

	public bool IsEditable
	{
		get
		{
			return _isEditable;
		}
		set
		{
			if (SetField(ref _isEditable, value, "IsEditable"))
			{
				OnPropertyChanged("CanEdit");
			}
		}
	}

	public bool CanEdit => IsVisible && IsEditable;

	public static bool IsBuiltInFieldCode(string fieldCode)
	{
		return !string.IsNullOrWhiteSpace(fieldCode) && BuiltInFieldCodes.Contains(fieldCode.Trim());
	}

	public static bool TryGetBuiltInPropertyName(string fieldCode, out string propertyName)
	{
		string text = fieldCode?.Trim().ToUpperInvariant();
		string text2 = text switch
		{
			"DRUG_NAME" => "DrugName", 
			"SPEC" => "Spec", 
			"DOSE" => "Dose", 
			"FREQ" => "Freq", 
			"ROUTE" => "Route", 
			"DAYS" => "DaysValue", 
			"QUANTITY" => "Quantity", 
			"AMOUNT" => "Amount", 
			"SKIN_TEST_STATUS" => "SkinTestStatus", 
			"AUDIT_STATUS" => "AuditStatus", 
			"ORDER_TYPE" => "OrderType",
			"GROUP_NO" => "GroupNo",
			"SKIN_TEST" => "SkinTest",
			"TOTAL_QTY" => "TotalQuantity",
			"TOTAL_UNIT" => "TotalUnit",
			"SINGLE_DOSE" => "SingleDose",
			"DOSE_UNIT" => "DoseUnit",
			"PKG_RATIO" => "PackageRatio",
			"PAYMENT_COUNT" => "PaymentCount",
			"FREQ_CODE" => "FrequencyCode",
			"ROUTE_NAME" => "RouteName",
			"HOSP_INJECT_CNT" => "HospitalInjectionCount",
			"DOCTOR_NAME" => "DoctorName",
			"EXEC_DEPT" => "ExecDeptName",
			"IS_URGENT" => "IsUrgent",
			"EXAM_PART" => "ExamBodyPart",
			"SPECIMEN_TYPE" => "SpecimenType",
			"DEDUCT_DEPT" => "DeductStockDept",
			"MEDICATION_NOTE" => "MedicationNote",
			"OPERATOR" => "OperatorName",
			"OPD_DEPT" => "DeptName",
			"CREATED_AT" => "CreatedAtText",
			"STOP_TIME" => "StopTimeText",
			"STOP_DOCTOR" => "StopDoctor",
			_ => string.Empty, 
		};
		propertyName = text2;
		return propertyName.Length > 0;
	}

	// 剂量、频次、天数、途径与数量构成处方核心字段，不随管理员列配置丢失。
	public static void EnsureCoreEditableColumns(IEnumerable<PrescriptionColumnModel> columns)
	{
		if (columns is not IList<PrescriptionColumnModel> list)
		{
			list = columns.ToList();
		}

		foreach (PrescriptionColumnModel column in list)
		{
			if (IsEditableCoreField(column.FieldCode))
			{
				column.IsEditable = true;
			}
		}

		PrescriptionColumnModel[] ordered = list.OrderBy(x => x.SortOrder).ToArray();
		for (int index = 0; index < ordered.Length; index++)
		{
			ordered[index].SortOrder = index;
		}
	}

	private static bool IsEditableCoreField(string fieldCode)
	{
		return fieldCode.Equals(DoseCode, StringComparison.OrdinalIgnoreCase)
			|| fieldCode.Equals("SINGLE_DOSE", StringComparison.OrdinalIgnoreCase)
			|| fieldCode.Equals(FreqCode, StringComparison.OrdinalIgnoreCase)
			|| fieldCode.Equals("FREQ_CODE", StringComparison.OrdinalIgnoreCase)
			|| fieldCode.Equals(RouteCode, StringComparison.OrdinalIgnoreCase)
			|| fieldCode.Equals("ROUTE_NAME", StringComparison.OrdinalIgnoreCase)
			|| fieldCode.Equals(DaysCode, StringComparison.OrdinalIgnoreCase)
			|| fieldCode.Equals(QuantityCode, StringComparison.OrdinalIgnoreCase)
			|| fieldCode.Equals("TOTAL_QTY", StringComparison.OrdinalIgnoreCase)
			|| fieldCode.Equals(DrugNameCode, StringComparison.OrdinalIgnoreCase);
	}

	public string GetColumnValue(string fieldCode)
	{
		if (string.IsNullOrWhiteSpace(fieldCode))
		{
			return string.Empty;
		}
		string code = fieldCode.Trim().ToUpperInvariant();
		if (_fieldValues.TryGetValue(code, out string value) && !string.IsNullOrWhiteSpace(value))
		{
			return value;
		}
		return code switch
		{
			"ORDER_TYPE" => _orderType,
			"DRUG_NAME" => !string.IsNullOrWhiteSpace(_fieldValues.GetValueOrDefault("DRUG_NAME")) ? _fieldValues["DRUG_NAME"] : string.Empty,
			"SPEC" => !string.IsNullOrWhiteSpace(_fieldValues.GetValueOrDefault("SPEC")) ? _fieldValues["SPEC"] : string.Empty,
			"GROUP_NO" => _groupNo,
			"SKIN_TEST" => _skinTest,
			"SKIN_TEST_STATUS" => !string.IsNullOrWhiteSpace(_fieldValues.GetValueOrDefault("SKIN_TEST_STATUS")) ? _fieldValues["SKIN_TEST_STATUS"] : _skinTest,
			"TOTAL_QTY" or "TOTAL_QUANTITY" or "QUANTITY" => !string.IsNullOrWhiteSpace(_fieldValues.GetValueOrDefault("QUANTITY")) ? _fieldValues["QUANTITY"] : _fieldValues.GetValueOrDefault("TOTAL_QTY", QuantityValue.ToString()),
			"TOTAL_UNIT" => _totalUnit,
			"SINGLE_DOSE" or "DOSE" => !string.IsNullOrWhiteSpace(_fieldValues.GetValueOrDefault("DOSE")) ? _fieldValues["DOSE"] : (!string.IsNullOrWhiteSpace(_fieldValues.GetValueOrDefault("SINGLE_DOSE")) ? _fieldValues["SINGLE_DOSE"] : _singleDose),
			"DOSE_UNIT" => _doseUnit,
			"PKG_RATIO" => _packageRatio,
			"PAYMENT_COUNT" => _paymentCount.ToString(),
			"FREQ_CODE" or "FREQUENCY_CODE" or "FREQ" => !string.IsNullOrWhiteSpace(_fieldValues.GetValueOrDefault("FREQ_CODE")) ? _fieldValues["FREQ_CODE"] : (!string.IsNullOrWhiteSpace(_fieldValues.GetValueOrDefault("FREQ")) ? _fieldValues["FREQ"] : _freqCode),
			"ROUTE_NAME" or "ROUTE" => !string.IsNullOrWhiteSpace(_fieldValues.GetValueOrDefault("ROUTE_NAME")) ? _fieldValues["ROUTE_NAME"] : (!string.IsNullOrWhiteSpace(_fieldValues.GetValueOrDefault("ROUTE")) ? _fieldValues["ROUTE"] : _routeName),
			"HOSP_INJECT_CNT" or "HOSPITAL_INJECTION_COUNT" => _hospitalInjectionCount.ToString(),
			"DOCTOR_NAME" => _doctorName,
			"EXEC_DEPT" or "EXEC_DEPT_NAME" => _execDeptName,
			"IS_URGENT" => _isUrgent ? "加急" : "常规",
			"EXAM_PART" or "EXAM_BODY_PART" => _examBodyPart,
			"SPECIMEN_TYPE" => _specimenType,
			"DEDUCT_DEPT" or "DEDUCT_STOCK_DEPT" => _deductStockDept,
			"MEDICATION_NOTE" => _medicationNote,
			"OPERATOR" or "OPERATOR_NAME" => _operatorName,
			"OPD_DEPT" or "DEPT_NAME" => _deptName,
			"CREATED_AT" or "CREATED_AT_TEXT" => _createdAtText,
			"STOP_TIME" or "STOP_TIME_TEXT" => _stopTimeText,
			"STOP_DOCTOR" => _stopDoctor,
			"AUDIT_STATUS" => !string.IsNullOrWhiteSpace(_fieldValues.GetValueOrDefault("AUDIT_STATUS")) ? _fieldValues["AUDIT_STATUS"] : "待审方",
			"DAYS" => DaysValue.ToString(),
			"AMOUNT" => !string.IsNullOrWhiteSpace(_fieldValues.GetValueOrDefault("AMOUNT")) ? _fieldValues["AMOUNT"] : "¥0.00",
			_ => _fieldValues.TryGetValue(fieldCode, out string extra) ? extra : string.Empty
		};
	}

	public void SetColumnValue(string fieldCode, string? value)
	{
		if (string.IsNullOrWhiteSpace(fieldCode))
		{
			return;
		}
		string text = fieldCode.Trim();
		string text2 = value ?? string.Empty;
		if (!_fieldValues.TryGetValue(text, out string value2) || !string.Equals(value2, text2, StringComparison.Ordinal))
		{
			_fieldValues[text] = text2;
			OnPropertyChanged(text);
			if (TryGetBuiltInPropertyName(text, out string propertyName))
			{
				OnPropertyChanged(propertyName);
			}
			RefreshCells();
		}
	}

	// 处方表格的所有单元格都通过这一个入口写回，频次和天数变化时同步数量。
	public void SetEditingValue(string fieldCode, string? value)
	{
		if (string.IsNullOrWhiteSpace(fieldCode))
		{
			return;
		}

		string code = fieldCode.Trim();
		string text = value ?? string.Empty;
		SetColumnValue(code, text);

		if (code.Equals(FreqCode, StringComparison.OrdinalIgnoreCase))
		{
			EditDraft.SetValue(FreqCode, text);
			if (IsEditing)
			{
				RecalculateQuantityFromFrequency();
			}
		}
		else if (code.Equals(DaysCode, StringComparison.OrdinalIgnoreCase))
		{
			int days = ParseDraftInteger(text, Days);
			Days = days;
			EditDraft.SetValue(DaysCode, days.ToString());
			if (IsEditing)
			{
				RecalculateQuantityFromFrequency(days);
			}
			else
			{
				OnPropertyChanged("DaysValue");
				OnPropertyChanged("DaysText");
			}
		}
		else if (code.Equals(QuantityCode, StringComparison.OrdinalIgnoreCase))
		{
			if (int.TryParse(text, out int quantity))
			{
				QuantityValue = Math.Max(quantity, 1);
			}
			EditDraft.SetValue(QuantityCode, QuantityValue.ToString());
			OnPropertyChanged("QuantityValue");
		}

		if (IsEditing)
		{
			EditDraft.SetValue(code, text);
		}
		RefreshCells();
	}

	public string GetCustomField(string fieldCode)
	{
		return GetColumnValue(fieldCode);
	}

	public void SetCustomField(string fieldCode, string? value)
	{
		SetColumnValue(fieldCode, value);
	}

	public void ReplaceCustomFields(IEnumerable<KeyValuePair<string, string>> fields)
	{
		string[] array = _fieldValues.Keys.Where(key => !IsBuiltInFieldCode(key)).ToArray();
		foreach (string key in array)
		{
			_fieldValues.Remove(key);
		}
		foreach (KeyValuePair<string, string> field in fields)
		{
			if (!string.IsNullOrWhiteSpace(field.Key) && !IsBuiltInFieldCode(field.Key))
			{
				_fieldValues[field.Key.Trim()] = field.Value ?? string.Empty;
			}
		}
		OnPropertyChanged("FieldValues");
		OnPropertyChanged("CustomFields");
		RefreshCells();
	}

	public void ReplaceFieldValues(IEnumerable<KeyValuePair<string, string>> fields)
	{
		_fieldValues.Clear();
		foreach (KeyValuePair<string, string> field in fields)
		{
			if (!string.IsNullOrWhiteSpace(field.Key))
			{
				_fieldValues[field.Key.Trim()] = field.Value ?? string.Empty;
			}
		}
		OnPropertyChanged("FieldValues");
		OnPropertyChanged("CustomFields");
		OnPropertyChanged("DrugName");
		OnPropertyChanged("Spec");
		OnPropertyChanged("Dose");
		OnPropertyChanged("Freq");
		OnPropertyChanged("Route");
				OnPropertyChanged("DaysText");
		OnPropertyChanged("Quantity");
		OnPropertyChanged("Amount");
		OnPropertyChanged("SkinTestStatus");
		OnPropertyChanged("AuditStatus");
		RefreshCells();
	}

	public void RebuildCells(IEnumerable<PrescriptionColumnModel> columns)
	{
		Cells.Clear();
		foreach (PrescriptionColumnModel item in columns.OrderBy((PrescriptionColumnModel x) => x.SortOrder))
		{
			Cells.Add(new PrescriptionCellModel(this, item));
		}
		RefreshCustomFieldsSummary();
	}

	public void RefreshCells()
	{
		foreach (PrescriptionCellModel cell in Cells)
		{
			cell.NotifyItemChanged();
		}
		RefreshCustomFieldsSummary();
	}

	private void RefreshCustomFieldsSummary()
	{
		CustomFieldsSummary = string.Join(" · ", from x in _fieldValues
			where !BuiltInFieldCodes.Contains(x.Key)
			where !string.IsNullOrWhiteSpace(x.Value)
			select x.Key + ": " + x.Value);
	}

	public void NotifyUpdated()
	{
		OnPropertyChanged("Seq");
		OnPropertyChanged("FieldValues");
		OnPropertyChanged("CustomFields");
		OnPropertyChanged("DrugName");
		OnPropertyChanged("Spec");
		OnPropertyChanged("Dose");
		OnPropertyChanged("Freq");
		OnPropertyChanged("Route");
		OnPropertyChanged("DaysText");
		OnPropertyChanged("Quantity");
		OnPropertyChanged("Amount");
		OnPropertyChanged("SkinTestStatus");
		OnPropertyChanged("SkinTestClass");
		OnPropertyChanged("AuditStatus");
		OnPropertyChanged("AuditClass");
		OnPropertyChanged("IsBlocked");
		OnPropertyChanged("CustomFieldsSummary");
		OnPropertyChanged("IsEditing");
		OnPropertyChanged("IsDeleted");
		OnPropertyChanged("IsVisible");
		OnPropertyChanged("IsReadOnly");
		OnPropertyChanged("DaysValue");
		OnPropertyChanged("DaysText");
		OnPropertyChanged("QuantityValue");
	}

	private static (decimal minDose, int packFactor) ParseSpec(string? spec)
	{
		if (string.IsNullOrWhiteSpace(spec)) return (1m, 1);
		decimal minDose = 1m;
		int packFactor = 1;

		var packMatch = System.Text.RegularExpressions.Regex.Match(spec, @"\*\s*(\d+)\s*(?:片|粒|支|袋|丸|瓶|板)?");
		if (packMatch.Success && int.TryParse(packMatch.Groups[1].Value, out int pf) && pf > 0)
		{
			packFactor = pf;
		}

		string dosePart = spec.Split('*')[0].Split('/')[0];
		if (dosePart.Contains(':'))
		{
			dosePart = dosePart.Split(':')[1];
		}
		var doseMatch = System.Text.RegularExpressions.Regex.Match(dosePart, @"(\d+(?:\.\d+)?)\s*(?:mg|g|ml|l|u|单位|片|粒|支|袋)?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
		if (doseMatch.Success && decimal.TryParse(doseMatch.Groups[1].Value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal md) && md > 0)
		{
			if (dosePart.EndsWith("g", StringComparison.OrdinalIgnoreCase) && !dosePart.EndsWith("mg", StringComparison.OrdinalIgnoreCase))
			{
				md *= 1000m;
			}
			minDose = md;
		}

		return (minDose, packFactor);
	}

	private static decimal ParseDose(string? dose)
	{
		if (string.IsNullOrWhiteSpace(dose)) return 1m;
		string trimmed = dose.Trim();
		var match = System.Text.RegularExpressions.Regex.Match(trimmed, @"(\d+(?:\.\d+)?)\s*(?:mg|g|ml|l|u|单位|片|粒|支|袋)?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
		if (match.Success && decimal.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal val))
		{
			if (trimmed.EndsWith("g", StringComparison.OrdinalIgnoreCase) && !trimmed.EndsWith("mg", StringComparison.OrdinalIgnoreCase))
			{
				val *= 1000m;
			}
			return val > 0 ? val : 1m;
		}
		return 1m;
	}

	private void RecalculateQuantityFromFrequency(int? daysOverride = null)
	{
		int days = daysOverride ?? Math.Max(ParseDraftInteger(EditDraft.GetValue("DAYS"), Days), 1);
		int dailyCount = Math.Max(1, ParseDailyFrequencyCount(EditDraft.GetValue("FREQ", Freq)));
		Days = days;

		string specStr = EditDraft.GetValue("SPEC", Spec);
		string doseStr = EditDraft.GetValue("DOSE", Dose);

		var (minDose, packFactor) = ParseSpec(specStr);
		decimal doseOnce = ParseDose(doseStr);

		bool isPieceDose = !string.IsNullOrWhiteSpace(doseStr) &&
			(doseStr.Contains("片") || doseStr.Contains("粒") || doseStr.Contains("支") || doseStr.Contains("袋") || doseStr.Contains("丸"));

		int totalPackages;
		if (isPieceDose)
		{
			decimal totalPieces = doseOnce * dailyCount * days;
			totalPackages = Math.Max(1, (int)Math.Ceiling(totalPieces / Math.Max(1, packFactor)));
		}
		else
		{
			decimal totalDose = doseOnce * dailyCount * days;
			decimal packDose = minDose * packFactor;
			if (packDose > 0)
			{
				totalPackages = Math.Max(1, (int)Math.Ceiling(totalDose / packDose));
			}
			else
			{
				totalPackages = Math.Max(1, (int)Math.Ceiling((decimal)(dailyCount * days) / Math.Max(1, packFactor)));
			}
		}

		QuantityValue = totalPackages;
		EditDraft.SetValue("DAYS", days.ToString());
		SetColumnValue("QUANTITY", QuantityValue.ToString());
		EditDraft.SetValue("QUANTITY", QuantityValue.ToString());
	}
}
