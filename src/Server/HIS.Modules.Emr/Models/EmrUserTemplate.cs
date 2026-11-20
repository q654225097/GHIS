using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Emr.Models;

/// <summary>
/// 临床医护个人常用病历/护理文书模板（衍生自系统主模板）
/// </summary>
public class EmrUserTemplate : BaseEntity
{
    public string UserId { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string? DeptId { get; set; }

    public string? DeptName { get; set; }

    public string BaseTemplateId { get; set; } = string.Empty;

    public string TemplateCode { get; set; } = string.Empty;

    public string TemplateName { get; set; } = string.Empty;

    public string EmrScene { get; set; } = "IPD"; // IPD (住院病历) / WARD (护理文书) / OPD (门诊病历)

    public string DocTypeCode { get; set; } = string.Empty;

    public string DefaultContentJson { get; set; } = "{}";

    public short IsSharedDept { get; set; } = 0; // 0=个人私有, 1=科室共享

    public int SortOrder { get; set; } = 0;

    public short IsActive { get; set; } = 1;
}
