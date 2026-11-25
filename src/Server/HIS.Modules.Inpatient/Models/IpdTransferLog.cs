using System;
using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Inpatient.Models;

public class IpdTransferLog : BaseEntity
{
	public string EncounterId { get; set; } = string.Empty;

	public string TransferType { get; set; } = "DEPT_TRANSFER";

	public string FromDeptId { get; set; } = string.Empty;

	public string FromWardId { get; set; } = string.Empty;

	public string? FromBedCode { get; set; }

	public string ToDeptId { get; set; } = string.Empty;

	public string ToWardId { get; set; } = string.Empty;

	public string? ToBedCode { get; set; }

	public long HandoverOutAtMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

	public long? HandoverInAtMs { get; set; }

	public string? SbarSituation { get; set; }

	public string? SbarBackground { get; set; }

	public string? SbarAssessment { get; set; }

	public string? SbarRecommendation { get; set; }

	public short OrdersReorganized { get; set; } = 0;

	public int ReorganizedCount { get; set; } = 0;
}
