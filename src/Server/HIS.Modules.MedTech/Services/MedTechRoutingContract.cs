using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.MedTech.Models;

namespace HIS.Modules.MedTech.Services;

public class MedTechRoutingContract : IMedTechRoutingContract
{
	private readonly IRepository<SysMedtechExecRouting> _routingRepo;

	public MedTechRoutingContract(IRepository<SysMedtechExecRouting> routingRepo)
	{
		_routingRepo = routingRepo;
	}

	public async Task<MedTechRoutingResultDto?> ResolveRoutingAsync(string campusId, string sourceScene, string orderDeptId, string serviceItemCode, long requestTimeMs, CancellationToken ct = default(CancellationToken))
	{
		IReadOnlyList<SysMedtechExecRouting> routings = await _routingRepo.QueryAsync("SELECT * FROM sys_medtech_exec_routings WHERE campus_id = @CampusId AND service_item_code = @ItemCode AND is_active = 1 ORDER BY priority_weight DESC", new
		{
			CampusId = campusId,
			ItemCode = serviceItemCode
		}, ct);
		SysMedtechExecRouting match = routings.FirstOrDefault((SysMedtechExecRouting r) => (r.SourceScene == sourceScene || r.SourceScene == "ALL") && (string.IsNullOrEmpty(r.OrderDeptId) || r.OrderDeptId == orderDeptId)) ?? routings.FirstOrDefault((SysMedtechExecRouting r) => r.SourceScene == "ALL" || r.SourceScene == sourceScene) ?? routings.FirstOrDefault();
		if (match == null)
		{
			return null;
		}
		return new MedTechRoutingResultDto(MedTechSubType: InferMedTechSubType(match.ServiceItemCode, match.ExecMedTechDeptId), ExecMedTechDeptId: match.ExecMedTechDeptId, ExecRoomCode: match.ExecRoomCode ?? "ROOM-01", ServiceItemCode: match.ServiceItemCode);
	}

	private static string InferMedTechSubType(string serviceItemCode, string execDeptId)
	{
		string text = (serviceItemCode ?? string.Empty).ToUpperInvariant();
		string text2 = (execDeptId ?? string.Empty).ToUpperInvariant();
		if (text.StartsWith("RAD_") || text.Contains("CT") || text.Contains("MRI") || text.Contains("DR") || text2.Contains("RAD"))
		{
			return "RADIOLOGY";
		}
		if (text.StartsWith("US_") || text.Contains("UCG") || text.Contains("ULTRASOUND") || text2.Contains("US"))
		{
			return "ULTRASOUND";
		}
		if (text.StartsWith("ENDO_") || text2.Contains("ENDO"))
		{
			return "ENDOSCOPY";
		}
		if (text.StartsWith("PATH_") || text2.Contains("PATH"))
		{
			return "PATHOLOGY";
		}
		if (text.StartsWith("ECG_") || text.StartsWith("EEG_") || text2.Contains("ECG"))
		{
			return "ECG_EEG";
		}
		if (text.StartsWith("OR_") || text2.Contains("OR"))
		{
			return "OR_ANES";
		}
		if (text.StartsWith("BLOOD_") || text2.Contains("BLOOD"))
		{
			return "BLOOD_BANK";
		}
		return "LAB";
	}
}
