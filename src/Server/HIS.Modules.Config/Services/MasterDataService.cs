using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Config.Models;

namespace HIS.Modules.Config.Services;

public class MasterDataService : IMasterDataReadOnlyContract, IPriceTableContract
{
	private sealed class DrugPriceRow
	{
		public string ItemCode { get; set; } = string.Empty;

		public string ItemName { get; set; } = string.Empty;

		public decimal UnitPrice { get; set; }

		public string UnitName { get; set; } = string.Empty;

		public short IsNarcotic { get; set; }
	}

	private readonly IRepository<CfgIcd10Diagnosis> _icdRepo;

	private readonly IRepository<CfgPriceItem> _priceRepo;

	private readonly IRepository<CfgOrderFrequency> _freqRepo;

	private readonly IRepository<CfgClinicalDictionary> _dictRepo;

	private readonly IDbConnectionFactory _connFactory;

	public MasterDataService(IRepository<CfgIcd10Diagnosis> icdRepo, IRepository<CfgPriceItem> priceRepo, IRepository<CfgOrderFrequency> freqRepo, IRepository<CfgClinicalDictionary> dictRepo, IDbConnectionFactory connFactory)
	{
		_icdRepo = icdRepo;
		_priceRepo = priceRepo;
		_freqRepo = freqRepo;
		_dictRepo = dictRepo;
		_connFactory = connFactory;
	}

	public async Task<Icd10DiagnosisDto?> GetDiagnosisByCodeAsync(string icdCode, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(icdCode))
		{
			return null;
		}
		CfgIcd10Diagnosis diag = await _icdRepo.QuerySingleOrDefaultAsync("SELECT * FROM cfg_icd10_diagnoses WHERE icd_code = @Code AND is_active = 1", new
		{
			Code = icdCode.Trim()
		}, ct);
		if (diag == null)
		{
			return null;
		}
		return new Icd10DiagnosisDto(diag.Id, diag.IcdCode, diag.DiseaseName, diag.PinyinCode, diag.CategoryName, diag.GenderExclusive, diag.IsChronicSpecial == 1, diag.IsInfectiousReport == 1, diag.IsActive == 1);
	}

	public async Task<OrderFrequencySnapshotDto?> GetOrderFrequencyAsync(string freqCode, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(freqCode))
		{
			return null;
		}
		string raw = freqCode.Trim();
		string token = raw.Split(new char[2] { ' ', '(' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? raw;
		CfgOrderFrequency freq = await _freqRepo.QuerySingleOrDefaultAsync("SELECT * FROM cfg_order_frequencies WHERE LOWER(freq_code) = LOWER(@Code) AND is_active = 1", new
		{
			Code = token
		}, ct);
		if (freq == null)
		{
			return null;
		}
		return new OrderFrequencySnapshotDto(ParsedTimePoints: ParseTimePoints(freq.TimePointsText), Id: freq.Id, FreqCode: freq.FreqCode, FreqName: freq.FreqName, DailyTimes: freq.DailyTimes, TimePointsText: freq.TimePointsText, ApplicableTerm: freq.ApplicableTerm, IsActive: freq.IsActive == 1);
	}

	public async Task<IReadOnlyList<ClinicalDictionaryItemDto>> GetDictionaryItemsAsync(string dictType, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(dictType))
		{
			return Array.Empty<ClinicalDictionaryItemDto>();
		}
		return (await _dictRepo.QueryAsync("SELECT * FROM cfg_clinical_dictionaries WHERE dict_type = @Type AND is_active = 1 ORDER BY sort_order ASC, item_code ASC", new
		{
			Type = dictType.Trim().ToUpperInvariant()
		}, ct)).Select((CfgClinicalDictionary x) => new ClinicalDictionaryItemDto(x.Id, x.DictType, x.ItemCode, x.ItemName, x.PinyinCode, x.SortOrder, x.IsDefault == 1, x.IsActive == 1)).ToList();
	}

	public async Task<PriceItemSnapshotDto?> GetPriceSnapshotAsync(string itemCode, long atTimeMs, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(itemCode))
		{
			return null;
		}
		string code = itemCode.Trim();
		CfgPriceItem priceItem = await _priceRepo.QuerySingleOrDefaultAsync("SELECT * FROM cfg_price_items WHERE (item_code = @Code OR id = @Code) AND is_active = 1", new
		{
			Code = code
		}, ct);
		if (priceItem != null)
		{
			return new PriceItemSnapshotDto(priceItem.ItemCode, priceItem.ItemName, priceItem.UnitPrice, priceItem.UnitName, priceItem.FeeCategory);
		}
		using DbConnection conn = _connFactory.CreateConnection();
		DrugPriceRow drugRow = await conn.QuerySingleOrDefaultAsync<DrugPriceRow>(new CommandDefinition("SELECT drug_code AS ItemCode, drug_name AS ItemName, price AS UnitPrice, unit_name AS UnitName, is_narcotic AS IsNarcotic FROM inv_drugs WHERE (drug_code = @Code OR id = @Code) AND is_active = 1", new
		{
			Code = code
		}, _connFactory.CurrentTransaction, null, null, CommandFlags.Buffered, ct));
		if (drugRow == null)
		{
			return null;
		}
		return new PriceItemSnapshotDto(drugRow.ItemCode, drugRow.ItemName, drugRow.UnitPrice, drugRow.UnitName, (drugRow.IsNarcotic == 1) ? "NARCOTIC_DRUG" : "WESTERN_DRUG");
	}

	public static IReadOnlyList<string> ParseTimePoints(string? timePointsText)
	{
		if (string.IsNullOrWhiteSpace(timePointsText))
		{
			return Array.Empty<string>();
		}
		return (from x in (from x in timePointsText.Split(new char[5] { ',', '，', ';', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select((string raw) =>
				{
					string text = raw.Trim();
					if (TimeOnly.TryParse(text, out var result))
					{
						return result.ToString("HH:mm");
					}
					int result2;
					return (int.TryParse(text, out result2) && result2 >= 0 && result2 <= 23) ? $"{result2:D2}:00" : text;
				})
				where !string.IsNullOrEmpty(x)
				select x).Distinct()
			orderby x
			select x).ToList();
	}
}
