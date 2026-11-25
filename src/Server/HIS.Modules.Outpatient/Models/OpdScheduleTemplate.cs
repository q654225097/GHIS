using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Outpatient.Models;

/// <summary>Reusable weekly rule used to publish dated outpatient slots.</summary>
public class OpdScheduleTemplate : BaseEntity
{
    public string CampusId { get; set; } = "CAMPUS-01";

    public string OpdDeptId { get; set; } = string.Empty;

    public string? ClinicRoomId { get; set; }

    public string DoctorId { get; set; } = string.Empty;

    public string TemplateName { get; set; } = string.Empty;

    public string WeekdayCodes { get; set; } = "1,2,3,4,5";

    public string SlotPeriodCode { get; set; } = "MORNING";

    public int StartMinute { get; set; } = 480;

    public int EndMinute { get; set; } = 720;

    public string RegLevelCode { get; set; } = "NORMAL";

    public decimal RegistrationFee { get; set; } = 15.00m;

    public int TotalCapacity { get; set; } = 30;

    public int SlotIntervalMinutes { get; set; } = 0;

    public int IsActive { get; set; } = 1;
}
