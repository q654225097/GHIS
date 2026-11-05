using System.Text.Json;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.OfdEngine;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.Emr.Models;

namespace HIS.Modules.Emr.Services;

public class EmrQualityControlService
{
    private readonly IRepository<EmrDataElement> _elementRepo;
    private readonly IPatientReadOnlyContract _patientContract;

    public EmrQualityControlService(
        IRepository<EmrDataElement> elementRepo,
        IPatientReadOnlyContract patientContract)
    {
        _elementRepo = elementRepo;
        _patientContract = patientContract;
    }

    public async Task<Result<bool>> ValidateStructuredElementsAsync(
        string patientId,
        Dictionary<string, string> elementValues,
        CancellationToken ct = default)
    {
        var patient = await _patientContract.GetPatientSummaryAsync(patientId, ct);
        var isMale = patient?.GenderCode == "1" || patient?.GenderCode?.ToUpperInvariant() == "MALE";
        var isFemale = patient?.GenderCode == "2" || patient?.GenderCode?.ToUpperInvariant() == "FEMALE";

        foreach (var (elementCode, val) in elementValues)
        {
            if (string.IsNullOrWhiteSpace(val)) continue;

            var elements = await _elementRepo.QueryAsync(
                "SELECT * FROM emr_data_elements WHERE element_code = @Code AND is_active = 1",
                new { Code = elementCode }, ct);
            var el = elements.FirstOrDefault();
            if (el == null) continue;

            // Check gender exclusivity
            if (isMale && el.GenderExclusive.Equals("FEMALE_ONLY", StringComparison.OrdinalIgnoreCase))
            {
                return Result<bool>.Fail(
                    $"患者为男性，禁止录入女性专有病历数据元 [{el.ElementName}]({el.ElementCode})",
                    "EMR_QC_GENDER_CONFLICT");
            }
            if (isFemale && el.GenderExclusive.Equals("MALE_ONLY", StringComparison.OrdinalIgnoreCase))
            {
                return Result<bool>.Fail(
                    $"患者为女性，禁止录入男性专有病历数据元 [{el.ElementName}]({el.ElementCode})",
                    "EMR_QC_GENDER_CONFLICT");
            }
        }

        return Result<bool>.Success(true);
    }
}

public class EmrOfdRenderService
{
    private readonly IRepository<EmrOfdArchive> _archiveRepo;

    public EmrOfdRenderService(IRepository<EmrOfdArchive> archiveRepo)
    {
        _archiveRepo = archiveRepo;
    }

    public async Task<EmrOfdArchive> RenderAndArchiveAsync(
        EmrDocument doc,
        string signerName,
        string? watermark = null,
        CancellationToken ct = default)
    {
        var ofdOptions = new OfdRenderOptions(
            DocId: doc.Id,
            DocNo: doc.DocNo,
            Title: doc.DocTitle,
            Author: signerName,
            CreationDate: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ContentText: doc.PlainTextSummary ?? "结构化病历内容",
            StructuredSemanticXml: $"""
            <EmrDocumentOfdData DocNo="{doc.DocNo}" Scene="{doc.EmrScene}" DocTypeCode="{doc.DocTypeCode}">
              <PatientId>{doc.PatientId}</PatientId>
              <VisitId>{doc.VisitOrEncounterId}</VisitId>
              <Signer>{signerName}</Signer>
              <StructuredAst>{doc.StructuredAstJson}</StructuredAst>
            </EmrDocumentOfdData>
            """,
            SignerName: signerName,
            WatermarkText: watermark
        );

        var ofdBytes = OfdDocumentPackage.BuildOfdPackage(ofdOptions);
        var sha256 = CryptoHelper.ComputeSha256(Convert.ToBase64String(ofdBytes));

        // Save binary stream (in memory / mock file store key)
        var fileKey = $"ofd/{doc.DocNo}.ofd";
        Directory.CreateDirectory("AppData/OfdStorage");
        await File.WriteAllBytesAsync(Path.Combine("AppData/OfdStorage", $"{doc.DocNo}.ofd"), ofdBytes, ct);

        var archive = new EmrOfdArchive
        {
            OfdNo = $"OFD-{doc.DocNo}",
            ArchiveScope = "SINGLE_DOC",
            VisitOrEncounterId = doc.VisitOrEncounterId,
            DocumentId = doc.Id,
            PatientId = doc.PatientId,
            OfdVersionStandard = "GB/T 33190-2016",
            PageCount = 1,
            FileStorageKey = fileKey,
            FileSizeBytes = ofdBytes.Length,
            ContentSha256 = sha256,
            Sm3Digest = sha256.Substring(0, 32),
            CaSignSerialNo = $"CA-{Guid.NewGuid():N}",
            TsaTimestampToken = $"TSA-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            HasEmbeddedSemanticXml = 1,
            ArchiveStatus = 0,
            GeneratedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await _archiveRepo.InsertAsync(archive, ct);
        return archive;
    }
}
