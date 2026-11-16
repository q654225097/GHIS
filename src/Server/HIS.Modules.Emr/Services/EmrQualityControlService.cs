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
		PatientSummaryDto patient = await _patientContract.GetPatientSummaryAsync(patientId, ct);
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
			EmrDataElement el = (await _elementRepo.QueryAsync("SELECT * FROM emr_data_elements WHERE element_code = @Code AND is_active = 1", new
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
				if (isMale && el.GenderExclusive.Equals("FEMALE_ONLY", StringComparison.OrdinalIgnoreCase))
				{
					return Result<bool>.Fail($"患者为男性，禁止录入女性专有病历数据元 [{el.ElementName}]({el.ElementCode})", "EMR_QC_GENDER_CONFLICT");
				}
				if (isFemale && el.GenderExclusive.Equals("MALE_ONLY", StringComparison.OrdinalIgnoreCase))
				{
					return Result<bool>.Fail($"患者为女性，禁止录入男性专有病历数据元 [{el.ElementName}]({el.ElementCode})", "EMR_QC_GENDER_CONFLICT");
				}
			}
		}
		return Result<bool>.Success(value: true);
	}
}
