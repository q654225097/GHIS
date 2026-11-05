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

    public async Task<MedTechRoutingResultDto?> ResolveRoutingAsync(
        string campusId,
        string sourceScene,
        string orderDeptId,
        string serviceItemCode,
        long requestTimeMs,
        CancellationToken ct = default)
    {
        var routings = await _routingRepo.QueryAsync(
            "SELECT * FROM sys_medtech_exec_routings WHERE campus_id = @CampusId AND service_item_code = @ItemCode AND is_active = 1 ORDER BY priority_weight DESC",
            new { CampusId = campusId, ItemCode = serviceItemCode }, ct);

        var match = routings.FirstOrDefault(r => r.SourceScene == "ALL" || r.SourceScene == sourceScene) ?? routings.FirstOrDefault();
        if (match == null) return null;

        return new MedTechRoutingResultDto(
            match.ExecMedTechDeptId,
            match.ExecRoomCode ?? "ROOM-01",
            match.ServiceItemCode,
            "LAB"
        );
    }
}

