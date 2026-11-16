# 住院医生站 (IpdDoctor) 功能对等清单

> 对应旧站：`InpatientDoctorView.axaml` + `MainWindow.axaml` (出院六严关抽屉 & 危急值弹窗)。
> 目标：保留完整床位选患者、长/临医嘱开立、停嘱/作废、查房病程、出院六严关核查与提交申请能力，消除全屏遮罩弹窗（危急值与出院核查均改为右侧 `RightDrawer`），补充高危停嘱/作废/出院确认及禁用原因明确说明。

| # | 旧版能力 | 新站对应实现 | 差异/增强 |
|---|---|---|---|
| 1 | 左侧住院患者列表与 KPI (`在院`/`病危`/`待审` + `InpatientRecords` + `SelectedInpatient`) | 左侧床位列表保留，并与顶部全局 `PatientBanner` 自动双向联动 | 切站不丢当前住院患者上下文 |
| 2 | 危急值双签收 (`OpenCriticalValue_Click`) | 顶部按钮直接呼出 Shell `CriticalValueDrawer` (`RightDrawer`) | 去掉全屏黑色遮罩弹窗 |
| 3 | 办理新入院 (`AdmitInpatient`) | 顶部 `办理新入院` 按钮 + Toast 反馈 | 即时提示入院办结结果 |
| 4 | 开立长期/临时医嘱 (`NewOrderContent` / `NewOrderDose` / `NewOrderRoute` / `NewOrderFrequency` + `AddInpatientOrder(true/false)`) | Tab 0 (`医嘱管理`) 顶部开立卡片 + 禁用态说明 | 输入内容支持 `F2` 快速聚焦；权限不足或未选患者时显示具体原因 |
| 5 | 医嘱筛选 (`ALL`/`LONG`/`TEMP`/`PENDING`) 与统计计数 | Tab 0 工具栏 4 筛选按钮 + 有效/长期/临时/待审统计 | 筛选互斥且高亮清晰 |
| 6 | 停止长期医嘱 / 作废待审医嘱 (`StopInpatientOrder` / `CancelInpatientOrder`) | Tab 0 表格行操作按钮 | 增加 `ConfirmDialog` 二次确认防误停/误作废 |
| 7 | 查房摘要、新增病程、单条审签、一键审签 (`AddProgressNote` / `SignProgressNote`) | Tab 1 (`查房与病程`) | 支持 `Ctrl+S` 快捷保存病程 |
| 8 | 出院六严关状态展示、查看六严关弹窗、提交出院申请 (`PrepareDischargeCheck` / `ApproveDischargeAsync`) | Tab 2 (`审签与出院`) + `RightDrawer` 六严关详情抽屉 (`F3`) | 出院申请增加 `ConfirmDialog` 确认及锁定原因提示 |

