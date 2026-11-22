using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;
using HIS.Contracts;

namespace HIS.Client.UrsaShell.Stations.WorkflowDesigner;

public class WorkflowNodeModel : ClientModelBase
{
    private string _id = Guid.NewGuid().ToString("N")[..8];
    private string _type = "APPROVAL"; // START, APPROVAL, EXCLUSIVE_GATEWAY, CC, END
    private string _name = "审批节点";
    private double _x = 100;
    private double _y = 100;
    private string _approvalMode = "SINGLE"; // SINGLE, AND_SIGN, OR_SIGN
    private string _assigneeStrategy = "ROLE"; // FIXED_USER, ROLE, DEPT_MANAGER, DIRECT_SUPERVISOR, ESCALATE_N, BO_EXPRESSION
    private string _assigneeConfig = "ROLE-DOCTOR-CHIEF";
    private string? _fallbackApproverId = "USR-ADMIN";
    private int _escalateLevels = 1;
    private bool _isSelected;

    public string Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string Type
    {
        get => _type;
        set
        {
            if (SetField(ref _type, value))
            {
                OnPropertyChanged(nameof(NodeBrush));
                OnPropertyChanged(nameof(TypeTitle));
                OnPropertyChanged(nameof(IsApprovalOrCC));
                OnPropertyChanged(nameof(IsApprovalOnly));
            }
        }
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public double X
    {
        get => _x;
        set
        {
            if (SetField(ref _x, value))
            {
                OnPropertyChanged(nameof(CenterX));
            }
        }
    }

    public double Y
    {
        get => _y;
        set
        {
            if (SetField(ref _y, value))
            {
                OnPropertyChanged(nameof(CenterY));
            }
        }
    }

    public double CenterX => X + 70;
    public double CenterY => Y + 30;

    public string ApprovalMode
    {
        get => _approvalMode;
        set => SetField(ref _approvalMode, value);
    }

    public string AssigneeStrategy
    {
        get => _assigneeStrategy;
        set => SetField(ref _assigneeStrategy, value);
    }

    public string AssigneeConfig
    {
        get => _assigneeConfig;
        set => SetField(ref _assigneeConfig, value);
    }

    public string? FallbackApproverId
    {
        get => _fallbackApproverId;
        set => SetField(ref _fallbackApproverId, value);
    }

    public int EscalateLevels
    {
        get => _escalateLevels;
        set => SetField(ref _escalateLevels, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public bool IsApprovalOrCC => Type == "APPROVAL" || Type == "CC";
    public bool IsApprovalOnly => Type == "APPROVAL";

    public string TypeTitle => Type switch
    {
        "START" => "🟢 开始",
        "APPROVAL" => "👤 审批",
        "EXCLUSIVE_GATEWAY" => "🔀 条件网关",
        "CC" => "📋 抄送",
        "END" => "🛑 结束",
        _ => Type
    };

    public string NodeBrush => Type switch
    {
        "START" => "#10B981",
        "APPROVAL" => "#2563EB",
        "EXCLUSIVE_GATEWAY" => "#D97706",
        "CC" => "#9333EA",
        "END" => "#475569",
        _ => "#0284C7"
    };
}

public class WorkflowEdgeModel : ClientModelBase
{
    private string _id = Guid.NewGuid().ToString("N")[..8];
    private string _sourceNodeId = string.Empty;
    private string _targetNodeId = string.Empty;
    private bool _isDefault;
    private string _label = string.Empty;
    private double _x1;
    private double _y1;
    private double _x2;
    private double _y2;
    private bool _isSelected;

    public string Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string SourceNodeId
    {
        get => _sourceNodeId;
        set => SetField(ref _sourceNodeId, value);
    }

    public string TargetNodeId
    {
        get => _targetNodeId;
        set => SetField(ref _targetNodeId, value);
    }

    public bool IsDefault
    {
        get => _isDefault;
        set
        {
            if (SetField(ref _isDefault, value))
            {
                UpdateLabel();
            }
        }
    }

    public string Label
    {
        get => _label;
        set => SetField(ref _label, value);
    }

    public double X1
    {
        get => _x1;
        set
        {
            if (SetField(ref _x1, value))
            {
                OnPropertyChanged(nameof(MidX));
                OnPropertyChanged(nameof(StartPoint));
            }
        }
    }

    public double Y1
    {
        get => _y1;
        set
        {
            if (SetField(ref _y1, value))
            {
                OnPropertyChanged(nameof(MidY));
                OnPropertyChanged(nameof(StartPoint));
            }
        }
    }

    public double X2
    {
        get => _x2;
        set
        {
            if (SetField(ref _x2, value))
            {
                OnPropertyChanged(nameof(MidX));
                OnPropertyChanged(nameof(EndPoint));
            }
        }
    }

    public double Y2
    {
        get => _y2;
        set
        {
            if (SetField(ref _y2, value))
            {
                OnPropertyChanged(nameof(MidY));
                OnPropertyChanged(nameof(EndPoint));
            }
        }
    }

    public Point StartPoint => new Point(X1, Y1);
    public Point EndPoint => new Point(X2, Y2);
    public double MidX => (X1 + X2) / 2.0;
    public double MidY => (Y1 + Y2) / 2.0;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public ObservableCollection<WorkflowConditionRowModel> Conditions { get; } = new();

    public void UpdateLabel()
    {
        if (IsDefault)
        {
            Label = "[默认兜底分支]";
            return;
        }
        if (Conditions.Count == 0)
        {
            Label = "直接流转";
            return;
        }
        Label = string.Join(" 且 ", Conditions.Select(c => $"{c.Field} {c.Op} {c.Value}"));
    }
}

public class WorkflowConditionRowModel : ClientModelBase
{
    private string _field = "amount";
    private string _op = ">";
    private string _value = "1000";
    private string _relation = "AND";

    public string Field
    {
        get => _field;
        set => SetField(ref _field, value);
    }

    public string Op
    {
        get => _op;
        set => SetField(ref _op, value);
    }

    public string Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }

    public string Relation
    {
        get => _relation;
        set => SetField(ref _relation, value);
    }
}

public class WorkflowDefinitionItemModel : ClientModelBase
{
    public string Id { get; set; } = string.Empty;
    public string DefKey { get; set; } = string.Empty;
    public string DefName { get; set; } = string.Empty;
    public string BoType { get; set; } = string.Empty;
    public int VersionNum { get; set; } = 1;
    public string Status { get; set; } = "DRAFT"; // DRAFT / PUBLISHED / ARCHIVED
    public string GraphJson { get; set; } = string.Empty;
    public string? FallbackApproverId { get; set; }

    public string VersionBadge => $"v{VersionNum} [{Status}]";
    public string StatusBrush => Status switch
    {
        "PUBLISHED" => "#10B981",
        "DRAFT" => "#D97706",
        _ => "#64748B"
    };
}

public class SimpleOptionItem
{
    public string Value { get; set; } = string.Empty;
    public string DisplayText { get; set; } = string.Empty;
}

public sealed class WorkflowDesignerViewModel : ClientModelBase, IShortcutTarget
{
    private readonly ShellViewModel? _shell;
    private WorkflowDefinitionItemModel? _selectedDefinition;
    private WorkflowNodeModel? _selectedNode;
    private WorkflowEdgeModel? _selectedEdge;
    private WorkflowNodeModel? _connectingSourceNode;
    private bool _isBusy;
    private string _statusNotice = string.Empty;

    // Definition Form Fields
    private string _defKey = "CUSTOM_WORKFLOW";
    private string _defName = "自定义业务审批流程";
    private string _selectedBoType = "CSM_REQUISITION";
    private string _fallbackApproverId = "USR-ADMIN";

    public WorkflowDesignerViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        AddNodeCommand = new RelayCommand(p => ExecuteAddNode(p as string ?? "APPROVAL"));
        SelectNodeCommand = new RelayCommand(p => ExecuteSelectNode(p as WorkflowNodeModel));
        SelectEdgeCommand = new RelayCommand(p => ExecuteSelectEdge(p as WorkflowEdgeModel));
        DeleteSelectedCommand = new RelayCommand(ExecuteDeleteSelected);
        StartConnectCommand = new RelayCommand(p => ExecuteStartConnect(p as WorkflowNodeModel));
        FinishConnectCommand = new RelayCommand(p => ExecuteFinishConnect(p as WorkflowNodeModel));
        CancelConnectCommand = new RelayCommand(() => ConnectingSourceNode = null);

        AddConditionRowCommand = new RelayCommand(ExecuteAddConditionRow);
        RemoveConditionRowCommand = new RelayCommand(p => ExecuteRemoveConditionRow(p as WorkflowConditionRowModel));

        SaveDraftCommand = new AsyncRelayCommand(SaveDraftAsync);
        PublishCommand = new AsyncRelayCommand(PublishAsync);
        RefreshDefinitionsCommand = new AsyncRelayCommand(LoadDefinitionsAsync);
        NewDefinitionCommand = new RelayCommand(ExecuteNewDefinition);

        _ = InitAsync();
    }

    public WorkbenchModel Workbench { get; }

    public ObservableCollection<WorkflowDefinitionItemModel> Definitions { get; } = new();
    public ObservableCollection<WorkflowNodeModel> Nodes { get; } = new();
    public ObservableCollection<WorkflowEdgeModel> Edges { get; } = new();

    public ObservableCollection<SimpleOptionItem> BoTypes { get; } = new();
    public ObservableCollection<SimpleOptionItem> AvailableBoFields { get; } = new();
    public ObservableCollection<SimpleOptionItem> UsersList { get; } = new();
    public ObservableCollection<SimpleOptionItem> RolesList { get; } = new();

    public ObservableCollection<string> ApprovalModes { get; } = new() { "SINGLE", "AND_SIGN", "OR_SIGN" };
    public ObservableCollection<string> AssigneeStrategies { get; } = new() { "FIXED_USER", "ROLE", "DEPT_MANAGER", "DIRECT_SUPERVISOR", "ESCALATE_N", "BO_EXPRESSION" };
    public ObservableCollection<string> Operators { get; } = new() { ">", ">=", "<", "<=", "==", "!=", "IN", "NOT_IN", "CONTAINS" };
    public ObservableCollection<string> Relations { get; } = new() { "AND", "OR" };

    public WorkflowDefinitionItemModel? SelectedDefinition
    {
        get => _selectedDefinition;
        set
        {
            if (SetField(ref _selectedDefinition, value) && value != null)
            {
                LoadGraphFromJson(value);
            }
        }
    }

    public WorkflowNodeModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (_selectedNode != null) _selectedNode.IsSelected = false;
            if (SetField(ref _selectedNode, value))
            {
                if (_selectedNode != null) _selectedNode.IsSelected = true;
                if (value != null && SelectedEdge != null) SelectedEdge = null;
                OnPropertyChanged(nameof(HasSelectedNode));
            }
        }
    }

    public bool HasSelectedNode => SelectedNode != null;

    public WorkflowEdgeModel? SelectedEdge
    {
        get => _selectedEdge;
        set
        {
            if (_selectedEdge != null) _selectedEdge.IsSelected = false;
            if (SetField(ref _selectedEdge, value))
            {
                if (_selectedEdge != null) _selectedEdge.IsSelected = true;
                if (value != null && SelectedNode != null) SelectedNode = null;
                OnPropertyChanged(nameof(HasSelectedEdge));
            }
        }
    }

    public bool HasSelectedEdge => SelectedEdge != null;

    public WorkflowNodeModel? ConnectingSourceNode
    {
        get => _connectingSourceNode;
        set
        {
            if (SetField(ref _connectingSourceNode, value))
            {
                OnPropertyChanged(nameof(IsConnectingMode));
            }
        }
    }

    public bool IsConnectingMode => ConnectingSourceNode != null;

    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    public string StatusNotice
    {
        get => _statusNotice;
        set => SetField(ref _statusNotice, value);
    }

    public string DefKey
    {
        get => _defKey;
        set => SetField(ref _defKey, value);
    }

    public string DefName
    {
        get => _defName;
        set => SetField(ref _defName, value);
    }

    public string SelectedBoType
    {
        get => _selectedBoType;
        set
        {
            if (SetField(ref _selectedBoType, value))
            {
                RefreshAvailableFieldsForBoType(value);
            }
        }
    }

    public string FallbackApproverId
    {
        get => _fallbackApproverId;
        set => SetField(ref _fallbackApproverId, value);
    }

    public ICommand AddNodeCommand { get; }
    public ICommand SelectNodeCommand { get; }
    public ICommand SelectEdgeCommand { get; }
    public ICommand DeleteSelectedCommand { get; }
    public ICommand StartConnectCommand { get; }
    public ICommand FinishConnectCommand { get; }
    public ICommand CancelConnectCommand { get; }
    public ICommand AddConditionRowCommand { get; }
    public ICommand RemoveConditionRowCommand { get; }
    public ICommand SaveDraftCommand { get; }
    public ICommand PublishCommand { get; }
    public ICommand RefreshDefinitionsCommand { get; }
    public ICommand NewDefinitionCommand { get; }

    public void MoveNodeByDelta(WorkflowNodeModel node, double deltaX, double deltaY)
    {
        if (node == null) return;
        node.X = Math.Max(10, node.X + deltaX);
        node.Y = Math.Max(10, node.Y + deltaY);
        UpdateConnectedEdges(node);
    }

    public void UpdateConnectedEdges(WorkflowNodeModel node)
    {
        foreach (var edge in Edges)
        {
            if (edge.SourceNodeId == node.Id)
            {
                edge.X1 = node.CenterX;
                edge.Y1 = node.CenterY;
            }
            if (edge.TargetNodeId == node.Id)
            {
                edge.X2 = node.CenterX;
                edge.Y2 = node.CenterY;
            }
        }
    }

    private void ExecuteAddNode(string type)
    {
        double startX = 60 + (Nodes.Count % 4) * 180;
        double startY = 80 + (Nodes.Count / 4) * 110;

        string name = type switch
        {
            "START" => "开始",
            "APPROVAL" => $"审批_{Nodes.Count + 1}",
            "EXCLUSIVE_GATEWAY" => "条件判断",
            "CC" => "抄送通知",
            "END" => "结束",
            _ => type
        };

        var node = new WorkflowNodeModel
        {
            Type = type,
            Name = name,
            X = startX,
            Y = startY,
            ApprovalMode = "SINGLE",
            AssigneeStrategy = type == "APPROVAL" ? "ROLE" : (type == "CC" ? "ROLE" : "NONE"),
            AssigneeConfig = type == "APPROVAL" ? "ROLE-DOCTOR-CHIEF" : "",
            FallbackApproverId = FallbackApproverId
        };

        Nodes.Add(node);
        SelectedNode = node;
        StatusNotice = $"已添加节点 [{node.Name}]";
    }

    private void ExecuteSelectNode(WorkflowNodeModel? node)
    {
        if (IsConnectingMode && node != null && node != ConnectingSourceNode)
        {
            ExecuteFinishConnect(node);
            return;
        }
        SelectedNode = node;
    }

    private void ExecuteSelectEdge(WorkflowEdgeModel? edge)
    {
        SelectedEdge = edge;
    }

    private void ExecuteStartConnect(WorkflowNodeModel? fromNode)
    {
        if (fromNode == null) return;
        ConnectingSourceNode = fromNode;
        StatusNotice = $"连线模式: 请点击目标节点完成连接...";
    }

    private void ExecuteFinishConnect(WorkflowNodeModel? toNode)
    {
        if (ConnectingSourceNode == null || toNode == null || ConnectingSourceNode.Id == toNode.Id)
        {
            ConnectingSourceNode = null;
            return;
        }

        // Avoid duplicate edge
        bool exists = Edges.Any(e => e.SourceNodeId == ConnectingSourceNode.Id && e.TargetNodeId == toNode.Id);
        if (!exists)
        {
            bool isGateway = ConnectingSourceNode.Type == "EXCLUSIVE_GATEWAY";
            bool isDefault = isGateway && !Edges.Any(e => e.SourceNodeId == ConnectingSourceNode.Id && e.IsDefault);

            var edge = new WorkflowEdgeModel
            {
                SourceNodeId = ConnectingSourceNode.Id,
                TargetNodeId = toNode.Id,
                X1 = ConnectingSourceNode.CenterX,
                Y1 = ConnectingSourceNode.CenterY,
                X2 = toNode.CenterX,
                Y2 = toNode.CenterY,
                IsDefault = isDefault
            };
            edge.UpdateLabel();
            Edges.Add(edge);
            SelectedEdge = edge;
            StatusNotice = $"成功建立连接: {ConnectingSourceNode.Name} ➔ {toNode.Name}";
        }

        ConnectingSourceNode = null;
    }

    private void ExecuteDeleteSelected()
    {
        if (SelectedNode != null)
        {
            string delId = SelectedNode.Id;
            Nodes.Remove(SelectedNode);
            // Remove connected edges
            var toRemove = Edges.Where(e => e.SourceNodeId == delId || e.TargetNodeId == delId).ToList();
            foreach (var e in toRemove) Edges.Remove(e);
            SelectedNode = null;
            StatusNotice = "已删除选中节点及其相连连线";
        }
        else if (SelectedEdge != null)
        {
            Edges.Remove(SelectedEdge);
            SelectedEdge = null;
            StatusNotice = "已删除选中连线";
        }
    }

    private void ExecuteAddConditionRow()
    {
        if (SelectedEdge == null) return;
        SelectedEdge.Conditions.Add(new WorkflowConditionRowModel
        {
            Field = AvailableBoFields.FirstOrDefault()?.Value ?? "amount",
            Op = ">",
            Value = "1000",
            Relation = "AND"
        });
        SelectedEdge.UpdateLabel();
    }

    private void ExecuteRemoveConditionRow(WorkflowConditionRowModel? row)
    {
        if (SelectedEdge == null || row == null) return;
        SelectedEdge.Conditions.Remove(row);
        SelectedEdge.UpdateLabel();
    }

    private void ExecuteNewDefinition()
    {
        SelectedDefinition = null;
        DefKey = "WF_" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        DefName = "新业务审批流程";
        Nodes.Clear();
        Edges.Clear();

        // Preset START and END
        var start = new WorkflowNodeModel { Type = "START", Name = "开始", X = 80, Y = 100 };
        var end = new WorkflowNodeModel { Type = "END", Name = "结束", X = 500, Y = 100 };
        Nodes.Add(start);
        Nodes.Add(end);
        StatusNotice = "已初始化空白流程定义画布";
    }

    private async Task InitAsync()
    {
        IsBusy = true;
        try
        {
            await LoadBoTypesAsync();
            await LoadUsersAndRolesAsync();
            await LoadDefinitionsAsync();
        }
        catch (Exception ex)
        {
            StatusNotice = "初始化加载失败: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LoadBoTypesAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.GetFromJsonAsync<ClientApiEnvelope<List<WorkflowBoTypeDto>>>("/api/v4/workflow/bo-types");
            if (res?.Code == 200 && res.Data != null)
            {
                BoTypes.Clear();
                foreach (var b in res.Data)
                {
                    BoTypes.Add(new SimpleOptionItem { Value = b.BoType, DisplayText = $"{b.BoTypeName} ({b.BoType})" });
                }
                RefreshAvailableFieldsForBoType(SelectedBoType);
            }
        }
        catch
        {
            // Seed defaults if offline
            if (BoTypes.Count == 0)
            {
                BoTypes.Add(new SimpleOptionItem { Value = "CSM_REQUISITION", DisplayText = "耗材请领审批 (CSM_REQUISITION)" });
                BoTypes.Add(new SimpleOptionItem { Value = "BLOOD_REQUEST", DisplayText = "临床用血审批 (BLOOD_REQUEST)" });
                BoTypes.Add(new SimpleOptionItem { Value = "EMR_ACCESS_GRANT", DisplayText = "病历跨科访问授权 (EMR_ACCESS_GRANT)" });
                BoTypes.Add(new SimpleOptionItem { Value = "INV_TRANSFER", DisplayText = "药品调拨审批 (INV_TRANSFER)" });
                BoTypes.Add(new SimpleOptionItem { Value = "INV_STOCK_TAKE", DisplayText = "药品盘点调整 (INV_STOCK_TAKE)" });
                BoTypes.Add(new SimpleOptionItem { Value = "SURGERY_SCHEDULE", DisplayText = "重大手术排程 (SURGERY_SCHEDULE)" });
                BoTypes.Add(new SimpleOptionItem { Value = "ORD_SPECIAL_DRUG", DisplayText = "特殊级抗生素审批 (ORD_SPECIAL_DRUG)" });
                BoTypes.Add(new SimpleOptionItem { Value = "IPD_DISCHARGE_REQUEST", DisplayText = "特殊出院申请审批 (IPD_DISCHARGE_REQUEST)" });
                RefreshAvailableFieldsForBoType(SelectedBoType);
            }
        }
    }

    private void RefreshAvailableFieldsForBoType(string boType)
    {
        AvailableBoFields.Clear();
        switch (boType)
        {
            case "CSM_REQUISITION":
                AvailableBoFields.Add(new SimpleOptionItem { Value = "total_amount", DisplayText = "请领总金额 (元)" });
                AvailableBoFields.Add(new SimpleOptionItem { Value = "has_high_value", DisplayText = "含高值耗材 (0/1)" });
                AvailableBoFields.Add(new SimpleOptionItem { Value = "item_count", DisplayText = "品规数量" });
                break;
            case "BLOOD_REQUEST":
                AvailableBoFields.Add(new SimpleOptionItem { Value = "volume_ml", DisplayText = "申请用血量 (ml)" });
                AvailableBoFields.Add(new SimpleOptionItem { Value = "urgency", DisplayText = "紧急度 (URGENT/ROUTINE)" });
                AvailableBoFields.Add(new SimpleOptionItem { Value = "blood_product_code", DisplayText = "血液品种代码" });
                break;
            case "EMR_ACCESS_GRANT":
                AvailableBoFields.Add(new SimpleOptionItem { Value = "is_external", DisplayText = "是否外院跨机构 (0/1)" });
                AvailableBoFields.Add(new SimpleOptionItem { Value = "duration_hours", DisplayText = "授权访问时长 (小时)" });
                break;
            default:
                AvailableBoFields.Add(new SimpleOptionItem { Value = "amount", DisplayText = "涉及金额" });
                AvailableBoFields.Add(new SimpleOptionItem { Value = "level", DisplayText = "等级" });
                break;
        }
    }

    public async Task LoadUsersAndRolesAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var usersRes = await http.GetFromJsonAsync<ClientApiEnvelope<List<UserAdminItemModel>>>("/api/v4/platform/users");
            if (usersRes?.Code == 200 && usersRes.Data != null)
            {
                UsersList.Clear();
                foreach (var u in usersRes.Data)
                {
                    UsersList.Add(new SimpleOptionItem { Value = u.UserId, DisplayText = $"{u.DisplayName} ({u.UserId})" });
                }
            }
        }
        catch
        {
            if (UsersList.Count == 0)
            {
                UsersList.Add(new SimpleOptionItem { Value = "USR-ADMIN", DisplayText = "系统管理员 (USR-ADMIN)" });
                UsersList.Add(new SimpleOptionItem { Value = "DOC-CHIEF-01", DisplayText = "王主任医师 (DOC-CHIEF-01)" });
                UsersList.Add(new SimpleOptionItem { Value = "DOC-VP-01", DisplayText = "李业务副院长 (DOC-VP-01)" });
            }
        }

        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var rolesRes = await http.GetFromJsonAsync<ClientApiEnvelope<List<RoleAdminItemModel>>>("/api/v4/platform/roles");
            if (rolesRes?.Code == 200 && rolesRes.Data != null)
            {
                RolesList.Clear();
                foreach (var r in rolesRes.Data)
                {
                    RolesList.Add(new SimpleOptionItem { Value = r.RoleCode, DisplayText = $"{r.RoleName} ({r.RoleCode})" });
                }
            }
        }
        catch
        {
            if (RolesList.Count == 0)
            {
                RolesList.Add(new SimpleOptionItem { Value = "ROLE-DOCTOR-CHIEF", DisplayText = "主任医师 (ROLE-DOCTOR-CHIEF)" });
                RolesList.Add(new SimpleOptionItem { Value = "ROLE-DEPT-DIRECTOR", DisplayText = "科室负责人 (ROLE-DEPT-DIRECTOR)" });
                RolesList.Add(new SimpleOptionItem { Value = "ROLE-MED-ADMIN", DisplayText = "医务处干事 (ROLE-MED-ADMIN)" });
                RolesList.Add(new SimpleOptionItem { Value = "ROLE-HOSPITAL-LEADER", DisplayText = "院领导 (ROLE-HOSPITAL-LEADER)" });
            }
        }
    }

    public async Task LoadDefinitionsAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.GetFromJsonAsync<ClientApiEnvelope<List<WorkflowDefinitionSummaryDto>>>("/api/v4/workflow/definitions");
            if (res?.Code == 200 && res.Data != null && res.Data.Count > 0)
            {
                Definitions.Clear();
                foreach (var d in res.Data)
                {
                    Definitions.Add(new WorkflowDefinitionItemModel
                    {
                        Id = d.Id,
                        DefKey = d.DefKey,
                        DefName = d.DefName,
                        BoType = d.BoType,
                        VersionNum = d.VersionNum,
                        Status = d.Status,
                        FallbackApproverId = d.FallbackApproverId
                    });
                }
                if (SelectedDefinition == null)
                {
                    SelectedDefinition = Definitions.FirstOrDefault(d => d.DefKey == "WF_DEF_CSM_REQUISITION") ?? Definitions[0];
                }
                return;
            }
        }
        catch (Exception ex)
        {
            StatusNotice = "加载流程定义失败: " + ex.Message;
        }

        if (Definitions.Count == 0)
        {
            SeedLocalDefaultWorkflows();
        }
    }

    private void SeedLocalDefaultWorkflows()
    {
        Definitions.Clear();
        Definitions.Add(new WorkflowDefinitionItemModel
        {
            Id = "WF-DEF-CSM-01",
            DefKey = "WF_DEF_CSM_REQUISITION",
            DefName = "科室耗材领用审批流",
            BoType = "CSM_REQUISITION",
            VersionNum = 1,
            Status = "PUBLISHED",
            FallbackApproverId = "USR-ADMIN"
        });
        Definitions.Add(new WorkflowDefinitionItemModel
        {
            Id = "WF-DEF-BLOOD-01",
            DefKey = "WF_DEF_BLOOD_REQUEST",
            DefName = "临床用血分级审批流",
            BoType = "BLOOD_REQUEST",
            VersionNum = 1,
            Status = "PUBLISHED",
            FallbackApproverId = "USR-ADMIN"
        });
        Definitions.Add(new WorkflowDefinitionItemModel
        {
            Id = "WF-DEF-EMR-01",
            DefKey = "WF_DEF_EMR_ACCESS_GRANT",
            DefName = "病历跨科与外院调阅审批",
            BoType = "EMR_ACCESS_GRANT",
            VersionNum = 1,
            Status = "PUBLISHED",
            FallbackApproverId = "USR-ADMIN"
        });

        SelectedDefinition = Definitions[0];
    }

    private void LoadGraphFromJson(WorkflowDefinitionItemModel def)
    {
        DefKey = def.DefKey;
        DefName = def.DefName;
        SelectedBoType = def.BoType;
        FallbackApproverId = def.FallbackApproverId ?? "USR-ADMIN";

        _ = LoadFullDefinitionDetailsAsync(def.Id);
    }

    private async Task LoadFullDefinitionDetailsAsync(string defId)
    {
        IsBusy = true;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.GetFromJsonAsync<ClientApiEnvelope<WorkflowDefinitionDetailDto>>($"/api/v4/workflow/definitions/{defId}");
            if (res?.Code == 200 && res.Data != null && !string.IsNullOrWhiteSpace(res.Data.GraphJson))
            {
                DeserializeGraph(res.Data.GraphJson);
                StatusNotice = $"已加载流程定义 [{res.Data.DefName} v{res.Data.VersionNum}]";
                return;
            }
        }
        catch (Exception ex)
        {
            StatusNotice = "加载流程图详情失败: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }

        LoadLocalDefaultGraph(defId);
    }

    private void LoadLocalDefaultGraph(string defId)
    {
        Nodes.Clear();
        Edges.Clear();

        var startNode = new WorkflowNodeModel { Type = "START", Name = "开始", X = 80, Y = 160 };
        var deptApprove = new WorkflowNodeModel { Type = "APPROVAL", Name = "科室负责人审批", X = 280, Y = 160, ApprovalMode = "SINGLE", AssigneeStrategy = "DEPT_MANAGER", AssigneeConfig = "DEPT-MANAGER" };
        var directorApprove = new WorkflowNodeModel { Type = "APPROVAL", Name = "业务部长会签", X = 500, Y = 160, ApprovalMode = "AND_SIGN", AssigneeStrategy = "ROLE", AssigneeConfig = "ROLE-DIRECTOR" };
        var endNode = new WorkflowNodeModel { Type = "END", Name = "结束归档", X = 720, Y = 160 };

        Nodes.Add(startNode);
        Nodes.Add(deptApprove);
        Nodes.Add(directorApprove);
        Nodes.Add(endNode);

        var e1 = new WorkflowEdgeModel { SourceNodeId = startNode.Id, TargetNodeId = deptApprove.Id, Label = "提交", X1 = startNode.CenterX, Y1 = startNode.CenterY, X2 = deptApprove.CenterX, Y2 = deptApprove.CenterY };
        var e2 = new WorkflowEdgeModel { SourceNodeId = deptApprove.Id, TargetNodeId = directorApprove.Id, Label = "同意", X1 = deptApprove.CenterX, Y1 = deptApprove.CenterY, X2 = directorApprove.CenterX, Y2 = directorApprove.CenterY };
        var e3 = new WorkflowEdgeModel { SourceNodeId = directorApprove.Id, TargetNodeId = endNode.Id, Label = "核准", X1 = directorApprove.CenterX, Y1 = directorApprove.CenterY, X2 = endNode.CenterX, Y2 = endNode.CenterY };

        Edges.Add(e1);
        Edges.Add(e2);
        Edges.Add(e3);

        StatusNotice = $"已加载流程定义标准图谱（4个节点，3条流向）";
    }

    private void DeserializeGraph(string? graphJson)
    {
        Nodes.Clear();
        Edges.Clear();
        if (string.IsNullOrWhiteSpace(graphJson)) return;

        try
        {
            var graph = JsonSerializer.Deserialize<WorkflowGraphDto>(graphJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (graph == null) return;

            foreach (var n in graph.Nodes)
            {
                Nodes.Add(new WorkflowNodeModel
                {
                    Id = n.NodeId,
                    Type = n.NodeType,
                    Name = n.NodeName,
                    X = n.X > 0 ? n.X : 80,
                    Y = n.Y > 0 ? n.Y : 80,
                    ApprovalMode = n.ApprovalMode ?? "SINGLE",
                    AssigneeStrategy = n.AssigneeConfig?.Strategy ?? n.AssigneeStrategy ?? "ROLE",
                    AssigneeConfig = n.AssigneeConfig?.RoleCode ?? (n.AssigneeConfig?.FixedUserIds != null ? string.Join(",", n.AssigneeConfig.FixedUserIds) : (n.AssigneeConfig?.BoExpression ?? "")),
                    FallbackApproverId = n.FallbackApproverId ?? FallbackApproverId,
                    EscalateLevels = n.AssigneeConfig?.EscalateLevels ?? n.EscalateLevels ?? 1
                });
            }

            foreach (var e in graph.Edges)
            {
                var src = Nodes.FirstOrDefault(n => n.Id == e.SourceNodeId);
                var tgt = Nodes.FirstOrDefault(n => n.Id == e.TargetNodeId);
                if (src == null || tgt == null) continue;

                var edge = new WorkflowEdgeModel
                {
                    Id = e.EdgeId,
                    SourceNodeId = e.SourceNodeId,
                    TargetNodeId = e.TargetNodeId,
                    X1 = src.CenterX,
                    Y1 = src.CenterY,
                    X2 = tgt.CenterX,
                    Y2 = tgt.CenterY,
                    IsDefault = e.IsDefault
                };

                if (e.Condition?.Conditions != null)
                {
                    foreach (var c in e.Condition.Conditions)
                    {
                        edge.Conditions.Add(new WorkflowConditionRowModel
                        {
                            Field = c.Field,
                            Op = c.Operator,
                            Value = c.Value?.ToString() ?? "",
                            Relation = c.LogicalRelation ?? "AND"
                        });
                    }
                }
                edge.UpdateLabel();
                Edges.Add(edge);
            }
        }
        catch (Exception ex)
        {
            StatusNotice = "反序列化流程图异常: " + ex.Message;
        }
    }

    private string SerializeGraph()
    {
        var graph = new WorkflowGraphDto
        {
            Nodes = Nodes.Select(n => new WorkflowNodeDto
            {
                NodeId = n.Id,
                NodeType = n.Type,
                NodeName = n.Name,
                X = n.X,
                Y = n.Y,
                ApprovalMode = n.ApprovalMode,
                AssigneeConfig = new AssigneeConfigDto
                {
                    Strategy = n.AssigneeStrategy,
                    RoleCode = n.AssigneeStrategy == "ROLE" ? n.AssigneeConfig : null,
                    FixedUserIds = n.AssigneeStrategy == "FIXED_USER" ? n.AssigneeConfig.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList() : null,
                    BoExpression = n.AssigneeStrategy == "BO_EXPRESSION" ? n.AssigneeConfig : null,
                    EscalateLevels = n.EscalateLevels
                },
                FallbackApproverId = n.FallbackApproverId
            }).ToList(),
            Edges = Edges.Select(e => new WorkflowEdgeDto
            {
                EdgeId = e.Id,
                SourceNodeId = e.SourceNodeId,
                TargetNodeId = e.TargetNodeId,
                IsDefault = e.IsDefault,
                Condition = e.Conditions.Count > 0 ? new WorkflowConditionGroupDto
                {
                    LogicalOperator = "AND",
                    Conditions = e.Conditions.Select(c => new WorkflowConditionRuleDto
                    {
                        Field = c.Field,
                        Operator = c.Op,
                        Value = c.Value ?? "",
                        LogicalRelation = c.Relation
                    }).ToList()
                } : null
            }).ToList()
        };

        return JsonSerializer.Serialize(graph, new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task SaveDraftAsync()
    {
        IsBusy = true;
        StatusNotice = "正在保存流程图草稿到数据库...";
        try
        {
            string graphJson = SerializeGraph();
            var payload = new
            {
                DefKey = DefKey.Trim(),
                DefName = DefName.Trim(),
                BoType = SelectedBoType,
                GraphJson = graphJson,
                FallbackApproverId = FallbackApproverId
            };

            using var http = AuthenticatedHttpClientFactory.Create();
            var response = await http.PostAsJsonAsync("/api/v4/workflow/definitions", payload);
            var result = await response.Content.ReadFromJsonAsync<ClientApiEnvelope<WorkflowDefinitionDetailDto>>();
            if (result?.Code == 200 && result.Data != null)
            {
                _shell?.ShowToast($"流程草稿 [{result.Data.DefName}] 保存成功！", "Success");
                StatusNotice = $"流程草稿 [{result.Data.DefName}] 成功保存至 SQLite (版本 v{result.Data.VersionNum})";
                await LoadDefinitionsAsync();
            }
            else
            {
                _shell?.ShowToast(result?.Message ?? "保存失败", "Danger");
                StatusNotice = "保存失败: " + result?.Message;
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast("保存异常: " + ex.Message, "Danger");
            StatusNotice = "保存异常: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task PublishAsync()
    {
        if (SelectedDefinition == null)
        {
            await SaveDraftAsync();
        }

        if (SelectedDefinition == null)
        {
            _shell?.ShowToast("请先选择或保存流程草稿后再发布", "Warning");
            return;
        }

        IsBusy = true;
        StatusNotice = $"正在发布流程定义 [{SelectedDefinition.DefName}] 并执行强校验...";
        try
        {
            // First save latest graph changes
            await SaveDraftAsync();

            using var http = AuthenticatedHttpClientFactory.Create();
            var response = await http.PostAsync($"/api/v4/workflow/definitions/{SelectedDefinition.Id}/publish", null);
            var result = await response.Content.ReadFromJsonAsync<ClientApiEnvelope<WorkflowDefinitionDetailDto>>();
            if (response.IsSuccessStatusCode && result?.Code == 200 && result.Data != null)
            {
                _shell?.ShowToast($"流程发布成功！新版本 v{result.Data.VersionNum} 已生效", "Success");
                StatusNotice = $"流程发布成功！版本递增至 v{result.Data.VersionNum}，已归档旧版本";
                await LoadDefinitionsAsync();
            }
            else
            {
                string errMsg = result?.Message ?? "流程图校验失败，请检查默认兜底分支与节点连线";
                _shell?.ShowToast(errMsg, "Danger");
                StatusNotice = "发布被拦截: " + errMsg;
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast("发布异常: " + ex.Message, "Danger");
            StatusNotice = "发布异常: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public bool OnShortcut(string keyGesture)
    {
        if (keyGesture == "Ctrl+S")
        {
            _ = SaveDraftAsync();
            return true;
        }
        if (keyGesture == "F5")
        {
            _ = LoadDefinitionsAsync();
            return true;
        }
        return false;
    }
}
