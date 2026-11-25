# GHIS 用户操作逻辑审计报告

审计日期：2026-11-23。审计对象：`D:\GHIS` 当前工作树，基准提交 `89af75c`，含未提交的业务改动。本次仅新增审计报告，不改业务代码。

## 一、院方验收结论

**当前版本不建议用于真实接诊、收费、报告签发或手术核查。** 主要问题不只是功能缺失：有些关键按钮会在后台未确认时显示成功；另一些按钮能写入正式状态，但缺少该状态应代表的真实业务凭据。主解决方案测试通过不能替代多岗位桌面验收。

**住院患者可以直接建档。** 收费站有“新建患者并开立住院卡”的界面和接口，集成测试覆盖收费员建档、开卡及对应审计记录。但是待登记列表自动选中首卡后，会隐藏新建档区域，而且建档、开卡没有在同一事务内完成。不能把“已有建档接口”认作“入院全程已验收”。

**范围与证据：** 盘点 36 个桌面站点目录、58 个控制器文件、652 处 HTTP 路由特性声明，按岗位核对主要业务的菜单、按钮命令、请求、状态、凭据和测试。计数是源码结构，不是实测可用功能数；未逐一实际操作每个路由。“确认”指代码证据明确；“风险”指存在触发条件但未观察到实际事故；“待验”指尚未实机或联调。P0 阻断相关流程上线，P1 为试运行前整改，P2 为操作质量整改。

## 二、P0：阻断上线

### P0-01 财务收费、退费、日结不等后台回执

收费先生成本地单号、标记已付款并解锁本地可发药状态；退费和日结也先设置完成，再发请求，不等实际结果。日结金额来自内存列表，不是按日期或班次查出的真实台账。顶栏收费没明确选单时还会自动取首条符合本地条件的记录。

证据：[收费](src/Client/HIS.Client.UrsaShell/ViewModels/WorkbenchModel.Finance.cs#L31)、[退费](src/Client/HIS.Client.UrsaShell/ViewModels/WorkbenchModel.Finance.cs#L91)、[日结](src/Client/HIS.Client.UrsaShell/ViewModels/WorkbenchModel.Finance.cs#L151)、[站点反馈](src/Client/HIS.Client.UrsaShell/Stations/Finance/FinanceStationViewModel.cs#L66)。

复验：没选单不能收费；明确核对患者、就诊、费用和支付方式。后台拒绝或断网不显示成功、不解锁药房；回执未知先查原单。收费、退款、日结和打印使用后台单号，两个终端和台账一致。

### P0-02 医技普通入口可提交固定的“正常”结论

确费、签发忽略后台返回并吞掉异常，随后推进本地状态。签发提交固定正常结论且标记非危急值。此入口还用 `RequestNo` 调签发，但后台按记录 ID 查找；匹配失败也能显示成功，若命中并满足状态门禁，后台会保存提交的文本。独立医技报告审签站的存在不能抵消这个问题。

证据：[确费与签发](src/Client/HIS.Client.UrsaShell/Stations/MedTech/MedTechStationViewModel.cs#L840)、[服务端签发](src/Server/HIS.Modules.MedTech/Controllers/MedTechController.cs#L537)。

复验：实际结果未录入、未复核时不能直接签发。测试异常结果、无结果、危急值及后台拒绝；医院再次登录、换机器看到相同报告，失败不显示正常或完成。

### P0-03 顶部危急值签收只改本地界面

医生签收、护士复核命令只填写状态文本、刷新徽标并提示成功，没有调用现有签收接口。界面显示“双签完成”不代表后台已留痕或报警已处理。

证据：[顶部抽屉命令](src/Client/HIS.Client.UrsaShell/Shell/ShellViewModel.cs#L117)、[后台签收](src/Server/HIS.Modules.MedTech/Controllers/MedTechController.cs#L619)。

复验：医生和护士分别以本人身份签收指定报告；退出重进和另一终端均可读回对应人员及时间。断网、错误岗位和未完成双签均不得提示闭环。

### P0-04 手术安全核查可一键标记全部阶段通过

排台核查按钮一次提交三个阶段与器械清点均为真；获准角色的写入会持久化这些状态，结束手术用它们作门禁。另一个抽屉入口只改变本地显示并宣称三方签发。二者都不代表逐阶段真实核查。

证据：[一键提交](src/Client/HIS.Client.UrsaShell/Stations/MedTech/MedTechStationViewModel.cs#L530)、[本地抽屉](src/Client/HIS.Client.UrsaShell/Stations/MedTech/MedTechStationViewModel.cs#L960)、[后台落库](src/Server/HIS.Modules.MedTech/Controllers/MedTechController.cs#L1464)、[结束门禁](src/Server/HIS.Modules.MedTech/Controllers/MedTechController.cs#L1506)。

复验：术前、切皮前、离室前分别确认，记录岗位、人员和时间；器械未核对或数量不平衡不得离室，禁止普通操作员一键代填所有阶段。

### P0-05 出院结算选中患者与费用预览可能错配

选中变化没有清空旧预览；忙碌时跳过新请求，旧应答仍可回填。结算请求用当前选中申请 ID，却取屏幕上的预览金额及明细。后台有重算和金额校验，可能拒绝交易，但不能证明前台核对的是正确患者；本次没有观察到实际错结。

证据：[切换选中项](src/Client/HIS.Client.UrsaShell/ViewModels/InpatientSettlementViewModel.cs#L102)、[可结算条件](src/Client/HIS.Client.UrsaShell/ViewModels/InpatientSettlementViewModel.cs#L224)、[预览回填](src/Client/HIS.Client.UrsaShell/ViewModels/InpatientSettlementViewModel.cs#L304)、[提交](src/Client/HIS.Client.UrsaShell/ViewModels/InpatientSettlementViewModel.cs#L424)、[后台校验](src/Server/HIS.Modules.Inpatient/Controllers/InpatientDischargeController.cs#L364)。

复验：两位患者同金额、不同金额各测一次，延迟先选患者的响应后快速切换。丢弃旧应答；当前患者预览未载入前禁止提交；确认页与凭证均核对患者、住院号、费用及押金。

### P0-06 门诊检查核收未绑定本次检查的付款项目

后台核收接受同就诊或同患者的有效门诊付款，也存在以挂号付费为依据的分支，未按本次申请检查已付明细。财务服务会把待缴医技项目纳入整次应收，但写总账时聚合为“门诊处方药品费”；财务站按钮上传的是单行处方金额。

证据：[核收取付款](src/Server/HIS.Modules.MedTech/Controllers/MedTechController.cs#L335)、[更新确费状态](src/Server/HIS.Modules.MedTech/Controllers/MedTechController.cs#L398)、[应收重算](src/Server/HIS.Modules.Finance/Controllers/FinanceSettlementService.cs#L23)、[费用总账](src/Server/HIS.Modules.Finance/Controllers/FinanceController.cs#L230)。

复验：仅付挂号费、旧就诊已缴费、同次仅部分申请已付和申请已退费时，未付申请不得到检核收。分别核对处方加检查、多张处方和纯检查，收费窗口、实际支付和财务凭证明细必须一致。

### P0-07 住院押金重试有重复入账风险

每次点击生成不同请求键，后台只能按同键重放；提交成功后若刷新抛出异常，又可能提示“押金收取失败”。“收取并打印收据”按钮没有实际打印调用。

证据：[收押金](src/Client/HIS.Client.UrsaShell/ViewModels/InpatientCashierViewModel.cs#L650)、[后台幂等](src/Server/HIS.Modules.Inpatient/Controllers/InpatientAdmissionController.cs#L749)、[按钮](src/Client/HIS.Client.UrsaShell/Views/InpatientCashierView.axaml#L426)。

复验：后台落账后丢掉回执，操作员再次点击只能查回原单，不能入账两次。到账、查询刷新和打印失败应分别反馈，打印失败可按原单补打。

## 三、P1：试运行前整改

| 编号 | 发现 | 验收判据 |
| --- | --- | --- |
| P1-01 | **直接建档入口会被隐藏，建档与开卡可能只完成一半。** 界面依赖 `!HasSelectedCard`，加载时自动选首卡；后台插入患者后再插入住院卡，未看到两者共用事务。登记允许零初始押金，病区接诊则无条件拦截零余额。[显示条件](src/Client/HIS.Client.UrsaShell/Views/InpatientCashierView.axaml#L211)、[默认选卡](src/Client/HIS.Client.UrsaShell/ViewModels/InpatientCashierViewModel.cs#L762)、[患者写入](src/Server/HIS.Modules.Inpatient/Controllers/InpatientAdmissionController.cs#L306)、[开卡](src/Server/HIS.Modules.Inpatient/Controllers/InpatientAdmissionController.cs#L381)、[接诊门禁](src/Server/HIS.Modules.Inpatient/Controllers/InpatientAdmissionController.cs#L852)。 | 待登记卡有无都能新建或查档；开卡失败可恢复，不重复建人或开卡。按院方政策区分普通欠费、授权绿色通道和免押金，例外有审批和审计。 |
| P1-02 | **部分业务站只挂管理员菜单；切换身份可能复用旧站点数据。** 报告审签、手麻、急诊、体检、预住院等独立站只有管理员菜单入口，不等于常规医技、护士站完全不可做相关业务。切换时会重建菜单，但按站点键复用视图，未统一清空选中患者、草稿或列表。后者需实机验证。[菜单](src/Client/HIS.Client.UrsaShell/Shell/ShellViewModel.cs#L616)、[管理员判断](src/Client/HIS.Client.Core/SessionContext.cs#L351)、[切换](src/Client/HIS.Client.UrsaShell/Shell/ShellViewModel.cs#L106)、[缓存](src/Client/HIS.Client.UrsaShell/Shell/ShellViewModel.cs#L715)。 | 用普通岗位账号从菜单进入；跨角色或跨科室时先处理未保存草稿，再清除或重载当前对象、列表与操作权限。两种身份都有同一站点时也要测。 |
| P1-03 | **完成诊疗与病历审签、审方、缴费的状态口径不同。** 前台要求签署及 OFD，后台没有核对归档和患者归属。结束诊疗拦截处方状态 `0/1/2`，含已审方待缴费 `1`，却未包含待前置审方 `6`；财务又禁止已完成就诊收费。纯检查和仍待审方等分支需复验。[客户端](src/Client/HIS.Client.UrsaShell/ViewModels/WorkbenchModel.OpdWorkflow.cs#L345)、[完成诊疗](src/Server/HIS.Modules.Outpatient/Controllers/OutpatientController.cs#L1264)、[处方状态](src/Server/HIS.Modules.Outpatient/Controllers/OutpatientController.cs#L1643)、[收费限制](src/Server/HIS.Modules.Finance/Controllers/FinanceController.cs#L157)。 | 区分临床诊疗关闭和缴费/发药结单，由院方确认各状态顺序；分别验收无处方、仅检查、待审方、已审未付、已付未发药。后台独立校验本患者本就诊的病历和归档。 |
| P1-04 | **动态表单业务记录缺少对象范围限制，保存会覆盖扩展列。** 列表查询及保存未见继承原对象岗位、科室和数据权限；保存重建整个 `ext_data_json` 并直接截断超长 JSON，可能覆盖原业务键并造成不可解析。配置菜单的管理员限制不能代替业务记录授权。[列表](src/Server/HIS.Modules.Config/Controllers/DynamicFormBoController.cs#L134)、[保存](src/Server/HIS.Modules.Config/Controllers/DynamicFormBoController.cs#L273)、[覆盖](src/Server/HIS.Modules.Config/Controllers/DynamicFormBoController.cs#L372)。 | 管理员管配置，业务人员只能录入其本职有权对象。旧键和值不丢失，超长明确拒绝不留半截数据；隐藏/停用的字段保留历史值，历史单据能按原版本读。 |
| P1-05 | **工作流发布不以最新草稿保存成功为前提。** 保存内部处理错误且不返回可核对版本，发布仍继续用当前选中定义，可能发布旧草稿。[保存草稿](src/Client/HIS.Client.UrsaShell/Stations/WorkflowDesigner/WorkflowDesignerViewModel.cs#L1056)、[发布](src/Client/HIS.Client.UrsaShell/Stations/WorkflowDesigner/WorkflowDesignerViewModel.cs#L1098)。 | 保存失败不得继续发布；只发布刚确认保存的定义版本。验收并行编辑、切定义以及旧审批实例按旧版本继续运行。 |
| P1-06 | **CSSD 默认值替代真实操作。** 打包可送出空内容，灭菌方法和有效期固定，发放目标病区写死。已有耗材申请、审批、发放和 UDI，不应把整个模块判为缺失。[打包](src/Client/HIS.Client.UrsaShell/Stations/Consumables/ConsumablesWarehouseViewModel.cs#L421)、[灭菌](src/Client/HIS.Client.UrsaShell/Stations/Consumables/ConsumablesWarehouseViewModel.cs#L448)、[发放](src/Client/HIS.Client.UrsaShell/Stations/Consumables/ConsumablesWarehouseViewModel.cs#L474)。 | 实录包内器械、批次、程序、监测、放行、接收科室及退回/召回；不合格不得放行，能按批次反查受影响患者或科室。 |

## 四、P2：操作质量与现场验证

- **收银台检索和统计：** 在院搜索未传关键词；“今日登记”取在院清单数，“今日预交金”以当前患者流水计算。用不同患者、跨日、跨班次及并发录入核对窗口和台账。[数据加载](src/Client/HIS.Client.UrsaShell/ViewModels/InpatientCashierViewModel.cs#L771)
- **以可核对单据作为操作入口：** 医技扫码、排程还有手工录入内部 ID 的入口；住院医师“医嘱设置”只弹提示。应以可见单号、患者腕带、申请单或追溯码为操作对象。[医技扫码](src/Client/HIS.Client.UrsaShell/Stations/MedTech/MedTechStationViewModel.cs#L1008)、[医嘱设置](src/Client/HIS.Client.UrsaShell/Stations/IpdDoctor/IpdDoctorViewModel.cs#L109)
- **打印、补打和保存需按单据验证：** 代码既有写出 HTML 后打开浏览器的打印，也有原生 Windows 路径。处方、押金收据、出院凭证、标签、病历分别验证选择打印机、取消、保存路径、纸型、无打印机和补打。[通用打印](src/Client/HIS.Client.UrsaShell/Printing/PrintEngineService.cs#L281)、[Windows 打印](src/Client/HIS.Client.Platform.Desktop/WindowsPrintService.cs#L24)

## 五、全模块覆盖矩阵

“代码中已看到”指至少找到代表性界面、命令或服务端实现，不能代替完整落地证明。下表列出真实业务需要但未证实完整的操作。

| 岗位／领域 | 代码中已看到 | 待实机验收 |
| --- | --- | --- |
| 工作台、登录、系统管理、人事 | 登录选岗位和科室；用户、角色、审计、人事入口。 | 用非管理员岗位验收；跨科切换、撤权、退出共享电脑、人员和科室审计。 |
| 排班、挂号、分诊、患者主索引 | 模板排班、挂号候诊及收费、建档查重。 | 套模板后请假替班、号源冲突、退改号、停诊；同名、无证及合并后历史归属。 |
| 门诊医生 | 接诊、诊断、处方编辑与扩展字段、检查申请、病历模板和签署。 | 按“先诊断→开处方/检查→选医生本人模板写病历→签署”验收；数量/频次重开核对、草稿增删改查、撤回不丢历史、诊疗和缴费衔接。 |
| 急诊、急重症、公卫及随访 | 分诊、优先级、证明，部分急重症质控、公卫登记、随访和营养入口。 | 无名抢救后合并、留观转住院、先救治后缴费、真实上报回执和撤回。 |
| 预住院、住院登记、收费 | 门诊开卡、收费处直接建档开卡、床位与预交金。 | 预住院转正式入院不重复建人/卡/费用；抢同一床、跨科登记、记账与开卡失败恢复。 |
| 住院医师、护士、护理与院感 | 医嘱、入病区接诊、责任护士/医生指派、执行和院感入口。 | 临时/长期医嘱开立、审核、停止、执行、拒绝；PDA 断网补偿、换床、转科、待执行任务交接。 |
| 出院协作和结算 | 出院申请、临床门禁、费用预览、押金冲抵、结算及释放床位。 | 未执行医嘱、未发药、退药及材料费用；同一患者确认、结算与补打，医保失败恢复及床位释放时点。 |
| 财务、门诊收费到药房 | 收费、退费、日结 API，有处方与药房衔接的集成测试。 | 现金、扫码、医保混合支付及退款；处方/医技逐项对账、真实收退费回执与窗口交接。 |
| 药库、药房、库存 | 药品基础数据、定价、采购入库、出库/调拨、门诊和住院发药及退药。 | 部分到货/拒收、批次效期、多库房调拨、旧新价格分界；门诊收费后异机队列可见、住院医嘱发药、实物退药后财务退费，防重复发药。 |
| 医技报告、治疗 | 医技开单、核收和审签，治疗预约和执行。 | 核收、执行、回传、复检、补充报告、危急值双签；治疗次数、执行人、撤销和成本/费用核对。 |
| 手术麻醉、输血、监护 | 排台、WHO、手麻与 PACU；血袋库存、申请、配血、发放及反应；监护模块。 | 急诊插台和分阶段核查，术后转运，血袋与患者双核验、退血和不良反应，设备延迟及重连。 |
| 耗材、UDI 与消毒供应 | 耗材申领、审批与出入库，UDI 和部分 CSSD 操作。 | 待检/合格/隔离、批次放行、退库、召回和代销；实物、账目及患者费用一致。 |
| 体检、病历、病案与质控 | 个检/团检、分科与总检；模板、档案、设计、病历质控及编码入口。 | 弃检/复检与重大阳性随访；归档病历版本、签章验证、授权借阅、导出留痕。 |
| 动态表单、工作流、打印 | 字段设计和实时预览区域，业务对象回写；审批待办、设计发布和打印模板。 | 管理员配字段；普通用户录入。类型、必填、禁用、历史值、实时预览与真实表单一致；审批归属和版本、打印纸张与分页。 |
| 运营统计、医务、院外接口 | 分析与自定义报表、医务；有 HIE、医保、LIS/PACS 相关服务或适配器；患者服务、成本中心及设备管理有服务端控制器，未找到对应桌面调用。 | 核对统计口径、权限、导出及审计；确认患者触达、成本核算和设备台账交付入口。医保、LIS/PACS、CA 和短信须用院方配置实际联调，Mock 成功不计为验收。 |

## 六、验证结果与复验顺序

- `dotnet build HIS.slnx --no-restore -v:minimal`：本次构建通过，0 警告、0 错误。
- `dotnet test HIS.slnx --no-restore -v:minimal`：151 项通过（3 架构、103 单元、45 集成）。代表性用例：[直接建档开卡](tests/HIS.IntegrationTests/ComplianceRemediationVerificationTests.cs#L559)、[药库药房财务](tests/HIS.IntegrationTests/EndToEndWorkflowTests.cs#L1262)、[住院发药防重](tests/HIS.IntegrationTests/EndToEndWorkflowTests.cs#L1461)、[出院及床位释放](tests/HIS.IntegrationTests/EndToEndWorkflowTests.cs#L1638)。这些集成用例调用服务端，不验证桌面菜单、按钮、界面反馈及选中患者切换。
- [独立集成测试项目](src/Server/HIS.Tests.Integration/HIS.Tests.Integration.csproj) 不在 `HIS.slnx`。单独还原后尝试执行，编译失败，涉及 `CancellationToken`、`RoleCode` 和已变更的出院检查属性等，如 [CrossModuleClosedLoopTests.cs](src/Server/HIS.Tests.Integration/CrossModuleClosedLoopTests.cs#L34)。不计入通过数。
- 本次未启动桌面应用，未使用真实患者数据，未联调打印机、医保或检验/影像设备；未找到覆盖以上桌面操作的 UI 自动化用例。

**整改顺序：** 先停用或修复假成功、代签、可能错患者或错账的入口；再打通岗位菜单、单据和状态；最后验证操作效率和外设。每条关键验收用例须同时留存界面结果、真实业务单号、后台状态和审计记录，并由下一个岗位核实能继续办理。
