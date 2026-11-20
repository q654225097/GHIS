using System;
using System.Collections.Generic;
using System.Text.Json;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public sealed class EmrUserTemplateModel : ClientModelBase
{
    private string _id = string.Empty;
    private string _userId = string.Empty;
    private string _userName = string.Empty;
    private string? _deptId;
    private string? _deptName;
    private string _baseTemplateId = string.Empty;
    private string _baseTemplateName = string.Empty;
    private string _templateCode = string.Empty;
    private string _templateName = string.Empty;
    private string _emrScene = "IPD";
    private string _docTypeCode = string.Empty;
    private string _defaultContentJson = "{}";
    private bool _isSharedDept;
    private int _sortOrder;

    public string Id { get => _id; set => SetField(ref _id, value); }
    public string UserId { get => _userId; set => SetField(ref _userId, value); }
    public string UserName { get => _userName; set => SetField(ref _userName, value); }
    public string? DeptId { get => _deptId; set => SetField(ref _deptId, value); }
    public string? DeptName { get => _deptName; set => SetField(ref _deptName, value); }
    public string BaseTemplateId { get => _baseTemplateId; set => SetField(ref _baseTemplateId, value); }
    public string BaseTemplateName { get => _baseTemplateName; set => SetField(ref _baseTemplateName, value); }
    public string TemplateCode { get => _templateCode; set => SetField(ref _templateCode, value); }
    public string TemplateName { get => _templateName; set => SetField(ref _templateName, value); }
    public string EmrScene { get => _emrScene; set => SetField(ref _emrScene, value); }
    public string DocTypeCode { get => _docTypeCode; set => SetField(ref _docTypeCode, value); }
    public string DefaultContentJson { get => _defaultContentJson; set => SetField(ref _defaultContentJson, value); }
    public bool IsSharedDept { get => _isSharedDept; set => SetField(ref _isSharedDept, value); }
    public int SortOrder { get => _sortOrder; set => SetField(ref _sortOrder, value); }

    public string DisplayText => IsSharedDept ? $"[科室共享] {TemplateName}" : $"[个人常用] {TemplateName}";
    public string ScopeBadgeText => IsSharedDept ? "科室共享" : "个人常用";
    public string ScopeBrush => IsSharedDept ? "#1565C0" : "#2E7D32";

    public string PreviewSummary => ExtractPreviewSummary();

    public string ExtractPreviewSummary()
    {
        if (string.IsNullOrWhiteSpace(DefaultContentJson) || DefaultContentJson == "{}")
            return "暂无预定义常用语";
        try
        {
            using var doc = JsonDocument.Parse(DefaultContentJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return DefaultContentJson;
            var parts = new List<string>();
            if (doc.RootElement.TryGetProperty("CHIEF_COMPLAINT", out var cc) && !string.IsNullOrWhiteSpace(cc.GetString()))
                parts.Add($"主诉: {cc.GetString()}");
            if (doc.RootElement.TryGetProperty("PRIMARY_DIAGNOSIS", out var diag) && !string.IsNullOrWhiteSpace(diag.GetString()))
                parts.Add($"诊断: {diag.GetString()}");
            if (doc.RootElement.TryGetProperty("PRESENT_ILLNESS", out var pi) && !string.IsNullOrWhiteSpace(pi.GetString()))
                parts.Add($"现病史: {pi.GetString()}");
            if (doc.RootElement.TryGetProperty("TREATMENT_PLAN", out var tp) && !string.IsNullOrWhiteSpace(tp.GetString()))
                parts.Add($"处置: {tp.GetString()}");
            if (doc.RootElement.TryGetProperty("PROGRESS_CONTENT", out var pc) && !string.IsNullOrWhiteSpace(pc.GetString()))
                parts.Add($"查房记录: {pc.GetString()}");
            if (doc.RootElement.TryGetProperty("PHYSICAL_EXAM", out var pe) && !string.IsNullOrWhiteSpace(pe.GetString()))
                parts.Add($"查体: {pe.GetString()}");
            return parts.Count > 0 ? string.Join(" | ", parts) : "预定义结构化字段已配置";
        }
        catch
        {
            return "预定义常用语数据";
        }
    }
}

public sealed record EmrUserTemplateRecordDto(
    string Id,
    string UserId,
    string UserName,
    string? DeptId,
    string? DeptName,
    string BaseTemplateId,
    string TemplateCode,
    string TemplateName,
    string EmrScene,
    string DocTypeCode,
    string DefaultContentJson,
    short IsSharedDept,
    int SortOrder
);
