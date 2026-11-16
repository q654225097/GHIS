# 窗口发药站 (PharmacyWindow) 功能对等清单

> 对应旧站：`PharmacyWorkstationView.axaml` Tab 3 (`药房发药`)、Tab 4 (`门诊发药`)、Tab 5 (`住院发药`)。
> 目标：按 R7 原则拆分"窗口发药"与"药库管理"，让发药药师专注接方、核对、发药闭环与退药质检，不混入采购入库与调价配置。

| # | 旧版能力 | 新站对应实现 | 差异/增强 |
|---|---|---|---|
| 1 | 顶部发药药房切换 (`PharmacyChoices` / `SelectedPharmacy`) | 保留顶部发药药房切换下拉 + 操作员/规则标签 | 去除无关库房下拉，减少误切换 |
| 2 | 刷新当前页 (`RefreshActiveTabAsync`) | 保留"刷新队列"按钮 + `F5` 快捷键 | 即时触发 Toast 反馈 |
| 3 | 门诊待发药处方列表 (`ReadyPrescriptions` / `SelectedReadyPrescription`) | Tab 0 (`门诊发药`) 左侧列表 + 处方号/患者快速筛选框 (`F2` 聚焦) | 支持处方号/病历号/患者姓名就地过滤 |
| 4 | 处方调配核对明细 (`ReadyPrescriptionLines` / 患者 / 病历号 / 金额 / 皮试结果) | Tab 0 右侧详情面板 | 禁用发药时直接展示明确原因说明 |
| 5 | 扫码核验并发药 (`DispenseOutpatientAsync`) | 右侧主操作按钮 + `Ctrl+Enter` 快捷键 | 自动触发 Toast 提示发药单号与关单结果 |
| 6 | 住院中心药房发药队列 (`WardOrders` / 筛选患者 / 多选发药量 / `WardDispenseAsync`) | Tab 1 (`住院发药`) | 保留患者筛选输入框与数量微调 |
| 7 | 发药记录与退药质检 (`DispenseOrders` / `DispenseLines` / `SelectDispenseAsync` / 退药原因 / 包装与冷链勾选项 / `ConfirmReturnAsync`) | Tab 2 (`发药记录与退药`) | 退药质检入库前弹出 `ConfirmDialog` 二次确认防误操作 |
| 8 | 通知与错误反馈 (`Notice` / `ErrorMessage`) | 同步转发至 Shell `ToastHost` 与底部状态历史 | 操作后视线不离开工作区即可看到结果 |

