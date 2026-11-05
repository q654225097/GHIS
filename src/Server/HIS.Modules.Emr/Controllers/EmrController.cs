using System.Text.Json;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.OfdEngine;
using HIS.Contracts;
using HIS.Modules.Emr.Models;
using HIS.Modules.Emr.Services;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Emr.Controllers;

[ApiController]
[Route("api/v4/emr")]
public class EmrController : ControllerBase
{
    private readonly IRepository<EmrDocument> _docRepo;
    private readonly IRepository<EmrDocumentElement> _docElementRepo;
    private readonly IRepository<EmrOfdArchive> _archiveRepo;
    private readonly EmrQualityControlService _qcService;
    private readonly EmrOfdRenderService _ofdRenderService;
    private readonly IUserSession? _userSession;

    public EmrController(
        IRepository<EmrDocument> docRepo,
        IRepository<EmrDocumentElement> docElementRepo,
        IRepository<EmrOfdArchive> archiveRepo,
        EmrQualityControlService qcService,
        EmrOfdRenderService ofdRenderService,
        IUserSession? userSession = null)
    {
        _docRepo = docRepo;
        _docElementRepo = docElementRepo;
        _archiveRepo = archiveRepo;
        _qcService = qcService;
        _ofdRenderService = ofdRenderService;
        _userSession = userSession;
    }

    public sealed record SaveEmrRequestDto(
        string DocNo,
        string EmrScene,
        string VisitOrEncounterId,
        string PatientId,
        string DeptId,
        string? WardId,
        string TemplateId,
        string DocTypeCode,
        string DocTitle,
        Dictionary<string, string> StructuredElements,
        string PlainTextSummary,
        string AuthorId
    );

    [HttpPost("documents")]
    public async Task<ApiResponse<EmrDocument>> SaveDocument([FromBody] SaveEmrRequestDto req)
    {
        // 1. Run Quality Control
        var qcResult = await _qcService.ValidateStructuredElementsAsync(req.PatientId, req.StructuredElements);
        if (!qcResult.IsSuccess)
        {
            return ApiResponse<EmrDocument>.Fail(400, qcResult.Error, default);
        }

        // 2. Insert or update doc
        var existingDocs = await _docRepo.QueryAsync(
            "SELECT * FROM emr_documents WHERE doc_no = @DocNo",
            new { DocNo = req.DocNo });

        EmrDocument doc;
        if (existingDocs.Count > 0)
        {
            doc = existingDocs[0];
            doc.StructuredAstJson = JsonSerializer.Serialize(req.StructuredElements);
            doc.PlainTextSummary = req.PlainTextSummary;
            doc.DocTitle = req.DocTitle;
            await _docRepo.UpdateAsync(doc);

            // Clear old elements
            await _docElementRepo.ExecuteAsync(
                "DELETE FROM emr_document_elements WHERE document_id = @DocId",
                new { DocId = doc.Id });
        }
        else
        {
            doc = new EmrDocument
            {
                DocNo = req.DocNo,
                EmrScene = req.EmrScene,
                VisitOrEncounterId = req.VisitOrEncounterId,
                PatientId = req.PatientId,
                DeptId = req.DeptId,
                WardId = req.WardId,
                TemplateId = req.TemplateId,
                DocTypeCode = req.DocTypeCode,
                DocTitle = req.DocTitle,
                StructuredAstJson = JsonSerializer.Serialize(req.StructuredElements),
                PlainTextSummary = req.PlainTextSummary,
                Status = 0,
                AuthorId = req.AuthorId,
                ClinicalTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            await _docRepo.InsertAsync(doc);
        }

        // 3. Save broken-down elements for ANSI relational search
        foreach (var (code, val) in req.StructuredElements)
        {
            var el = new EmrDocumentElement
            {
                DocumentId = doc.Id,
                PatientId = req.PatientId,
                EmrScene = req.EmrScene,
                DocTypeCode = req.DocTypeCode,
                SectionCode = "MAIN",
                ElementCode = code,
                ValString = val
            };
            await _docElementRepo.InsertAsync(el);
        }

        return ApiResponse<EmrDocument>.Success(doc, "病历草稿保存成功");
    }

    public sealed record SignEmrRequestDto(string SignerId, string SignerName, int SignLevel);

    [HttpPost("documents/{docId}/sign-and-generate-ofd")]
    public async Task<ApiResponse<EmrOfdArchive>> SignAndGenerateOfd(string docId, [FromBody] SignEmrRequestDto req)
    {
        var doc = await _docRepo.GetByIdAsync(docId);
        if (doc == null)
            return ApiResponse<EmrOfdArchive>.Fail(404, "未找到病历记录");

        // Validate QC again before signing
        if (!string.IsNullOrEmpty(doc.StructuredAstJson))
        {
            var elements = JsonSerializer.Deserialize<Dictionary<string, string>>(doc.StructuredAstJson) ?? new();
            var qc = await _qcService.ValidateStructuredElementsAsync(doc.PatientId, elements);
            if (!qc.IsSuccess)
                return ApiResponse<EmrOfdArchive>.Fail(400, qc.Error, default);
        }

        // Update sign status
        if (req.SignLevel == 1)
        {
            doc.Status = 1; // ResidentSigned
            doc.ResidentSignedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
        else if (req.SignLevel == 2)
        {
            doc.Status = 2; // AttendingSigned
            doc.AttendingDoctorId = req.SignerId;
            doc.AttendingSignedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
        else if (req.SignLevel >= 3)
        {
            doc.Status = 3; // ChiefLocked
            doc.ChiefDoctorId = req.SignerId;
            doc.ChiefSignedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        // Render and archive OFD
        var archive = await _ofdRenderService.RenderAndArchiveAsync(doc, req.SignerName);
        doc.LatestOfdArchiveId = archive.Id;
        await _docRepo.UpdateAsync(doc);

        return ApiResponse<EmrOfdArchive>.Success(archive, "病历审签成功并已生成符合 GB/T 33190-2016 标准的 OFD 版式文档");
    }

    [HttpGet("archives/{archiveId}/export-ofd")]
    public async Task<IActionResult> ExportOfd(string archiveId)
    {
        // Check permission if session exists
        if (_userSession != null && !_userSession.HasPermission("EMR.Toolbar.BtnExportOfd"))
        {
            return StatusCode(403, new { Code = 403, Message = "无权限导出 OFD 文档 (需要 EMR.Toolbar.BtnExportOfd)" });
        }

        var archive = await _archiveRepo.GetByIdAsync(archiveId);
        if (archive == null)
            return NotFound(new { Code = 404, Message = "未找到 OFD 归档文件" });

        var filePath = Path.Combine("AppData/OfdStorage", $"{archive.OfdNo.Replace("OFD-", "")}.ofd");
        if (!System.IO.File.Exists(filePath))
            return NotFound(new { Code = 404, Message = "物理 OFD 文件不存在" });

        var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
        return File(bytes, "application/ofd", $"{archive.OfdNo}.ofd");
    }
}

