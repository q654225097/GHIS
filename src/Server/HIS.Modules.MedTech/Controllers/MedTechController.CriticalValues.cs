using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.Contracts;
using HIS.Modules.MedTech.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.MedTech.Controllers;

public partial class MedTechController
{
	public sealed record SignCriticalValueDto(string? ClinicalActionNote);

	public sealed class CriticalValueWorkItem : MedtechCriticalValue
	{
		public string ServiceRequestId { get; set; } = string.Empty;
		public string ReportNo { get; set; } = string.Empty;
		public string EncounterNo { get; set; } = string.Empty;
		public string PatientName { get; set; } = string.Empty;
		public string SigningRole { get; set; } = string.Empty;
		public bool CanSign => Status is "PUBLISHED" or "PENDING_ACK"
			&& (SigningRole == "DOCTOR" ? DocAckedBy == null : SigningRole == "NURSE" && NurseAckedBy == null);
	}

	// Resolve through the published report and its request, not the legacy alert encounter text.
	private const string CriticalValueScopeSql = """
		FROM medtech_critical_values cv
		LEFT JOIN mtc_diagnostic_reports d ON d.id = cv.report_id
		JOIN mtc_service_requests r ON r.id = COALESCE(d.service_request_id, cv.report_id)
		JOIN pat_patients p ON p.id = r.patient_id
		LEFT JOIN opd_visits o ON r.source_type = 'OPD' AND o.id = r.source_visit_id AND o.patient_id = r.patient_id
		LEFT JOIN ipd_encounters i ON r.source_type = 'IPD' AND i.id = r.source_visit_id AND i.patient_id = r.patient_id
		WHERE r.status = 3 AND r.is_critical_value = 1 AND cv.patient_id = r.patient_id
		  AND (d.id IS NULL OR (d.service_request_id = r.id AND d.patient_id = r.patient_id
		       AND d.status = 'SIGNED' AND d.is_critical_value = 1))
		  AND cv.status IN ('PUBLISHED', 'PENDING_ACK', 'ACKNOWLEDGED')
		  AND (@Id IS NULL OR cv.id = @Id)
		  AND (@ReportId IS NULL OR cv.report_id = @ReportId)
		  AND (@EncounterId IS NULL OR r.source_visit_id = @EncounterId)
		  AND (@Status IS NULL OR (@Status = 'PENDING_ACK' AND cv.status IN ('PUBLISHED', 'PENDING_ACK')) OR cv.status = @Status)
		  AND ((@Source = 'OPD' AND o.campus_id = @Campus AND o.opd_dept_id = @Dept
		        AND (@Role = 'NURSE' OR o.doctor_id = @User))
		    OR (@Source = 'IPD' AND i.campus_id = @Campus
		        AND ((@Role = 'DOCTOR' AND i.admit_dept_id = @Dept AND i.attending_doctor_id = @User)
		          OR (@Role = 'NURSE' AND i.primary_nurse_id = @User AND i.admit_ward_id IN @Wards))))
		""";

	private (string Source, string Role) CriticalValueSigningContext() => RoleCode.Normalize(_session?.RoleCode) switch
	{
		RoleCode.OpdDoctor => ("OPD", "DOCTOR"),
		RoleCode.IpdResident or RoleCode.IpdAttending => ("IPD", "DOCTOR"),
		"OPD-NURSE" => ("OPD", "NURSE"),
		RoleCode.WardNurse => ("IPD", "NURSE"),
		_ => (string.Empty, string.Empty)
	};

	private async Task NotifyCriticalValueAsync(string alertId, MtcServiceRequest request)
	{
		if (_alertPublisher == null) return;
		try
		{
			await _alertPublisher.PublishReportCriticalValueAsync(alertId, request.OrderDeptId, request.OrderWardId);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Trace.TraceWarning("Critical value notification failed: {0}", ex.GetType().Name);
		}
	}

	private async Task<IReadOnlyList<CriticalValueWorkItem>> QueryCriticalValueWorkItemsAsync(
		CancellationToken ct, string? id = null, string? reportId = null, string? encounterId = null, string? status = null)
	{
		var (source, role) = CriticalValueSigningContext();
		if (role.Length == 0 || CurrentUserId.Length == 0)
			return Array.Empty<CriticalValueWorkItem>();

		var wards = (_session!.LinkedWardIds ?? Array.Empty<string>()).Append(_session.ActiveWardId)
			.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
		using var conn = _connFactory.CreateConnection();
		return (await conn.QueryAsync<CriticalValueWorkItem>(new CommandDefinition("""
			SELECT cv.id, cv.report_id, cv.patient_id, cv.item_code, cv.item_name, cv.critical_value_text,
			       cv.published_by, cv.published_at_ms, cv.doc_acked_by, cv.doc_acked_at_ms,
			       cv.nurse_acked_by, cv.nurse_acked_at_ms, cv.status, r.id AS service_request_id,
			       r.source_type, r.source_visit_id AS visit_or_encounter_id,
			       COALESCE(d.report_no, r.request_no) AS report_no,
			       COALESCE(o.visit_no, i.admission_no) AS encounter_no, p.full_name AS patient_name,
			       @Role AS signing_role
			""" + "\n" + CriticalValueScopeSql + """

			ORDER BY CASE WHEN cv.status = 'ACKNOWLEDGED' THEN 1 ELSE 0 END, cv.published_at_ms DESC, cv.id DESC
			LIMIT 100
			""", new { Id = id, ReportId = reportId, EncounterId = encounterId, Status = status,
				Source = source, Role = role, Campus = _session.CampusId, Dept = _session.ActiveDeptId, User = CurrentUserId, Wards = wards },
			_connFactory.CurrentTransaction, cancellationToken: ct))).AsList();
	}

	[HttpGet("critical-values")]
	public async Task<ApiResponse<IReadOnlyList<CriticalValueWorkItem>>> GetCriticalValues(
		[FromQuery] string? encounterId = null, [FromQuery] string? status = null,
		[FromQuery] string? alertId = null, CancellationToken ct = default)
	{
		if (CriticalValueSigningContext().Role.Length == 0 || CurrentUserId.Length == 0)
			return ApiResponse<IReadOnlyList<CriticalValueWorkItem>>.Fail(403, "当前岗位不能查看临床危急值签收队列");
		return ApiResponse<IReadOnlyList<CriticalValueWorkItem>>.Success(
			await QueryCriticalValueWorkItemsAsync(ct, id: alertId, encounterId: encounterId, status: status));
	}

	[HttpPost("critical-values/{alertId}/acknowledge")]
	public async Task<ApiResponse<CriticalValueWorkItem>> AcknowledgeCriticalValueItem(
		string alertId, [FromBody] SignCriticalValueDto dto, CancellationToken ct = default)
	{
		if (CriticalValueSigningContext().Role.Length == 0 || CurrentUserId.Length == 0)
			return ApiResponse<CriticalValueWorkItem>.Fail(403, "危急值签收仅限当前临床医师或护士岗位");
		if (dto.ClinicalActionNote?.Trim().Length > 1000)
			return ApiResponse<CriticalValueWorkItem>.Fail(400, "处置记录不能超过 1000 字");
		if (_auditTrail == null)
			return ApiResponse<CriticalValueWorkItem>.Fail(503, "审计服务不可用，暂不能签收");

		return await _connFactory.ExecuteInTransactionAsync(async token =>
		{
			var item = (await QueryCriticalValueWorkItemsAsync(token, id: alertId)).SingleOrDefault();
			if (item == null)
				return ApiResponse<CriticalValueWorkItem>.Fail(404, "当前岗位下未找到可核对的危急值记录");
			bool isDoctor = item.SigningRole == "DOCTOR";
			string? signedBy = isDoctor ? item.DocAckedBy : item.NurseAckedBy;
			string? otherSigner = isDoctor ? item.NurseAckedBy : item.DocAckedBy;
			string actorId = CurrentUserId;
			if (string.Equals(signedBy, actorId, StringComparison.Ordinal))
				return ApiResponse<CriticalValueWorkItem>.Success(item, "本人签收已记录");
			if (signedBy != null || item.Status == "ACKNOWLEDGED")
				return ApiResponse<CriticalValueWorkItem>.Fail(409, "当前环节已签收，不能覆盖原记录");
			if (string.Equals(otherSigner, actorId, StringComparison.Ordinal))
				return ApiResponse<CriticalValueWorkItem>.Fail(409, "医护双签必须由两个不同账号完成");

			long now = SystemTime.UtcNowMs;
			string by = isDoctor ? "doc_acked_by" : "nurse_acked_by";
			string at = isDoctor ? "doc_acked_at_ms" : "nurse_acked_at_ms";
			string other = isDoctor ? "nurse_acked_by" : "doc_acked_by";
			using var conn = _connFactory.CreateConnection();
			int changed = await conn.ExecuteAsync(new CommandDefinition($"""
				UPDATE medtech_critical_values
				SET {by} = @Actor, {at} = @Now, status = CASE WHEN {other} IS NULL THEN 'PENDING_ACK' ELSE 'ACKNOWLEDGED' END,
				    updated_by = @Actor, updated_at_ms = @Now, version_no = version_no + 1
				WHERE id = @Id AND status IN ('PUBLISHED', 'PENDING_ACK')
				  AND {by} IS NULL AND ({other} IS NULL OR {other} <> @Actor)
				""", new { Id = alertId, Actor = actorId, Now = now }, _connFactory.CurrentTransaction, cancellationToken: token));
			if (changed != 1)
				return ApiResponse<CriticalValueWorkItem>.Fail(409, "危急值签收状态已变化，请刷新原记录");

			// Retain the request's legacy signature projection only for request-based reports.
			if (item.ReportId == item.ServiceRequestId)
			{
				string column = isDoctor ? "critical_ack_doc_id" : "critical_ack_nurse_id";
				int updated = await conn.ExecuteAsync(new CommandDefinition($"""
					UPDATE mtc_service_requests SET {column} = @Actor, updated_by = @Actor, updated_at_ms = @Now, version_no = version_no + 1
					WHERE id = @Id AND ({column} IS NULL OR {column} = @Actor)
					""", new { Id = item.ServiceRequestId, Actor = actorId, Now = now }, _connFactory.CurrentTransaction, cancellationToken: token));
				if (updated != 1)
					throw new InvalidOperationException("危急值历史签收与申请记录不一致，禁止覆盖原签名");
			}

			await _auditTrail.RecordAuditAsync("MTC_CRITICAL_VALUE_ACK", "MedtechCriticalValue", alertId,
				JsonSerializer.Serialize(new { item.ReportId, item.ServiceRequestId, item.VisitOrEncounterId, item.SigningRole,
					SignedAtMs = now, ClinicalActionNote = dto.ClinicalActionNote?.Trim() }),
				actorId, _session!.DisplayName, _session.ActiveDeptId, token);
			var saved = (await QueryCriticalValueWorkItemsAsync(token, id: alertId)).Single();
			return ApiResponse<CriticalValueWorkItem>.Success(saved, saved.Status == "ACKNOWLEDGED" ? "医护双签均已记录" : "本人签收已记录，等待另一岗位签收");
		}, ct);
	}

	[HttpPost("requests/{reqId}/critical-ack")]
	public async Task<ApiResponse<MtcServiceRequest>> AcknowledgeCriticalValue(
		string reqId, [FromBody] CriticalValueAckRequestDto dto, CancellationToken ct = default)
	{
		string role = CriticalValueSigningContext().Role;
		if (role.Length == 0 || CurrentUserId.Length == 0
			|| (role == "DOCTOR" && (!string.IsNullOrWhiteSpace(dto.NurseId) || dto.DoctorId?.Trim() != CurrentUserId))
			|| (role == "NURSE" && (!string.IsNullOrWhiteSpace(dto.DoctorId) || dto.NurseId?.Trim() != CurrentUserId)))
			return ApiResponse<MtcServiceRequest>.Fail(403, "只能以当前登录岗位和本人身份签收");
		var matches = await QueryCriticalValueWorkItemsAsync(ct, reportId: reqId);
		if (matches.Count != 1 || matches[0].ServiceRequestId != reqId)
			return ApiResponse<MtcServiceRequest>.Fail(404, "未找到与申请唯一关联的危急值签收记录");
		var signed = await AcknowledgeCriticalValueItem(matches[0].Id, new SignCriticalValueDto(dto.ClinicalActionNote), ct);
		if (signed.Code != 200)
			return ApiResponse<MtcServiceRequest>.Fail(signed.Code, signed.Message);
		return ApiResponse<MtcServiceRequest>.Success(await _requestRepo.GetByIdAsync(reqId, ct), signed.Message);
	}

	public sealed record ClinicalInterventionDto(string ActionDetail, string PatientOutcome, string? RecheckResult = null);

	[HttpPost("critical-values/{alertId}/intervention")]
	public async Task<ApiResponse<bool>> RecordCriticalValueIntervention(
		string alertId, [FromBody] ClinicalInterventionDto dto, CancellationToken ct = default)
	{
		if (CriticalValueSigningContext().Role != "DOCTOR" && !IsAdmin())
			return ApiResponse<bool>.Fail(403, "危急值临床干预处置记录仅限主管医师或主治医师填写");
		if (string.IsNullOrWhiteSpace(dto.ActionDetail))
			return ApiResponse<bool>.Fail(400, "必须填写采取的临床处置措施（如：立即降钾用药、给氧、抢救等）");
		if (string.IsNullOrWhiteSpace(dto.PatientOutcome))
			return ApiResponse<bool>.Fail(400, "必须记录处置后患者转归（如：好转、稳定、转ICU等）");

		using var conn = _connFactory.CreateConnection();
		var cv = await conn.QuerySingleOrDefaultAsync<MedtechCriticalValue>(
			"SELECT * FROM medtech_critical_values WHERE id = @Id", new { Id = alertId });
		if (cv == null) return ApiResponse<bool>.Fail(404, "危急值记录不存在");

		long now = SystemTime.UtcNowMs;
		await conn.ExecuteAsync(@"
			UPDATE medtech_critical_values
			SET status = 'CLOSED', doc_acked_by = COALESCE(doc_acked_by, @User),
			    doc_acked_at_ms = COALESCE(doc_acked_at_ms, @Now)
			WHERE id = @Id", new { Id = alertId, User = CurrentUserId, Now = now });

		if (!string.IsNullOrWhiteSpace(cv.ReportId))
		{
			await conn.ExecuteAsync(@"
				UPDATE mtc_diagnostic_reports
				SET clinical_intervention_action = @Action, intervention_doctor_id = @User,
				    intervention_at_ms = @Now, patient_outcome = @Outcome, is_closed = 1
				WHERE id = @ReportId",
				new { ReportId = cv.ReportId, Action = dto.ActionDetail, User = CurrentUserId, Now = now, Outcome = dto.PatientOutcome });
		}

		if (_auditTrail != null)
			await _auditTrail.RecordAuditAsync("CRITICAL_VALUE_INTERVENTION_CLOSED", nameof(MedtechCriticalValue), alertId,
				$"临床医师 {CurrentUserId} 录入危急值处置闭环：{dto.ActionDetail}，转归：{dto.PatientOutcome}", CurrentUserId, CurrentUserId, _session?.ActiveDeptId);

		return ApiResponse<bool>.Success(true, "危急值临床干预措施已记录，该危急值流程正式闭环归档");
	}
}
