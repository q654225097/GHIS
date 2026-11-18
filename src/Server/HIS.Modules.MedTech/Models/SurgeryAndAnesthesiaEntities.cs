using System;
using System.Collections.Generic;
using HIS.BuildingBlocks.Database;

namespace HIS.Modules.MedTech.Models;

public class MteSurgerySchedule : BaseEntity
{
    public string ScheduleNo { get; set; } = string.Empty;
    public string EncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string SurgeryCode { get; set; } = string.Empty;
    public string SurgeryName { get; set; } = string.Empty;
    public int SurgeryLevel { get; set; } = 1;
    public string OrRoomId { get; set; } = string.Empty;
    public int TableSeq { get; set; } = 1;
    public string SurgeonId { get; set; } = string.Empty;
    public string SurgeonName { get; set; } = string.Empty;
    public string? AssistantIdsJson { get; set; }
    public string AnesthesiologistId { get; set; } = string.Empty;
    public string AnesthesiologistName { get; set; } = string.Empty;
    public string? CirculatingNurseId { get; set; }
    public string? ScrubNurseId { get; set; }
    public long ScheduledStartMs { get; set; }
    public int EstimatedDurationMin { get; set; } = 60;
    public long? ActualStartMs { get; set; }
    public long? ActualEndMs { get; set; }
    public int Status { get; set; } = 0; // 0=SCHEDULED, 1=STARTED/IN_PROGRESS, 2=FINISHED, 3=CANCELLED
    public short WhoSignInOk { get; set; } = 0;
    public short WhoTimeoutOk { get; set; } = 0;
    public short WhoSignOutOk { get; set; } = 0;
    public short InstrumentCountBalanced { get; set; } = 0;
}

public class MteAnesthesiaRecord : BaseEntity
{
    public string RecordNo { get; set; } = string.Empty;
    public string ScheduleId { get; set; } = string.Empty;
    public string EncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string AsaGrade { get; set; } = "I"; // I, II, III, IV, V, E
    public string AnesthesiaType { get; set; } = "GENERAL"; // GENERAL, SPINAL, EPIDURAL, LOCAL, NERVE_BLOCK
    public string? PreopDiagnosis { get; set; }
    public string? AirwayAssessment { get; set; }
    public string AnesthesiologistId { get; set; } = string.Empty;
    public string AnesthesiologistName { get; set; } = string.Empty;
    public long? AnesthesiaStartMs { get; set; }
    public long? AnesthesiaEndMs { get; set; }
    public decimal BloodLossMl { get; set; } = 0m;
    public decimal UrineOutputMl { get; set; } = 0m;
    public decimal FluidInputMl { get; set; } = 0m;
    public string? IntraopEventsJson { get; set; }
    public int Status { get; set; } = 0; // 0=IN_PROGRESS, 1=COMPLETED
}

public class MteAnesthesiaMedication : BaseEntity
{
    public string AnesthesiaRecordId { get; set; } = string.Empty;
    public string DrugId { get; set; } = string.Empty;
    public string DrugName { get; set; } = string.Empty;
    public decimal Dose { get; set; }
    public string DoseUnit { get; set; } = "mg";
    public string Route { get; set; } = "IV";
    public long GivenAtMs { get; set; }
    public string OperatorId { get; set; } = string.Empty;
    public string? Remark { get; set; }
}

public class MteRecoveryRecord : BaseEntity
{
    public string AnesthesiaRecordId { get; set; } = string.Empty;
    public string ScheduleId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string? PacuBedNo { get; set; }
    public long EntryTimeMs { get; set; }
    public long? ExitTimeMs { get; set; }
    public int StewardScore { get; set; } = 0;
    public int AldreteScore { get; set; } = 0;
    public string? Consciousness { get; set; }
    public string? Respiration { get; set; }
    public string? Circulation { get; set; }
    public decimal? Spo2 { get; set; }
    public short DischargeCriteriaMet { get; set; } = 0;
    public string EvaluatorId { get; set; } = string.Empty;
    public string? Remark { get; set; }
}

public class MtePostopFollowup : BaseEntity
{
    public string AnesthesiaRecordId { get; set; } = string.Empty;
    public string ScheduleId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public int FollowupDay { get; set; } = 1;
    public int PainScore { get; set; } = 0;
    public short NauseaVomiting { get; set; } = 0;
    public short ConsciousnessRecovery { get; set; } = 1;
    public short ThroatPain { get; set; } = 0;
    public string? Complications { get; set; }
    public long FollowupTimeMs { get; set; }
    public string FollowupBy { get; set; } = string.Empty;
}

// DTOs
public sealed record ScheduleSurgeryRequest(
    string EncounterId,
    string PatientId,
    string PatientName,
    string SurgeryCode,
    string SurgeryName,
    int SurgeryLevel,
    string OrRoomId,
    int TableSeq,
    string SurgeonId,
    string SurgeonName,
    string AnesthesiologistId,
    string AnesthesiologistName,
    string? CirculatingNurseId,
    string? ScrubNurseId,
    long ScheduledStartMs,
    int EstimatedDurationMin = 60,
    List<string>? AssistantIds = null
);

public sealed record UpdateScheduleRequest(
    string? OrRoomId,
    int? TableSeq,
    string? SurgeonId,
    string? SurgeonName,
    string? AnesthesiologistId,
    string? AnesthesiologistName,
    long? ScheduledStartMs,
    int? EstimatedDurationMin,
    string? Status
);

public sealed record WhoThreeCheckRequest(
    bool SignInOk,
    bool TimeOutOk,
    bool SignOutOk,
    bool InstrumentCountBalanced
);

public sealed record CreateAnesthesiaRecordRequest(
    string ScheduleId,
    string AsaGrade,
    string AnesthesiaType,
    string? PreopDiagnosis,
    string? AirwayAssessment,
    string? AnesthesiologistId,
    string? AnesthesiologistName
);

public sealed record UpdateAnesthesiaRecordRequest(
    string? AsaGrade,
    string? AnesthesiaType,
    decimal? BloodLossMl,
    decimal? UrineOutputMl,
    decimal? FluidInputMl,
    string? IntraopEventsJson,
    bool MarkCompleted = false
);

public sealed record AddAnesthesiaMedicationRequest(
    string DrugId,
    string DrugName,
    decimal Dose,
    string DoseUnit,
    string Route,
    string? Remark
);

public sealed record PacuRecoveryRequest(
    string? PacuBedNo,
    int StewardScore,
    int AldreteScore,
    string? Consciousness,
    string? Respiration,
    string? Circulation,
    decimal? Spo2,
    bool DischargeCriteriaMet,
    string? Remark
);

public sealed record PostopFollowupRequest(
    int FollowupDay,
    int PainScore,
    bool NauseaVomiting,
    bool ConsciousnessRecovery,
    bool ThroatPain,
    string? Complications
);
