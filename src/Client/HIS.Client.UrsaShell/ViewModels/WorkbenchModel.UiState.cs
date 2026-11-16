using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public partial class WorkbenchModel
{
    public System.Windows.Input.ICommand? OpenWorkspaceCommand { get; set; }
    public System.Windows.Input.ICommand? CloseWindowCommand { get; set; }

	private sealed class ServerConnectionHandler : DelegatingHandler
	{
		private readonly WorkbenchModel _owner;

		public ServerConnectionHandler(WorkbenchModel owner)
		{
			_owner = owner;
		}

		protected override async Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			try
			{
				HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
				_owner.UpdateServerConnection(true);
				return response;
			}
			catch (HttpRequestException)
			{
				_owner.UpdateServerConnection(false);
				throw;
			}
			catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				_owner.UpdateServerConnection(false);
				throw;
			}
		}
	}

	private static string Present(string? value) =>
		string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

	private string CurrentPatientName => SelectedPatient?.PatientName ?? SelectedInpatient?.PatientName ?? "—";

	private string CurrentPatientGender => SelectedPatient?.Gender ?? SelectedInpatient?.Gender ?? "—";

	private string CurrentPatientAgeText =>
		SelectedPatient is { Age: > 0 } patient
			? $"{patient.Age}岁"
			: SelectedInpatient is { Age: > 0 } inpatient
				? $"{inpatient.Age}岁"
				: "—";

	public bool HasSelectedPatient => SelectedPatient != null;

	public string BorrowBedPatientSummary =>
		$"{CurrentPatientName} · {CurrentPatientGender} · {CurrentPatientAgeText}";

	public string BorrowBedAdmissionSummary =>
		SelectedPatient?.VisitNo is { Length: > 0 } visitNo
			? $"门诊流水 {visitNo}"
			: SelectedInpatient?.AdmissionNo is { Length: > 0 } admissionNo
				? $"住院号 {admissionNo}"
				: "—";

	public string BorrowBedAdmittingDept => Present(SelectedInpatient?.AdmittingDept);

	public string BorrowBedNursingWard => Present(SelectedInpatient?.NursingWard);

	public string BorrowBedBedCode => Present(SelectedInpatient?.BedCode);

	public string BorrowBedAttendingDoctor => Present(SelectedInpatient?.AttendingDoctor);

	public string BorrowBedAccommodationSummary =>
		SelectedInpatient == null
			? "—"
			: $"{Present(SelectedInpatient.NursingWard)} · {Present(SelectedInpatient.BedCode)}";

	public string BorrowBedTargetWard =>
		Present(SessionContext.Current.ActiveDeptName);

	public bool IsInpatientContext => SelectedInpatient != null;

	public string ServerStatusBrushKey =>
		IsServerReachable ? "SemiColorSuccess" : "SemiColorWarning";

	public bool HasFeeLockedMedTechOrder =>
		MedTechOrders.Any(item => item.IsFeeLocked);

	public string FeeLockedMedTechOrderText
	{
		get
		{
			MedTechOrderItemModel? item = MedTechOrders.FirstOrDefault(order => order.IsFeeLocked);
			return item == null
				? string.Empty
				: $"{item.ItemName}（{item.RequestNo}）已完成确费，请按退费流程处理。";
		}
	}

	public string OpdAllergyBlockText
	{
		get
		{
			PrescriptionItemModel? item = Prescriptions.FirstOrDefault(
				prescription => prescription.IsVisible && prescription.IsBlocked);
			return item == null
				? SelectedPatientAllergyText
				: $"{item.DrugName} 存在用药禁忌。{SelectedPatientAllergyText}";
		}
	}

	private void NotifyUiStateChanged()
	{
		foreach (string property in new[]
		{
			nameof(HasSelectedPatient),
			nameof(BorrowBedPatientSummary),
			nameof(BorrowBedAdmissionSummary),
			nameof(BorrowBedAdmittingDept),
			nameof(BorrowBedNursingWard),
			nameof(BorrowBedBedCode),
			nameof(BorrowBedAttendingDoctor),
			nameof(BorrowBedAccommodationSummary),
			nameof(IsInpatientContext),
			nameof(BorrowBedTargetWard),
			nameof(ServerStatusBrushKey),
			nameof(HasFeeLockedMedTechOrder),
			nameof(FeeLockedMedTechOrderText),
			nameof(OpdAllergyBlockText)
		})
		{
			OnPropertyChanged(property);
		}
	}

	private void RebuildNavigationItems()
	{
		int currentTarget = SelectedNavIndex;
		var groups = new List<NavigationGroupModel>
		{
			new("clinical", "临床诊疗"),
			new("medication", "药事"),
			new("finance", "收费结算"),
			new("records", "病历文书"),
			new("system", "系统管理", isExpanded: false)
		};

		void Add(string groupKey, NavigationItemModel item)
		{
			NavigationGroupModel group = groups.First(group => group.Key == groupKey);
			group.Items.Add(item with { Group = group.Title });
		}

		if (CanSeeOpdStation)
			Add("clinical", new NavigationItemModel(0, "门诊医生站", "◈") { Key = "opd" });
		if (CanSeeIpdStation)
			Add("clinical", new NavigationItemModel(1, "住院医生站", "＋") { Key = "ipd" });
		if (CanSeeWardStation)
			Add("clinical", new NavigationItemModel(2, "住院护士站", "♡") { Key = "ward" });
		if (CanSeeMedTech)
			Add("clinical", new NavigationItemModel(3, "医技中心", "⌁") { Key = "medtech" });
		if (CanSeePharmacy)
			Add("medication", new NavigationItemModel(4, "药房药库", "□") { Key = "pharmacy" });
		if (CanSeeFinance)
			Add("finance", new NavigationItemModel(9, "财务收费", "¥") { Key = "finance" });
		if (CanSeeEmrOfd)
			Add("records", new NavigationItemModel(5, "病历归档与验签", "▤") { Key = "emr-archive" });

		if (CanSeeAdminPanel)
		{
			Add("system", new NavigationItemModel(7, "用户账号管理", "☷") { Key = "admin-users" });
			Add("system", new NavigationItemModel(8, "角色与权限", "◇") { Key = "admin-roles" });
			Add("system", new NavigationItemModel(11, "操作审计日志", "▥") { Key = "admin-audit" });
		}
		if (CanSeeDynFormConfig)
			Add("system", new NavigationItemModel(6, "动态表单配置", "⚙") { Key = "admin-forms" });
		if (CanSeeSchedule)
			Add("system", new NavigationItemModel(10, "门诊号源排班", "▦") { Key = "admin-schedule" });

		NavigationGroups.Clear();
		foreach (NavigationGroupModel group in groups.Where(group => group.Items.Count > 0))
		{
			NavigationGroups.Add(group);
		}

		NavigationItems.Clear();
		foreach (NavigationItemModel item in NavigationGroups.SelectMany(group => group.Items))
		{
			NavigationItems.Add(item);
		}

		if (NavigationItems.All(item => item.TargetIndex != currentTarget) && NavigationItems.Count > 0)
		{
			currentTarget = DefaultStationIndex;
			if (NavigationItems.All(item => item.TargetIndex != currentTarget))
			{
				currentTarget = NavigationItems[0].TargetIndex;
			}
			_selectedNavIndex = currentTarget;
			OnPropertyChanged(nameof(SelectedNavIndex));
			OnPropertyChanged(nameof(CurrentStationTitle));
		}
		_selectedNavigationItem = NavigationItems.FirstOrDefault(item => item.TargetIndex == currentTarget)
			?? NavigationItems.FirstOrDefault();
		OnPropertyChanged(nameof(SelectedNavigationItem));
	}
}
