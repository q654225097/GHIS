using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HIS.Contracts;
using HIS.Client.UrsaShell.ViewModels;
using HIS.Modules.Outpatient.Models;
using HIS.Modules.Outpatient.Services;
using Xunit;

namespace HIS.UnitTests;

public class PrescriptionReviewEngineAndVitalChartTests
{
    private class MockPatientContract : IPatientReadOnlyContract
    {
        private readonly PatientSummaryDto? _patient;

        public MockPatientContract(PatientSummaryDto? patient)
        {
            _patient = patient;
        }

        public Task<PatientSummaryDto?> GetPatientSummaryAsync(string patientId, CancellationToken ct = default)
        {
            return Task.FromResult(_patient);
        }

        public Task<IReadOnlyList<PatientSummaryDto>> SearchPatientsAsync(string keyword, int limit = 20, CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<PatientSummaryDto>>(_patient != null ? new[] { _patient } : Array.Empty<PatientSummaryDto>());
        }

        public Task<bool> AppendPatientAllergyAsync(string patientId, string allergyTag, CancellationToken ct = default)
        {
            return Task.FromResult(true);
        }
    }

    private static PatientSummaryDto CreatePatient(int age = 35, string[]? allergies = null)
    {
        return new PatientSummaryDto(
            Id: "PAT-TEST-001",
            MedicalRecordNo: "MRN001",
            FullName: "张三",
            GenderCode: "MALE",
            BirthDateInt: DateTime.Today.Year - age * 10000 + 101,
            AgeYears: age,
            PhoneMasked: "138****0001",
            AllergyTags: allergies ?? Array.Empty<string>(),
            IsTempEmergency: false,
            IsActive: true);
    }

    private static DrugCatalogSnapshotDto CreateDrug(
        string drugId,
        string drugName,
        decimal price = 20m,
        int antibioticLevel = 0,
        bool isNarcotic = false,
        bool isSkinTest = false)
    {
        return new DrugCatalogSnapshotDto(
            DrugId: drugId,
            DrugCode: drugId,
            DrugName: drugName,
            Spec: "100mg*30片",
            UnitName: "盒",
            UnitPrice: price,
            AntibioticLevel: antibioticLevel,
            IsNarcotic: isNarcotic,
            IsSkinTestRequired: isSkinTest,
            IsActive: true);
    }

    private static OpdPrescriptionItem CreateItem(
        string drugId,
        string drugName,
        decimal doseOnce = 100m,
        string doseUnit = "mg",
        string freq = "QD",
        int days = 3,
        int quantity = 1,
        int skinTestFlag = 0)
    {
        return new OpdPrescriptionItem
        {
            DrugId = drugId,
            DrugNameSnapshot = drugName,
            DoseOnce = doseOnce,
            DoseUnit = doseUnit,
            FrequencyCode = freq,
            RouteCode = "PO",
            Days = days,
            TotalQuantity = quantity,
            SkinTestFlag = skinTestFlag
        };
    }

    [Fact]
    public async Task Rule1_AllergyConflict_BlocksPrescription()
    {
        var patient = CreatePatient(35, new[] { "青霉素过敏" });
        var engine = new PrescriptionReviewEngine(new MockPatientContract(patient));

        var drug = CreateDrug("DRUG-AMOX", "阿莫西林胶囊");
        var item = CreateItem("DRUG-AMOX", "阿莫西林胶囊");

        var result = await engine.ReviewAsync(patient.Id, 2, false, new[] { item }, new[] { drug });

        Assert.Equal("BLOCK", result.OverallStatus);
        Assert.False(result.CanProceed);
        Assert.Contains(result.Issues, x => x.RuleCode == "RULE_ALLERGY" && x.Severity == "BLOCK");
    }

    [Fact]
    public async Task Rule1_SkinTestPositive_BlocksPrescription()
    {
        var patient = CreatePatient(35);
        var engine = new PrescriptionReviewEngine(new MockPatientContract(patient));

        var drug = CreateDrug("DRUG-PEN", "注射用青霉素钠", isSkinTest: true);
        var item = CreateItem("DRUG-PEN", "注射用青霉素钠", skinTestFlag: 4); // 4 = Positive

        var result = await engine.ReviewAsync(patient.Id, 2, false, new[] { item }, new[] { drug });

        Assert.Equal("BLOCK", result.OverallStatus);
        Assert.Contains(result.Issues, x => x.RuleCode == "RULE_SKIN_TEST_POSITIVE");
    }

    [Fact]
    public async Task Rule1_SkinTestPending_BlocksPrescription()
    {
        var patient = CreatePatient(35);
        var engine = new PrescriptionReviewEngine(new MockPatientContract(patient));

        var drug = CreateDrug("DRUG-PEN", "注射用青霉素钠", isSkinTest: true);
        var item = CreateItem("DRUG-PEN", "注射用青霉素钠", skinTestFlag: 1); // 1 = Pending

        var result = await engine.ReviewAsync(patient.Id, 2, false, new[] { item }, new[] { drug });

        Assert.Equal("BLOCK", result.OverallStatus);
        Assert.Contains(result.Issues, x => x.RuleCode == "RULE_SKIN_TEST_PENDING");
    }

    [Fact]
    public async Task Rule2_AspirinExtremeDose_BlocksPrescription()
    {
        var patient = CreatePatient(45);
        var engine = new PrescriptionReviewEngine(new MockPatientContract(patient));

        var drug = CreateDrug("DRUG-ASP", "阿司匹林肠溶片");
        var item = CreateItem("DRUG-ASP", "阿司匹林肠溶片", doseOnce: 500m); // > 300mg limit

        var result = await engine.ReviewAsync(patient.Id, 2, false, new[] { item }, new[] { drug });

        Assert.Equal("BLOCK", result.OverallStatus);
        Assert.Contains(result.Issues, x => x.RuleCode == "RULE_DOSE_LIMIT_ASPIRIN");
    }

    [Fact]
    public async Task Rule2_AtorvastatinExtremeDose_BlocksPrescription()
    {
        var patient = CreatePatient(50);
        var engine = new PrescriptionReviewEngine(new MockPatientContract(patient));

        var drug = CreateDrug("DRUG-LIPITOR", "阿托伐他汀钙片");
        var item = CreateItem("DRUG-LIPITOR", "阿托伐他汀钙片", doseOnce: 100m); // > 80mg limit

        var result = await engine.ReviewAsync(patient.Id, 2, false, new[] { item }, new[] { drug });

        Assert.Equal("BLOCK", result.OverallStatus);
        Assert.Contains(result.Issues, x => x.RuleCode == "RULE_DOSE_LIMIT_STATIN");
    }

    [Fact]
    public async Task Rule2_AcetaminophenExtremeDose_BlocksPrescription()
    {
        var patient = CreatePatient(28);
        var engine = new PrescriptionReviewEngine(new MockPatientContract(patient));

        var drug = CreateDrug("DRUG-TYL", "对乙酰氨基酚片");
        var item = CreateItem("DRUG-TYL", "对乙酰氨基酚片", doseOnce: 1500m); // > 1000mg limit

        var result = await engine.ReviewAsync(patient.Id, 2, false, new[] { item }, new[] { drug });

        Assert.Equal("BLOCK", result.OverallStatus);
        Assert.Contains(result.Issues, x => x.RuleCode == "RULE_DOSE_LIMIT_ACETAMINOPHEN");
    }

    [Fact]
    public async Task Rule3_WarfarinAndNsaid_Interaction_BlocksPrescription()
    {
        var patient = CreatePatient(60);
        var engine = new PrescriptionReviewEngine(new MockPatientContract(patient));

        var drug1 = CreateDrug("DRUG-WARF", "华法林钠片");
        var drug2 = CreateDrug("DRUG-IBU", "布洛芬缓释胶囊");
        var item1 = CreateItem("DRUG-WARF", "华法林钠片", doseOnce: 2.5m);
        var item2 = CreateItem("DRUG-IBU", "布洛芬缓释胶囊", doseOnce: 300m);

        var result = await engine.ReviewAsync(patient.Id, 2, false, new[] { item1, item2 }, new[] { drug1, drug2 });

        Assert.Equal("BLOCK", result.OverallStatus);
        Assert.Contains(result.Issues, x => x.RuleCode == "RULE_INTERACTION_WARFARIN_NSAID");
    }

    [Fact]
    public async Task Rule3_CeftriaxoneAndCalcium_Interaction_BlocksPrescription()
    {
        var patient = CreatePatient(40);
        var engine = new PrescriptionReviewEngine(new MockPatientContract(patient));

        var drug1 = CreateDrug("DRUG-CEF3", "注射用头孢曲松钠");
        var drug2 = CreateDrug("DRUG-CALC", "葡萄糖酸钙注射液");
        var item1 = CreateItem("DRUG-CEF3", "注射用头孢曲松钠", doseOnce: 1000m);
        var item2 = CreateItem("DRUG-CALC", "葡萄糖酸钙注射液", doseOnce: 10m);

        var result = await engine.ReviewAsync(patient.Id, 2, false, new[] { item1, item2 }, new[] { drug1, drug2 });

        Assert.Equal("BLOCK", result.OverallStatus);
        Assert.Contains(result.Issues, x => x.RuleCode == "RULE_INTERACTION_CEFTRIAXONE_CALCIUM");
    }

    [Fact]
    public async Task Rule3_Pde5AndNitrate_Interaction_BlocksPrescription()
    {
        var patient = CreatePatient(55);
        var engine = new PrescriptionReviewEngine(new MockPatientContract(patient));

        var drug1 = CreateDrug("DRUG-SIL", "枸橼酸西地那非片");
        var drug2 = CreateDrug("DRUG-NIT", "单硝酸异山梨酯缓释片");
        var item1 = CreateItem("DRUG-SIL", "枸橼酸西地那非片", doseOnce: 50m);
        var item2 = CreateItem("DRUG-NIT", "单硝酸异山梨酯缓释片", doseOnce: 20m);

        var result = await engine.ReviewAsync(patient.Id, 2, false, new[] { item1, item2 }, new[] { drug1, drug2 });

        Assert.Equal("BLOCK", result.OverallStatus);
        Assert.Contains(result.Issues, x => x.RuleCode == "RULE_INTERACTION_PDE5_NITRATE");
    }

    [Fact]
    public async Task Rule3_SpironolactoneAndAcei_WarnsPrescription()
    {
        var patient = CreatePatient(62);
        var engine = new PrescriptionReviewEngine(new MockPatientContract(patient));

        var drug1 = CreateDrug("DRUG-SPIRO", "螺内酯片");
        var drug2 = CreateDrug("DRUG-CAPTO", "卡托普利片");
        var item1 = CreateItem("DRUG-SPIRO", "螺内酯片", doseOnce: 20m);
        var item2 = CreateItem("DRUG-CAPTO", "卡托普利片", doseOnce: 25m);

        var result = await engine.ReviewAsync(patient.Id, 2, false, new[] { item1, item2 }, new[] { drug1, drug2 });

        Assert.Equal("WARNING", result.OverallStatus);
        Assert.Contains(result.Issues, x => x.RuleCode == "RULE_INTERACTION_HYPERKALEMIA");
    }

    [Fact]
    public async Task Rule3_ClopidogrelAndOmeprazole_WarnsPrescription()
    {
        var patient = CreatePatient(50);
        var engine = new PrescriptionReviewEngine(new MockPatientContract(patient));

        var drug1 = CreateDrug("DRUG-CLOP", "硫酸氢氯吡格雷片");
        var drug2 = CreateDrug("DRUG-OME", "奥美拉唑肠溶胶囊");
        var item1 = CreateItem("DRUG-CLOP", "硫酸氢氯吡格雷片", doseOnce: 75m);
        var item2 = CreateItem("DRUG-OME", "奥美拉唑肠溶胶囊", doseOnce: 20m);

        var result = await engine.ReviewAsync(patient.Id, 2, false, new[] { item1, item2 }, new[] { drug1, drug2 });

        Assert.Equal("WARNING", result.OverallStatus);
        Assert.Contains(result.Issues, x => x.RuleCode == "RULE_INTERACTION_CLOPIDOGREL_PPI");
    }

    [Fact]
    public async Task Rule4_DualNsaids_DuplicateTherapy_BlocksPrescription()
    {
        var patient = CreatePatient(30);
        var engine = new PrescriptionReviewEngine(new MockPatientContract(patient));

        var drug1 = CreateDrug("DRUG-IBU", "布洛芬缓释胶囊");
        var drug2 = CreateDrug("DRUG-DICLO", "双氯芬酸钠缓释片");
        var item1 = CreateItem("DRUG-IBU", "布洛芬缓释胶囊", doseOnce: 300m);
        var item2 = CreateItem("DRUG-DICLO", "双氯芬酸钠缓释片", doseOnce: 75m);

        var result = await engine.ReviewAsync(patient.Id, 2, false, new[] { item1, item2 }, new[] { drug1, drug2 });

        Assert.Equal("BLOCK", result.OverallStatus);
        Assert.Contains(result.Issues, x => x.RuleCode == "RULE_DUPLICATE_NSAIDS");
    }

    [Fact]
    public async Task Rule4_DualStatins_DuplicateTherapy_BlocksPrescription()
    {
        var patient = CreatePatient(55);
        var engine = new PrescriptionReviewEngine(new MockPatientContract(patient));

        var drug1 = CreateDrug("DRUG-ATORV", "阿托伐他汀钙片");
        var drug2 = CreateDrug("DRUG-ROSUV", "瑞舒伐他汀钙片");
        var item1 = CreateItem("DRUG-ATORV", "阿托伐他汀钙片", doseOnce: 20m);
        var item2 = CreateItem("DRUG-ROSUV", "瑞舒伐他汀钙片", doseOnce: 10m);

        var result = await engine.ReviewAsync(patient.Id, 2, false, new[] { item1, item2 }, new[] { drug1, drug2 });

        Assert.Equal("BLOCK", result.OverallStatus);
        Assert.Contains(result.Issues, x => x.RuleCode == "RULE_DUPLICATE_STATINS");
    }

    [Fact]
    public async Task Rule4_DualPpis_DuplicateTherapy_WarnsPrescription()
    {
        var patient = CreatePatient(45);
        var engine = new PrescriptionReviewEngine(new MockPatientContract(patient));

        var drug1 = CreateDrug("DRUG-OME", "奥美拉唑肠溶胶囊");
        var drug2 = CreateDrug("DRUG-PANTO", "泮托拉唑钠肠溶胶囊");
        var item1 = CreateItem("DRUG-OME", "奥美拉唑肠溶胶囊", doseOnce: 20m);
        var item2 = CreateItem("DRUG-PANTO", "泮托拉唑钠肠溶胶囊", doseOnce: 40m);

        var result = await engine.ReviewAsync(patient.Id, 2, false, new[] { item1, item2 }, new[] { drug1, drug2 });

        Assert.Equal("WARNING", result.OverallStatus);
        Assert.Contains(result.Issues, x => x.RuleCode == "RULE_DUPLICATE_PPIS");
    }

    [Fact]
    public async Task Rule5_AntibioticRestrictedLevel_BlocksUnderqualifiedDoctor()
    {
        var patient = CreatePatient(40);
        var engine = new PrescriptionReviewEngine(new MockPatientContract(patient));

        var drug = CreateDrug("DRUG-MOXI", "盐酸莫西沙星片", antibioticLevel: 1); // Restricted
        var item = CreateItem("DRUG-MOXI", "盐酸莫西沙星片", doseOnce: 400m);

        // Doctor with level 1 (Resident) cannot prescribe Restricted (level 1 requires >= 2)
        var result = await engine.ReviewAsync(patient.Id, 1, false, new[] { item }, new[] { drug });

        Assert.Equal("BLOCK", result.OverallStatus);
        Assert.Contains(result.Issues, x => x.RuleCode == "RULE_ANTIBIOTIC_LEVEL_RESTRICTED");
    }

    [Fact]
    public async Task Rule6_PediatricFluoroquinolone_ContraindicatedUnder18()
    {
        var pediatricPatient = CreatePatient(age: 12);
        var engine = new PrescriptionReviewEngine(new MockPatientContract(pediatricPatient));

        var drug = CreateDrug("DRUG-LEVO", "左氧氟沙星片");
        var item = CreateItem("DRUG-LEVO", "左氧氟沙星片", doseOnce: 200m);

        var result = await engine.ReviewAsync(pediatricPatient.Id, 2, false, new[] { item }, new[] { drug });

        Assert.Equal("BLOCK", result.OverallStatus);
        Assert.Contains(result.Issues, x => x.RuleCode == "RULE_PEDIATRIC_FLUOROQUINOLONE");
    }

    [Fact]
    public async Task Rule6_PediatricTetracycline_ContraindicatedUnder8()
    {
        var youngChild = CreatePatient(age: 6);
        var engine = new PrescriptionReviewEngine(new MockPatientContract(youngChild));

        var drug = CreateDrug("DRUG-DOXY", "多西环素片");
        var item = CreateItem("DRUG-DOXY", "多西环素片", doseOnce: 50m);

        var result = await engine.ReviewAsync(youngChild.Id, 2, false, new[] { item }, new[] { drug });

        Assert.Equal("BLOCK", result.OverallStatus);
        Assert.Contains(result.Issues, x => x.RuleCode == "RULE_PEDIATRIC_TETRACYCLINE");
    }

    [Fact]
    public async Task Rule6_ElderlySedatives_WarnsPrescription()
    {
        var elderlyPatient = CreatePatient(age: 78);
        var engine = new PrescriptionReviewEngine(new MockPatientContract(elderlyPatient));

        var drug = CreateDrug("DRUG-DIAZ", "地西泮片");
        var item = CreateItem("DRUG-DIAZ", "地西泮片", doseOnce: 2.5m);

        var result = await engine.ReviewAsync(elderlyPatient.Id, 2, false, new[] { item }, new[] { drug });

        Assert.Equal("WARNING", result.OverallStatus);
        Assert.Contains(result.Issues, x => x.RuleCode == "RULE_ELDERLY_FALL_RISK");
    }

    [Fact]
    public async Task ValidPrescription_PassesReviewCleanly()
    {
        var patient = CreatePatient(35);
        var engine = new PrescriptionReviewEngine(new MockPatientContract(patient));

        var drug = CreateDrug("DRUG-AMLO", "苯磺酸氨氯地平片");
        var item = CreateItem("DRUG-AMLO", "苯磺酸氨氯地平片", doseOnce: 5m);

        var result = await engine.ReviewAsync(patient.Id, 2, false, new[] { item }, new[] { drug });

        Assert.Equal("PASS", result.OverallStatus);
        Assert.True(result.CanProceed);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void VitalSignModel_CalculatesValuesAndHandlesCoolingRetest()
    {
        var vit = new VitalSignModel
        {
            RecordedAt = "2026-11-06 14:00",
            Temperature = "38.6",
            CoolingTemperature = "37.2",
            Pulse = "98",
            HeartRate = "98",
            Respiration = "20",
            BloodPressure = "130/80",
            Spo2 = "98",
            MeasureType = "AXILLARY",
            NurseName = "李护士",
            IsAbnormal = true,
            StoolCount = "1",
            UrineOutput = "1500",
            TotalIntake = "2200",
            TotalOutput = "1800",
            WeightKg = "65.0",
            MilestoneEvent = "物理降温"
        };

        Assert.Equal(38.6, vit.TemperatureValue);
        Assert.Equal(37.2, vit.CoolingTemperatureValue);
        Assert.Equal(98.0, vit.PulseValue);
        Assert.Equal("腋温", vit.MeasureTypeText);
        Assert.Equal("异常", vit.StatusText);
        Assert.Contains("38.6℃", vit.Summary);
        Assert.Contains("复测 37.2℃", vit.Summary);
    }
}
