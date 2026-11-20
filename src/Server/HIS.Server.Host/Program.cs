using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.Config.Controllers;
using HIS.Modules.Config.Models;
using HIS.Modules.Config.Services;
using HIS.Modules.Consumables.Controllers;
using HIS.Modules.Consumables.Models;
using HIS.Modules.Consumables.Services;
using HIS.Modules.Emr.Controllers;
using HIS.Modules.Emr.Models;
using HIS.Modules.Emr.Services;
using HIS.Modules.Finance.Controllers;
using HIS.Modules.Finance.Models;
using HIS.Modules.Inpatient.Controllers;
using HIS.Modules.Inpatient.Models;
using HIS.Modules.Inpatient.Services;
using HIS.Modules.Integration.Services;
using HIS.Modules.Inventory.Controllers;
using HIS.Modules.Inventory.Models;
using HIS.Modules.Inventory.Services;
using HIS.Modules.MedTech.Controllers;
using HIS.Modules.MedTech.Models;
using HIS.Modules.MedTech.Services;
using HIS.Modules.Operations.Controllers;
using HIS.Modules.Operations.Models;
using HIS.Modules.Operations.Services;
using HIS.Modules.Ordering.Controllers;
using HIS.Modules.Ordering.Models;
using HIS.Modules.Outpatient.Controllers;
using HIS.Modules.Outpatient.Models;
using HIS.Modules.Patient.Controllers;
using HIS.Modules.Patient.Models;
using HIS.Modules.Patient.Services;
using HIS.Modules.Pharmacy.Controllers;
using HIS.Modules.Pharmacy.Models;
using HIS.Modules.Platform.Controllers;
using HIS.Modules.Platform.Models;
using HIS.Modules.Platform.Services;
using HIS.Modules.KnowledgeBase.Controllers;
using HIS.Modules.KnowledgeBase.Models;
using HIS.Modules.KnowledgeBase.Services;
using HIS.Modules.ClinicalPathway.Controllers;
using HIS.Modules.ClinicalPathway.Models;
using HIS.Modules.ClinicalPathway.Services;
using HIS.Modules.Treatment.Controllers;
using HIS.Modules.Treatment.Models;
using HIS.Modules.Treatment.Services;
using HIS.Modules.Monitoring.Controllers;
using HIS.Modules.Monitoring.Models;
using HIS.Modules.Monitoring.Services;
using HIS.Modules.BloodBank.Controllers;
using HIS.Modules.BloodBank.Models;
using HIS.Modules.BloodBank.Services;
using HIS.Modules.EmrQc.Controllers;
using HIS.Modules.EmrQc.Models;
using HIS.Modules.EmrQc.Services;
using HIS.Modules.HieGateway.Controllers;
using HIS.Modules.HieGateway.Models;
using HIS.Modules.HieGateway.Services;
using HIS.Modules.Workflow.Controllers;
using HIS.Modules.Workflow.Models;
using HIS.Modules.Workflow.Services;
using HIS.Modules.Hr.Controllers;
using HIS.Modules.Hr.Models;
using HIS.Server.Host.Hubs;
using HIS.Server.Host;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

public class Program
{
	private static async Task Main(string[] args)
	{
		WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
		builder.Services.AddControllers(options =>
		{
			options.Filters.Add<PermissionAuthorizationFilter>();
		}).AddApplicationPart(typeof(PlatformController).Assembly).AddApplicationPart(typeof(ConfigController).Assembly)
			.AddApplicationPart(typeof(EmrController).Assembly)
			.AddApplicationPart(typeof(PatientController).Assembly)
			.AddApplicationPart(typeof(OutpatientController).Assembly)
			.AddApplicationPart(typeof(InpatientController).Assembly)
			.AddApplicationPart(typeof(OrderingController).Assembly)
			.AddApplicationPart(typeof(MedTechController).Assembly)
			.AddApplicationPart(typeof(PharmacyController).Assembly)
			.AddApplicationPart(typeof(InventoryController).Assembly)
			.AddApplicationPart(typeof(FinanceController).Assembly)
			.AddApplicationPart(typeof(OperationsController).Assembly)
			.AddApplicationPart(typeof(ConsumablesController).Assembly)
			.AddApplicationPart(typeof(KnowledgeController).Assembly)
			.AddApplicationPart(typeof(ClinicalPathwayController).Assembly)
			.AddApplicationPart(typeof(TreatmentController).Assembly)
			.AddApplicationPart(typeof(MonitoringController).Assembly)
			.AddApplicationPart(typeof(BloodBankController).Assembly)
			.AddApplicationPart(typeof(EmrQcController).Assembly)
			.AddApplicationPart(typeof(HieGatewayController).Assembly)
			.AddApplicationPart(typeof(WorkflowController).Assembly)
			.AddApplicationPart(typeof(HrController).Assembly);
		builder.Services.AddSignalR();
		builder.Services.AddSingleton((Func<IServiceProvider, IDbConnectionFactory>)((IServiceProvider sp) => new SqliteConnectionFactory()));
		RegisterRepo<SysDepartment>(builder.Services, "sys_departments");
		RegisterRepo<SysDeptWardRelation>(builder.Services, "sys_dept_ward_relations");
		RegisterRepo<SysMedtechExecRouting>(builder.Services, "sys_medtech_exec_routings");
		RegisterRepo<SysUser>(builder.Services, "sys_users");
		RegisterRepo<SysUserDeptPost>(builder.Services, "sys_user_dept_posts");
		RegisterRepo<SysRole>(builder.Services, "sys_roles");
		RegisterRepo<SysPermissionResource>(builder.Services, "sys_permission_resources");
		RegisterRepo<SysRolePermission>(builder.Services, "sys_role_permissions");
		RegisterRepo<SysRefreshToken>(builder.Services, "sys_refresh_tokens");
		RegisterRepo<CfgFormSchema>(builder.Services, "cfg_form_schemas");
		RegisterRepo<CfgFieldDefinition>(builder.Services, "cfg_field_definitions");
		RegisterRepo<CfgEntityFieldValue>(builder.Services, "cfg_entity_field_values");
		RegisterRepo<CfgIcd10Diagnosis>(builder.Services, "cfg_icd10_diagnoses");
		RegisterRepo<CfgPriceItem>(builder.Services, "cfg_price_items");
		RegisterRepo<CfgOrderFrequency>(builder.Services, "cfg_order_frequencies");
		RegisterRepo<CfgClinicalDictionary>(builder.Services, "cfg_clinical_dictionaries");
		RegisterRepo<EmrDataElement>(builder.Services, "emr_data_elements");
		RegisterRepo<EmrOfdLayoutSchema>(builder.Services, "emr_ofd_layout_schemas");
		RegisterRepo<EmrDocTemplate>(builder.Services, "emr_doc_templates");
		RegisterRepo<EmrSectionSchema>(builder.Services, "emr_section_schemas");
		RegisterRepo<EmrDocument>(builder.Services, "emr_documents");
		RegisterRepo<EmrDocumentElement>(builder.Services, "emr_document_elements");
		RegisterRepo<EmrOfdArchive>(builder.Services, "emr_ofd_archives");
		RegisterRepo<EmrUserTemplate>(builder.Services, "emr_user_templates");
		RegisterRepo<EmrDigitizedHistory>(builder.Services, "emr_digitized_history");
		RegisterRepo<EmrStoragePolicy>(builder.Services, "emr_storage_policies");
		RegisterRepo<EmrExternalHealthRecord>(builder.Services, "emr_external_health_records");
		RegisterRepo<PatPatient>(builder.Services, "pat_patients");
		RegisterRepo<OpdScheduleSlot>(builder.Services, "opd_schedule_slots");
		RegisterRepo<OpdScheduleTemplate>(builder.Services, "opd_schedule_templates");
		RegisterRepo<OpdAppointment>(builder.Services, "opd_appointments");
		RegisterRepo<OpdVisit>(builder.Services, "opd_visits");
		RegisterRepo<OpdTriageQueue>(builder.Services, "opd_triage_queues");
		RegisterRepo<OpdPrescription>(builder.Services, "opd_prescriptions");
		RegisterRepo<OpdPrescriptionItem>(builder.Services, "opd_prescription_items");
		RegisterRepo<IpdBed>(builder.Services, "ipd_beds");
		RegisterRepo<IpdEncounter>(builder.Services, "ipd_encounters");
		RegisterRepo<IpdTransferLog>(builder.Services, "ipd_transfer_logs");
		RegisterRepo<IpdAdmissionCard>(builder.Services, "ipd_admission_cards");
		RegisterRepo<IpdPrepayment>(builder.Services, "ipd_prepayments");
		RegisterRepo<IpdDischargeRequest>(builder.Services, "ipd_discharge_requests");
		RegisterRepo<OrdMedicalOrder>(builder.Services, "ord_medical_orders");
		RegisterRepo<OrdExecutionPlan>(builder.Services, "ord_execution_plans");
		RegisterRepo<MtcServiceRequest>(builder.Services, "mtc_service_requests");
		RegisterRepo<MtcSurgerySchedule>(builder.Services, "mtc_surgery_schedules");
		RegisterRepo<MtcBloodRequest>(builder.Services, "mtc_blood_requests");
		RegisterRepo<InvDrug>(builder.Services, "inv_drugs");
		RegisterRepo<InvDrugBatch>(builder.Services, "inv_drug_batches");
		RegisterRepo<InvStockLedger>(builder.Services, "inv_stock_ledgers");
		RegisterRepo<InvDrugPriceLog>(builder.Services, "inv_drug_price_logs");
		RegisterRepo<InvTransferOrder>(builder.Services, "inv_transfer_orders");
		RegisterRepo<PhmDispenseOrder>(builder.Services, "phm_dispense_orders");
		RegisterRepo<PhmDispenseOrderLine>(builder.Services, "phm_dispense_order_lines");
		RegisterRepo<FinPaymentOrder>(builder.Services, "fin_payment_orders");
		RegisterRepo<FinRefundOrder>(builder.Services, "fin_refund_orders");
		RegisterRepo<FinChargeLedger>(builder.Services, "fin_charge_ledgers");
		RegisterRepo<FinDailyClosing>(builder.Services, "fin_daily_closings");
		RegisterRepo<FinInpatientFeeItem>(builder.Services, "fin_inpatient_fee_items");
		RegisterRepo<InvDrugTraceCode>(builder.Services, "inv_drug_trace_codes");
		RegisterRepo<InvStockTake>(builder.Services, "inv_stock_takes");
		RegisterRepo<InvStockTakeLine>(builder.Services, "inv_stock_take_lines");
		RegisterRepo<MedtechCriticalValue>(builder.Services, "medtech_critical_values");
		RegisterRepo<OpdInfectiousReportCard>(builder.Services, "opd_infectious_report_cards");
		RegisterRepo<EmrInpatientFrontSheet>(builder.Services, "emr_inpatient_front_sheets");
		RegisterRepo<OpsAuditEvent>(builder.Services, "ops_audit_events");
		RegisterRepo<OpsAuditAnchor>(builder.Services, "ops_audit_anchors");
		RegisterRepo<OpsIdempotencyKey>(builder.Services, "ops_idempotency_keys");
		RegisterRepo<OpsOutboxMessage>(builder.Services, "ops_outbox_messages");
		RegisterRepo<OpsSecurityAlert>(builder.Services, "ops_security_alerts");
		RegisterRepo<OpsKmsKey>(builder.Services, "ops_kms_keys");
		RegisterRepo<CsmConsumable>(builder.Services, "csm_consumables");
		RegisterRepo<CsmStock>(builder.Services, "csm_stocks");
		RegisterRepo<CsmBatch>(builder.Services, "csm_batches");
		RegisterRepo<CsmStockMovement>(builder.Services, "csm_stock_movements");
		RegisterRepo<CsmRequisition>(builder.Services, "csm_requisitions");
		RegisterRepo<CsmRequisitionLine>(builder.Services, "csm_requisition_lines");
		RegisterRepo<CsmSterilePackage>(builder.Services, "csm_sterile_packages");
		RegisterRepo<CsmSterilizationBatch>(builder.Services, "csm_sterilization_batches");
		RegisterRepo<CsmPackageMovement>(builder.Services, "csm_package_movements");
		RegisterRepo<CsmTraceCode>(builder.Services, "csm_trace_codes");

		// P1-C: Surgery and Anesthesia Management
		RegisterRepo<MteSurgerySchedule>(builder.Services, "mte_surgery_schedules");
		RegisterRepo<MteAnesthesiaRecord>(builder.Services, "mte_anesthesia_records");
		RegisterRepo<MteAnesthesiaMedication>(builder.Services, "mte_anesthesia_medications");
		RegisterRepo<MteRecoveryRecord>(builder.Services, "mte_recovery_records");
		RegisterRepo<MtePostopFollowup>(builder.Services, "mte_postop_followups");

		RegisterRepo<NurCarePlan>(builder.Services, "nur_care_plans");
		RegisterRepo<NurCarePlanEvaluation>(builder.Services, "nur_care_plan_evaluations");
		RegisterRepo<NurPressureUlcerAssessment>(builder.Services, "nur_pressure_ulcer_assessments");
		RegisterRepo<NurFallAssessment>(builder.Services, "nur_fall_assessments");
		RegisterRepo<NurQualityIndicator>(builder.Services, "nur_quality_indicators");

		RegisterRepo<IpcInfectionReport>(builder.Services, "ipc_infection_reports");
		RegisterRepo<IpcIsolationOrder>(builder.Services, "ipc_isolation_orders");
		RegisterRepo<IpcOccupationalExposure>(builder.Services, "ipc_occupational_exposures");
		RegisterRepo<IpcAdverseEvent>(builder.Services, "ipc_adverse_events");
		RegisterRepo<IpcHandHygieneAudit>(builder.Services, "ipc_hand_hygiene_audits");

		RegisterRepo<SrvSatisfactionSurvey>(builder.Services, "srv_satisfaction_surveys");
		RegisterRepo<SrvComplaint>(builder.Services, "srv_complaints");
		RegisterRepo<SrvInformedConsent>(builder.Services, "srv_informed_consents");

		RegisterRepo<FinCostCenter>(builder.Services, "fin_cost_centers");

		RegisterRepo<EqpEquipment>(builder.Services, "eqp_equipments");
		RegisterRepo<EqpMaintenanceRecord>(builder.Services, "eqp_maintenance_records");

		// Custom SQL Reports & Parameterized Dynamic Queries
		RegisterRepo<OpsReportDefinition>(builder.Services, "ops_report_definitions");
		RegisterRepo<OpsReportExecution>(builder.Services, "ops_report_executions");

		// EMR Level 7 Security, Audit & Access Grants
		RegisterRepo<SecDigitalSignature>(builder.Services, "sec_digital_signatures");
		RegisterRepo<SecEmrAccessGrant>(builder.Services, "sec_emr_access_grants");
		RegisterRepo<EmrSectionAcl>(builder.Services, "emr_section_acl");
		RegisterRepo<SecEmrReadAuditLog>(builder.Services, "sec_emr_read_audit_logs");

		// EMR Level 7 CDSS & CPW
		RegisterRepo<KbDocument>(builder.Services, "kb_documents");
		RegisterRepo<KbClinicalRule>(builder.Services, "kb_clinical_rules");
		RegisterRepo<KbOrderBinding>(builder.Services, "kb_order_bindings");
		RegisterRepo<KbExternalSource>(builder.Services, "kb_external_sources");
		RegisterRepo<CpwPathway>(builder.Services, "cpw_pathways");
		RegisterRepo<CpwStage>(builder.Services, "cpw_stages");
		RegisterRepo<CpwStageItem>(builder.Services, "cpw_stage_items");
		RegisterRepo<CpwPatientEnrollment>(builder.Services, "cpw_patient_enrollments");
		RegisterRepo<CpwVariance>(builder.Services, "cpw_variances");

		// EMR Level 7 Pharmacy & Rational Drug Review
		RegisterRepo<OrdOrderReviewLog>(builder.Services, "ord_order_review_logs");
		RegisterRepo<PhmPrescriptionComment>(builder.Services, "phm_prescription_comments");
		RegisterRepo<AdrReport>(builder.Services, "adr_reports");
		RegisterRepo<PhmWardPivasBatch>(builder.Services, "phm_ward_pivas_batches");
		RegisterRepo<PhmUnitDoseBarcode>(builder.Services, "phm_unit_dose_barcodes");
		RegisterRepo<PhmMedicationReconciliation>(builder.Services, "phm_medication_reconciliations");
		RegisterRepo<PhmExternalPrescription>(builder.Services, "phm_external_prescriptions");

		// EMR Level 7 Treatment & Monitoring
		RegisterRepo<TrtTreatmentCatalog>(builder.Services, "trt_treatment_catalog");
		RegisterRepo<TrtRequest>(builder.Services, "trt_requests");
		RegisterRepo<TrtAppointment>(builder.Services, "trt_appointments");
		RegisterRepo<TrtParameterRule>(builder.Services, "trt_parameter_rules");
		RegisterRepo<TrtExecutionRecord>(builder.Services, "trt_execution_records");
		RegisterRepo<MonDevice>(builder.Services, "mon_devices");
		RegisterRepo<MonVitalStream>(builder.Services, "mon_vital_streams");
		RegisterRepo<MonAlarm>(builder.Services, "mon_alarms");
		RegisterRepo<MonRiskScore>(builder.Services, "mon_risk_scores");

		// EMR Level 7 Blood Bank
		RegisterRepo<BldBloodDictionary>(builder.Services, "bld_blood_dictionary");
		RegisterRepo<BldInventoryBag>(builder.Services, "bld_inventory_bags");
		RegisterRepo<BldStockPolicy>(builder.Services, "bld_stock_policies");
		RegisterRepo<BldCrossmatchRecord>(builder.Services, "bld_crossmatch_records");
		RegisterRepo<BldTransfusionExecution>(builder.Services, "bld_transfusion_executions");
		RegisterRepo<BldTransfusionReaction>(builder.Services, "bld_transfusion_reactions");
		RegisterRepo<BldExternalSyncLog>(builder.Services, "bld_external_sync_logs");

		// EMR Level 7 Quality Control
		RegisterRepo<EmrQcRule>(builder.Services, "emr_qc_rules");
		RegisterRepo<EmrQcTask>(builder.Services, "emr_qc_tasks");
		RegisterRepo<EmrQcDefect>(builder.Services, "emr_qc_defects");
		RegisterRepo<EmrQcCrossOrgTrack>(builder.Services, "emr_qc_cross_org_tracks");

		// EMR Level 7 HIE
		RegisterRepo<HieEmpiMapping>(builder.Services, "hie_empi_mappings");
		RegisterRepo<HieItemCodeMapping>(builder.Services, "hie_item_code_mappings");
		RegisterRepo<HieExchangeLog>(builder.Services, "hie_exchange_logs");

		// Workflow Engine
		RegisterRepo<WfDefinition>(builder.Services, "wf_definitions");
		RegisterRepo<WfInstance>(builder.Services, "wf_instances");
		RegisterRepo<WfTask>(builder.Services, "wf_tasks");
		RegisterRepo<WfHistoryTrack>(builder.Services, "wf_history_tracks");
		RegisterRepo<WfBusinessBinding>(builder.Services, "wf_business_bindings");
		RegisterRepo<WfNotification>(builder.Services, "wf_notifications");
		RegisterRepo<HrEmployee>(builder.Services, "hr_employees");
		RegisterRepo<HrEmployeeCredential>(builder.Services, "hr_employee_credentials");
		RegisterRepo<HrChangeRecord>(builder.Services, "hr_change_records");
		RegisterRepo<HrContract>(builder.Services, "hr_contracts");

		SecurityKeys securityKeys = SecurityKeyConfiguration.Resolve(builder.Configuration, builder.Environment);
		CryptoHelper.ConfigureKeys(securityKeys.HmacKey, securityKeys.AesMasterKey);
		builder.Services.AddSingleton(new JwtTokenService(securityKeys.JwtSigningKey));
		builder.Services.AddSingleton<LoginAttemptLockoutService>();
		builder.Services.AddSingleton<IEmrPrefetchCache, EmrPrefetchCache>();
		builder.Services.AddHostedService<EmrPrefetchBackgroundService>();
		builder.Services.AddHostedService<EmrQcTimelinessWorker>();
		builder.Services.AddHostedService<NursingCarePlanReminderWorker>();
		builder.Services.AddHostedService<OutboxBackgroundDispatcher>();
		builder.Services.AddScoped((Func<IServiceProvider, IUserSession>)((IServiceProvider sp) =>
		{
			HttpContext? httpContext = sp.GetRequiredService<IHttpContextAccessor>().HttpContext;
			ClaimsPrincipal? principal = httpContext?.User;
			if (principal?.Identity?.IsAuthenticated != true)
			{
				return new UserSession();
			}
			UserSession userSession = UserSession.FromClaims(principal);
			if (!string.IsNullOrWhiteSpace(userSession.RoleId))
			{
				IRepository<SysRolePermission> rolePermissionRepo = sp.GetRequiredService<IRepository<SysRolePermission>>();
				IReadOnlyList<SysRolePermission> permissions = rolePermissionRepo.QueryAsync("SELECT * FROM sys_role_permissions WHERE role_id = @RoleId", new { userSession.RoleId }).GetAwaiter().GetResult();
				foreach (SysRolePermission item in permissions)
				{
					userSession.Permissions[item.ResourceKey] = (ControlAccessPolicy)item.AccessPolicy;
				}
			}
			return userSession;
		}));
		builder.Services.AddHttpContextAccessor();
		builder.Services.AddScoped<IDepartmentReadOnlyContract, PlatformDepartmentContract>();
		builder.Services.AddScoped<IPatientReadOnlyContract, PatientReadOnlyContract>();
		builder.Services.AddScoped<IMedTechRoutingContract, MedTechRoutingContract>();
		builder.Services.AddScoped<MasterDataService>();
		builder.Services.AddScoped((Func<IServiceProvider, IMasterDataReadOnlyContract>)((IServiceProvider sp) => sp.GetRequiredService<MasterDataService>()));
		builder.Services.AddScoped((Func<IServiceProvider, IPriceTableContract>)((IServiceProvider sp) => sp.GetRequiredService<MasterDataService>()));
		builder.Services.AddScoped<PharmacyInventoryService>();
		builder.Services.AddScoped((Func<IServiceProvider, IPharmacyInventoryContract>)((IServiceProvider sp) => sp.GetRequiredService<PharmacyInventoryService>()));
		builder.Services.AddScoped<IEmrMacroExtractorContract, EmrMacroExtractorService>();
		string insuranceProvider = builder.Configuration["HIS_INSURANCE_PROVIDER"] ?? "Mock";
		builder.Services.AddSingleton<IMedicalInsuranceAdapter>(insuranceProvider switch
		{
			"Mock" => new MockMedicalInsuranceAdapter(),
			_ => throw new InvalidOperationException($"未知医保适配器: {insuranceProvider}")
		});

		string lisProvider = builder.Configuration["HIS_LIS_PROVIDER"] ?? "HL7";
		builder.Services.AddSingleton<ILisAdapter>((Func<IServiceProvider, ILisAdapter>)(sp => lisProvider switch
		{
			"HL7" => new Hl7AstMLisAdapter(sp.GetRequiredService<IDbConnectionFactory>()),
			"Mock" => new MockLisAdapter(),
			_ => new MockLisAdapter()
		}));

		string pacsProvider = builder.Configuration["HIS_PACS_PROVIDER"] ?? "Orthanc";
		builder.Services.AddSingleton<IPacsAdapter>((Func<IServiceProvider, IPacsAdapter>)(sp => pacsProvider switch
		{
			"Orthanc" => new OrthancDicomWebPacsAdapter(sp.GetRequiredService<IDbConnectionFactory>()),
			"Mock" => new MockPacsAdapter(),
			_ => new MockPacsAdapter()
		}));
		builder.Services.AddScoped<EmrQualityControlService>();
		builder.Services.AddScoped<EmrOfdRenderService>();
		builder.Services.AddScoped<DischargeChecklistService>();
		builder.Services.AddScoped<ConsumableSupplyService>();
		builder.Services.AddScoped<HIS.Modules.Finance.Services.FinanceSettlementContract>();
		builder.Services.AddScoped<IFinanceSettlementContract>((Func<IServiceProvider, IFinanceSettlementContract>)((IServiceProvider sp) => sp.GetRequiredService<HIS.Modules.Finance.Services.FinanceSettlementContract>()));
		builder.Services.AddScoped<AuditLogService>();
		builder.Services.AddScoped((Func<IServiceProvider, IAuditTrailContract>)((IServiceProvider sp) => sp.GetRequiredService<AuditLogService>()));
		builder.Services.AddScoped<IdempotencyService>();
		builder.Services.AddScoped((Func<IServiceProvider, IIdempotencyContract>)((IServiceProvider sp) => sp.GetRequiredService<IdempotencyService>()));
		builder.Services.AddScoped<OutboxService>();
		builder.Services.AddScoped((Func<IServiceProvider, IOutboxContract>)((IServiceProvider sp) => sp.GetRequiredService<OutboxService>()));
		builder.Services.AddScoped<SecurityIntrusionDetectionService>();
		builder.Services.AddScoped<KmsKeyLifecycleService>();
		builder.Services.AddScoped<HIS.Modules.Outpatient.Services.IPrescriptionReviewEngine, HIS.Modules.Outpatient.Services.PrescriptionReviewEngine>();
		builder.Services.AddScoped<IClinicalOrderReviewEngine, HIS.Modules.Outpatient.Services.UnifiedClinicalOrderReviewEngine>();

		// Level 7 Subsystem Contracts & Protocol Gateways
		string caProvider = builder.Configuration["HIS_CA_PROVIDER"] ?? "DevSandbox";
		builder.Services.AddSingleton<ICaSignatureProvider>(sp => caProvider switch
		{
			"HttpThirdParty" => new HttpThirdPartyCaProvider(sp.GetService<System.Net.Http.IHttpClientFactory>()?.CreateClient()),
			"DevSandbox" => new DevSandboxCaProvider(),
			_ => new DevSandboxCaProvider()
		});

		string tsaProvider = builder.Configuration["HIS_TSA_PROVIDER"] ?? "Rfc3161Http";
		builder.Services.AddSingleton<IExternalTsaClient>(sp => tsaProvider switch
		{
			"Rfc3161Http" => new Rfc3161HttpTsaClient(sp.GetService<System.Net.Http.IHttpClientFactory>()?.CreateClient()),
			"DevSandbox" => new Rfc3161HttpTsaClient(sp.GetService<System.Net.Http.IHttpClientFactory>()?.CreateClient()),
			_ => new Rfc3161HttpTsaClient(sp.GetService<System.Net.Http.IHttpClientFactory>()?.CreateClient())
		});

		string hieProvider = builder.Configuration["HIS_HIE_PROVIDER"] ?? "LocalSimulator";
		builder.Services.AddHttpClient("HieFhirClient");
		builder.Services.AddSingleton<IHieRemoteTransport>(sp => hieProvider switch
		{
			"FhirHttp" => new HttpFhirHieTransport(
				sp.GetRequiredService<System.Net.Http.IHttpClientFactory>(),
				sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>(),
				sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<HttpFhirHieTransport>>()),
			"LocalSimulator" => new LocalFederatedHieTransport(),
			_ => new LocalFederatedHieTransport()
		});

		if (builder.Environment.IsProduction())
		{
			var devAdapters = new List<string>();
			if (string.Equals(caProvider, "DevSandbox", StringComparison.OrdinalIgnoreCase)) devAdapters.Add("CA (DevSandbox)");
			if (string.Equals(tsaProvider, "DevSandbox", StringComparison.OrdinalIgnoreCase)) devAdapters.Add("TSA (DevSandbox)");
			if (string.Equals(lisProvider, "Mock", StringComparison.OrdinalIgnoreCase)) devAdapters.Add("LIS (Mock)");
			if (string.Equals(pacsProvider, "Mock", StringComparison.OrdinalIgnoreCase)) devAdapters.Add("PACS (Mock)");
			if (string.Equals(hieProvider, "LocalSimulator", StringComparison.OrdinalIgnoreCase)) devAdapters.Add("HIE (LocalSimulator)");

			if (devAdapters.Count > 0)
			{
				throw new InvalidOperationException(
					$"[生产环境门禁阻断] 生产环境严禁使用演示/模拟适配器！已阻断组件: {string.Join(", ", devAdapters)}。请在配置或环境变量中指定正式生产对接参数。");
			}
		}

		builder.Services.AddScoped<IClinicalProcessSignatureContract, ClinicalProcessSignatureService>();
		builder.Services.AddScoped<IEmrAccessPolicyContract, EmrAccessControlService>();
		builder.Services.AddScoped<IClinicalKnowledgeContract, ClinicalKnowledgeService>();
		builder.Services.AddScoped<IClinicalPathwayContract, ClinicalPathwayService>();
		builder.Services.AddScoped<ITreatmentContract, TreatmentService>();
		builder.Services.AddScoped<IMonitoringContract, MonitoringService>();
		builder.Services.AddScoped<IBloodBankContract, BloodBankService>();
		builder.Services.AddScoped<IEmrQcContract, EmrQcService>();
		builder.Services.AddScoped<IHieGatewayContract, HieGatewayService>();
		builder.Services.AddScoped<IClinicalAlertPublisher, ClinicalAlertPublisher>();
		builder.Services.AddScoped<WorkflowAssigneeResolver>();
		builder.Services.AddScoped<WorkflowBusinessCallbackDispatcher>();
		builder.Services.AddScoped<IWorkflowBusinessCallback>(sp => sp.GetRequiredService<WorkflowBusinessCallbackDispatcher>());
		builder.Services.AddScoped<WorkflowRuntimeService>();
		builder.Services.AddScoped<IWorkflowEngineContract>(sp => sp.GetRequiredService<WorkflowRuntimeService>());
		builder.Services.AddCors((CorsOptions opt) =>
		{
			opt.AddDefaultPolicy((CorsPolicyBuilder p) =>
			{
				string? allowedOrigins = builder.Configuration["AllowedCorsOrigins"];
				if (!string.IsNullOrWhiteSpace(allowedOrigins) && allowedOrigins != "*")
				{
					string[] origins = allowedOrigins.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
					p.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader().AllowCredentials();
				}
				else
				{
					p.SetIsOriginAllowed(origin =>
					{
						if (string.IsNullOrWhiteSpace(origin)) return false;
						if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
						{
							return uri.Host is "localhost" or "127.0.0.1" || uri.Host.EndsWith(".localhost");
						}
						return false;
					})
					.AllowAnyMethod()
					.AllowAnyHeader()
					.AllowCredentials();
				}
			});
		});
		WebApplication app = builder.Build();
		app.UseCors();
		app.UseRouting();
		app.Use(async (HttpContext context, Func<Task> next) =>
		{
			PathString path = context.Request.Path;
			bool isAnonymous = HttpMethods.IsOptions(context.Request.Method)
				|| path == "/"
				|| path.Equals("/health", StringComparison.OrdinalIgnoreCase)
				|| path.StartsWithSegments("/hubs")
				|| path.Equals("/api/v4/platform/auth/login", StringComparison.OrdinalIgnoreCase)
				|| path.Equals("/api/v4/platform/auth/refresh", StringComparison.OrdinalIgnoreCase)
				|| path.Equals("/api/v4/platform/auth/contexts", StringComparison.OrdinalIgnoreCase);
			if (isAnonymous)
			{
				await next();
				return;
			}

			string authorization = context.Request.Headers.Authorization.ToString();
			if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
				|| string.IsNullOrWhiteSpace(authorization.Substring(7)))
			{
				context.Response.StatusCode = StatusCodes.Status401Unauthorized;
				await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail(401, "请先登录"));
				return;
			}

			JwtTokenService jwtService = context.RequestServices.GetRequiredService<JwtTokenService>();
			ClaimsPrincipal? principal = jwtService.ValidateToken(authorization.Substring(7).Trim());
			if (principal == null)
			{
				context.Response.StatusCode = StatusCodes.Status401Unauthorized;
				await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail(401, "登录令牌无效或已过期"));
				return;
			}

			string userId = principal.FindFirst("sub")?.Value ?? string.Empty;
			string sessionVersionText = principal.FindFirst("ver")?.Value ?? string.Empty;
			IRepository<SysUser> userRepo = context.RequestServices.GetRequiredService<IRepository<SysUser>>();
			SysUser? user = string.IsNullOrWhiteSpace(userId) ? null : await userRepo.GetByIdAsync(userId);
			if (user == null
				|| user.IsActive != 1
				|| !int.TryParse(sessionVersionText, out int tokenVersion)
				|| tokenVersion != user.SessionVersion)
			{
				context.Response.StatusCode = StatusCodes.Status401Unauthorized;
				await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail(401, "登录会话已失效，请重新登录"));
				return;
			}

			context.User = principal;
			await next();
		});
		app.Use(async (HttpContext context, Func<Task> next) =>
		{
			context.Response.Headers["X-HIS-Server"] = "V4.3";
			try
			{
				await next();
			}
			catch (DbConcurrencyException ex)
			{
				context.Response.StatusCode = 409;
				await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail(409, ex.Message));
			}
		});
		app.MapControllers();
		app.MapHub<ClinicalAlertHub>("/hubs/clinical-alerts");
		app.MapGet("/", (Func<IResult>)(() => Results.Ok(new
		{
			System = "HIS V4.3 Hospital Information System",
			Status = "Healthy",
			Runtime = Environment.Version.ToString(),
			Architecture = "Pure Full-Stack MVC",
			Decoupling = "4-Dimensional Department (OPD/IPD/WARD/MEDTECH)",
			GB_Standard = "GB/T 33190-2016 OFD"
		})));
		app.MapGet("/health", (Func<IResult>)(() => Results.Ok(new
		{
			Status = "Healthy",
			Timestamp = DateTime.UtcNow
		}))).AllowAnonymous();
		using (IServiceScope scope = app.Services.CreateScope())
		{
			IDbConnectionFactory connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
			await DbSchemaInitializer.InitializeAsync(connFactory);
			await DbSeedData.SeedAsync(connFactory, builder.Environment.IsDevelopment());
		}
		app.Run();
		static void RegisterRepo<T>(IServiceCollection services, string tableName) where T : notnull, BaseEntity
		{
			services.AddScoped((Func<IServiceProvider, IRepository<T>>)((IServiceProvider sp) => new DapperRepository<T>(sp.GetRequiredService<IDbConnectionFactory>(), tableName)));
		}
	}
}
