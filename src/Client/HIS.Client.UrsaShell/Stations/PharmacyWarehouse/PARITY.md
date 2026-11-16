# 药库与药品目录站 (PharmacyWarehouse) 功能对等清单

> 对应旧站：`PharmacyWorkstationView.axaml` Tab 0 (`药品信息`)、Tab 1 (`采购入库`)、Tab 2 (`药库发药`)。
> 目标：按 R7 原则从窗口发药工作台拆出，专职处理采购收货、批号/效期/冷链登记、FEFO 库房出库、跨库调拨、药品主目录与价格维护。

| # | 旧版能力 | 新站对应实现 | 差异/增强 |
|---|---|---|---|
| 1 | 顶部业务库房切换 (`WarehouseChoices` / `SelectedWarehouse`) | 保留顶部当前库房下拉 + 刷新当前页 (`F5`) | 去掉无关窗口药房下拉，焦点集中在仓储库房 |
| 2 | 采购入库 KPI 与表单 (`ActiveDrugCount` / `TotalBatchQuantity` / `ExpiringBatchCount` / 批号 / 效期 / 数量 / 冷链实测与上限 / 发票号 / `StockInAsync`) | Tab 0 (`采购入库与结存`) | 入库后立即反馈 Toast |
| 3 | 当前库房药品结存表与批次明细 (`StockRows` / `Batches` / `RefreshStockAsync`) | Tab 0 下方双栏表格 | 保持完整列对齐与效期预警展示 |
| 4 | 药库发药与跨库调拨 (`TargetPharmacy` / `IssueDrug` / `IssueQuantity` / 说明 / `WarehouseIssueAsync` / `TransferAsync` / `Transfers`) | Tab 1 (`药库出库与调拨`) | 跨库调拨前弹出 `ConfirmDialog` 二次确认；保留台账表 |
| 5 | 药品基础目录检索与维护 (`DrugKeyword` / `SearchDrugsAsync` / `Drugs` / `SelectedDrug` / `NewDrug` / `SaveDrugAsync` / `ToggleDrugAsync` / `CanEditDrugs` / 调价依据) | Tab 2 (`药品主目录`) | 非管理员账号明确显示只读原因；停用/启用药品前弹出确认弹窗；检索框支持 `Enter` / `F2` |

