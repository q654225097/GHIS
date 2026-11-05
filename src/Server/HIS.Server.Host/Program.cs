using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.Config.Models;
using HIS.Modules.Emr.Models;
using HIS.Modules.Emr.Services;
using HIS.Modules.Finance.Models;
using HIS.Modules.Inpatient.Models;
using HIS.Modules.Inpatient.Services;
using HIS.Modules.Inventory.Models;
using HIS.Modules.MedTech.Models;
using HIS.Modules.MedTech.Services;
using HIS.Modules.Operations.Models;
using HIS.Modules.Operations.Services;
using HIS.Modules.Ordering.Models;
using HIS.Modules.Outpatient.Models;
using HIS.Modules.Patient.Models;
using HIS.Modules.Patient.Services;
using HIS.Modules.Pharmacy.Models;
using HIS.Modules.Platform.Models;
using HIS.Modules.Platform.Services;
using HIS.Server.Host;

var builder = WebApplication.CreateBuilder(args);

// Add Controllers and discover all controllers from referenced module assemblies
builder.Services.AddControllers()
    .AddApplicationPart(typeof(HIS.Modules.Platform.Controllers.PlatformController).Assembly)
    .AddApplicationPart(typeof(HIS.Modules.Config.Controllers.ConfigController).Assembly)
    .AddApplicationPart(typeof(HIS.Modules.Emr.Controllers.EmrController).Assembly)
    .AddApplicationPart(typeof(HIS.Modules.Patient.Controllers.PatientController).Assembly)
    .AddApplicationPart(typeof(HIS.Modules.Outpatient.Controllers.OutpatientController).Assembly)
    .AddApplicationPart(typeof(HIS.Modules.Inpatient.Controllers.InpatientController).Assembly)
    .AddApplicationPart(typeof(HIS.Modules.Ordering.Controllers.OrderingController).Assembly)
    .AddApplicationPart(typeof(HIS.Modules.MedTech.Controllers.MedTechController).Assembly)
    .AddApplicationPart(typeof(HIS.Modules.Pharmacy.Controllers.PharmacyController).Assembly)
    .AddApplicationPart(typeof(HIS.Modules.Finance.Controllers.FinanceController).Assembly)
    .AddApplicationPart(typeof(HIS.Modules.Operations.Controllers.OperationsController).Assembly);

// Database Connection & Pure ANSI Storage
builder.Services.AddSingleton<IDbConnectionFactory>(sp => new SqliteConnectionFactory("Data Source=his_v4.db;Mode=ReadWriteCreate"));

// Generic Repositories for 27+ tables
void RegisterRepo<T>(IServiceCollection services, string tableName) where T : BaseEntity
{
    services.AddScoped<IRepository<T>>(sp => new DapperRepository<T>(sp.GetRequiredService<IDbConnectionFactory>(), tableName));
}

RegisterRepo<SysDepartment>(builder.Services, "sys_departments");
RegisterRepo<SysDeptWardRelation>(builder.Services, "sys_dept_ward_relations");
RegisterRepo<SysMedtechExecRouting>(builder.Services, "sys_medtech_exec_routings");
RegisterRepo<SysUser>(builder.Services, "sys_users");
RegisterRepo<SysUserDeptPost>(builder.Services, "sys_user_dept_posts");
RegisterRepo<SysRole>(builder.Services, "sys_roles");
RegisterRepo<SysPermissionResource>(builder.Services, "sys_permission_resources");
RegisterRepo<SysRolePermission>(builder.Services, "sys_role_permissions");

RegisterRepo<CfgFormSchema>(builder.Services, "cfg_form_schemas");
RegisterRepo<CfgFieldDefinition>(builder.Services, "cfg_field_definitions");
RegisterRepo<CfgEntityFieldValue>(builder.Services, "cfg_entity_field_values");

RegisterRepo<EmrDataElement>(builder.Services, "emr_data_elements");
RegisterRepo<EmrOfdLayoutSchema>(builder.Services, "emr_ofd_layout_schemas");
RegisterRepo<EmrDocTemplate>(builder.Services, "emr_doc_templates");
RegisterRepo<EmrSectionSchema>(builder.Services, "emr_section_schemas");
RegisterRepo<EmrDocument>(builder.Services, "emr_documents");
RegisterRepo<EmrDocumentElement>(builder.Services, "emr_document_elements");
RegisterRepo<EmrOfdArchive>(builder.Services, "emr_ofd_archives");

RegisterRepo<PatPatient>(builder.Services, "pat_patients");

RegisterRepo<OpdScheduleSlot>(builder.Services, "opd_schedule_slots");
RegisterRepo<OpdAppointment>(builder.Services, "opd_appointments");
RegisterRepo<OpdVisit>(builder.Services, "opd_visits");
RegisterRepo<OpdTriageQueue>(builder.Services, "opd_triage_queues");
RegisterRepo<OpdPrescription>(builder.Services, "opd_prescriptions");
RegisterRepo<OpdPrescriptionItem>(builder.Services, "opd_prescription_items");

RegisterRepo<IpdBed>(builder.Services, "ipd_beds");
RegisterRepo<IpdEncounter>(builder.Services, "ipd_encounters");
RegisterRepo<IpdTransferLog>(builder.Services, "ipd_transfer_logs");

RegisterRepo<OrdMedicalOrder>(builder.Services, "ord_medical_orders");
RegisterRepo<OrdExecutionPlan>(builder.Services, "ord_execution_plans");

RegisterRepo<MtcServiceRequest>(builder.Services, "mtc_service_requests");
RegisterRepo<MtcSurgerySchedule>(builder.Services, "mtc_surgery_schedules");
RegisterRepo<MtcBloodRequest>(builder.Services, "mtc_blood_requests");

RegisterRepo<InvDrug>(builder.Services, "inv_drugs");
RegisterRepo<InvDrugBatch>(builder.Services, "inv_drug_batches");
RegisterRepo<InvStockLedger>(builder.Services, "inv_stock_ledgers");
RegisterRepo<PhmDispenseOrder>(builder.Services, "phm_dispense_orders");

RegisterRepo<FinPaymentOrder>(builder.Services, "fin_payment_orders");
RegisterRepo<FinRefundOrder>(builder.Services, "fin_refund_orders");
RegisterRepo<FinChargeLedger>(builder.Services, "fin_charge_ledgers");
RegisterRepo<FinDailyClosing>(builder.Services, "fin_daily_closings");

RegisterRepo<OpsAuditEvent>(builder.Services, "ops_audit_events");
RegisterRepo<OpsIdempotencyKey>(builder.Services, "ops_idempotency_keys");
RegisterRepo<OpsOutboxMessage>(builder.Services, "ops_outbox_messages");

// Domain Services & Contracts
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<IDepartmentReadOnlyContract, PlatformDepartmentContract>();
builder.Services.AddScoped<IPatientReadOnlyContract, PatientReadOnlyContract>();
builder.Services.AddScoped<IMedTechRoutingContract, MedTechRoutingContract>();
builder.Services.AddScoped<EmrQualityControlService>();
builder.Services.AddScoped<EmrOfdRenderService>();
builder.Services.AddScoped<DischargeChecklistService>();
builder.Services.AddScoped<AuditLogService>();

// CORS & Middleware
builder.Services.AddCors(opt => opt.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

app.UseCors();
app.UseRouting();

// Global Request Middleware: Active Dept & Ward headers
app.Use(async (context, next) =>
{
    // Extract headers: X-Active-Dept-Id, X-Active-Ward-Id
    context.Response.Headers["X-HIS-Server"] = "V4.3";
    await next();
});

app.MapControllers();
app.MapGet("/", () => Results.Ok(new {
    System = "HIS V4.3 Hospital Information System",
    Status = "Healthy",
    Runtime = Environment.Version.ToString(),
    Architecture = "Pure Full-Stack MVC",
    Decoupling = "4-Dimensional Department (OPD/IPD/WARD/MEDTECH)",
    GB_Standard = "GB/T 33190-2016 OFD"
}));

// Initialize DB schema & seeds
using (var scope = app.Services.CreateScope())
{
    var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
    await DbSchemaInitializer.InitializeAsync(connFactory);
    await DbSeedData.SeedAsync(connFactory);
}

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }

