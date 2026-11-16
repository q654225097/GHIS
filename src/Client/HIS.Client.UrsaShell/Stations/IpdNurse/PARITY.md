# 病区护士站 (IpdNurse) 功能对等清单

> 对应旧站：`InpatientNurseView.axaml` + 危急值弹窗 / 频次分解弹窗 / 借床弹窗。
> 目标：保持护嘱校对、长嘱分解、护理执行双核、生命体征、交接班与病区床位全量能力，将 3 类全屏遮罩弹窗替换为右侧 `RightDrawer`，增加批量执行确认弹窗及禁用态原因明示。

| # | 旧版能力 | 新站对应实现 | 差异/增强 |
|---|---|---|---|
| 1 | 左侧病区患者与 KPI (`在院`/`待执行`/`待核对` + `ActiveContextSummary`) | 左侧床位列表保留并与顶部全局 `PatientBanner` 自动同步 | 上下文切站不丢失 |
| 2 | 危急值签收 (`OpenCriticalValue_Click`) | 呼出 Shell `CriticalValueDrawer` (`RightDrawer`) | 无全屏黑色遮罩 |
| 3 | 调整护理级别 (`CycleNursingLevel`) | 顶部按钮直接流转 + Toast 提示 | 即时反馈当前级别 |
| 4 | 医嘱校对与分解 (`ReviewSelectedInpatientOrders` / `ReviewSingleInpatientOrder` / `DecomposeTodayOrders` / `DecomposeTomorrowOrders`) | Tab 0 (`医嘱校对与分解`) | 禁用"批量校对审签"时给出具体原因文案 |
| 5 | 频次分解规则配置 (`OpenFrequencyConfigModal` / 编辑各频次时间点 / 新增自定义频次 / 恢复默认) | 在 Tab 0/1 呼出 `RightDrawer` 频次分解抽屉 | 右侧抽屉即可就地编辑时间点与新增频次并保存同步 |
| 6 | 护理执行与双人核对 (`ExecuteAllNursingTasks` / `ExecuteNursingTask` / `VerifyNursingTask`) | Tab 1 (`护理执行`) | 批量执行前弹出 `ConfirmDialog` 确认防误批量核销 |
| 7 | 生命体征录入与异常标注 (`SaveVitalSigns` / 6项体征指标 / 历史列表) | Tab 2 (`生命体征`) | 支持 `F2` 聚焦体温框、`Ctrl+S` 快捷保存 |
| 8 | 交接班记录与接班确认 (`AddHandoverNote` / `ConfirmHandoverNote`) | Tab 3 (`交接班`) | 支持 `Ctrl+S` 在本页快捷提交交接班 |
| 9 | 病区床位矩阵与跨病区借床 (`BedCards` / `OpenBorrowBed_Click`) | Tab 4 (`病区床位`) + `RightDrawer` 借床向导 | 去掉全屏遮罩借床弹窗 |

