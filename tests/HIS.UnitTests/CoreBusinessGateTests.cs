using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.OfdEngine;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.Emr.Models;
using HIS.Modules.Emr.Services;
using HIS.Modules.Inpatient.Services;
using Xunit;

namespace HIS.UnitTests;

public class CoreBusinessGateTests
{
    private sealed class MockPatientContract : IPatientReadOnlyContract
    {
        private readonly string _genderCode;
        public MockPatientContract(string genderCode) => _genderCode = genderCode;

        public Task<PatientSummaryDto?> GetPatientSummaryAsync(string patientId, CancellationToken ct = default)
        {
            return Task.FromResult<PatientSummaryDto?>(new PatientSummaryDto(
                patientId, "MRN001", "张小明", _genderCode, 19900101, 35, "138****0000",
                new[] { "青霉素过敏" }, false, true));
        }
    }

    [Fact]
    public async Task Gate03_MalePatient_FemaleOnlyElement_MustBeBlockedWith_EMR_QC_GENDER_CONFLICT()
    {
        var connFactory = new SqliteConnectionFactory("Data Source=:memory:;Mode=Memory;Cache=Shared");
        await DbSchemaInitializer.InitializeAsync(connFactory);

        // Seed data elements
        var elementRepo = new DapperRepository<EmrDataElement>(connFactory, "emr_data_elements");
        await elementRepo.InsertAsync(new EmrDataElement
        {
            ElementCode = "DE_PREGNANCY_COUNT",
            ElementName = "孕产次数",
            GenderExclusive = "FEMALE_ONLY",
            DataType = "INTEGER",
            UrsaControlType = "u:NumericIntUpDown"
        });

        // Test with Male Patient
        var patientContract = new MockPatientContract("1"); // 1 = Male
        var qcService = new EmrQualityControlService(elementRepo, patientContract);

        var elements = new Dictionary<string, string>
        {
            { "DE_PREGNANCY_COUNT", "2" }
        };

        var result = await qcService.ValidateStructuredElementsAsync("PAT-MALE-01", elements);

        Assert.False(result.IsSuccess);
        Assert.Equal("EMR_QC_GENDER_CONFLICT", result.ErrorCode);
        Assert.Contains("禁止录入女性专有病历数据元", result.Error);
    }

    [Fact]
    public void Gate04_GB_T_33190_2016_Ofd_Package_And_TamperDetection()
    {
        var options = new OfdRenderOptions(
            DocId: "DOC-2026-TEST",
            DocNo: "MRN-20261105-001",
            Title: "住院入院记录 (国标版式)",
            Author: "王崇 (主治医师)",
            CreationDate: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ContentText: "【主诉】发作性胸痛3天。\n【现病史】劳累后胸骨后压榨性疼痛。\n【初步诊断】冠状动脉粥样硬化性心脏病。",
            SignerName: "王崇 (CA数字签章)",
            WatermarkText: "HIS-V4 归档防伪件"
        );

        var ofdBytes = OfdDocumentPackage.BuildOfdPackage(options);
        Assert.NotNull(ofdBytes);
        Assert.True(ofdBytes.Length > 0);

        // 1. Verify valid package structure
        var verification = OfdDocumentPackage.VerifyOfdPackage(ofdBytes);
        Assert.True(verification.IsValid);
        Assert.Equal("DOC-2026-TEST", verification.DocId);
        Assert.True(verification.HasSemanticXml);
        Assert.True(verification.HasSignature);
        Assert.NotNull(verification.ContentSha256);

        // 2. Tamper the binary (corrupt PK zip magic bytes)
        var tamperedBytes = (byte[])ofdBytes.Clone();
        tamperedBytes[0] = 0x00;
        tamperedBytes[1] = 0x00;

        var tamperedVerification = OfdDocumentPackage.VerifyOfdPackage(tamperedBytes);
        Assert.False(tamperedVerification.IsValid);
    }

    [Fact]
    public void Gate04_Reverse_Xml_SemanticData_Lossless_Extraction_Test()
    {
        var structuredXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <EmrStructuredData xmlns="http://www.ofdspec.org/2016" Version="1.0" DocNo="DOC-2026-REVERSE">
          <PatientInfo>
            <PatientName>周建强</PatientName>
            <Gender>男</Gender>
            <Age>41</Age>
          </PatientInfo>
          <ClinicalData>
            <ChiefComplaint>阵发性心前区隐痛、心悸3天</ChiefComplaint>
            <Diagnosis>冠状动脉粥样硬化性心脏病</Diagnosis>
          </ClinicalData>
        </EmrStructuredData>
        """;

        var options = new OfdRenderOptions(
            DocId: "DOC-2026-REVERSE",
            DocNo: "MRN-20261105-002",
            Title: "门诊病历 (国标版式)",
            Author: "张建国 (主治医师)",
            CreationDate: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ContentText: "【主诉】阵发性心前区隐痛、心悸3天。\n【初步诊断】冠状动脉粥样硬化性心脏病。",
            StructuredSemanticXml: structuredXml,
            SignerName: "张建国 (主治医师)",
            WatermarkText: "三甲门诊病历归档专章"
        );

        var ofdBytes = OfdDocumentPackage.BuildOfdPackage(options);
        Assert.NotNull(ofdBytes);

        using var ms = new MemoryStream(ofdBytes);
        using var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
        var entry = archive.GetEntry("Doc_0/CustomTags/EmrStructuredData.xml");
        Assert.NotNull(entry);

        using var reader = new StreamReader(entry.Open(), System.Text.Encoding.UTF8);
        var extractedXml = reader.ReadToEnd();
        var xdoc = System.Xml.Linq.XDocument.Parse(extractedXml);

        var patientName = xdoc.Descendants().FirstOrDefault(x => x.Name.LocalName == "PatientName")?.Value;
        var diagnosis = xdoc.Descendants().FirstOrDefault(x => x.Name.LocalName == "Diagnosis")?.Value;

        Assert.Equal("周建强", patientName);
        Assert.Equal("冠状动脉粥样硬化性心脏病", diagnosis);
    }

    [Fact]
    public void CryptoHelper_AesGcm_And_BlindIndex_Tests()
    {
        var plainIdCard = "110101199003072345";
        var blindIndex = CryptoHelper.ComputeBlindIndex(plainIdCard);
        Assert.NotEmpty(blindIndex);

        // Exact match repeatable
        var blindIndex2 = CryptoHelper.ComputeBlindIndex(plainIdCard);
        Assert.Equal(blindIndex, blindIndex2);

        // Encryption and decryption
        var cipher = CryptoHelper.EncryptAesGcm(plainIdCard);
        Assert.NotEqual(plainIdCard, cipher);

        var decrypted = CryptoHelper.DecryptAesGcm(cipher);
        Assert.Equal(plainIdCard, decrypted);

        // Masking
        var masked = CryptoHelper.MaskIdCard(plainIdCard);
        Assert.Equal("1101**********2345", masked);
    }

    [Fact]
    public async Task Gate07_Inpatient_DischargeSixItemCheck_BlocksWhenOrdersNotStopped()
    {
        var connFactory = new SqliteConnectionFactory("Data Source=:memory:;Mode=Memory;Cache=Shared");
        await DbSchemaInitializer.InitializeAsync(connFactory);

        // Insert unstopped order
        using (var conn = connFactory.CreateConnection())
        {
            await Dapper.SqlMapper.ExecuteAsync(conn, """
            INSERT INTO ord_medical_orders (id, order_no, encounter_id, patient_id, order_ipd_dept_id, patient_ward_id, exec_target_dept_id, order_term_type, group_no, order_category, item_code, item_name, dosage, frequency_code, route_code, status, order_doctor_id, plan_start_at_ms, schema_version, created_at_ms, updated_at_ms, version_no)
            VALUES ('ORD-01', 'ORD001', 'ENC-01', 'PAT-01', 'DEPT-IPD-01', 'DEPT-WARD-01', 'DEPT-WARD-01', 1, 1, 'DRUG', 'ITEM-01', '阿司匹林', 100, 'QD', 'PO', 1, 'DOC-01', 1000, 1, 1000, 1000, 1)
            """);
        }

        var dischargeService = new DischargeChecklistService(connFactory);
        var checkResult = await dischargeService.PerformSixItemCheckAsync("ENC-01");

        Assert.False(checkResult.AllPassed);
        Assert.Contains(checkResult.FailedItems, item => item.Contains("未停止的长期医嘱"));
    }
}
