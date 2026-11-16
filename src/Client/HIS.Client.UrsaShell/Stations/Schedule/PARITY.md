# 门诊排班与号源管理工作站 (Schedule) 功能对等清单

> 对应旧站：`ScheduleWorkstationView.axaml` / `ScheduleWorkstationView.axaml.cs` (Carousel Slot 10)。
> 目标：完整保留排班模板增改、诊室/时段/挂号别/费别/号源容量配置、周排班勾选、按日期区间批量生成号源、按科室与日期检索号源、动态调号容与停诊/恢复开诊能力；消除旧版 `Watermark=` 编译告警，增加批量生成与停诊二次确认，并在成功/失败时触发 `ToastHost` 即时提示。

| # | 旧版能力 | 新站对应实现 | 差异/增强 |
|---|---|---|---|
| 1 | 首次进入加载 (`EnsureLoadedAsync`) | `Attach()` 自动触发加载 | 不阻塞界面线程 |
| 2 | 排班模板列表与切换 (`Templates` + `SelectTemplate` + `NewTemplate`) | 左侧模板列表 + `新建模板 (F2)` | 高亮当前选中模板并同步编辑摘要 |
| 3 | 模板表单编辑（名称/科室/医生/诊室/星期一至日/时段/起止时间/挂号级别/挂号费/总号容/启用） | 中上方模板配置区 + `创建/保存模板 (Ctrl+S)` | 使用 `PlaceholderText` 消除 AVLN5001 警告，支持 `Ctrl+S` 保存 |
| 4 | 按模板批量发布号源 (`GenerateStartDate` / `GenerateEndDate` + `GenerateAsync`) | 号源发布卡片 | 接入 `ConfirmDialog` 二次确认防误发大批号源；未选模板时显示禁用提示 |
| 5 | 号源池检索 (`FilterDeptId` / `QueryDate` + `FilterAsync`) | 筛选工具栏 + `查询号源 (F3)` | 支持清空筛选快捷回归全院视图 |
| 6 | 号源容量修改 (`UpdateSlotCapacityAsync`) | 行内容量输入框 + `保存容量` | Toast 回执操作结果 |
| 7 | 单时段停诊/恢复 (`ToggleSlotAsync`) | 行内 `停诊/恢复` 按钮 | 执行“停诊”前接入 `ConfirmDialog` 二次确认；已有预约时明确不可停诊 |

