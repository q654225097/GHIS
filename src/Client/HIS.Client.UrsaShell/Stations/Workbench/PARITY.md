# Workbench / 我的待办 — PARITY

## 1. 定位与来源
- **新增首页工作台**（原设计 R8 解决各角色首页缺少"我的待办"入口的问题）。
- 全部数据直接来自 `WorkbenchModel` 与 `SessionContext.Current`，不新增任何服务端接口，不修改任何权限判定。

## 2. 能力清单
| 角色能力判断 | 数据来源 | 展示与跳转 |
|---|---|---|
| 所有临床角色（存在未闭环危急值） | `Workbench.CriticalValueAlert` / `PendingCriticalValueCount` | 红色警告横幅，点击直接打开右侧危急值双签抽屉 |
| 门诊医生 (`SessionContext.Current.CanSeeOpd`) | `WaitingPatientCount`、`CompletedPatientCount`、`PatientQueue` | 候诊队列摘要 + 前 6 位待诊患者直达卡片（点击选定患者并进入门诊医生站）+ 一键叫号 |
| 住院医生 (`SessionContext.Current.CanSeeIpdDoctor`) | `InpatientRecords`、`PendingOrderReviewCount` | 在院患者摘要卡片（点击选定在院患者并进入住院医生站） |
| 病区护士 (`SessionContext.Current.CanSeeWardNurse`) | `PendingOrderReviewCount`、`WaitingDecomposeTotalCount`、`PendingNursingTaskCount` | 待校对/待分解/待执行任务数统计 + 进入病区护士站 |
| 药师 (`SessionContext.Current.CanSeePharmacy`) | `PendingPharmacyDispenseCount`、`ReturnedPharmacyOrderCount`、`PharmacyRecords` | 待发药处方列表（前 6 条）+ 进入药房/药库工作站 |
| 收费/结算员 (`SessionContext.Current.CanSeeFinance` / `CanSeeSchedule`) | `PendingFinanceSettleCount`、`PendingFinancialRefundCount` | 待收费/待退费统计 + 进入收费结算或排班挂号 |
| 系统管理员 (`SessionContext.Current.CanSeeAdminSection`) | `DynamicFormTemplates.Count`、`SystemUsers.Count`、`RolesList.Count` | 动态表单、用户账号、角色权限、审计日志快捷入口 |

