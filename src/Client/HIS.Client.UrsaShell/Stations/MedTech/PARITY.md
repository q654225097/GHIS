# 医技检查检验工作站 (MedTech) 功能对等清单

> 对应旧站：`MainWindow.axaml` Carousel Slot 3 (医技检查检验与手术安全核查台)。
> 目标：保留医技申请单确费锁单、出具报告、撤单退费拦截说明、WHO 手术三方核查与危急值处置全部能力，去除全屏黑幕遮罩，改用右侧 `RightDrawer` + `ConfirmDialog` + `ToastHost`。

| # | 旧版能力 | 新站对应实现 | 差异/增强 |
|---|---|---|---|
| 1 | 医技申请列表 (`Workbench.MedTechOrders`) | `FilteredOrders`（支持按申请单号/项目/科室/状态实时过滤） | 新增状态统计条与关键词检索 |
| 2 | 确认收费并锁单 (`ConfirmMedTechFee`) | 行内 `确费锁单` 按钮 | `ToastHost` 即时提示 + 状态徽章更新 |
| 3 | 出具检查检验报告 (`PublishMedTechReport`) | 行内 `发布报告` 按钮（未确费时禁用并显示原因） | 显式说明需先确费锁单后才可出报告 |
| 4 | 撤销医技申请 (`TryCancelMedTechOrder`) | 行内 `撤单退回` 按钮 | 高风险撤单接入 `ConfirmDialog` 二次确认 |
| 5 | 已确费锁单提醒与退费依据说明 (`HasFeeLockedMedTechOrder` / `FeeLockedMedTechOrderText`) | 顶部黄色提示条 + `查看退费依据` 抽屉 | 由居中阻塞提示改用右侧 `RightDrawer` |
| 6 | WHO 手术安全三方核查 (`IsWhoSurgeryCheckOpen`) | 顶部 `WHO 手术安全核查 (F2)` + `RightDrawer` 三方核验签署 | 取消全屏黑底弹窗，改为右侧核查单抽屉 |
| 7 | 危急值双签收入口 | 顶部 `危急值处置` 按钮调起 Shell 全局抽屉 | 全站统一入口 |

