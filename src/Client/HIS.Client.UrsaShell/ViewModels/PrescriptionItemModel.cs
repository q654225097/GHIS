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

	private static readonly HashSet<string> BuiltInFieldCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DRUG_NAME", "SPEC", "DOSE", "FREQ", "ROUTE", "DAYS", "QUANTITY", "AMOUNT", "SKIN_TEST_STATUS", "AUDIT_STATUS" };

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
		}
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

		int nextOrder = list.Count == 0 ? 0 : list.Max(x => x.SortOrder) + 1;
		int quantityIndex = -1;
		for (int index = 0; index < list.Count; index++)
		{
			if (list[index].FieldCode.Equals(QuantityCode, StringComparison.OrdinalIgnoreCase))
			{
				quantityIndex = index;
				break;
			}
		}
		void Ensure(string code, string label, string controlType, int width, int order)
		{
			if (list.Any(x => x.FieldCode.Equals(code, StringComparison.OrdinalIgnoreCase)))
			{
				return;
			}
			list.Add(new PrescriptionColumnModel
			{
				FieldCode = code,
				FieldLabel = label,
				DataType = controlType.Contains("Numeric", StringComparison.Ordinal) ? "NUMBER" : "STRING",
				ControlType = controlType,
				Width = width,
				SortOrder = order,
				IsSystemField = true,
				IsEditable = true,
				Placeholder = label
			});
		}

		int insertAt = quantityIndex >= 0 ? quantityIndex : nextOrder;
		Ensure(DoseCode, "单次剂量", "u:TextBox", 100, insertAt);
		Ensure(FreqCode, "频次", "u:TextBox", 120, insertAt + 1);
		Ensure(DaysCode, "天数", "u:NumericIntUpDown", 88, insertAt + 2);
		Ensure(RouteCode, "用法", "u:TextBox", 100, insertAt + 3);
		Ensure(QuantityCode, "数量", "u:NumericIntUpDown", 88, insertAt + 4);

		PrescriptionColumnModel[] ordered = list.OrderBy(x => x.SortOrder).ToArray();
		for (int index = 0; index < ordered.Length; index++)
		{
			ordered[index].SortOrder = index;
		}
	}

	private static bool IsEditableCoreField(string fieldCode)
	{
		return fieldCode.Equals(DoseCode, StringComparison.OrdinalIgnoreCase)
			|| fieldCode.Equals(FreqCode, StringComparison.OrdinalIgnoreCase)
			|| fieldCode.Equals(RouteCode, StringComparison.OrdinalIgnoreCase)
			|| fieldCode.Equals(DaysCode, StringComparison.OrdinalIgnoreCase)
			|| fieldCode.Equals(QuantityCode, StringComparison.OrdinalIgnoreCase);
	}

	public string GetColumnValue(string fieldCode)
	{
		if (string.IsNullOrWhiteSpace(fieldCode))
		{
			return string.Empty;
		}
		string value;
		return _fieldValues.TryGetValue(fieldCode, out value) ? value : string.Empty;
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

	private void RecalculateQuantityFromFrequency(int? daysOverride = null)
	{
		// 数量 = 用药天数 x 每日给药次数；处方行内修改频次或天数时立即同步。
		int days = daysOverride ?? Math.Max(ParseDraftInteger(EditDraft.GetValue("DAYS"), Days), 1);
		int dailyCount = Math.Max(1, ParseDailyFrequencyCount(EditDraft.GetValue("FREQ", Freq)));
		Days = days;
		QuantityValue = days * dailyCount;
		EditDraft.SetValue("DAYS", days.ToString());
		SetColumnValue("QUANTITY", QuantityValue.ToString());
		EditDraft.SetValue("QUANTITY", QuantityValue.ToString());
	}
}
