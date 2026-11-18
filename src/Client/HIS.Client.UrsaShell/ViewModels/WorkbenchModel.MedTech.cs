using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public partial class WorkbenchModel
{
	public void ConfirmMedTechFee(MedTechOrderItemModel? item)
	{
		if (item != null)
		{
			if (item.IsFeeLocked)
			{
				SystemNotice = "医技申请单 [" + item.RequestNo + "] 已完成【步骤B：扫码确费锁单】，请继续执行【步骤C：审核发布报告】！";
				return;
			}
			item.IsFeeLocked = true;
			item.Status = "已核收确费 (已锁单)";
			item.StatusClass = "Primary";
			TimelineEvents.Insert(0, new TimelineEventModel
			{
				EventTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
				EventType = "MEDTECH_LOCK",
				EventTitle = "医技扫码到检并确费锁单 [" + item.ItemName + "]",
				Department = item.DeptName,
				DoctorOrOperator = CurrentOperatorName,
				Summary = "申请单 " + item.RequestNo + " 已确费锁单，临床直接撤单与退费已锁止，现可审核发布报告。",
				BadgeClass = "Warning",
				ResultIcon = "\ud83d\udd12"
			});
			SystemNotice = $"\ud83d\udd12 申请单 [{item.RequestNo} · {item.ItemName}] 已确费锁单，现可审核发布报告！";
		}
	}

	public void PublishMedTechReport(MedTechOrderItemModel? item)
	{
		if (item != null)
		{
			if (!item.IsFeeLocked)
			{
				SystemNotice = $"⚠\ufe0f [A➔B➔C 顺序门禁拦截] 申请单 [{item.RequestNo} · {item.ItemName}] 尚处于【步骤A：未到检确费】状态，必须先执行【步骤B：扫码到检确费 (锁单)】后方可发布报告！";
				return;
			}
			item.Status = "已出报告";
			item.StatusClass = "Success";
			TimelineEvents.Insert(0, new TimelineEventModel
			{
				EventTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
				EventType = "MEDTECH_REPORT",
				EventTitle = "医技检查检验报告已审核发布 [" + item.ItemName + "]",
				Department = item.DeptName,
				DoctorOrOperator = CurrentOperatorName,
				Summary = "申请单 " + item.RequestNo + " 报告已签发并同步至临床医生工作站与患者全息时间轴。",
				BadgeClass = "Success",
				ResultIcon = "\ud83e\uddea"
			});
			SystemNotice = $"✓ 医技申请单 [{item.RequestNo} · {item.ItemName}] 报告已审核发布，结果已回写临床诊疗工作台！";
		}
	}

	public void TryCancelMedTechOrder(MedTechOrderItemModel? item)
	{
		if (item != null)
		{
			if (item.IsFeeLocked)
			{
				SystemNotice = $"\ud83d\udee1\ufe0f 申请单 [{item.RequestNo} · {item.ItemName}] 已在医技科室扫码确费，不可直接撤单或直接退费，请走冲正流程！";
			}
			else
			{
				item.Status = "已撤单退回";
				item.StatusClass = "Secondary";
				SystemNotice = $"✓ 申请单 [{item.RequestNo} · {item.ItemName}] 尚未确费，已完成临床撤单。";
			}
		}
	}

	public void AcknowledgeCriticalValueByDoctor()
	{
		if (!CanOperateOpdClinical && !CanOperateIpdClinical)
		{
			SystemNotice = "只有经治或值班医师方可执行危急值首接审签。";
			return;
		}
		CriticalValueAlert.DoctorAck = $"✓ 已签收 ({CurrentOperatorName} {DateTime.Now:HH:mm:ss})";
		SystemNotice = $"✓ 医师 [{CurrentOperatorName}] 已完成危急值首接审签，请责任护士复核并启动相应处置！";
	}

	public void AcknowledgeCriticalValueByNurse()
	{
		if (!CanOperateWardNursing)
		{
			SystemNotice = "只有病区责任护士方可执行危急值复核签收。";
			return;
		}
		CriticalValueAlert.NurseAck = $"✓ 已签收 ({CurrentOperatorName} {DateTime.Now:HH:mm:ss})";
		SystemNotice = $"✓ 责任护士 [{CurrentOperatorName}] 已完成危急值复核签收，危急值闭环已归档！";
	}
}
