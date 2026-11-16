using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.Config.Models;
using HIS.Modules.Config.Services;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Config.Controllers;

[ApiController]
[Route("api/v4/config")]
public class ConfigController : ControllerBase
{
	public sealed record FormSchemaDetailDto(CfgFormSchema Schema, IReadOnlyList<CfgFieldDefinition> Fields, string? OperatorId = null);

	public sealed record SaveFormSchemaRequest(CfgFormSchema Schema, IReadOnlyList<CfgFieldDefinition> Fields, string? OperatorId = null);

	public sealed record MasterDataSummaryDto(int DepartmentCount, int BedCount, int UserCount, int RoleCount, int DrugCount, int DrugBatchCount, int Icd10DiagnosisCount, int PriceItemCount, int OrderFrequencyCount, int ClinicalDictionaryCount, int EmrDataElementCount, int DynamicFormSchemaCount);

	private const string PrescriptionItemFieldsFormCode = "FORM-PRESCRIPTION-ITEM-FIELDS";

	private const string PrescriptionDrugNameFieldCode = "DRUG_NAME";

	private readonly IRepository<CfgFormSchema> _schemaRepo;

	private readonly IRepository<CfgFieldDefinition> _fieldRepo;

	private readonly IRepository<CfgEntityFieldValue> _valRepo;

	private readonly IRepository<CfgIcd10Diagnosis>? _icdRepo;

	private readonly IRepository<CfgPriceItem>? _priceRepo;

	private readonly IRepository<CfgOrderFrequency>? _freqRepo;

	private readonly IRepository<CfgClinicalDictionary>? _dictRepo;

	private readonly IDbConnectionFactory _connFactory;

	private readonly IAuditTrailContract? _auditTrail;

	private readonly IUserSession? _userSession;

	private static readonly HashSet<string> AllowedDataTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "STRING", "TEXT", "INTEGER", "DECIMAL", "BOOLEAN", "DATE", "DATETIME", "ENUM" };

	private static readonly HashSet<string> AllowedControls = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "u:TextBox", "u:TextArea", "u:NumericIntUpDown", "u:NumericDoubleUpDown", "ComboBox", "DatePicker", "CheckBox" };

	private static readonly Regex FieldCodePattern = new Regex("^[A-Za-z][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant);

	public ConfigController(IRepository<CfgFormSchema> schemaRepo, IRepository<CfgFieldDefinition> fieldRepo, IRepository<CfgEntityFieldValue> valRepo, IDbConnectionFactory? connFactory = null, IRepository<CfgIcd10Diagnosis>? icdRepo = null, IRepository<CfgPriceItem>? priceRepo = null, IRepository<CfgOrderFrequency>? freqRepo = null, IRepository<CfgClinicalDictionary>? dictRepo = null, IAuditTrailContract? auditTrail = null, IUserSession? userSession = null)
	{
		_schemaRepo = schemaRepo;
		_fieldRepo = fieldRepo;
		_valRepo = valRepo;
		_connFactory = connFactory ?? new SqliteConnectionFactory();
		_icdRepo = icdRepo;
		_priceRepo = priceRepo;
		_freqRepo = freqRepo;
		_dictRepo = dictRepo;
		_auditTrail = auditTrail;
		_userSession = userSession;
	}

	[HttpGet("schemas")]
	public async Task<ApiResponse<IReadOnlyList<CfgFormSchema>>> GetSchemas()
	{
		return ApiResponse<IReadOnlyList<CfgFormSchema>>.Success(await _schemaRepo.QueryAsync("SELECT * FROM cfg_form_schemas ORDER BY updated_at_ms DESC, form_name ASC"));
	}

	[HttpGet("schemas/{formCode}")]
	public async Task<ApiResponse<FormSchemaDetailDto>> GetSchema(string formCode, [FromQuery] bool includeDraft = false)
	{
		string publishedFilter = (includeDraft ? string.Empty : " AND is_published = 1");
		CfgFormSchema schema = (await _schemaRepo.QueryAsync("SELECT * FROM cfg_form_schemas WHERE form_code = @FormCode" + publishedFilter, new
		{
			FormCode = formCode
		})).FirstOrDefault();
		if (schema == null)
		{
			return ApiResponse<FormSchemaDetailDto>.Fail(404, "未找到表单配置：" + formCode);
		}
		return ApiResponse<FormSchemaDetailDto>.Success(new FormSchemaDetailDto(schema, await _fieldRepo.QueryAsync("SELECT * FROM cfg_field_definitions WHERE form_schema_id = @SchemaId AND is_deleted = 0 ORDER BY sort_order ASC", new
		{
			SchemaId = schema.Id
		})));
	}

	[RequireAdmin]
	[HttpPost("schemas")]
	public async Task<ApiResponse<FormSchemaDetailDto>> SaveSchema([FromBody] SaveFormSchemaRequest request)
	{
		CfgFormSchema schema = request.Schema;
		IReadOnlyList<CfgFieldDefinition> fields = request.Fields ?? Array.Empty<CfgFieldDefinition>();
		if (string.IsNullOrWhiteSpace(schema.FormCode) || schema.FormCode.Length > 64)
		{
			return ApiResponse<FormSchemaDetailDto>.Fail(400, "模板代码不能为空且不能超过 64 个字符");
		}
		if (string.IsNullOrWhiteSpace(schema.FormName))
		{
			return ApiResponse<FormSchemaDetailDto>.Fail(400, "模板名称不能为空");
		}
		if (string.IsNullOrWhiteSpace(schema.EntityType))
		{
			return ApiResponse<FormSchemaDetailDto>.Fail(400, "实体类型不能为空");
		}
		int gridColumns = schema.GridColumns;
		if ((gridColumns < 1 || gridColumns > 4) ? true : false)
		{
			return ApiResponse<FormSchemaDetailDto>.Fail(400, "栅格列数必须在 1 到 4 之间");
		}
		if (fields.Any((CfgFieldDefinition x) => string.IsNullOrWhiteSpace(x.FieldCode) || string.IsNullOrWhiteSpace(x.FieldLabel)))
		{
			return ApiResponse<FormSchemaDetailDto>.Fail(400, "每个字段都必须填写字段代码和字段标签");
		}
		if (fields.Any((CfgFieldDefinition x) => !FieldCodePattern.IsMatch(x.FieldCode.Trim())))
		{
			return ApiResponse<FormSchemaDetailDto>.Fail(400, "字段代码必须以字母开头，且只能包含字母、数字和下划线");
		}
		if (fields.GroupBy((CfgFieldDefinition x) => x.FieldCode.Trim(), StringComparer.OrdinalIgnoreCase).Any((IGrouping<string, CfgFieldDefinition> g) => g.Count() > 1))
		{
			return ApiResponse<FormSchemaDetailDto>.Fail(400, "字段代码不能重复");
		}
		if (fields.Where((CfgFieldDefinition x) => !string.IsNullOrWhiteSpace(x.Id)).GroupBy((CfgFieldDefinition x) => x.Id.Trim(), StringComparer.OrdinalIgnoreCase).Any((IGrouping<string, CfgFieldDefinition> g) => g.Count() > 1))
		{
			return ApiResponse<FormSchemaDetailDto>.Fail(400, "字段标识不能重复");
		}
		if (fields.Any((CfgFieldDefinition x) => !AllowedDataTypes.Contains(x.DataType) || !AllowedControls.Contains(x.UrsaControlType)))
		{
			return ApiResponse<FormSchemaDetailDto>.Fail(400, "字段数据类型或 Ursa 控件类型不受支持");
		}
		if (string.Equals(schema.FormCode.Trim(), "FORM-PRESCRIPTION-ITEM-FIELDS", StringComparison.OrdinalIgnoreCase) && !fields.Any((CfgFieldDefinition x) => string.Equals(x.FieldCode.Trim(), "DRUG_NAME", StringComparison.OrdinalIgnoreCase)))
		{
			return ApiResponse<FormSchemaDetailDto>.Fail(400, "药品名称是处方明细的主字段，不能从表格配置中删除或隐藏");
		}
		foreach (CfgFieldDefinition field in fields)
		{
			if (!string.IsNullOrWhiteSpace(field.ValidationRuleJson))
			{
				string jsonError = ValidateValidationRuleJson(field.ValidationRuleJson);
				if (jsonError != null)
				{
					return ApiResponse<FormSchemaDetailDto>.Fail(400, "字段 " + field.FieldCode + " 的配置无效：" + jsonError);
				}
			}
		}
		return ApiResponse<FormSchemaDetailDto>.Success(new FormSchemaDetailDto(Fields: await _connFactory.ExecuteInTransactionAsync(async (CancellationToken ct) =>
		{
			CfgFormSchema existing = (await _schemaRepo.QueryAsync("SELECT * FROM cfg_form_schemas WHERE form_code = @FormCode", new { schema.FormCode }, ct)).FirstOrDefault();
			if (existing == null)
			{
				schema.Id = (string.IsNullOrWhiteSpace(schema.Id) ? Guid.NewGuid().ToString("D") : schema.Id);
				schema.CreatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
				schema.UpdatedAtMs = schema.CreatedAtMs;
				schema.VersionNo = 1;
				await _schemaRepo.InsertAsync(schema, ct);
			}
			else
			{
				schema.Id = existing.Id;
				schema.CreatedAtMs = existing.CreatedAtMs;
				schema.CreatedBy = existing.CreatedBy;
				schema.VersionNo = existing.VersionNo;
				schema.UpdatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
				await _schemaRepo.UpdateAsync(schema, ct);
			}
			List<CfgFieldDefinition> currentFields = (await _fieldRepo.QueryAsync("SELECT * FROM cfg_field_definitions WHERE form_schema_id = @SchemaId", new
			{
				SchemaId = schema.Id
			}, ct)).ToList();
			List<CfgFieldDefinition> incomingFields = fields.OrderBy((CfgFieldDefinition x) => x.SortOrder).ToList();
			HashSet<string> claimedExistingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			List<(CfgFieldDefinition Incoming, CfgFieldDefinition? Existing)> assignments = new List<(CfgFieldDefinition, CfgFieldDefinition)>();
			foreach (CfgFieldDefinition field2 in incomingFields)
			{
				CfgFieldDefinition matchedField = (string.IsNullOrWhiteSpace(field2.Id) ? null : currentFields.FirstOrDefault((CfgFieldDefinition x) => !claimedExistingIds.Contains(x.Id) && string.Equals(x.Id, field2.Id.Trim(), StringComparison.OrdinalIgnoreCase)));
				if (matchedField != null)
				{
					claimedExistingIds.Add(matchedField.Id);
				}
				assignments.Add((field2, matchedField));
			}
			for (int i = 0; i < assignments.Count; i++)
			{
				var (incoming, matchedField2) = assignments[i];
				if (matchedField2 == null)
				{
					CfgFieldDefinition codeMatch = (from x in currentFields
						where !claimedExistingIds.Contains(x.Id) && string.Equals(x.FieldCode, incoming.FieldCode.Trim(), StringComparison.OrdinalIgnoreCase)
						orderby x.IsDeleted != 0, x.UpdatedAtMs descending
						select x).FirstOrDefault();
					if (codeMatch != null)
					{
						claimedExistingIds.Add(codeMatch.Id);
						assignments[i] = (incoming, codeMatch);
					}
				}
			}
			long removedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			string operatorId = (string.IsNullOrWhiteSpace(request.OperatorId) ? "SYSTEM" : request.OperatorId.Trim());
			foreach (CfgFieldDefinition removed in currentFields.Where((CfgFieldDefinition x) => x.IsDeleted == 0 && !claimedExistingIds.Contains(x.Id)))
			{
				removed.IsDeleted = 1;
				removed.DeletedAtMs = removedAtMs;
				removed.DeletedBy = operatorId;
				removed.UpdatedAtMs = removedAtMs;
				await _fieldRepo.UpdateAsync(removed, ct);
			}
			int sortOrder = 0;
			foreach (var item in assignments)
			{
				CfgFieldDefinition field3 = item.Incoming;
				CfgFieldDefinition matchedField3 = item.Existing;
				field3.FormSchemaId = schema.Id;
				field3.FieldCode = field3.FieldCode.Trim();
				field3.FieldLabel = field3.FieldLabel.Trim();
				field3.SortOrder = sortOrder++;
				if (matchedField3 == null)
				{
					field3.Id = (string.IsNullOrWhiteSpace(field3.Id) ? Guid.NewGuid().ToString("D") : field3.Id);
					field3.CreatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
					field3.UpdatedAtMs = field3.CreatedAtMs;
					field3.VersionNo = 1;
					field3.IsDeleted = 0;
					field3.DeletedAtMs = null;
					field3.DeletedBy = null;
					await _fieldRepo.InsertAsync(field3, ct);
				}
				else
				{
					field3.Id = matchedField3.Id;
					field3.CreatedAtMs = matchedField3.CreatedAtMs;
					field3.CreatedBy = matchedField3.CreatedBy;
					field3.VersionNo = matchedField3.VersionNo;
					field3.UpdatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
					field3.IsDeleted = 0;
					field3.DeletedAtMs = null;
					field3.DeletedBy = null;
					await _fieldRepo.UpdateAsync(field3, ct);
				}
			}
			var resultFields = (await _fieldRepo.QueryAsync("SELECT * FROM cfg_field_definitions WHERE form_schema_id = @SchemaId ORDER BY sort_order ASC", new
			{
				SchemaId = schema.Id
			}, ct)).Where((CfgFieldDefinition x) => x.IsDeleted == 0).ToList();
			if (_auditTrail != null)
			{
				await _auditTrail.RecordAuditAsync("CFG_FORM_SCHEMA_UPDATE", "CfgFormSchema", schema.FormCode, $"更新动态表单 Schema [{schema.FormCode} - {schema.FormName}]，包含 {resultFields.Count} 个有效字段", _userSession?.UserId, _userSession?.DisplayName);
			}
			return resultFields;
		}), Schema: schema), "动态表单模板已保存");
	}

	private static string? ValidateValidationRuleJson(string json)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(json);
			JsonElement rootElement = jsonDocument.RootElement;
			if (rootElement.ValueKind != JsonValueKind.Object)
			{
				return "根节点必须是 JSON 对象";
			}
			if (rootElement.TryGetProperty("width", out var value))
			{
				if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var value2))
				{
					return "width 必须是整数";
				}
				if ((value2 < 48 || value2 > 600) ? true : false)
				{
					return "width 必须在 48 到 600 之间";
				}
			}
			string[] array = new string[2] { "system", "editable" };
			foreach (string text in array)
			{
				if (rootElement.TryGetProperty(text, out var value3) && value3.ValueKind != JsonValueKind.True && value3.ValueKind != JsonValueKind.False)
				{
					return text + " 必须是布尔值";
				}
			}
			return null;
		}
		catch (JsonException)
		{
			return "不是有效 JSON";
		}
	}

	[HttpPost("values")]
	public async Task<ApiResponse<int>> SaveEntityValues([FromBody] List<CfgEntityFieldValue> values)
	{
		return ApiResponse<int>.Success(await _connFactory.ExecuteInTransactionAsync(async (CancellationToken ct) =>
		{
			int c = 0;
			foreach (CfgEntityFieldValue val in values)
			{
				IReadOnlyList<CfgEntityFieldValue> existing = await _valRepo.QueryAsync("SELECT * FROM cfg_entity_field_values WHERE entity_type = @Type AND entity_id = @Id AND field_code = @Code", new
				{
					Type = val.EntityType,
					Id = val.EntityId,
					Code = val.FieldCode
				}, ct);
				if (existing.Count > 0)
				{
					CfgEntityFieldValue cur = existing[0];
					cur.ValString = val.ValString;
					cur.ValInt = val.ValInt;
					cur.ValDecimal = val.ValDecimal;
					cur.ValTimeMs = val.ValTimeMs;
					await _valRepo.UpdateAsync(cur, ct);
				}
				else
				{
					await _valRepo.InsertAsync(val, ct);
				}
				c++;
			}
			return c;
		}), "动态扩展字段保存成功");
	}

	[HttpGet("values/{entityType}/{entityId}")]
	public async Task<ApiResponse<IReadOnlyList<CfgEntityFieldValue>>> GetEntityValues(string entityType, string entityId)
	{
		return ApiResponse<IReadOnlyList<CfgEntityFieldValue>>.Success(await _valRepo.QueryAsync("SELECT * FROM cfg_entity_field_values WHERE entity_type = @Type AND entity_id = @Id", new
		{
			Type = entityType,
			Id = entityId
		}));
	}

	[HttpGet("master/icd10")]
	public async Task<ApiResponse<IReadOnlyList<CfgIcd10Diagnosis>>> GetIcd10Diagnoses([FromQuery] string? keyword = null, [FromQuery] string? genderCode = null, [FromQuery] string? categoryName = null)
	{
		if (_icdRepo == null)
		{
			return ApiResponse<IReadOnlyList<CfgIcd10Diagnosis>>.Fail(500, "ICD-10 仓储未初始化");
		}
		bool flag;
		switch (genderCode)
		{
		case "1":
		case "男":
		case "MALE":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		bool isMale = flag;
		switch (genderCode)
		{
		case "2":
		case "女":
		case "FEMALE":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		bool isFemale = flag;
		string kw = (string.IsNullOrWhiteSpace(keyword) ? null : ("%" + keyword.Trim() + "%"));
		IReadOnlyList<CfgIcd10Diagnosis> list = await _icdRepo.QueryAsync("SELECT * FROM cfg_icd10_diagnoses\nWHERE is_active = 1\n  AND (@Category IS NULL OR category_name = @Category)\n  AND (@Kw IS NULL OR icd_code LIKE @Kw OR disease_name LIKE @Kw OR pinyin_code LIKE @Kw)\nORDER BY icd_code ASC", new
		{
			Category = categoryName,
			Kw = kw
		});
		if (isMale)
		{
			list = list.Where((CfgIcd10Diagnosis x) => !x.GenderExclusive.Equals("FEMALE_ONLY", StringComparison.OrdinalIgnoreCase)).ToList();
		}
		else if (isFemale)
		{
			list = list.Where((CfgIcd10Diagnosis x) => !x.GenderExclusive.Equals("MALE_ONLY", StringComparison.OrdinalIgnoreCase)).ToList();
		}
		return ApiResponse<IReadOnlyList<CfgIcd10Diagnosis>>.Success(list);
	}

	[RequireAdmin]
	[HttpPost("master/icd10")]
	public async Task<ApiResponse<CfgIcd10Diagnosis>> SaveIcd10Diagnosis([FromBody] CfgIcd10Diagnosis req)
	{
		if (_icdRepo == null)
		{
			return ApiResponse<CfgIcd10Diagnosis>.Fail(500, "ICD-10 仓储未初始化");
		}
		if (string.IsNullOrWhiteSpace(req.IcdCode) || string.IsNullOrWhiteSpace(req.DiseaseName))
		{
			return ApiResponse<CfgIcd10Diagnosis>.Fail(400, "ICD-10 编码和疾病名称不能为空");
		}
		CfgIcd10Diagnosis existing = await _icdRepo.QuerySingleOrDefaultAsync("SELECT * FROM cfg_icd10_diagnoses WHERE icd_code = @Code", new
		{
			Code = req.IcdCode.Trim()
		});
		if (existing != null)
		{
			existing.DiseaseName = req.DiseaseName.Trim();
			existing.PinyinCode = req.PinyinCode?.Trim().ToUpperInvariant();
			existing.CategoryName = (string.IsNullOrWhiteSpace(req.CategoryName) ? "GENERAL" : req.CategoryName.Trim());
			existing.GenderExclusive = (string.IsNullOrWhiteSpace(req.GenderExclusive) ? "ALL" : req.GenderExclusive.Trim().ToUpperInvariant());
			existing.IsChronicSpecial = req.IsChronicSpecial;
			existing.IsInfectiousReport = req.IsInfectiousReport;
			existing.IsActive = req.IsActive;
			await _icdRepo.UpdateAsync(existing);
			if (_auditTrail != null)
			{
				await _auditTrail.RecordAuditAsync("CFG_ICD10_UPDATE", "CfgIcd10Diagnosis", existing.IcdCode, $"更新 ICD-10 诊断条目 [{existing.IcdCode} - {existing.DiseaseName}]", _userSession?.UserId, _userSession?.DisplayName);
			}
			return ApiResponse<CfgIcd10Diagnosis>.Success(existing, "ICD-10 诊断主数据已更新");
		}
		req.Id = (string.IsNullOrWhiteSpace(req.Id) ? $"ICD-{Guid.NewGuid():N}".Substring(0, 16).ToUpperInvariant() : req.Id);
		req.IcdCode = req.IcdCode.Trim();
		req.DiseaseName = req.DiseaseName.Trim();
		req.PinyinCode = req.PinyinCode?.Trim().ToUpperInvariant();
		req.GenderExclusive = (string.IsNullOrWhiteSpace(req.GenderExclusive) ? "ALL" : req.GenderExclusive.Trim().ToUpperInvariant());
		await _icdRepo.InsertAsync(req);
		if (_auditTrail != null)
		{
			await _auditTrail.RecordAuditAsync("CFG_ICD10_UPDATE", "CfgIcd10Diagnosis", req.IcdCode, $"新增 ICD-10 诊断条目 [{req.IcdCode} - {req.DiseaseName}]", _userSession?.UserId, _userSession?.DisplayName);
		}
		return ApiResponse<CfgIcd10Diagnosis>.Success(req, "ICD-10 诊断主数据已新增");
	}

	[HttpGet("master/price-items")]
	public async Task<ApiResponse<IReadOnlyList<CfgPriceItem>>> GetPriceItems([FromQuery] string? feeCategory = null, [FromQuery] string? keyword = null, [FromQuery] bool activeOnly = true)
	{
		if (_priceRepo == null)
		{
			return ApiResponse<IReadOnlyList<CfgPriceItem>>.Fail(500, "收费价表仓储未初始化");
		}
		string kw = (string.IsNullOrWhiteSpace(keyword) ? null : ("%" + keyword.Trim() + "%"));
		return ApiResponse<IReadOnlyList<CfgPriceItem>>.Success(await _priceRepo.QueryAsync("SELECT * FROM cfg_price_items\nWHERE (@ActiveOnly = 0 OR is_active = 1)\n  AND (@Category IS NULL OR fee_category = @Category)\n  AND (@Kw IS NULL OR item_code LIKE @Kw OR item_name LIKE @Kw OR pinyin_code LIKE @Kw OR national_ins_code LIKE @Kw)\nORDER BY fee_category ASC, item_code ASC", new
		{
			ActiveOnly = (activeOnly ? 1 : 0),
			Category = feeCategory,
			Kw = kw
		}));
	}

	[RequireAdmin]
	[HttpPost("master/price-items")]
	public async Task<ApiResponse<CfgPriceItem>> SavePriceItem([FromBody] CfgPriceItem req)
	{
		if (_priceRepo == null)
		{
			return ApiResponse<CfgPriceItem>.Fail(500, "收费价表仓储未初始化");
		}
		if (string.IsNullOrWhiteSpace(req.ItemCode) || string.IsNullOrWhiteSpace(req.ItemName))
		{
			return ApiResponse<CfgPriceItem>.Fail(400, "收费项目编码和名称不能为空");
		}
		if (req.UnitPrice < 0m)
		{
			return ApiResponse<CfgPriceItem>.Fail(400, "收费项目单价不能为负数");
		}
		CfgPriceItem existing = await _priceRepo.QuerySingleOrDefaultAsync("SELECT * FROM cfg_price_items WHERE item_code = @Code", new
		{
			Code = req.ItemCode.Trim()
		});
		if (existing != null)
		{
			decimal oldPrice = existing.UnitPrice;
			existing.NationalInsCode = req.NationalInsCode ?? existing.NationalInsCode;
			existing.ItemName = req.ItemName.Trim();
			existing.PinyinCode = req.PinyinCode ?? existing.PinyinCode;
			existing.FeeCategory = req.FeeCategory;
			existing.Spec = req.Spec;
			existing.UnitName = req.UnitName;
			existing.UnitPrice = req.UnitPrice;
			existing.ReimburseType = req.ReimburseType;
			existing.DefaultExecDeptId = req.DefaultExecDeptId;
			existing.IsActive = req.IsActive;
			await _priceRepo.UpdateAsync(existing);
			if (_auditTrail != null)
			{
				string msg = (oldPrice != req.UnitPrice)
					? $"物价调价审计：项目 [{existing.ItemCode} {existing.ItemName}] 单价由 ¥{oldPrice:0.00} 调整为 ¥{req.UnitPrice:0.00}"
					: $"更新物价条目 [{existing.ItemCode} {existing.ItemName}]";
				await _auditTrail.RecordAuditAsync("CFG_PRICE_ADJUST", "CfgPriceItem", existing.Id, msg, _userSession?.UserId, _userSession?.DisplayName);
			}
			return ApiResponse<CfgPriceItem>.Success(existing, "医疗服务收费价表主数据已更新");
		}
		req.Id = (string.IsNullOrWhiteSpace(req.Id) ? $"PRC-{Guid.NewGuid():N}".Substring(0, 16).ToUpperInvariant() : req.Id);
		req.ItemCode = req.ItemCode.Trim();
		req.ItemName = req.ItemName.Trim();
		await _priceRepo.InsertAsync(req);
		if (_auditTrail != null)
		{
			await _auditTrail.RecordAuditAsync("CFG_PRICE_ADJUST", "CfgPriceItem", req.Id, $"新增物价条目 [{req.ItemCode} {req.ItemName}] 单价 ¥{req.UnitPrice:0.00}", _userSession?.UserId, _userSession?.DisplayName);
		}
		return ApiResponse<CfgPriceItem>.Success(req, "医疗服务收费价表主数据已新增");
	}

	[HttpGet("master/frequencies")]
	public async Task<ApiResponse<IReadOnlyList<CfgOrderFrequency>>> GetOrderFrequencies([FromQuery] string? applicableTerm = null)
	{
		if (_freqRepo == null)
		{
			return ApiResponse<IReadOnlyList<CfgOrderFrequency>>.Fail(500, "医嘱频次仓储未初始化");
		}
		return ApiResponse<IReadOnlyList<CfgOrderFrequency>>.Success(await _freqRepo.QueryAsync("SELECT * FROM cfg_order_frequencies\nWHERE is_active = 1\n  AND (@Term IS NULL OR applicable_term = @Term OR applicable_term = 'ALL')\nORDER BY sort_order ASC, freq_code ASC", new
		{
			Term = applicableTerm
		}));
	}

	[RequireAdmin]
	[HttpPost("master/frequencies")]
	public async Task<ApiResponse<CfgOrderFrequency>> SaveOrderFrequency([FromBody] CfgOrderFrequency req)
	{
		if (_freqRepo == null)
		{
			return ApiResponse<CfgOrderFrequency>.Fail(500, "医嘱频次仓储未初始化");
		}
		if (string.IsNullOrWhiteSpace(req.FreqCode) || string.IsNullOrWhiteSpace(req.FreqName))
		{
			return ApiResponse<CfgOrderFrequency>.Fail(400, "频次编码和频次名称不能为空");
		}
		string code = req.FreqCode.Trim().ToLowerInvariant();
		IReadOnlyList<string> normalizedPoints = MasterDataService.ParseTimePoints(req.TimePointsText);
		string normalizedText = ((normalizedPoints.Count > 0) ? string.Join(", ", normalizedPoints) : req.TimePointsText.Trim());
		int dailyTimes = ((normalizedPoints.Count > 0 && normalizedPoints[0].Contains(':')) ? normalizedPoints.Count : Math.Max(1, req.DailyTimes));
		CfgOrderFrequency existing = await _freqRepo.QuerySingleOrDefaultAsync("SELECT * FROM cfg_order_frequencies WHERE LOWER(freq_code) = LOWER(@Code)", new
		{
			Code = code
		});
		if (existing != null)
		{
			existing.FreqName = req.FreqName.Trim();
			existing.TimePointsText = normalizedText;
			existing.DailyTimes = dailyTimes;
			existing.ApplicableTerm = (string.IsNullOrWhiteSpace(req.ApplicableTerm) ? existing.ApplicableTerm : req.ApplicableTerm);
			existing.Description = req.Description ?? existing.Description;
			existing.SortOrder = req.SortOrder;
			existing.IsActive = req.IsActive;
			await _freqRepo.UpdateAsync(existing);
			if (_auditTrail != null)
			{
				await _auditTrail.RecordAuditAsync("CFG_ORDER_FREQ_UPDATE", "CfgOrderFrequency", existing.FreqCode, $"更新医嘱频次 [{existing.FreqCode} - {existing.FreqName}] 分解点: {existing.TimePointsText}", _userSession?.UserId, _userSession?.DisplayName);
			}
			return ApiResponse<CfgOrderFrequency>.Success(existing, "医嘱频次 [" + existing.FreqCode + "] 分解时间点已更新为：" + existing.TimePointsText);
		}
		req.Id = (string.IsNullOrWhiteSpace(req.Id) ? $"FREQ-{Guid.NewGuid():N}".Substring(0, 16).ToUpperInvariant() : req.Id);
		req.FreqCode = code;
		req.FreqName = req.FreqName.Trim();
		req.TimePointsText = normalizedText;
		req.DailyTimes = dailyTimes;
		req.IsActive = (short)(req.IsActive == 0 ? 1 : req.IsActive);
		await _freqRepo.InsertAsync(req);
		if (_auditTrail != null)
		{
			await _auditTrail.RecordAuditAsync("CFG_ORDER_FREQ_UPDATE", "CfgOrderFrequency", req.FreqCode, $"新增医嘱频次 [{req.FreqCode} - {req.FreqName}] 分解点: {req.TimePointsText}", _userSession?.UserId, _userSession?.DisplayName);
		}
		return ApiResponse<CfgOrderFrequency>.Success(req, "医嘱频次 [" + req.FreqCode + "] 已新增，分解时间点：" + req.TimePointsText);
	}

	[HttpGet("master/dictionaries/{dictType}")]
	public async Task<ApiResponse<IReadOnlyList<CfgClinicalDictionary>>> GetClinicalDictionaries(string dictType)
	{
		if (_dictRepo == null)
		{
			return ApiResponse<IReadOnlyList<CfgClinicalDictionary>>.Fail(500, "临床字典仓储未初始化");
		}
		return ApiResponse<IReadOnlyList<CfgClinicalDictionary>>.Success(await _dictRepo.QueryAsync("SELECT * FROM cfg_clinical_dictionaries\nWHERE dict_type = @Type AND is_active = 1\nORDER BY sort_order ASC, item_code ASC", new
		{
			Type = dictType.Trim().ToUpperInvariant()
		}));
	}

	[RequireAdmin]
	[HttpPost("master/dictionaries")]
	public async Task<ApiResponse<CfgClinicalDictionary>> SaveClinicalDictionary([FromBody] CfgClinicalDictionary req)
	{
		if (_dictRepo == null)
		{
			return ApiResponse<CfgClinicalDictionary>.Fail(500, "临床字典仓储未初始化");
		}
		if (string.IsNullOrWhiteSpace(req.DictType) || string.IsNullOrWhiteSpace(req.ItemCode) || string.IsNullOrWhiteSpace(req.ItemName))
		{
			return ApiResponse<CfgClinicalDictionary>.Fail(400, "字典分类、字典编码和字典名称不能为空");
		}
		string dictType = req.DictType.Trim().ToUpperInvariant();
		string itemCode = req.ItemCode.Trim().ToUpperInvariant();
		CfgClinicalDictionary existing = await _dictRepo.QuerySingleOrDefaultAsync("SELECT * FROM cfg_clinical_dictionaries WHERE dict_type = @Type AND item_code = @Code", new
		{
			Type = dictType,
			Code = itemCode
		});
		if (existing != null)
		{
			existing.ItemName = req.ItemName.Trim();
			existing.PinyinCode = req.PinyinCode?.Trim().ToUpperInvariant();
			existing.ParentCode = req.ParentCode;
			existing.SortOrder = req.SortOrder;
			existing.IsDefault = req.IsDefault;
			existing.IsActive = req.IsActive;
			await _dictRepo.UpdateAsync(existing);
			if (_auditTrail != null)
			{
				await _auditTrail.RecordAuditAsync("CFG_CLINICAL_DICT_UPDATE", "CfgClinicalDictionary", itemCode, $"更新临床字典 [{dictType}.{itemCode} - {existing.ItemName}]", _userSession?.UserId, _userSession?.DisplayName);
			}
			return ApiResponse<CfgClinicalDictionary>.Success(existing, "临床通用字典项已更新");
		}
		req.Id = (string.IsNullOrWhiteSpace(req.Id) ? $"DICT-{Guid.NewGuid():N}".Substring(0, 16).ToUpperInvariant() : req.Id);
		req.DictType = dictType;
		req.ItemCode = itemCode;
		req.ItemName = req.ItemName.Trim();
		req.PinyinCode = req.PinyinCode?.Trim().ToUpperInvariant();
		await _dictRepo.InsertAsync(req);
		if (_auditTrail != null)
		{
			await _auditTrail.RecordAuditAsync("CFG_CLINICAL_DICT_UPDATE", "CfgClinicalDictionary", itemCode, $"新增临床字典 [{dictType}.{itemCode} - {req.ItemName}]", _userSession?.UserId, _userSession?.DisplayName);
		}
		return ApiResponse<CfgClinicalDictionary>.Success(req, "临床通用字典项已新增");
	}

	[HttpGet("master/summary")]
	public async Task<ApiResponse<MasterDataSummaryDto>> GetMasterDataSummary()
	{
		using DbConnection conn = _connFactory.CreateConnection();
		MasterDataSummaryDto summary = new MasterDataSummaryDto(await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM sys_departments WHERE is_active = 1"), await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM ipd_beds"), await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM sys_users WHERE is_active = 1"), await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM sys_roles WHERE is_active = 1"), await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM inv_drugs WHERE is_active = 1"), await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM inv_drug_batches"), await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM cfg_icd10_diagnoses WHERE is_active = 1"), await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM cfg_price_items WHERE is_active = 1"), await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM cfg_order_frequencies WHERE is_active = 1"), await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM cfg_clinical_dictionaries WHERE is_active = 1"), await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM emr_data_elements WHERE is_active = 1"), await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM cfg_form_schemas"));
		return ApiResponse<MasterDataSummaryDto>.Success(summary, "全院 12 类核心主数据实时统计已加载");
	}
}
