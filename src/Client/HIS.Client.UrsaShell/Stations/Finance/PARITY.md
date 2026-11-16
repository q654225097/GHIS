# 收费与住院结算站 (Finance) 功能对等清单

> 对应旧站：`FinanceWorkstationView.axaml` + `InpatientSettlementView.axaml`。
> 目标：保留全量收费、退费冲红、住院结算与药品进销存对账能力，补充禁用原因说明与高危财务操作二次确认。

| # | 旧版能力 | 新站对应实现 | 差异/增强 |
|---|---|---|---|
| 1 | 顶部 4 项财务 KPI（待收费处方/待退款/已冲红/日结状态） | 保留顶部 KPI 栏与快捷工具栏 | 统一卡片排版并增加操作后 Toast 反馈 |
| 2 | 收费并通知药房 (`CashierSettleAndNotifyPharmacy` / 单行 `收费`) | Toolbar 快捷执行 + Tab 0 (`门急诊收费`) 行级操作 | 无权限时显示明确原因文案 |
| 3 | 原路退费冲红 (`ExecuteFinancialRefundForOrder` / 单行 `审核入库并退费`) | Toolbar 快捷执行 + Tab 1 (`退药退费`) 行级操作 | 增加 `ConfirmDialog` 二次确认防误退款 |
| 4 | 收费员日结 (`GenerateCashierDailyClosing`) | Toolbar 日结按钮 | 增加 `ConfirmDialog` 二次确认防误轧账 |
| 5 | 住院出院结算 (`InpatientSettlementView`) | Tab 2 (`住院结算`) 直接嵌入 | 复用现有六项核查、医保统筹与预交金抵扣模型 |
| 6 | 药品进销存与收支汇总 (`DrugFinanceBreakdownList` + 成本/净销售/退药/调价损益) | Tab 3 (`药品对账`) | 全量字段对等保留 |

