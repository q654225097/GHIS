using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Services;

namespace HIS.Client.UrsaShell.ViewModels;

public partial class WorkbenchModel
{
	public int DecomposeStandingOrdersForPatient(InpatientRecordModel patient, DateOnly targetDate, bool announce = true)
	{
		if (!patient.IsActiveInpatient)
		{
			if (announce)
			{
				SystemNotice = "患者【" + patient.PatientName + "】已出院，无法执行医嘱分解。";
			}
			return 0;
		}
		List<InpatientOrderModel> list = patient.Orders.Where((InpatientOrderModel x) => x.OrderType == "临时医嘱" && x.IsReviewedWaitingDecompose).ToList();
		List<InpatientOrderModel> list2 = patient.Orders.Where((InpatientOrderModel x) => x.OrderType == "长期医嘱" && x.IsActive && !x.IsPendingReview).ToList();
		if (list2.Count == 0 && list.Count == 0)
		{
			if (announce)
			{
				SystemNotice = ((patient.PendingOrderCount > 0) ? $"⚠\ufe0f [A➔B➔C 闭环门禁拦截] 患者【{patient.PatientName}】有 {patient.PendingOrderCount} 条医嘱尚处于【步骤A：待护士校对】状态，请先点击「✓ 步骤B：批量校对审签」后再执行【步骤C：分解生成执行单】！" : ("患者【" + patient.PatientName + "】当前无已校对生效的医嘱可供分解。"));
			}
			return 0;
		}
		string dateStr = targetDate.ToString("yyyy-MM-dd");
		int num = 0;
		foreach (InpatientOrderModel tempOrder in list)
		{
			tempOrder.Status = "执行中";
			if (!patient.NursingTasks.Any((NursingTaskModel t) => t.SourceOrder == tempOrder.OrderNo))
			{
				NursingTaskModel nursingTaskModel = CreateNursingTaskFromOrder(tempOrder, patient.NursingTasks.Count);
				nursingTaskModel.ScheduledDate = dateStr;
				nursingTaskModel.TimePoint = DateTime.Now.ToString("HH:mm");
				nursingTaskModel.FrequencySnapshot = "st";
				nursingTaskModel.IsDecomposedTask = true;
				patient.NursingTasks.Insert(0, nursingTaskModel);
				num++;
			}
		}
		foreach (InpatientOrderModel order in list2)
		{
			if (order.IsReviewedWaitingDecompose)
			{
				order.Status = "执行中";
			}
			OrderFrequencyDefinition orderFrequencyDefinition = FindFrequencyDefinition(order.Frequency) ?? FrequencyDefinitions.FirstOrDefault((OrderFrequencyDefinition f) => f.Code == "qd") ?? new OrderFrequencyDefinition
			{
				Code = "qd",
				Name = "每日一次",
				TimePointsText = "08:00"
			};
			IReadOnlyList<string> readOnlyList = orderFrequencyDefinition.ParsedTimePoints;
			if (readOnlyList.Count == 0)
			{
				readOnlyList = new string[1] { "08:00" };
			}
			for (int num2 = 0; num2 < readOnlyList.Count; num2++)
			{
				string tp = readOnlyList[num2];
				string scheduledDateTimeStr = dateStr + " " + tp;
				if (DateTime.TryParse(scheduledDateTimeStr, out var result) && (!DateTime.TryParse(order.StartTime, out var result2) || !(result < result2)) && (!order.IsStopped || !DateTime.TryParse(order.StopTime, out var result3) || !(result > result3)) && !patient.NursingTasks.Any((NursingTaskModel t) => t.SourceOrder == order.OrderNo && ((t.ScheduledDate == dateStr && t.TimePoint == tp) || t.ScheduledTime == scheduledDateTimeStr || (targetDate == DateOnly.FromDateTime(DateTime.Today) && t.ScheduledTime == tp))))
				{
					string priority;
					if (patient.IsCritical || patient.NursingLevel.Contains("特级"))
					{
						priority = "高";
					}
					else
					{
						priority = (patient.NursingLevel.Contains("一级") ? "中" : "常规");
					}
					string category = DetermineTaskCategory(order);
					string value = ((readOnlyList.Count > 1) ? $" · 批次 {num2 + 1}/{readOnlyList.Count}" : "");
					NursingTaskModel item = new NursingTaskModel
					{
						TaskNo = NextInpatientId("NT", patient.NursingTasks.Count),
						ScheduledDate = dateStr,
						TimePoint = tp,
						ScheduledTime = dateStr + " " + tp,
						Category = category,
						Content = $"{order.Content} [{order.Dose}] ({order.Route}){value}",
						SourceOrder = order.OrderNo,
						FrequencySnapshot = orderFrequencyDefinition.Code,
						Priority = priority,
						Status = "待执行",
						IsDecomposedTask = true
					};
					patient.NursingTasks.Insert(0, item);
					num++;
				}
			}
		}
		patient.NotifyWorkflowChanged();
		RefreshInpatientMetrics();
		if (announce)
		{
			string value2;
			if (targetDate == DateOnly.FromDateTime(DateTime.Today))
			{
				value2 = "今日 (" + dateStr + ")";
			}
			else
			{
				value2 = ((targetDate == DateOnly.FromDateTime(DateTime.Today.AddDays(1.0))) ? ("明日 (" + dateStr + ")") : dateStr);
			}
			if (num > 0)
			{
				SystemNotice = $"✓ [步骤C：医嘱分解完成] 已为患者【{patient.PatientName}】将已校对医嘱转为【执行中】并分解生成 {num} 项 {value2} 护理执行单，请切至「2. 护理任务与执行单」执行步骤D（扫码执行）与步骤E（双人核对）！";
			}
			else if (patient.PendingOrderCount > 0)
			{
				SystemNotice = $"⚠\ufe0f [A➔B➔C 闭环提示] 仍有 {patient.PendingOrderCount} 条新开医嘱处于【步骤A：待护士校对】状态，请先完成【步骤B：校对审签】后再分解！";
			}
			else
			{
				SystemNotice = $"患者【{patient.PatientName}】{value2} 的已校对医嘱已全部完成分解，无新增任务。";
			}
		}
		return num;
	}

	public void DecomposeTodayOrders()
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null)
		{
			SystemNotice = "请先选择住院患者。";
		}
		else
		{
			DecomposeStandingOrdersForPatient(selectedInpatient, DateOnly.FromDateTime(DateTime.Today));
		}
	}

	public void DecomposeTomorrowOrders()
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null)
		{
			SystemNotice = "请先选择住院患者。";
		}
		else
		{
			DecomposeStandingOrdersForPatient(selectedInpatient, DateOnly.FromDateTime(DateTime.Today.AddDays(1.0)));
		}
	}

	public void DecomposeAllActiveInpatients()
	{
		List<InpatientRecordModel> list = InpatientRecords.Where((InpatientRecordModel x) => x.IsActiveInpatient).ToList();
		int num = 0;
		DateOnly targetDate = DateOnly.FromDateTime(DateTime.Today);
		foreach (InpatientRecordModel item in list)
		{
			num += DecomposeStandingOrdersForPatient(item, targetDate, announce: false);
		}
		SystemNotice = $"✓ 病区批量医嘱分解完成：为 {list.Count} 位在院患者生成共 {num} 条今日护理执行单！";
	}

	public void OpenFrequencyConfigModal()
	{
		IsFrequencyConfigModalOpen = true;
	}

	public void CloseFrequencyConfigModal()
	{
		SyncFrequencyOptions();
		IsFrequencyConfigModalOpen = false;
		IsAddFrequencyFormOpen = false;
		SystemNotice = "已更新医嘱频次与执行时间点配置，分解引擎已按新配置同步生效。";
	}

	public void ResetFrequencyDefinitionsToDefault()
	{
		InitFrequencyDefinitions();
		SystemNotice = "已恢复三甲医院标准临床医嘱执行频次与分解时间点推荐配置。";
	}

	public void ToggleAddFrequencyPanel()
	{
		IsAddFrequencyFormOpen = !IsAddFrequencyFormOpen;
	}

	public void CancelAddFrequency()
	{
		IsAddFrequencyFormOpen = false;
		NewFreqCode = string.Empty;
		NewFreqName = string.Empty;
		NewFreqDesc = string.Empty;
	}

	public void SaveNewFrequency()
	{
		if (string.IsNullOrWhiteSpace(NewFreqCode) || string.IsNullOrWhiteSpace(NewFreqName))
		{
			SystemNotice = "频次代码和频次名称不能为空。";
			return;
		}
		string code = NewFreqCode.Trim().ToLowerInvariant();
		if (FrequencyDefinitions.Any((OrderFrequencyDefinition f) => string.Equals(f.Code, code, StringComparison.OrdinalIgnoreCase)))
		{
			SystemNotice = "频次代码 [" + code + "] 已存在，不能重复添加。";
			return;
		}
		string timePointsText = (string.IsNullOrWhiteSpace(NewFreqTimePoints) ? "08:00" : NewFreqTimePoints.Trim());
		OrderFrequencyDefinition orderFrequencyDefinition = new OrderFrequencyDefinition
		{
			Code = code,
			Name = NewFreqName.Trim(),
			TimePointsText = timePointsText,
			Description = (string.IsNullOrWhiteSpace(NewFreqDesc) ? "科室自定义频次" : NewFreqDesc.Trim()),
			IsLongTermApplicable = NewFreqIsLongTerm,
			IsCustom = true
		};
		FrequencyDefinitions.Add(orderFrequencyDefinition);
		SyncFrequencyOptions();
		_ = PersistFrequencyToServerAsync(orderFrequencyDefinition);
		CancelAddFrequency();
		SystemNotice = $"已新增临床频次 [{orderFrequencyDefinition.Code}]，分解时间点: {orderFrequencyDefinition.TimePointsBadge}。";
	}

	private static string DetermineTaskCategory(InpatientOrderModel order)
	{
		string text = (order.Content + " " + order.Route).ToLowerInvariant();
		if (text.Contains("监护") || text.Contains("吸氧") || text.Contains("血氧") || text.Contains("心电"))
		{
			return "监护";
		}
		if (text.Contains("护理") || text.Contains("翻身") || text.Contains("口腔") || text.Contains("导尿") || text.Contains("会阴"))
		{
			return "护理";
		}
		if (text.Contains("采血") || text.Contains("血常规") || text.Contains("检验") || text.Contains("化验"))
		{
			return "检验";
		}
		if (text.Contains("雾化") || text.Contains("清创") || text.Contains("换药") || text.Contains("引流"))
		{
			return "处置";
		}
		return "给药";
	}

	public void SelectInpatientByBedCard(BedCardModel? bed)
	{
		if (bed == null)
		{
			return;
		}
		if (!bed.IsOccupied || bed.AdmissionNo == "-")
		{
			SystemNotice = "床位 [" + bed.BedCode + "] 当前为空床待收治状态，可点击上方「新患者入科」或「跨病区借床入科」办理收治。";
			return;
		}
		InpatientRecordModel inpatientRecordModel = InpatientRecords.FirstOrDefault((InpatientRecordModel x) => x.AdmissionNo == bed.AdmissionNo) ?? AllInpatientRecords.FirstOrDefault((InpatientRecordModel x) => x.AdmissionNo == bed.AdmissionNo || x.PatientName == bed.PatientName);
		if (inpatientRecordModel != null)
		{
			SelectedInpatient = inpatientRecordModel;
			SystemNotice = $"已联动选中 [{bed.BedCode}] 在院患者：{inpatientRecordModel.PatientName} ({inpatientRecordModel.Gender}, {inpatientRecordModel.Age}岁) · 住院号: {inpatientRecordModel.AdmissionNo}";
		}
	}

	public void RefreshInpatientMetrics()
	{
		OnPropertyChanged("TotalInpatientCount");
		OnPropertyChanged("CriticalInpatientCount");
		OnPropertyChanged("PendingOrderReviewCount");
		OnPropertyChanged("WaitingDecomposeTotalCount");
		OnPropertyChanged("PendingNursingTaskCount");
		OnPropertyChanged("UnverifiedNursingTaskCount");
		OnPropertyChanged("DischargeReadyCount");
		OnPropertyChanged("CurrentOperatorName");
		OnPropertyChanged("CurrentOperatorRole");
		OnPropertyChanged("CanOperateOpdClinical");
		OnPropertyChanged("CanOperateIpdClinical");
		OnPropertyChanged("CanOperateWardNursing");
		OnPropertyChanged("CanOperatePharmacy");
		OnPropertyChanged("CanOperateFinance");
		OnPropertyChanged("CurrentSodRoleBannerText");
		OnPropertyChanged("CanCreateInpatientOrder");
		OnPropertyChanged("CanReviewSelectedInpatientOrders");
		OnPropertyChanged("CanDecomposeSelectedInpatientOrders");
		OnPropertyChanged("CanSignSelectedProgressNotes");
		OnPropertyChanged("CanExecuteSelectedNursingTasks");
		OnPropertyChanged("CanVerifySelectedNursingTasks");
		OnPropertyChanged("CanApproveSelectedDischarge");
		DischargeCheckDetails.Refresh();
	}

	public void AddInpatientOrder(bool longTerm)
	{
		if (!CanOperateIpdClinical)
		{
			SystemNotice = "\ud83d\udee1\ufe0f [医护职责权限分离拦截 (SoD)] 当前登录角色【" + CurrentOperatorRole + "】无住院医嘱处方权！住院医嘱仅限【住院医师 / 主治医师】开立。";
			return;
		}
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null || !selectedInpatient.IsActiveInpatient)
		{
			SystemNotice = "请先选择一名在院患者，再下达医嘱。";
			return;
		}
		if (string.IsNullOrWhiteSpace(NewOrderContent))
		{
			SystemNotice = "医嘱内容不能为空。";
			return;
		}
		string dose = (string.IsNullOrWhiteSpace(NewOrderDose) ? "1次" : NewOrderDose.Trim());
		string route = (string.IsNullOrWhiteSpace(NewOrderRoute) ? "口服" : NewOrderRoute.Trim());
		string frequency;
		if (string.IsNullOrWhiteSpace(NewOrderFrequency))
		{
			frequency = (longTerm ? "qd (每日一次)" : "st (立即执行)");
		}
		else
		{
			frequency = NewOrderFrequency.Trim();
		}
		InpatientOrderModel inpatientOrderModel = new InpatientOrderModel
		{
			OrderNo = NextInpatientId("ORD", selectedInpatient.Orders.Count),
			OrderType = (longTerm ? "长期医嘱" : "临时医嘱"),
			Content = NewOrderContent.Trim(),
			Dose = dose,
			Route = route,
			Frequency = frequency,
			StartTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
			DoctorName = CurrentOperatorName,
			Status = "待护士校对",
			NurseSign = "--",
			ReviewInfo = "[步骤A] 由医生 " + CurrentOperatorName + " 开立，等待病区责任护士执行【步骤B：校对审签】"
		};
		selectedInpatient.Orders.Insert(0, inpatientOrderModel);
		SyncInpatientBed(selectedInpatient);
		NewOrderContent = string.Empty;
		NotifyInpatientChanged(selectedInpatient);
		SystemNotice = $"✓ [步骤A：医生开立医嘱完成] {inpatientOrderModel.OrderType} [{inpatientOrderModel.Content}] 状态为【待护士校对】。下一步必须执行【步骤B：护士校对审签】➔【步骤C：分解生成执行单】➔【步骤D：扫码执行】➔【步骤E：双人核对】！";

		try
		{
			using HttpClient http = CreateConfigHttpClient();
			var orderTermType = longTerm ? 1 : 2;
			var createDto = new
			{
				EncounterId = selectedInpatient.AdmissionNo,
				PatientId = selectedInpatient.PatientId,
				OrderIpdDeptId = "DEPT-IPD-01",
				PatientWardId = "DEPT-WARD-02",
				ExecTargetDeptId = "DEPT-WARD-02",
				OrderTermType = orderTermType,
				GroupNo = selectedInpatient.Orders.Count + 1,
				OrderCategory = "DRUG",
				ItemCode = "ORD-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
				ItemName = inpatientOrderModel.Content,
				Dosage = 1.0m,
				FrequencyCode = inpatientOrderModel.Frequency,
				RouteCode = inpatientOrderModel.Route,
				OrderDoctorId = CurrentOperatorId
			};
			_ = http.PostAsJsonAsync("/api/v4/ordering/orders", createDto);
		}
		catch { }
	}

	public void ReviewSelectedInpatientOrders()
	{
		if (!CanOperateWardNursing)
		{
			SystemNotice = "\ud83d\udee1\ufe0f [医护职责权限分离拦截 (SoD)] 当前登录角色【" + CurrentOperatorRole + "】不可越权代签护士校对！医嘱校对审签(步骤B)与分解执行(步骤C~E)必须由【\ud83d\udc69\u200d⚕\ufe0f 病区护士】执行。";
			return;
		}
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null)
		{
			SystemNotice = "请先选择住院患者。";
			return;
		}
		List<InpatientOrderModel> list = selectedInpatient.Orders.Where((InpatientOrderModel x) => x.IsPendingReview).ToList();
		if (list.Count == 0)
		{
			SystemNotice = "所选患者当前没有处于【步骤A：待护士校对】的医嘱。";
			return;
		}
		string text = ((SessionContext.Current.IsWardNurse && !string.IsNullOrWhiteSpace(SessionContext.Current.DisplayName)) ? SessionContext.Current.DisplayName : "陈劳");
		foreach (InpatientOrderModel item in list)
		{
			item.Status = ((item.OrderType == "长期医嘱") ? "已校对待分解" : "已校对待执行");
			item.NurseSign = text + " (已校对)";
			item.ReviewInfo = $"[步骤B] 责任护士 {text} 于 {DateTime.Now:HH:mm} 完成校对审签，等待【步骤C：分解生成执行单】";
		}
		NotifyInpatientChanged(selectedInpatient);
		SystemNotice = $"✓ [步骤B：护士校对审签完成] 已核对 {list.Count} 条医嘱并转为【已校对待分解】，已解锁【步骤C：⚡ 自动分解今日医嘱】，请点击分解生成执行单！";

		try
		{
			using HttpClient http = CreateConfigHttpClient();
			foreach (var item in list)
			{
				_ = http.PostAsJsonAsync($"/api/v4/ordering/orders/{item.OrderNo}/verify-and-split", new
				{
					VerifyNurseId = CurrentOperatorId,
					WardId = "DEPT-WARD-02"
				});
			}
		}
		catch { }
	}

	public void ReviewSingleInpatientOrder(InpatientOrderModel order)
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null || !selectedInpatient.Orders.Contains(order) || !order.IsPendingReview)
		{
			SystemNotice = "该医嘱当前无需校对。";
			return;
		}
		string text = ((SessionContext.Current.IsWardNurse && !string.IsNullOrWhiteSpace(SessionContext.Current.DisplayName)) ? SessionContext.Current.DisplayName : "陈劳");
		order.Status = ((order.OrderType == "长期医嘱") ? "已校对待分解" : "已校对待执行");
		order.NurseSign = text + " (已校对)";
		order.ReviewInfo = $"[步骤B] 责任护士 {text} 于 {DateTime.Now:HH:mm} 完成校对审签，等待【步骤C：分解生成执行单】";
		NotifyInpatientChanged(selectedInpatient);
		SystemNotice = $"✓ [步骤B：护士校对完成] 医嘱 [{order.Content}] 已转为【{order.Status}】，请点击上方「⚡ 步骤C：自动分解今日医嘱」生成护理执行单！";
	}

	public void StopInpatientOrder(InpatientOrderModel order)
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null || !selectedInpatient.Orders.Contains(order))
		{
			return;
		}
		if (order.IsPendingReview)
		{
			SystemNotice = "⚠\ufe0f [A➔B 顺序拦截] 未经护士校对的新开医嘱不能执行「停止」，若需撤回请点击「作废」！";
			return;
		}
		if (!order.CanStop)
		{
			SystemNotice = "该医嘱当前不可停止。";
			return;
		}
		order.Status = "已停止";
		order.StopTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
		order.StopDoctor = CurrentOperatorName;
		order.ReviewInfo = $"{CurrentOperatorName} 于 {DateTime.Now:HH:mm} 停止医嘱";
		DateTime now = DateTime.Now;
		int num = 0;
		foreach (NursingTaskModel item in selectedInpatient.NursingTasks.Where((NursingTaskModel t) => t.SourceOrder == order.OrderNo && t.IsPending).ToList())
		{
			if (!DateTime.TryParse(item.ScheduledTime, out var result) || result >= now.AddHours(-12.0))
			{
				item.Status = "已退回";
				item.ExecutedAt = $"已随医嘱停止撤销 ({DateTime.Now:HH:mm})";
				num++;
			}
		}
		NotifyInpatientChanged(selectedInpatient);
		SystemNotice = ((num > 0) ? $"✓ 医嘱 [{order.Content}] 已下达停止，并自动撤销其关联的 {num} 项未执行分解任务。" : ("✓ 医嘱 [" + order.Content + "] 已停止，出院医嘱闭环校验已刷新。"));

		try
		{
			using HttpClient http = CreateConfigHttpClient();
			_ = http.PostAsync($"/api/v4/ordering/orders/{order.OrderNo}/stop", null);
		}
		catch { }
	}

	public void CancelInpatientOrder(InpatientOrderModel order)
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient != null && selectedInpatient.Orders.Contains(order))
		{
			if (!order.CanCancel)
			{
				SystemNotice = "⚠️ [A➔B➔C 闭环拦截] 该医嘱已由护士校对或进入执行阶段，严禁直接作废！请使用「停止」医嘱流程。";
				return;
			}
			order.Status = "已作废";
			order.CancelTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
			order.CancelDoctor = CurrentOperatorName;
			order.ReviewInfo = $"{CurrentOperatorName} 于 {DateTime.Now:HH:mm} 作废未校对医嘱";
			NotifyInpatientChanged(selectedInpatient);
			SystemNotice = "✓ 未校对医嘱 [" + order.Content + "] 已作废。";

			try
			{
				using HttpClient http = CreateConfigHttpClient();
				_ = http.PostAsync($"/api/v4/ordering/orders/{order.OrderNo}/discontinue", null);
			}
			catch { }
		}
	}

	public void AddProgressNote()
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null || !selectedInpatient.IsActiveInpatient)
		{
			SystemNotice = "请先选择一名在院患者。";
			return;
		}
		if (string.IsNullOrWhiteSpace(NewProgressNote))
		{
			SystemNotice = "病程记录内容不能为空。";
			return;
		}
		ProgressNoteModel item = new ProgressNoteModel
		{
			RecordTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
			Author = CurrentOperatorName + " (" + CurrentOperatorRole + ")",
			RecordType = "日常病程记录",
			Content = NewProgressNote.Trim(),
			IsSigned = false
		};
		selectedInpatient.ProgressNotes.Insert(0, item);
		NewProgressNote = string.Empty;
		NotifyInpatientChanged(selectedInpatient);
		SystemNotice = "✓ [步骤A：病程书写完成] 病程已保存为【待主治审签】，请点击「步骤B：主治审签」完成上级医师审签闭环！";
	}

	public void SignProgressNote(ProgressNoteModel note)
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null || !selectedInpatient.ProgressNotes.Contains(note))
		{
			SystemNotice = "未找到对应的病程记录。";
			return;
		}
		if (note.IsSigned)
		{
			SystemNotice = "该病程记录已完成审签。";
			return;
		}
		note.IsSigned = true;
		NotifyInpatientChanged(selectedInpatient);
		SystemNotice = "✓ [步骤B：主治审签完成] 病程记录已由 " + CurrentOperatorName + " 完成电子审签。";
	}

	public void ExecuteNursingTask(NursingTaskModel task)
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null || !selectedInpatient.NursingTasks.Contains(task))
		{
			SystemNotice = "未找到对应的护理任务。";
			return;
		}
		if (!task.CanExecute)
		{
			SystemNotice = "该护理任务已执行或已核对。";
			return;
		}
		task.NurseName = CurrentOperatorName;
		task.ExecutedAt = DateTime.Now.ToString("HH:mm");
		task.Status = "已执行";
		NotifyInpatientChanged(selectedInpatient);
		SystemNotice = "✓ [步骤D：扫码执行完成] 护理任务 [" + task.Content + "] 状态已变更为【已执行】，已解锁【步骤E：双人核对】按钮！";
	}

	public void VerifyNursingTask(NursingTaskModel task)
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null || !selectedInpatient.NursingTasks.Contains(task))
		{
			SystemNotice = "未找到对应的护理任务。";
			return;
		}
		if (task.IsPending)
		{
			SystemNotice = "⚠\ufe0f [D➔E 顺序门禁拦截] 该任务尚处于【步骤D：待执行】状态，严禁跳过扫码执行直接点击核对！";
			return;
		}
		if (!task.CanVerify)
		{
			SystemNotice = "该护理任务已完成核对。";
			return;
		}
		task.Status = "已核对";
		NotifyInpatientChanged(selectedInpatient);
		SystemNotice = "✓ [步骤E：双人核对闭环完成] 护理任务 [" + task.Content + "] 已完成双人核对签字！";
	}

	public void ExecuteAllNursingTasks()
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null)
		{
			SystemNotice = "请先选择住院患者。";
			return;
		}
		List<NursingTaskModel> list = selectedInpatient.NursingTasks.Where((NursingTaskModel x) => x.IsPending).ToList();
		if (list.Count == 0)
		{
			SystemNotice = "所选患者没有处于【步骤D：待执行】的护理任务。";
			return;
		}
		foreach (NursingTaskModel item in list)
		{
			item.NurseName = CurrentOperatorName;
			item.ExecutedAt = DateTime.Now.ToString("HH:mm");
			item.Status = "已执行";
		}
		NotifyInpatientChanged(selectedInpatient);
		SystemNotice = $"✓ [步骤D：批量扫码执行完成] 已执行 {list.Count} 项护理任务，已解锁右侧「✓ 步骤E：批量双人核对」！";
	}

	public void VerifyAllExecutedNursingTasks()
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null)
		{
			SystemNotice = "请先选择住院患者。";
			return;
		}
		List<NursingTaskModel> list = selectedInpatient.NursingTasks.Where((NursingTaskModel x) => x.CanVerify).ToList();
		if (list.Count == 0)
		{
			SystemNotice = "⚠\ufe0f [D➔E 顺序门禁拦截] 当前没有处于【已执行·待核对】的任务！请先完成【步骤D：扫码执行】后再进行双人核对。";
			return;
		}
		foreach (NursingTaskModel item in list)
		{
			item.Status = "已核对";
		}
		NotifyInpatientChanged(selectedInpatient);
		SystemNotice = $"✓ [步骤E：批量双人核对完成] 已核销 {list.Count} 项已执行护理任务，医嘱执行全链路闭环！";
	}

	public void CompleteDischargeGate1_Orders()
	{
		InpatientRecordModel inpatientRecordModel = SelectedInpatient ?? DischargeCheckDetails.Patient;
		if (inpatientRecordModel == null)
		{
			return;
		}
		foreach (InpatientOrderModel item in inpatientRecordModel.Orders.Where((InpatientOrderModel x) => x.IsActive).ToList())
		{
			if (item.IsPendingReview)
			{
				item.Status = "已作废";
				item.CancelTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
				item.CancelDoctor = CurrentOperatorName;
			}
			else
			{
				item.Status = "已停止";
				item.StopTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
				item.StopDoctor = CurrentOperatorName;
			}
		}
		foreach (NursingTaskModel item2 in inpatientRecordModel.NursingTasks.Where((NursingTaskModel x) =>
		{
			string status = x.Status;
			return (status == "待执行" || status == "已执行") ? true : false;
		}).ToList())
		{
			item2.ExecutedAt = DateTime.Now.ToString("HH:mm");
			item2.Status = "已核对";
		}
		NotifyInpatientChanged(inpatientRecordModel);
		SystemNotice = "✓ [出院六严关·步骤A完成] 患者【" + inpatientRecordModel.PatientName + "】所有在院长期/临时医嘱已停止，全部护理任务已双人核销闭环！";
	}

	public void CompleteDischargeGate2_MedTech()
	{
		InpatientRecordModel inpatientRecordModel = SelectedInpatient ?? DischargeCheckDetails.Patient;
		if (inpatientRecordModel != null)
		{
			inpatientRecordModel.MedTechReportsAllSigned = true;
			NotifyInpatientChanged(inpatientRecordModel);
			SystemNotice = "✓ [出院六严关·步骤B完成] 患者【" + inpatientRecordModel.PatientName + "】所有住院检验/检查/影像医技报告已完成审核归档！";
		}
	}

	public void CompleteDischargeGate3_DischargeMeds()
	{
		InpatientRecordModel inpatientRecordModel = SelectedInpatient ?? DischargeCheckDetails.Patient;
		if (inpatientRecordModel != null)
		{
			if (!CanOperatePharmacy)
			{
				SystemNotice = "\ud83d\udee1\ufe0f [临床与药房职责权限分离拦截 (SoD)] 当前角色【" + CurrentOperatorRole + "】无权核发出院带药！【步骤C：出院带药清零】属于【\ud83d\udc8a 药剂科药师】专属职责，请点击右侧「➔ 交接药师核发带药」或切至【药房与药库中心】办理！";
			}
			else
			{
				DispenseInpatientDischargeMedsForPatient(inpatientRecordModel);
			}
		}
	}

	public void CompleteDischargeGate4_SignNotes()
	{
		InpatientRecordModel inpatientRecordModel = SelectedInpatient ?? DischargeCheckDetails.Patient;
		if (inpatientRecordModel == null)
		{
			return;
		}
		if (!CanFinalizeInpatientOrders)
		{
			SystemNotice = "\ud83d\udee1\ufe0f [职责权限分离拦截 (SoD)] 当前角色【" + CurrentOperatorRole + "】无主治医师病程终审权限！";
			return;
		}
		foreach (ProgressNoteModel item in inpatientRecordModel.ProgressNotes.Where((ProgressNoteModel x) => !x.IsSigned))
		{
			item.IsSigned = true;
		}
		NotifyInpatientChanged(inpatientRecordModel);
		SystemNotice = "✓ [出院六严关·步骤D完成] 患者【" + inpatientRecordModel.PatientName + "】所有住院病程与出院记录已由主治医师审签锁定！";
	}

	public void CompleteDischargeGate5_Insurance()
	{
		InpatientRecordModel inpatientRecordModel = SelectedInpatient ?? DischargeCheckDetails.Patient;
		if (inpatientRecordModel != null)
		{
			if (!CanOperateFinance)
			{
				SystemNotice = "\ud83d\udee1\ufe0f [临床与财务职责权限分离拦截 (SoD)] 当前角色【" + CurrentOperatorRole + "】严禁操作医保财务结算！【步骤E：DRG/DIP医保结算核验】属于独立模块【\ud83d\udcb0 财务收费与结算中心】专属职责，请点击「➔ 交接财务收费员结算」！";
			}
			else
			{
				SettleInpatientInsuranceForPatient(inpatientRecordModel);
			}
		}
	}

	public void CompleteDischargeGate6_Prepayment()
	{
		InpatientRecordModel inpatientRecordModel = SelectedInpatient ?? DischargeCheckDetails.Patient;
		if (inpatientRecordModel != null)
		{
			if (!CanOperateFinance)
			{
				SystemNotice = "\ud83d\udee1\ufe0f [临床与财务职责权限分离拦截 (SoD)] 当前角色【" + CurrentOperatorRole + "】严禁操作住院预交金结清！【步骤F：预交金多退少补结清】属于独立模块【\ud83d\udcb0 财务收费与结算中心】专属职责，请点击「➔ 交接财务收费员结算」！";
			}
			else
			{
				ClearInpatientPrepaymentForPatient(inpatientRecordModel);
			}
		}
	}

	public void SaveVitalSigns()
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null || !selectedInpatient.IsActiveInpatient)
		{
			SystemNotice = "请先选择一名在院患者。";
			return;
		}
		if (!TryParseVital(TemperatureText, out var result) || !TryParseVital(PulseText, out var result2) || !TryParseVital(RespirationText, out var result3) || !TryParseVital(SystolicText, out var result4) || !TryParseVital(DiastolicText, out var result5) || !TryParseVital(Spo2Text, out var result6))
		{
			SystemNotice = "生命体征必须填写为有效数字。";
			return;
		}
		if (result4 < result5 || result5 <= 0m || result6 > 100m)
		{
			SystemNotice = "血压或血氧数值不符合有效范围。";
			return;
		}
		bool flag = result >= 37.3m || result2 > 100m || result3 > 22m || result4 >= 140m || result4 < 90m || result5 >= 90m || result6 < 95m;
		VitalSignModel item = new VitalSignModel
		{
			RecordedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
			Temperature = FormatVital(result),
			Pulse = FormatVital(result2),
			Respiration = FormatVital(result3),
			BloodPressure = FormatVital(result4) + "/" + FormatVital(result5),
			Spo2 = FormatVital(result6),
			NurseName = CurrentOperatorName,
			IsAbnormal = flag
		};
		selectedInpatient.VitalSigns.Insert(0, item);
		NotifyInpatientChanged(selectedInpatient);
		SystemNotice = (flag ? "⚠\ufe0f 生命体征已保存，检测到异常指标，请立即通知经治医师并复核。" : "✓ 生命体征已保存，指标均在正常范围。");
	}

	public void AddHandoverNote()
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null || !selectedInpatient.IsActiveInpatient)
		{
			SystemNotice = "请先选择一名在院患者。";
			return;
		}
		if (string.IsNullOrWhiteSpace(HandoverDraft))
		{
			SystemNotice = "交接班内容不能为空。";
			return;
		}
		HandoverNoteModel item = new HandoverNoteModel
		{
			Shift = SelectedHandoverShift + " (交接)",
			NurseName = CurrentOperatorName + " (" + CurrentOperatorRole + ")",
			RecordTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
			Content = HandoverDraft.Trim(),
			IsConfirmed = false
		};
		selectedInpatient.HandoverNotes.Insert(0, item);
		HandoverDraft = string.Empty;
		NotifyInpatientChanged(selectedInpatient);
		SystemNotice = "✓ [步骤A：交班提交完成] 交接班记录已保存，请由接班护士点击「步骤B：接班确认」完成交接闭环！";
	}

	public void ConfirmHandoverNote(HandoverNoteModel note)
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null || !selectedInpatient.HandoverNotes.Contains(note))
		{
			SystemNotice = "未找到对应的交接班记录。";
			return;
		}
		if (note.IsConfirmed)
		{
			SystemNotice = "该交接班记录已确认。";
			return;
		}
		note.IsConfirmed = true;
		NotifyInpatientChanged(selectedInpatient);
		SystemNotice = "✓ [步骤B：接班确认完成] 交接班记录已双人确认，护理责任已闭环移交。";
	}

	public void CycleNursingLevel()
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null || !selectedInpatient.IsActiveInpatient)
		{
			SystemNotice = "请先选择一名在院患者。";
			return;
		}
		if (selectedInpatient.NursingLevel.Contains("特级") || selectedInpatient.IsCritical)
		{
			SystemNotice = "病危/特级护理患者不允许直接下调护理级别，请先完成病情评估与医嘱变更。";
			return;
		}
		InpatientRecordModel inpatientRecordModel = selectedInpatient;
		string nursingLevel = selectedInpatient.NursingLevel;
		string nursingLevel2 = nursingLevel switch
		{
			"一级护理" => "二级护理", 
			"二级护理" => "三级护理", 
			"三级护理" => "一级护理", 
			_ => "二级护理", 
		};
		inpatientRecordModel.NursingLevel = nursingLevel2;
		SyncInpatientBed(selectedInpatient);
		NotifyInpatientChanged(selectedInpatient);
		SystemNotice = "✓ 护理级别已调整为 [" + selectedInpatient.NursingLevel + "]，病区床位卡已同步。";
	}

	public void PrepareDischargeCheck()
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null)
		{
			SystemNotice = "请先选择需要办理出院的患者。";
			return;
		}
		DischargeCheckDetails.Patient = selectedInpatient;
		DischargeCheckDetails.Refresh();
		SystemNotice = (DischargeCheckDetails.AllPassed ? ("✓ " + selectedInpatient.PatientName + " 六严关已全部通过，可点击「步骤G：终审办结出院」办结。") : $"⚠\ufe0f {selectedInpatient.PatientName} 尚有关卡未通过（{selectedInpatient.DischargeGateSummary}），请按 A➔B➔C➔D➔E➔F 顺序逐关推进。");
	}

	public void PrepareCriticalValueAlert()
	{
		InpatientRecordModel selectedInpatient = SelectedInpatient;
		if (selectedInpatient == null)
		{
			SystemNotice = "请先选择住院患者。";
			return;
		}
		CriticalValueAlert.PatientName = selectedInpatient.PatientName;
		CriticalValueAlert.BedCode = selectedInpatient.BedCode;
		SystemNotice = "已调取 " + selectedInpatient.PatientName + " 的危急值双签收工作台（遵循 A.经治医生首接审签 ➔ B.责任护士复核签收 ➔ C.启动急救绿通 顺序闭环）。";
	}

	public void AdmitInpatient()
	{
		BedCardModel bedCardModel = BedCards.FirstOrDefault((BedCardModel x) => !x.IsOccupied) ?? AllBedCards.FirstOrDefault((BedCardModel x) => !x.IsOccupied) ?? BedCards.FirstOrDefault((BedCardModel x) => x.BedCode.StartsWith("03", StringComparison.Ordinal));
		string admissionNo = $"ADM{DateTime.Now:yyyyMMddHHmmss}";
		InpatientRecordModel inpatientRecordModel = new InpatientRecordModel
		{
			AdmissionNo = admissionNo,
			PatientName = $"新入院患者{AllInpatientRecords.Count + 1:D2}",
			Gender = "男",
			Age = 52,
			MedicalRecordNo = $"PAT{DateTime.Now:yyyyMMddHHmmss}",
			BedCode = (bedCardModel?.BedCode.Replace(" ", string.Empty) ?? "加床"),
			AdmittingDept = (string.IsNullOrWhiteSpace(ActiveDeptName) ? "心血管内科住院一科" : ActiveDeptName),
			NursingWard = (bedCardModel?.NursingWardName ?? "内科一病区"),
			NursingLevel = "二级护理",
			Diagnosis = "待完善入院诊断",
			AttendingDoctor = CurrentOperatorName + " (" + CurrentOperatorRole + ")",
			AdmissionDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
			DailySummary = "新入院患者，已完成入科登记与身份核对，待完成首次病程与查房评估。",
			MedTechReportsAllSigned = false,
			DischargeMedsDispensed = false,
			MedicalInsuranceSettled = false,
			PrepaymentCleared = false,
			PrepaymentAmount = 5000m,
			ActualSelfPay = 0m
		};
		inpatientRecordModel.Orders.Add(new InpatientOrderModel
		{
			OrderNo = NextInpatientId("ORD", 0),
			OrderType = "长期医嘱",
			Content = "持续心电监护",
			Dose = "1套",
			Route = "床旁",
			Frequency = "持续",
			StartTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
			DoctorName = CurrentOperatorName,
			Status = "待护士校对",
			NurseSign = "--",
			ReviewInfo = "[步骤A] 入院常规医嘱，等待责任护士执行【步骤B：校对审签】"
		});
		inpatientRecordModel.ApplyOrderFilter();
		inpatientRecordModel.ProgressNotes.Add(new ProgressNoteModel
		{
			RecordTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
			Author = CurrentOperatorName + " (" + CurrentOperatorRole + ")",
			RecordType = "入院记录",
			Content = "患者新入院，已核对身份、腕带与床位信息，待完善既往史、过敏史与系统查体。",
			IsSigned = false
		});
		inpatientRecordModel.VitalSigns.Add(new VitalSignModel
		{
			RecordedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
			Temperature = "36.5",
			Pulse = "76",
			Respiration = "18",
			BloodPressure = "126/78",
			Spo2 = "98",
			NurseName = CurrentOperatorName,
			IsAbnormal = false
		});
		if (bedCardModel != null)
		{
			bedCardModel.PatientName = inpatientRecordModel.PatientName;
			bedCardModel.Gender = inpatientRecordModel.Gender;
			bedCardModel.Age = inpatientRecordModel.Age;
			bedCardModel.AdmissionNo = inpatientRecordModel.AdmissionNo;
			bedCardModel.AdmittingDeptName = inpatientRecordModel.AdmittingDept;
			bedCardModel.NursingWardName = inpatientRecordModel.NursingWard;
			bedCardModel.NursingLevel = inpatientRecordModel.NursingLevel;
			bedCardModel.Diagnosis = inpatientRecordModel.Diagnosis;
			bedCardModel.IsOccupied = true;
			bedCardModel.CardTheme = "Primary";
			bedCardModel.NotifyUpdated();
		}
		AllInpatientRecords.Add(inpatientRecordModel);
		RebuildScopedInpatientData();
		SelectedInpatient = inpatientRecordModel;
		RefreshInpatientMetrics();
		SystemNotice = $"✓ 已为 {inpatientRecordModel.PatientName} 办理入科并预占 {inpatientRecordModel.BedCode}，入院医嘱处于【步骤A：待护士校对】，请按 A➔B➔C➔D➔E 推进！";
	}

	public bool ApproveDischarge()
	{
		InpatientRecordModel inpatientRecordModel = SelectedInpatient ?? DischargeCheckDetails.Patient;
		if (inpatientRecordModel == null)
		{
			SystemNotice = "请先选择需要办理出院的患者。";
			return false;
		}
		DischargeCheckDetails.Patient = inpatientRecordModel;
		DischargeCheckDetails.Refresh();
		if (!DischargeCheckDetails.AllPassed)
		{
			SystemNotice = $"⚠\ufe0f {inpatientRecordModel.PatientName} 出院核查未全部通过（{inpatientRecordModel.DischargeGateSummary}），请先完成全部检查项！";
			return false;
		}
		inpatientRecordModel.IsDischargeReady = false;
		inpatientRecordModel.IsDischarged = true;
		ReleaseInpatientBed(inpatientRecordModel);
		DischargeCheckDetails.Refresh();
		NotifyInpatientChanged(inpatientRecordModel);
		SystemNotice = $"✓ [步骤G：出院闭环完成] {inpatientRecordModel.PatientName} A➔B➔C➔D➔E➔F 六严关终审全部通过，已办结出院并释放 {inpatientRecordModel.BedCode}！";
		return true;
	}

	public async Task ApproveDischargeAsync()
	{
		InpatientRecordModel patient = SelectedInpatient ?? DischargeCheckDetails.Patient;
		if (patient == null)
		{
			SystemNotice = "请先选择需要申请出院的患者。";
			return;
		}
		if (!CanOperateIpdClinical)
		{
			SystemNotice = "当前登录角色没有住院出院申请权限。";
			return;
		}
		DischargeCheckDetails.Patient = patient;
		DischargeCheckDetails.Refresh();
		if (!DischargeCheckDetails.AllPassed)
		{
			SystemNotice = $"⚠ {patient.PatientName} 仍有未完成核查项（{patient.DischargeGateSummary}）。可先提交出院申请，由收费处结算前完成全部核查。";
		}
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			using HttpResponseMessage activeResponse = await http.GetAsync("/api/v4/ipd/encounters/active");
			ClientApiEnvelope<List<DischargeEncounterDto>> activeEnvelope =
				await activeResponse.Content.ReadFromJsonAsync<ClientApiEnvelope<List<DischargeEncounterDto>>>(JsonOptions);
			if (!activeResponse.IsSuccessStatusCode || activeEnvelope == null || activeEnvelope.Code != 200)
			{
				SystemNotice = "出院申请失败：" + (activeEnvelope?.Message ?? "无法加载在院患者");
				return;
			}
			DischargeEncounterDto? encounter = activeEnvelope.Data?.FirstOrDefault(x =>
				string.Equals(x.AdmissionNo, patient.AdmissionNo, StringComparison.OrdinalIgnoreCase));
			if (encounter == null)
			{
				SystemNotice = $"未找到住院号 {patient.AdmissionNo} 的在院记录，可能已完成结算或尚未由收费处登记。";
				return;
			}
			var body = new { DischargeDiagnosis = patient.Diagnosis, DischargeInstructions = patient.DailySummary };
			using HttpResponseMessage response = await http.PostAsJsonAsync(
				"/api/v4/ipd/encounters/" + Uri.EscapeDataString(encounter.Id) + "/discharge-request", body, JsonOptions);
			ClientApiEnvelope<DischargeRequestDto> envelope =
				await response.Content.ReadFromJsonAsync<ClientApiEnvelope<DischargeRequestDto>>(JsonOptions);
			if (!response.IsSuccessStatusCode || envelope == null || envelope.Code != 200)
			{
				SystemNotice = "出院申请失败：" + (envelope?.Message ?? "服务无响应");
				return;
			}
			patient.IsDischargeReady = true;
			NotifyInpatientChanged(patient);
			SystemNotice = $"✓ 已提交 {patient.PatientName} 的出院申请（{envelope.Data?.RequestNo}）。请收费处完成六项核查与财务结算，结算后自动释放床位。";
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
		{
			SystemNotice = "出院申请失败，请检查服务连接：" + ex.Message;
			ClientDiagnosticsLog.Error("ipd.discharge", "提交出院申请失败", ex);
		}
	}

	private NursingTaskModel CreateNursingTaskFromOrder(InpatientOrderModel order, int sequence)
	{
		string category = DetermineTaskCategory(order);
		return new NursingTaskModel
		{
			TaskNo = NextInpatientId("NT", sequence),
			ScheduledTime = DateTime.Now.ToString("HH:mm"),
			Category = category,
			Content = $"{order.Content} [{order.Dose}] ({order.Route})",
			SourceOrder = order.OrderNo,
			Priority = ((order.OrderType == "临时医嘱") ? "高" : "中"),
			Status = "待执行"
		};
	}

	public void NotifyInpatientChanged(InpatientRecordModel patient)
	{
		patient.NotifyWorkflowChanged();
		SyncInpatientBed(patient);
		if (SelectedInpatient == patient)
		{
			CriticalValueAlert.PatientName = patient.PatientName;
			CriticalValueAlert.BedCode = patient.BedCode;
			DischargeCheckDetails.Patient = patient;
			DischargeCheckDetails.Refresh();
		}
		RefreshInpatientMetrics();
	}

	public void SyncInpatientBed(InpatientRecordModel patient)
	{
		BedCardModel bedCardModel = AllBedCards.FirstOrDefault((BedCardModel x) => x.AdmissionNo == patient.AdmissionNo);
		if (bedCardModel != null)
		{
			bedCardModel.PatientName = patient.PatientName;
			bedCardModel.Gender = patient.Gender;
			bedCardModel.Age = patient.Age;
			bedCardModel.AdmissionNo = patient.AdmissionNo;
			bedCardModel.AdmittingDeptName = patient.AdmittingDept;
			bedCardModel.NursingWardName = patient.NursingWard;
			bedCardModel.NursingLevel = patient.NursingLevel;
			bedCardModel.Diagnosis = patient.Diagnosis;
			bedCardModel.IsOccupied = patient.IsActiveInpatient;
			BedCardModel bedCardModel2 = bedCardModel;
			string cardTheme;
			if (patient.IsDischarged)
			{
				cardTheme = "Success";
			}
			else if (patient.IsCritical)
			{
				cardTheme = "Danger";
			}
			else
			{
				cardTheme = (patient.IsDischargeReady ? "Warning" : "Primary");
			}
			bedCardModel2.CardTheme = cardTheme;
			bedCardModel.NotifyUpdated();
		}
	}

	private void ReleaseInpatientBed(InpatientRecordModel patient)
	{
		BedCardModel bedCardModel = AllBedCards.FirstOrDefault((BedCardModel x) => x.AdmissionNo == patient.AdmissionNo);
		if (bedCardModel != null)
		{
			bedCardModel.PatientName = "【空床待收治】";
			bedCardModel.Gender = "-";
			bedCardModel.Age = 0;
			bedCardModel.AdmissionNo = "-";
			bedCardModel.NursingLevel = "未指定";
			bedCardModel.Diagnosis = "无";
			bedCardModel.BedType = "标准空床";
			bedCardModel.IsOccupied = false;
			bedCardModel.IsBorrowed = false;
			bedCardModel.CardTheme = "Success";
			bedCardModel.NotifyUpdated();
		}
	}

	public string NextInpatientId(string prefix, int sequence)
	{
		string value = DateTime.Now.ToString("MMddHHmmss");
		return $"{prefix}-{value}-{sequence + 1:D2}";
	}

	private static bool TryParseVital(string value, out decimal result)
	{
		return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result) || decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out result);
	}

	private static string FormatVital(decimal value)
	{
		return value.ToString("0.#", CultureInfo.InvariantCulture);
	}
}
