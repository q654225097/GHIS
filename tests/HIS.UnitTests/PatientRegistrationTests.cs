using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Client.UrsaShell.ViewModels;
using HIS.Modules.Patient.Controllers;
using HIS.Modules.Patient.Models;
using Xunit;

namespace HIS.UnitTests;

public class PatientRegistrationTests
{
    [Fact]
    public async Task ClientViewModel_PatientForm_ShowHideAndValidation()
    {
        var vm = new RegistrationCashierViewModel();
        Assert.False(vm.IsPatientFormVisible);
        Assert.True(vm.IsPatientListVisible);

        vm.ShowPatientForm();
        Assert.True(vm.IsPatientFormVisible);
        Assert.False(vm.IsPatientListVisible);

        // Validation: Empty name
        var result = await vm.RegisterNewPatientAsync();
        Assert.Null(result);
        Assert.True(vm.HasPatientFormError);
        Assert.Contains("姓名", vm.PatientFormError);

        // Validation: Missing ID Card
        vm.NewPatientName = "测试患者";
        result = await vm.RegisterNewPatientAsync();
        Assert.Null(result);
        Assert.Contains("身份证", vm.PatientFormError);

        // Validation: Missing Phone
        vm.NewPatientIdCard = "110101199001011234";
        result = await vm.RegisterNewPatientAsync();
        Assert.Null(result);
        Assert.Contains("联系电话", vm.PatientFormError);

        // Validation: Invalid Birth Date
        vm.NewPatientPhone = "13800138000";
        vm.NewPatientBirthDate = "invalid";
        result = await vm.RegisterNewPatientAsync();
        Assert.Null(result);
        Assert.Contains("出生日期", vm.PatientFormError);

        // Hide form
        vm.HidePatientForm();
        Assert.False(vm.IsPatientFormVisible);
        Assert.True(vm.IsPatientListVisible);
        Assert.False(vm.HasPatientFormError);
    }

    [Fact]
    public async Task ServerPatientController_AdultRegistration_And_DuplicateCheck()
    {
        HIS.BuildingBlocks.Security.CryptoHelper.ConfigureKeys(
            "HIS-TEST-ONLY-HMAC-KEY-2026-ABCD1234EFGH",
            "HIS-TEST-ONLY-AES-KEY-2026-ABCD1234EFGH");

        var memConnFactory = new SqliteConnectionFactory(":memory:");
        using (var conn = memConnFactory.CreateConnection())
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE pat_patients (
                    id TEXT PRIMARY KEY,
                    medical_record_no TEXT NOT NULL,
                    full_name TEXT NOT NULL,
                    pinyin_code TEXT,
                    gender_code TEXT NOT NULL,
                    birth_date_int INTEGER NOT NULL,
                    id_card_type TEXT NOT NULL,
                    id_card_cipher TEXT,
                    id_card_hash TEXT NOT NULL,
                    phone_cipher TEXT,
                    phone_masked TEXT,
                    guardian_name TEXT,
                    guardian_id_hash TEXT,
                    allergy_tags_json TEXT,
                    is_temp_emergency INTEGER NOT NULL DEFAULT 0,
                    merged_to_patient_id TEXT,
                    is_active INTEGER NOT NULL DEFAULT 1,
                    ext_data_json TEXT,
                    schema_version INTEGER NOT NULL DEFAULT 1,
                    created_at_ms INTEGER NOT NULL,
                    created_by TEXT,
                    updated_at_ms INTEGER NOT NULL,
                    updated_by TEXT,
                    version_no INTEGER NOT NULL DEFAULT 1
                );
            ";
            cmd.ExecuteNonQuery();
        }

        var repo = new DapperRepository<PatPatient>(memConnFactory, "pat_patients");
        var controller = new PatientController(repo, memConnFactory);

        // 1. Child < 14 without guardian should fail (WF-PAT-03)
        var childYear = SystemTime.ShanghaiNow.Year - 5;
        var childReq = new PatientController.RegisterPatientRequestDto(
            FullName: "小明",
            GenderCode: "1",
            BirthDateInt: childYear * 10000 + 101,
            IdCardNo: "110101202101011234",
            Phone: "13800138001",
            GuardianName: null
        );
        var childResult = await controller.Register(childReq);
        Assert.Equal(400, childResult.Code);
        Assert.Contains("14", childResult.Message);

        // 2. Adult registration should succeed
        var adultReq = new PatientController.RegisterPatientRequestDto(
            FullName: "张三",
            GenderCode: "1",
            BirthDateInt: 19900101,
            IdCardNo: "110101199001011234",
            Phone: "13800138000",
            AllergyTags: new List<string> { "青霉素", "头孢" }
        );
        var adultResult = await controller.Register(adultReq);
        Assert.Equal(200, adultResult.Code);
        Assert.NotNull(adultResult.Data);
        Assert.StartsWith("MRN", adultResult.Data.MedicalRecordNo);
        Assert.Equal("张三", adultResult.Data.FullName);
        Assert.NotNull(adultResult.Data.PhoneMasked);

        // 3. Duplicate registration should be rejected with 409
        var dupResult = await controller.Register(adultReq);
        Assert.Equal(409, dupResult.Code);
        Assert.Contains("已建档", dupResult.Message);

        // 4. Emergency temp patient creation
        var emergencyResult = await controller.CreateEmergencyTempPatient();
        Assert.Equal(200, emergencyResult.Code);
        Assert.NotNull(emergencyResult.Data);
        Assert.StartsWith("TEMP-", emergencyResult.Data.MedicalRecordNo);
        Assert.Equal(1, emergencyResult.Data.IsTempEmergency);
    }
}
