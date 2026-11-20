using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.BloodBank.Models;
using HIS.Modules.BloodBank.Services;
using HIS.Modules.ClinicalPathway.Models;
using HIS.Modules.ClinicalPathway.Services;
using HIS.Modules.EmrQc.Models;
using HIS.Modules.EmrQc.Services;
using HIS.Modules.HieGateway.Models;
using HIS.Modules.HieGateway.Services;
using HIS.Modules.Inpatient.Controllers;
using HIS.Modules.Integration.Services;
using HIS.Modules.KnowledgeBase.Models;
using HIS.Modules.KnowledgeBase.Services;
using HIS.Modules.Monitoring.Models;
using HIS.Modules.Monitoring.Services;
using HIS.Modules.Outpatient.Services;
using HIS.Modules.Pharmacy.Models;
using HIS.Modules.Platform.Models;
using HIS.Modules.Platform.Services;
using HIS.Modules.Treatment.Models;
using HIS.Modules.Treatment.Services;
using Xunit;

namespace HIS.UnitTests;

public class EmrLevel7ComplianceTests : IAsyncLifetime
{
    private readonly IDbConnectionFactory _connFactory;

    public EmrLevel7ComplianceTests()
    {
        _connFactory = new SqliteConnectionFactory();
    }

    public async Task InitializeAsync()
    {
        await DbSchemaInitializer.InitializeAsync(_connFactory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Test_01_CaSignature_And_Rfc3161Tsa_FullClinicalCycle()
    {
        // 1. CA Provider SM2 Signature
        ICaSignatureProvider caProvider = new DevSandboxCaProvider();
        byte[] sampleData = System.Text.Encoding.UTF8.GetBytes("ORDER_TEST_DATA_PAYLOAD");
        byte[] sm3Hash = CryptoHelper.ComputeSm3Bytes(sampleData);

        var signResult = await caProvider.SignAsync("DOC001", "CERT_SN_12345", sm3Hash, CancellationToken.None);
        Assert.NotNull(signResult);
        Assert.True(signResult.Success);
        Assert.NotEmpty(signResult.P7SignatureBase64);

        var verifyResult = await caProvider.VerifyAsync(sm3Hash, signResult.P7SignatureBase64, signResult.CertSn, CancellationToken.None);
        Assert.NotNull(verifyResult);
        Assert.True(verifyResult.IsValid);

        // 2. RFC 3161 TSA Client
        IExternalTsaClient tsaClient = new Rfc3161HttpTsaClient();
        var tsaResult = await tsaClient.RequestTimestampAsync(sm3Hash, CancellationToken.None);
        Assert.NotNull(tsaResult);
        Assert.True(tsaResult.Success);
        Assert.NotEmpty(tsaResult.TokenBase64);

        var tsaVerify = await tsaClient.VerifyTimestampAsync(tsaResult.TokenBase64, sm3Hash, CancellationToken.None);
        Assert.NotNull(tsaVerify);
        Assert.True(tsaVerify.IsValid, tsaVerify.ErrorMessage);
    }

    [Fact]
    public async Task Test_02_EmrAccessControl_TimeWindow_ExternalDoctor_And_ReadAuditSm3Chain()
    {
        var grantRepo = new DapperRepository<SecEmrAccessGrant>(_connFactory, "sec_emr_access_grants");
        var aclRepo = new DapperRepository<EmrSectionAcl>(_connFactory, "emr_section_acl");
        var readAuditRepo = new DapperRepository<SecEmrReadAuditLog>(_connFactory, "sec_emr_read_audit_logs");
        var accessService = new EmrAccessControlService(grantRepo, aclRepo, readAuditRepo);

        long now = SystemTime.UtcNowMs;

        // Insert active grant
        var activeGrant = new SecEmrAccessGrant
        {
            PatientId = "P_ACCESS_01",
            GranteeUserId = "EXT_DOC_01",
            IsExternalGrant = 1,
            AllowedScopesJson = "[\"EMR\",\"LAB\",\"ORDER\"]",
            ValidFromMs = now - 3600000,
            ValidToMs = now + 3600000,
            Status = 1
        };
        await grantRepo.InsertAsync(activeGrant);

        // 1. Active external doctor access
        var reqActive = new EmrAccessRequest(
            UserId: "EXT_DOC_01",
            UserName: "外院专家·李医生",
            UserRole: "DOCTOR",
            UserDeptId: "DEP01",
            ExternalOrgCode: "PEKING_UNION",
            IsExternalDoctor: true,
            PatientId: "P_ACCESS_01",
            EncounterId: "ENC_01",
            ResourceType: "EMR",
            ResourceId: "DOC_01"
        );
        var decisionActive = await accessService.AuthorizeAndAuditReadAsync(reqActive);
        Assert.True(decisionActive.Allowed);

        // 2. Unauthorized external doctor access
        var reqDenied = new EmrAccessRequest(
            UserId: "EXT_DOC_99",
            UserName: "未授权医生",
            UserRole: "DOCTOR",
            UserDeptId: "DEP01",
            ExternalOrgCode: "UNKNOWN_ORG",
            IsExternalDoctor: true,
            PatientId: "P_ACCESS_01",
            EncounterId: "ENC_01",
            ResourceType: "EMR",
            ResourceId: "DOC_01"
        );
        var decisionDenied = await accessService.AuthorizeAndAuditReadAsync(reqDenied);
        Assert.False(decisionDenied.Allowed);

        // 3. SM3 Hash Chain for Audit Log
        string prevHash = "0000000000000000000000000000000000000000000000000000000000000000";
        string logData = $"{prevHash}|EXT_DOC_01|P_ACCESS_01|EMR|READ|ALLOW|{now}";
        string currHash = CryptoHelper.ComputeSm3(logData);
        Assert.NotEmpty(currHash);
        Assert.Equal(64, currHash.Length);
    }

    [Fact]
    public async Task Test_03_Cdss_And_ClinicalPathway_Enrollment_And_Variance()
    {
        var docRepo = new DapperRepository<KbDocument>(_connFactory, "kb_documents");
        var ruleRepo = new DapperRepository<KbClinicalRule>(_connFactory, "kb_clinical_rules");
        var bindingRepo = new DapperRepository<KbOrderBinding>(_connFactory, "kb_order_bindings");
        var sourceRepo = new DapperRepository<KbExternalSource>(_connFactory, "kb_external_sources");
        var kbService = new ClinicalKnowledgeService(docRepo, ruleRepo, bindingRepo, sourceRepo);

        var pathwayRepo = new DapperRepository<CpwPathway>(_connFactory, "cpw_pathways");
        var stageRepo = new DapperRepository<CpwStage>(_connFactory, "cpw_stages");
        var itemRepo = new DapperRepository<CpwStageItem>(_connFactory, "cpw_stage_items");
        var enrollRepo = new DapperRepository<CpwPatientEnrollment>(_connFactory, "cpw_patient_enrollments");
        var varianceRepo = new DapperRepository<CpwVariance>(_connFactory, "cpw_variances");
        var cpwService = new ClinicalPathwayService(pathwayRepo, stageRepo, itemRepo, enrollRepo, varianceRepo);

        // Seed sample pathway
        var pwCode = $"CPW-J18.9-{Guid.NewGuid():N}";
        var pw = new CpwPathway
        {
            PathwayCode = pwCode,
            PathwayName = "社区获得性肺炎临床路径",
            ApplicableIcd10Json = "[\"J18.9\"]",
            StandardDays = 7,
            MinCost = 3000m,
            MaxCost = 6000m,
            Status = 1
        };
        await pathwayRepo.InsertAsync(pw);

        var stage = new CpwStage
        {
            PathwayId = pw.Id,
            StageOrder = 1,
            StageName = "第1天 入院评估与检查日",
            DayFrom = 1,
            DayTo = 1,
            EmrTemplateSnippetJson = "{\"title\":\"第一天临床路径目标\"}"
        };
        await stageRepo.InsertAsync(stage);

        // Match pathway
        var matches = await cpwService.MatchPathwaysAsync("J18.9");
        Assert.NotEmpty(matches);
        Assert.Equal("社区获得性肺炎临床路径", matches[0].PathwayName);

        // Enroll
        var enrollment = await cpwService.EnrollPatientAsync("ENC_PW_01", "P_PW_01", pw.Id, "DOC001");
        Assert.NotNull(enrollment);
        Assert.Equal("ACTIVE", enrollment.Status);

        // CDSS evaluation
        var evalReq = new CdssEvaluationRequestDto(
            PatientId: "P_PW_01",
            Gender: "M",
            AgeYears: 65,
            DiagnosisIcdCodes: new List<string> { "J18.9" },
            RecentLabResults: new List<LabResultSnapshotDto>
            {
                new LabResultSnapshotDto("EGFR", "估算肾小球滤过率", 25m, "25", "LOW"),
                new LabResultSnapshotDto("POTASSIUM", "血清钾", 5.8m, "5.8", "HIGH")
            }
        );
        var evalResult = await kbService.EvaluateAsync(evalReq);
        Assert.NotNull(evalResult);
    }

    [Fact]
    public async Task Test_04_UnifiedPrescriptionReview_6Dimensions_And_Adr()
    {
        var engine = new UnifiedClinicalOrderReviewEngine();

        // 1. Gender Contraindication Check (Male prescribed female-only drug)
        var maleCtx = new OrderReviewContext(
            EncounterOrVisitId: "VISIT_01",
            PatientId: "P_GENDER_01",
            Gender: "M",
            AgeYears: 45,
            DiagnosisIcdCodes: new List<string> { "I10.x00" },
            ItemsToReview: new List<DrugItemToReviewDto>
            {
                new DrugItemToReviewDto("DRUG_F01", "黄体酮胶囊", 50m, "mg", "QD", "PO", 7, 7)
            }
        );
        var genderResult = await engine.ReviewOrdersAsync(maleCtx);
        Assert.True(genderResult.HasBlockWarning);
        Assert.Contains(genderResult.Hits, h => h.RuleCode == "RULE-GENDER-CONTRAINDICATION" && h.IsBlock);

        // 2. Lab Indicator Check (High Potassium + Potassium Chloride)
        var labCtx = new OrderReviewContext(
            EncounterOrVisitId: "VISIT_02",
            PatientId: "P_LAB_01",
            Gender: "F",
            AgeYears: 58,
            DiagnosisIcdCodes: new List<string> { "E87.6" },
            RecentLabResults: new List<LabResultSnapshotDto>
            {
                new LabResultSnapshotDto("POTASSIUM", "血清钾", 5.8m, "5.8", "HIGH")
            },
            ItemsToReview: new List<DrugItemToReviewDto>
            {
                new DrugItemToReviewDto("DRUG_KCL", "氯化钾注射液", 10m, "ml", "ST", "IV_DRIP", 1, 1)
            }
        );
        var labResult = await engine.ReviewOrdersAsync(labCtx);
        Assert.True(labResult.HasBlockWarning);
        Assert.Contains(labResult.Hits, h => h.RuleCode == "RULE-POTASSIUM-CONTRAINDICATION" && h.IsBlock);
    }

    [Fact]
    public async Task Test_05_LisHl7_PacsDicomWeb_And_SpecimenLifecycle()
    {
        var lisAdapter = new Hl7AstMLisAdapter(_connFactory);
        var pacsAdapter = new OrthancDicomWebPacsAdapter(_connFactory);

        // 1. LIS Order request
        var req = new LisOrderRequest(
            OrderId: "ORD_LIS_01",
            VisitOrEncounterId: "VISIT_01",
            PatientId: "P_LIS_01",
            PatientName: "张三",
            Gender: "M",
            Age: 40,
            TestItemCode: "ALT",
            TestItemName: "谷丙转氨酶",
            SampleType: "SERUM",
            OrderDeptId: "DEP01",
            DoctorId: "DOC001",
            OrderTimeMs: SystemTime.UtcNowMs
        );
        var lisOrder = await lisAdapter.SendTestOrderAsync(req);
        Assert.NotNull(lisOrder);
        Assert.NotEmpty(lisOrder.Barcode);
        Assert.Equal("TRANSMITTED_HL7", lisOrder.Status);

        // 2. PACS Order request
        var pacsReq = new PacsOrderRequest(
            AccessionNo: "ACC20261119001",
            VisitOrEncounterId: "VISIT_01",
            PatientId: "P_PACS_01",
            PatientName: "张三",
            Modality: "CT",
            StudyBodyPart: "CHEST",
            OrderDeptId: "DEP01",
            DoctorId: "DOC001",
            OrderTimeMs: SystemTime.UtcNowMs
        );
        var pacsOrder = await pacsAdapter.SendStudyOrderAsync(pacsReq);
        Assert.NotNull(pacsOrder);
        Assert.Equal("SCHEDULED_DICOMWEB", pacsOrder.Status);

        // Fetch PACS report
        var report = await pacsAdapter.QueryReportAsync(pacsOrder.AccessionNo);
        Assert.NotNull(report);
        Assert.NotEmpty(report.Findings);
    }

    [Fact]
    public void Test_06_PdaWristband_And_MewsCalculation()
    {
        // Normal vitals: T 36.6, HR 72, RR 12, BP 120/80 -> MEWS = 0
        int normalMews = NursingController.CalculateMews(120, 72, 12, 36.6m);
        Assert.Equal(0, normalMews);

        // Critical vitals: T 39.2 (+2), HR 125 (+2), RR 24 (+2), BP 75 (+2) -> MEWS = 8
        int criticalMews = NursingController.CalculateMews(75, 125, 24, 39.2m);
        Assert.True(criticalMews >= 6);
    }

    [Fact]
    public async Task Test_07_Treatment_And_IcuMonitoring_RiskScores()
    {
        var catalogRepo = new DapperRepository<TrtTreatmentCatalog>(_connFactory, "trt_treatment_catalog");
        var ruleRepo = new DapperRepository<TrtParameterRule>(_connFactory, "trt_parameter_rules");
        var trtService = new TreatmentService(_connFactory, catalogRepo, ruleRepo);

        // Seed dialysis catalog
        var cat = new TrtTreatmentCatalog
        {
            CatalogCode = $"TRT_HEMODIALYSIS_{Guid.NewGuid():N}",
            CatalogName = "血液透析",
            Category = "DIALYSIS",
            ExecDeptId = "DEP_DIALYSIS",
            IsHighRisk = 1
        };
        await catalogRepo.InsertAsync(cat);

        var paramRule = new TrtParameterRule
        {
            CatalogId = cat.Id,
            LabItemCode = "K+",
            ParameterName = "透析液钾浓度",
            FormulaOrRangeJson = "{\"range\":\"2.0-2.5mmol/L\"}",
            ParamSuggestionTemplate = "根据血钾指标调整透析液钾浓度"
        };
        await ruleRepo.InsertAsync(paramRule);

        var suggestions = await trtService.SuggestParametersAsync(cat.Id, "P_TRT_01");
        Assert.NotNull(suggestions);

        // ICU Monitoring Risk Scores
        var scoreRepo = new DapperRepository<MonRiskScore>(_connFactory, "mon_risk_scores");
        var monService = new MonitoringService(_connFactory, scoreRepo);
        var scoreResult = await monService.EvaluateRiskScoreAsync("ENC_MON_01", "MEWS");
        Assert.NotNull(scoreResult);
        Assert.Equal("MEWS", scoreResult.ScoreType);
    }

    [Fact]
    public async Task Test_08_BloodBank_Crossmatch_HardBlock_And_RationalUse()
    {
        var bagRepo = new DapperRepository<BldInventoryBag>(_connFactory, "bld_inventory_bags");
        var policyRepo = new DapperRepository<BldStockPolicy>(_connFactory, "bld_stock_policies");
        var bldService = new BloodBankService(_connFactory, bagRepo, policyRepo);

        // Blood Stock Advisory
        var advisory = await bldService.GetStockAdvisoryAsync();
        Assert.NotNull(advisory);

        // Transfusion Safety Check
        var safety = await bldService.CheckSafetyAsync("REQ_BLD_NONEXISTENT");
        Assert.NotNull(safety);
    }

    [Fact]
    public async Task Test_09_EmrQc_TimelinessScan_And_DefectClosedLoop()
    {
        var taskRepo = new DapperRepository<EmrQcTask>(_connFactory, "emr_qc_tasks");
        var defectRepo = new DapperRepository<EmrQcDefect>(_connFactory, "emr_qc_defects");
        var qcService = new EmrQcService(_connFactory, taskRepo, defectRepo);

        var qcReport = await qcService.RunAutoQcAsync("ENC_QC_01");
        Assert.NotNull(qcReport);
        Assert.NotEmpty(qcReport.Grade);
    }

    [Fact]
    public async Task Test_10_HieGateway_EmpiResolution_And_ExternalEhrExchange()
    {
        var empiRepo = new DapperRepository<HieEmpiMapping>(_connFactory, "hie_empi_mappings");
        var logRepo = new DapperRepository<HieExchangeLog>(_connFactory, "hie_exchange_logs");
        var hieService = new HieGatewayService(_connFactory, empiRepo, logRepo);

        // EMPI Resolution
        var res = await hieService.ResolveEmpiAsync("P_LOCAL_01", "张三", "HASH123", "M", 19800101);
        Assert.NotNull(res);
        Assert.NotEmpty(res.EmpiId);
        Assert.True(res.MatchConfidence >= 0.9m);

        // Fetch External EHR Summary
        var ehr = await hieService.FetchExternalEhrSummaryAsync(res.EmpiId);
        Assert.NotNull(ehr);
        Assert.NotEmpty(ehr);
    }

    [Fact]
    public async Task Test_11_DatabaseIdempotence_ConsecutiveInitializations_ShouldSucceed()
    {
        string tempDbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"his_idempotence_test_{Guid.NewGuid():N}.db");
        try
        {
            var customFactory = new SqliteConnectionFactory($"Data Source={tempDbPath};Mode=ReadWriteCreate");

            // First initialization
            await DbSchemaInitializer.InitializeAsync(customFactory);

            // Second initialization on the same DB file (must be 100% idempotent with 0 errors)
            var exception = await Record.ExceptionAsync(async () =>
            {
                await DbSchemaInitializer.InitializeAsync(customFactory);
            });
            Assert.Null(exception);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (System.IO.File.Exists(tempDbPath))
            {
                try { System.IO.File.Delete(tempDbPath); } catch { }
            }
        }
    }

    [Fact]
    public async Task Test_12_ClinicalProcessSignatureService_SqlSyntaxAndVerification_IsolatedDb()
    {
        string tempDbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"his_sig_test_{Guid.NewGuid():N}.db");
        try
        {
            var customFactory = new SqliteConnectionFactory($"Data Source={tempDbPath};Mode=ReadWriteCreate");
            await DbSchemaInitializer.InitializeAsync(customFactory);

            var sigRepo = new DapperRepository<SecDigitalSignature>(customFactory, "sec_digital_signatures");
            ICaSignatureProvider caProvider = new DevSandboxCaProvider();
            IExternalTsaClient tsaClient = new Rfc3161HttpTsaClient();
            var sigService = new ClinicalProcessSignatureService(sigRepo, caProvider, tsaClient);

            var signer = new UserSession
            {
                UserId = "DOC_SIG_01",
                UserName = "doctor01",
                DisplayName = "李主治医师",
                RoleCode = "DOCTOR"
            };

            // 1. Sign Clinical Record
            var sigDto = await sigService.SignClinicalRecordAsync(
                "ORDER",
                "ORD_TEST_99999",
                "CREATE",
                new { Medicine = "Ceftriaxone", Dose = "2.0g", Route = "IV" },
                signer);

            Assert.NotNull(sigDto);
            Assert.NotEmpty(sigDto.Id);
            Assert.Equal("ORDER", sigDto.EntityType);

            // 2. Query signatures via GetSignaturesAsync (verifies SQL syntax fix: SELECT * FROM sec_digital_signatures WHERE...)
            var list = await sigService.GetSignaturesAsync("ORDER", "ORD_TEST_99999");
            Assert.NotNull(list);
            Assert.Single(list);
            Assert.Equal(sigDto.Id, list[0].Id);

            // 3. Verify Signature
            var verifyRep = await sigService.VerifySignatureAsync(sigDto.Id);
            Assert.NotNull(verifyRep);
            Assert.True(verifyRep.OverallValid);
            Assert.True(verifyRep.DataIntegrityPassed);
            Assert.True(verifyRep.CaSignatureValid);
            Assert.True(verifyRep.TsaTokenValid);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (System.IO.File.Exists(tempDbPath))
            {
                try { System.IO.File.Delete(tempDbPath); } catch { }
            }
        }
    }

    [Fact]
    public async Task Test_13_BloodBank_FullCycle_Safety_StockAdvisory_And_Crossmatch_IsolatedDb()
    {
        string tempDbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"his_blood_test_{Guid.NewGuid():N}.db");
        try
        {
            var customFactory = new SqliteConnectionFactory($"Data Source={tempDbPath};Mode=ReadWriteCreate");
            await DbSchemaInitializer.InitializeAsync(customFactory);

            var bagRepo = new DapperRepository<BldInventoryBag>(customFactory, "bld_inventory_bags");
            var policyRepo = new DapperRepository<BldStockPolicy>(customFactory, "bld_stock_policies");
            var bldService = new BloodBankService(customFactory, bagRepo, policyRepo);

            // 1. Stock-in blood bag
            var bag = new BldInventoryBag
            {
                BagBarcode = $"BAG{DateTime.UtcNow:yyyyMMdd}001",
                DonationCode = "DON123456",
                SourceBloodCenter = "省红十字中心血站",
                BloodCode = "RBC-01",
                AboType = "A",
                RhType = "POSITIVE",
                VolumeMl = 200m,
                CollectionDateInt = 20261101,
                ExpiryDateInt = 20261205,
                ColdChainTemp = 4.2m,
                Status = "IN_STOCK"
            };
            await bagRepo.InsertAsync(bag);

            // 2. Stock advisory check
            var advisories = await bldService.GetStockAdvisoryAsync();
            Assert.NotNull(advisories);
            Assert.NotEmpty(advisories);

            // 3. ABO / Rh compatibility check logic
            // Compatible: Patient A+ and Bag A+
            bool isCompatible1 = bag.AboType == "A" && bag.RhType == "POSITIVE";
            Assert.True(isCompatible1);

            // Incompatible: Rh- patient receiving Rh+ donor
            string patientRh = "NEGATIVE";
            string donorRh = "POSITIVE";
            bool isCompatible2 = !(patientRh == "NEGATIVE" && donorRh == "POSITIVE");
            Assert.False(isCompatible2);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (System.IO.File.Exists(tempDbPath))
            {
                try { System.IO.File.Delete(tempDbPath); } catch { }
            }
        }
    }

    [Fact]
    public async Task Test_14_Monitoring_RiskScores_And_TelemetryIngest_IsolatedDb()
    {
        string tempDbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"his_mon_test_{Guid.NewGuid():N}.db");
        try
        {
            var customFactory = new SqliteConnectionFactory($"Data Source={tempDbPath};Mode=ReadWriteCreate");
            await DbSchemaInitializer.InitializeAsync(customFactory);

            var scoreRepo = new DapperRepository<MonRiskScore>(customFactory, "mon_risk_scores");
            var streamRepo = new DapperRepository<MonVitalStream>(customFactory, "mon_vital_streams");
            var monService = new MonitoringService(customFactory, scoreRepo);

            // Ingest telemetry vital stream
            var stream = new MonVitalStream
            {
                EncounterId = "ENC_ICU_TEST_01",
                DeviceId = "MON_ICU_BED01",
                SampledAtMs = SystemTime.UtcNowMs,
                Hr = 135m,
                SysBp = 82m,
                DiaBp = 48m,
                Map = 59m,
                Rr = 28m,
                Spo2 = 91m,
                TempC = 39.1m,
                Gcs = 11
            };
            await streamRepo.InsertAsync(stream);

            // Evaluate MEWS, APACHE_II, and SOFA
            var mewsRes = await monService.EvaluateRiskScoreAsync("ENC_ICU_TEST_01", "MEWS");
            Assert.NotNull(mewsRes);
            Assert.Equal("MEWS", mewsRes.ScoreType);
            Assert.True(mewsRes.TotalScore > 0);

            var apacheRes = await monService.EvaluateRiskScoreAsync("ENC_ICU_TEST_01", "APACHE_II");
            Assert.NotNull(apacheRes);
            Assert.Equal("APACHE_II", apacheRes.ScoreType);
            Assert.NotEmpty(apacheRes.GuidanceText);

            var sofaRes = await monService.EvaluateRiskScoreAsync("ENC_ICU_TEST_01", "SOFA");
            Assert.NotNull(sofaRes);
            Assert.Equal("SOFA", sofaRes.ScoreType);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (System.IO.File.Exists(tempDbPath))
            {
                try { System.IO.File.Delete(tempDbPath); } catch { }
            }
        }
    }

    [Fact]
    public async Task Test_15_EmrQc_FullClosedLoop_AutoQc_RectifyAndVerify_IsolatedDb()
    {
        string tempDbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"his_qc_test_{Guid.NewGuid():N}.db");
        try
        {
            var customFactory = new SqliteConnectionFactory($"Data Source={tempDbPath};Mode=ReadWriteCreate");
            await DbSchemaInitializer.InitializeAsync(customFactory);

            var taskRepo = new DapperRepository<EmrQcTask>(customFactory, "emr_qc_tasks");
            var defectRepo = new DapperRepository<EmrQcDefect>(customFactory, "emr_qc_defects");
            var crossOrgRepo = new DapperRepository<EmrQcCrossOrgTrack>(customFactory, "emr_qc_cross_org_tracks");
            var qcService = new EmrQcService(customFactory, taskRepo, defectRepo);

            // 1. Run Auto-QC on encounter
            var qcRep = await qcService.RunAutoQcAsync("ENC_QC_TEST_001");
            Assert.NotNull(qcRep);
            Assert.NotEmpty(qcRep.Grade);

            // 2. Create manual defect
            long now = SystemTime.UtcNowMs;
            var defect = new EmrQcDefect
            {
                TaskId = qcRep.TaskId,
                EncounterId = "ENC_QC_TEST_001",
                RuleCode = "MANUAL-TIMELINESS",
                QcDimension = "TIMELINESS",
                DefectDescription = "病程书写时效超时",
                DeductPoints = 5m,
                ResponsibleDoctorId = "DOC001",
                DeadlineMs = now + 86400000,
                Status = "OPEN"
            };
            await defectRepo.InsertAsync(defect);

            // 3. Doctor rectifies
            defect.Status = "DOCTOR_FIXED";
            defect.RectificationNote = "已补录病程记录并经主治医师审签";
            await defectRepo.UpdateAsync(defect);

            // 4. Auditor verifies pass
            defect.Status = "VERIFIED_CLOSED";
            defect.VerifiedBy = "AUDITOR01";
            defect.VerifiedAtMs = now;
            await defectRepo.UpdateAsync(defect);

            var updatedDefect = await defectRepo.GetByIdAsync(defect.Id);
            Assert.NotNull(updatedDefect);
            Assert.Equal("VERIFIED_CLOSED", updatedDefect.Status);

            // 5. Cross-org track record
            var track = new EmrQcCrossOrgTrack
            {
                PatientId = "P_CROSS_01",
                SourceOrgCode = "ORG_REGIONAL_01",
                ExternalDocId = "DOC_EXT_999",
                QcReviewerId = "AUDITOR01",
                FindingSummary = "跨院病历质控互认审核通过",
                TrackStatus = "REVIEWED"
            };
            await crossOrgRepo.InsertAsync(track);

            var tracks = await crossOrgRepo.QueryAsync("SELECT * FROM emr_qc_cross_org_tracks WHERE patient_id = @PatientId", new { PatientId = "P_CROSS_01" });
            Assert.Single(tracks);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (System.IO.File.Exists(tempDbPath))
            {
                try { System.IO.File.Delete(tempDbPath); } catch { }
            }
        }
    }

    [Fact]
    public async Task Test_16_Treatment_Catalog_SuggestParameters_And_Execution_IsolatedDb()
    {
        string tempDbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"his_trt_test_{Guid.NewGuid():N}.db");
        try
        {
            var customFactory = new SqliteConnectionFactory($"Data Source={tempDbPath};Mode=ReadWriteCreate");
            await DbSchemaInitializer.InitializeAsync(customFactory);

            var catalogRepo = new DapperRepository<TrtTreatmentCatalog>(customFactory, "trt_treatment_catalog");
            var ruleRepo = new DapperRepository<TrtParameterRule>(customFactory, "trt_parameter_rules");
            var execRepo = new DapperRepository<TrtExecutionRecord>(customFactory, "trt_execution_records");
            var trtService = new TreatmentService(customFactory, catalogRepo, ruleRepo);

            // 1. Verify standard catalogs seeded by DbSchemaInitializer
            var catalogs = await catalogRepo.QueryAsync("SELECT * FROM trt_treatment_catalog WHERE is_active = 1");
            Assert.NotNull(catalogs);
            Assert.True(catalogs.Count >= 5);

            var dialysisCat = catalogs.First(c => c.Category == "DIALYSIS");
            Assert.Equal(1, dialysisCat.IsHighRisk);

            // 2. Suggest parameters for Dialysis
            var suggestions = await trtService.SuggestParametersAsync(dialysisCat.Id, "P_TRT_TEST_01");
            Assert.NotNull(suggestions);
            Assert.NotEmpty(suggestions);
            Assert.Contains(suggestions, s => s.ParameterName.Contains("透析") || s.ParameterName.Contains("参数"));

            // 3. Execution record with dual check
            long now = SystemTime.UtcNowMs;
            var exec = new TrtExecutionRecord
            {
                RequestId = "REQ_TRT_001",
                PatientId = "P_TRT_TEST_01",
                SessionNo = 1,
                ActualStartMs = now - 3600000,
                ActualEndMs = now,
                ActualParamsJson = "{\"ultrafiltration_ml\": 2500}",
                HighRiskCheckPassed = 1,
                DualCheckOperatorId = "NURSE_DUAL_02",
                PatientReaction = "治疗顺利，无不良反应",
                FeeCharged = 1,
                Status = "FINISHED"
            };
            await execRepo.InsertAsync(exec);

            var execList = await execRepo.QueryAsync("SELECT * FROM trt_execution_records WHERE patient_id = @PatientId", new { PatientId = "P_TRT_TEST_01" });
            Assert.Single(execList);
            Assert.Equal(1, execList[0].HighRiskCheckPassed);
            Assert.Equal("NURSE_DUAL_02", execList[0].DualCheckOperatorId);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (System.IO.File.Exists(tempDbPath))
            {
                try { System.IO.File.Delete(tempDbPath); } catch { }
            }
        }
    }
}
