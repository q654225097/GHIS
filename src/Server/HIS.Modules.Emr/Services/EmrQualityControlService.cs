using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Emr.Models;

namespace HIS.Modules.Emr.Services;

public class EmrQualityControlService
{
	private readonly IRepository<EmrDataElement> _elementRepo;

	private readonly IPatientReadOnlyContract _patientContract;

	public EmrQualityControlService(IRepository<EmrDataElement> elementRepo, IPatientReadOnlyContract patientContract)
	{
		_elementRepo = elementRepo;
		_patientContract = patientContract;
	}

	public async Task<Result<bool>> ValidateStructuredElementsAsync(string patientId, Dictionary<string, string> elementValues, CancellationToken ct = default(CancellationToken))
	{
		PatientSummaryDto? patient = await _patientContract.GetPatientSummaryAsync(patientId, ct);
		string gender = (patient?.GenderCode ?? string.Empty).Trim().ToUpperInvariant();
		bool isMale = gender is "1" or "MALE" or "男";
		bool isFemale = gender is "2" or "FEMALE" or "女";
		bool genderUnknown = !isMale && !isFemale;
		foreach (var (elementCode, val) in elementValues)
		{
			if (string.IsNullOrWhiteSpace(val))
			{
				continue;
			}
			EmrDataElement? el = (await _elementRepo.QueryAsync("SELECT * FROM emr_data_elements WHERE element_code = @Code AND is_active = 1", new
			{
				Code = elementCode
			}, ct)).FirstOrDefault();
			if (el != null)
			{
				string exclusive = (el.GenderExclusive ?? "ALL").Trim().ToUpperInvariant();
				if (genderUnknown && exclusive is "MALE_ONLY" or "FEMALE_ONLY")
				{
					return Result<bool>.Fail($"患者性别未确认，禁止录入性别专有病历数据元 [{el.ElementName}]({el.ElementCode})，请先完成实名建档或核实性别", "EMR_QC_GENDER_UNKNOWN");
				}
				if (isMale && "FEMALE_ONLY".Equals(el.GenderExclusive, StringComparison.OrdinalIgnoreCase))
				{
					return Result<bool>.Fail($"患者为男性，禁止录入女性专有病历数据元 [{el.ElementName}]({el.ElementCode})", "EMR_QC_GENDER_CONFLICT");
				}
				if (isFemale && "MALE_ONLY".Equals(el.GenderExclusive, StringComparison.OrdinalIgnoreCase))
				{
					return Result<bool>.Fail($"患者为女性，禁止录入男性专有病历数据元 [{el.ElementName}]({el.ElementCode})", "EMR_QC_GENDER_CONFLICT");
				}

				// 生理指标合理性内涵质控
				if (decimal.TryParse(val, out decimal numVal))
				{
					string codeUpper = elementCode.ToUpperInvariant();
					if ((codeUpper.Contains("HEART_RATE") || codeUpper.Contains("PULSE")) && (numVal < 20 || numVal > 250))
					{
						return Result<bool>.Fail($"心率/脉搏数值 [{numVal}] 超出生理极限范围 (20-250 bpm)，请复核", "EMR_QC_PHYSIOLOGY_RANGE");
					}
					if (codeUpper.Contains("TEMP") && (numVal < 34.0m || numVal > 43.0m))
					{
						return Result<bool>.Fail($"体温数值 [{numVal}°C] 超出生理极限范围 (34.0-43.0°C)，请复核", "EMR_QC_PHYSIOLOGY_RANGE");
					}
					if (codeUpper.Contains("SYS_BP") && (numVal < 40 || numVal > 300))
					{
						return Result<bool>.Fail($"收缩压数值 [{numVal} mmHg] 超出生理极限范围 (40-300 mmHg)，请复核", "EMR_QC_PHYSIOLOGY_RANGE");
					}
					if (codeUpper.Contains("DIA_BP") && (numVal < 20 || numVal > 200))
					{
						return Result<bool>.Fail($"舒张压数值 [{numVal} mmHg] 超出生理极限范围 (20-200 mmHg)，请复核", "EMR_QC_PHYSIOLOGY_RANGE");
					}
				}
			}
		}
		return Result<bool>.Success(value: true);
	}

	public Result<bool> ValidateWritingTimeLimit(string docTypeCode, long createTimeMs, long baselineTimeMs)
	{
		long elapsedHours = (createTimeMs - baselineTimeMs) / (3600 * 1000);
		if (elapsedHours < 0) return Result<bool>.Success(true);

		string type = (docTypeCode ?? "").ToUpperInvariant();
		if (type.Contains("RESCUE") && elapsedHours > 6)
		{
			return Result<bool>.Fail($"抢救记录超出国家规范书写时限（6小时），当前已耗时 {elapsedHours} 小时", "EMR_QC_TIME_LIMIT_EXCEEDED");
		}
		if ((type.Contains("FIRST") || type.Contains("ADMISSION")) && elapsedHours > 24)
		{
			return Result<bool>.Fail($"入院记录/首次病程记录超出国家规范书写时限（24小时），当前已耗时 {elapsedHours} 小时", "EMR_QC_TIME_LIMIT_EXCEEDED");
		}

		return Result<bool>.Success(true);
	}
}
