# 系统管理与配置工作站群 (Admin) 功能对等清单

> 对应旧站：`MainWindow.axaml` Carousel Slots 6–9（动态表单引擎、用户账号维护、角色权限矩阵、安全审计日志）+ 方案第 1 节 R6 / **D5 确认项**（原门诊处方页面的“配置明细字段”、“+ 新增明细字段”配置入口从医生接诊工作流剥离并归口至动态表单配置站）。
> 目标：保留 4 个管理台的全部 CRUD、字段编辑器、RBAC 矩阵切换与密码学审计验真能力，将旧版 `IsFormDesignerOpen` / `IsFieldEditorOpen` / `IsCreateUserModalOpen` / `IsCreateRoleModalOpen` 四套全屏遮罩弹窗全部重构为 `RightDrawer` + `ConfirmDialog` + `ToastHost`。

| 站点 | 旧版能力 | 新站对应实现 | 差异/增强 |
|---|---|---|---|
| `DynamicForm` | `DynamicFormTemplates` 列表与 `DesignForm_Click` / `IsFormDesignerOpen` / `IsFieldEditorOpen` | `DynamicFormStationView` + `RightDrawer` 表单字段设计器与属性编辑器 | 取消两层全屏黑底弹窗，抽屉内完成新增、上移、下移、编辑、软删与保存发布 |
| `DynamicForm` | 门诊处方明细字段配置（`BeginDesignPrescriptionItemFieldsAsync` / `BeginAddPrescriptionItemFieldAsync`）【D5】 | 顶部独立 `门诊处方动态列配置` 卡片 + 即时列刷新 | 明确由管理员统一配置，不再混入门诊医生开方界面 |
| `UserAdmin` | `UsersList` 账号清单、停/启用 (`ToggleUserStatusAsync`)、重置密码 (`ResetUserPasswordAsync`)、新建 (`NewUserForm` / `SaveNewUserAsync`) | `UserAdminStationView` + 关键词过滤 + `RightDrawer` 新建抽屉 | 停启用与重置密码均接入 `ConfirmDialog` 二次确认 |
| `RoleAdmin` | `RolesList` 角色清单、`NewRoleForm` (`SaveNewRole`)、服务端角色资源权限查询与保存 | `RoleAdminStationView`（角色列表 + 当前角色资源权限表 + `RightDrawer` 新建/编辑角色与新增权限） | 支持任意自定义角色；权限按服务端真实资源键维护，级别为仅隐藏/脱敏/只读/完全授权，可逐项或批量调整 |
| `AuditLog` | `AuditLogsList` 检索 (`AuditEventTypeFilter`/`AuditTargetEntityFilter`/`AuditOperatorFilter`)、`VerifyAuditChainAsync`、`CreateAuditAnchorAsync` | `AuditLogStationView`（进入自动拉取，行点击看完整哈希详情抽屉） | 锚点固化前接入 `ConfirmDialog`，验证结论通过状态徽章与 Toast 同步反馈 |

角色权限说明：页面只展示并写入 `GET/PUT /api/v4/platform/roles/{roleId}/permissions` 返回的资源键，不预置虚构角色模板。角色新建、编辑使用现有平台接口，不改变 `SessionContext` 的站点可见性规则。

