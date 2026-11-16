using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.Admin;

public sealed class RoleAdminStationViewModel : ClientModelBase, IShortcutTarget
{
    private static readonly Regex PermissionKeyPattern =
        new("^[A-Za-z][A-Za-z0-9_.-]{2,159}$", RegexOptions.Compiled);

    private readonly ShellViewModel? _shell;
    private readonly List<PermissionMatrixItemModel> _rolePermissions = new();
    private RoleAdminItemModel? _selectedRole;
    private string _resourceFilter = string.Empty;
    private bool _isLoadingPermissions;
    private bool _subscribedRoles;
    private bool _suppressPermissionLoad;
    private string? _loadedRoleId;
    private int _loadRevision;

    public RoleAdminStationViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        OpenCreateRoleDrawerCommand = new RelayCommand(OpenCreateRoleDrawer);
        AddPermissionCommand = new AsyncRelayCommand(OpenAddPermissionDrawerAsync, () => HasSelectedRole);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        EditRoleCommand = new AsyncRelayCommand(ExecuteEditRoleAsync);
        SetFullAccessCommand = new AsyncRelayCommand(
            p => SetPolicyAsync(p as PermissionMatrixItemModel, 3),
            p => CanSavePolicy(p as PermissionMatrixItemModel));
        SetReadOnlyCommand = new AsyncRelayCommand(
            p => SetPolicyAsync(p as PermissionMatrixItemModel, 2),
            p => CanSavePolicy(p as PermissionMatrixItemModel));
        SetMaskedCommand = new AsyncRelayCommand(
            p => SetPolicyAsync(p as PermissionMatrixItemModel, 1),
            p => CanSavePolicy(p as PermissionMatrixItemModel));
        SetHiddenCommand = new AsyncRelayCommand(
            p => SetPolicyAsync(p as PermissionMatrixItemModel, 0),
            p => CanSavePolicy(p as PermissionMatrixItemModel));
        BulkFullAccessCommand = new RelayCommand(
            () => ConfirmBulkPolicy(3, "完全授权"),
            () => FilteredPermissions.Count > 0);
        BulkReadOnlyCommand = new RelayCommand(
            () => ConfirmBulkPolicy(2, "只读"),
            () => FilteredPermissions.Count > 0);
        BulkHiddenCommand = new RelayCommand(
            () => ConfirmBulkPolicy(0, "仅隐藏"),
            () => FilteredPermissions.Count > 0);

        RebuildFilteredPermissions();
    }

    public WorkbenchModel Workbench { get; }
    public ObservableCollection<PermissionMatrixItemModel> FilteredPermissions { get; } = new();
    public Action? FocusSearchAction { get; set; }

    public RoleAdminItemModel? SelectedRole
    {
        get => _selectedRole;
        set
        {
            if (!SetField(ref _selectedRole, value))
            {
                return;
            }

            RaiseRoleProperties();
            _loadedRoleId = null;
            if (!_suppressPermissionLoad)
            {
                _ = LoadSelectedRolePermissionsAsync();
            }
        }
    }

    public string ResourceFilter
    {
        get => _resourceFilter;
        set
        {
            if (SetField(ref _resourceFilter, value))
            {
                RebuildFilteredPermissions();
            }
        }
    }

    public bool IsLoadingPermissions
    {
        get => _isLoadingPermissions;
        private set
        {
            if (SetField(ref _isLoadingPermissions, value))
            {
                OnPropertyChanged(nameof(IsPermissionTableVisible));
            }
        }
    }

    public bool HasSelectedRole => SelectedRole != null;
    public bool HasRolePermissions => _rolePermissions.Count > 0;
    public bool HasFilteredPermissions => FilteredPermissions.Count > 0;
    public bool IsPermissionTableVisible => HasSelectedRole && !IsLoadingPermissions;
    public bool IsFilteredEmpty => HasSelectedRole && !IsLoadingPermissions && !HasFilteredPermissions;
    public string PermissionCountText => ResourceFilter.Length == 0
        ? $"{_rolePermissions.Count} 项权限"
        : $"{FilteredPermissions.Count} / {_rolePermissions.Count} 项权限";
    public string SelectedRoleName => SelectedRole?.RoleName ?? "未选择角色";
    public string SelectedRoleCode => SelectedRole?.RoleCode ?? string.Empty;
    public string SelectedRoleScope => SelectedRole?.ScopeDescription ?? string.Empty;
    public string SelectedRoleStatus => SelectedRole?.StatusText ?? string.Empty;
    public string SelectedRoleStatusClass => SelectedRole?.StatusClass ?? "Neutral";
    public string EmptyStateTitle => HasRolePermissions ? "没有匹配的权限" : "该角色尚未配置权限";
    public string EmptyStateDescription => HasRolePermissions
        ? "调整搜索条件，或清除搜索查看该角色的全部权限。"
        : "新增真实资源键并选择授权级别，保存后会写入当前角色。";

    public ICommand OpenCreateRoleDrawerCommand { get; }
    public ICommand AddPermissionCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand EditRoleCommand { get; }
    public ICommand SetFullAccessCommand { get; }
    public ICommand SetReadOnlyCommand { get; }
    public ICommand SetMaskedCommand { get; }
    public ICommand SetHiddenCommand { get; }
    public ICommand BulkFullAccessCommand { get; }
    public ICommand BulkReadOnlyCommand { get; }
    public ICommand BulkHiddenCommand { get; }

    public void Attach()
    {
        if (!_subscribedRoles)
        {
            Workbench.RolesList.CollectionChanged += OnRolesChanged;
            _subscribedRoles = true;
        }

        EnsureSelectedRole();
        if (SelectedRole != null
            && !IsLoadingPermissions
            && !string.Equals(_loadedRoleId, SelectedRole.RoleId, StringComparison.OrdinalIgnoreCase))
        {
            _ = LoadSelectedRolePermissionsAsync();
        }
    }

    public void Detach()
    {
        if (!_subscribedRoles)
        {
            return;
        }

        Workbench.RolesList.CollectionChanged -= OnRolesChanged;
        _subscribedRoles = false;
    }

    public bool OnShortcut(string keyGesture)
    {
        switch (keyGesture)
        {
            case "F2":
                OpenCreateRoleDrawer();
                return true;
            case "F3":
                if (AddPermissionCommand.CanExecute(null))
                {
                    AddPermissionCommand.Execute(null);
                }
                return true;
            case "F5":
                RefreshCommand.Execute(null);
                return true;
            case "Ctrl+F":
                FocusSearchAction?.Invoke();
                return true;
            default:
                return false;
        }
    }

    private void OnRolesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (SelectedRole != null && !Workbench.RolesList.Contains(SelectedRole))
        {
            SelectedRole = null;
        }
        EnsureSelectedRole();
    }

    private void EnsureSelectedRole()
    {
        if (SelectedRole != null && Workbench.RolesList.Contains(SelectedRole))
        {
            return;
        }

        SelectedRole = Workbench.RolesList.FirstOrDefault(x =>
            string.Equals(x.RoleCode, "ADMIN", StringComparison.OrdinalIgnoreCase))
            ?? Workbench.RolesList.FirstOrDefault();
    }

    private async Task LoadSelectedRolePermissionsAsync()
    {
        RoleAdminItemModel? role = SelectedRole;
        int revision = Interlocked.Increment(ref _loadRevision);
        _rolePermissions.Clear();
        RebuildFilteredPermissions();

        if (role == null)
        {
            IsLoadingPermissions = false;
            RaisePermissionProperties();
            return;
        }

        IsLoadingPermissions = true;
        var loaded = new List<PermissionMatrixItemModel>();
        bool ok = await Workbench.LoadRolePermissionsAsync(role.RoleId, loaded);
        if (revision != _loadRevision || !ReferenceEquals(role, SelectedRole))
        {
            return;
        }

        if (ok)
        {
            _rolePermissions.AddRange(loaded);
            _loadedRoleId = role.RoleId;
        }
        else
        {
            _loadedRoleId = null;
        }

        IsLoadingPermissions = false;
        RebuildFilteredPermissions();
        if (!ok)
        {
            _shell?.ShowToast(Workbench.SystemNotice, "Warning");
        }
    }

    private void RebuildFilteredPermissions()
    {
        FilteredPermissions.Clear();
        string keyword = (ResourceFilter ?? string.Empty).Trim();
        foreach (PermissionMatrixItemModel item in _rolePermissions)
        {
            if (keyword.Length > 0
                && !item.ResourceKey.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                && !item.ResourceName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                && !item.PolicyLabel.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            FilteredPermissions.Add(item);
        }

        RaisePermissionProperties();
    }

    private void RaisePermissionProperties()
    {
        OnPropertyChanged(nameof(HasRolePermissions));
        OnPropertyChanged(nameof(HasFilteredPermissions));
        OnPropertyChanged(nameof(IsPermissionTableVisible));
        OnPropertyChanged(nameof(IsFilteredEmpty));
        OnPropertyChanged(nameof(PermissionCountText));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateDescription));
        (BulkFullAccessCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (BulkReadOnlyCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (BulkHiddenCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void RaiseRoleProperties()
    {
        OnPropertyChanged(nameof(HasSelectedRole));
        OnPropertyChanged(nameof(IsPermissionTableVisible));
        OnPropertyChanged(nameof(IsFilteredEmpty));
        OnPropertyChanged(nameof(SelectedRoleName));
        OnPropertyChanged(nameof(SelectedRoleCode));
        OnPropertyChanged(nameof(SelectedRoleScope));
        OnPropertyChanged(nameof(SelectedRoleStatus));
        OnPropertyChanged(nameof(SelectedRoleStatusClass));
        (AddPermissionCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private bool CanSavePolicy(PermissionMatrixItemModel? item)
    {
        return HasSelectedRole && item != null && !IsLoadingPermissions;
    }

    private async Task SetPolicyAsync(PermissionMatrixItemModel? item, int policy)
    {
        if (!CanSavePolicy(item) || item!.AccessPolicy == policy)
        {
            return;
        }

        bool saved = await Workbench.SaveRolePermissionPolicyAsync(SelectedRole!.RoleId, item, policy);
        _shell?.ShowToast(Workbench.SystemNotice, saved ? "Success" : "Warning");
    }

    private void ConfirmBulkPolicy(int policy, string policyLabel)
    {
        RoleAdminItemModel? role = SelectedRole;
        if (role == null || FilteredPermissions.Count == 0)
        {
            return;
        }

        int count = FilteredPermissions.Count;
        string scopeText = ResourceFilter.Length == 0 ? "当前角色的全部权限" : "当前筛选结果";
        _shell?.RequestConfirm(
            "批量调整授权级别",
            $"将{scopeText}（{count} 项）统一设为“{policyLabel}”。此操作会写入角色权限配置。",
            $"设为{policyLabel}",
            () => _ = BulkSetPolicyAsync(role, policy));
    }

    private async Task BulkSetPolicyAsync(RoleAdminItemModel role, int policy)
    {
        List<PermissionMatrixItemModel> items = FilteredPermissions.ToList();
        bool saved = await Workbench.SaveRolePermissionPoliciesAsync(role.RoleId, items, policy);
        _shell?.ShowToast(Workbench.SystemNotice, saved ? "Success" : "Warning");
    }

    private async Task RefreshAsync()
    {
        RoleAdminItemModel? previous = SelectedRole;
        await Workbench.ReloadAdminDataAsync();
        SelectRoleWithoutLoading(
            Workbench.RolesList.FirstOrDefault(x =>
                string.Equals(x.RoleId, previous?.RoleId, StringComparison.OrdinalIgnoreCase))
            ?? Workbench.RolesList.FirstOrDefault(x =>
                string.Equals(x.RoleCode, "ADMIN", StringComparison.OrdinalIgnoreCase))
            ?? Workbench.RolesList.FirstOrDefault());
        await LoadSelectedRolePermissionsAsync();
        _shell?.ShowToast("角色清单与权限配置已刷新。", "Info");
    }

    private void SelectRoleWithoutLoading(RoleAdminItemModel? role)
    {
        _suppressPermissionLoad = true;
        try
        {
            SelectedRole = role;
        }
        finally
        {
            _suppressPermissionLoad = false;
        }
    }

    private Task ExecuteEditRoleAsync(object? parameter)
    {
        if (parameter is RoleAdminItemModel role)
        {
            OpenEditRoleDrawer(role);
        }
        return Task.CompletedTask;
    }

    private void OpenCreateRoleDrawer()
    {
        Workbench.NewRoleForm.Reset();
        var form = Workbench.NewRoleForm;
        var panel = new StackPanel { Spacing = 10 };

        var codeBox = new TextBox { PlaceholderText = "如 QUALITY_CTRL", Text = form.RoleCode };
        codeBox.TextChanged += (_, _) => form.RoleCode = codeBox.Text ?? string.Empty;

        var nameBox = new TextBox { PlaceholderText = "如 病案质控员", Text = form.RoleName };
        nameBox.TextChanged += (_, _) => form.RoleName = nameBox.Text ?? string.Empty;

        var scopeItems = new[]
        {
            new ScopeOption("ALL_CAMPUS", "全院"),
            new ScopeOption("DEPT_ONLY", "所属科室")
        };
        var scopeCombo = new ComboBox
        {
            ItemsSource = scopeItems,
            SelectedItem = scopeItems.First(x => x.Value == form.DataScope),
            DisplayMemberBinding = new Avalonia.Data.Binding(nameof(ScopeOption.Label)),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        scopeCombo.SelectionChanged += (_, _) =>
        {
            if (scopeCombo.SelectedItem is ScopeOption option) form.DataScope = option.Value;
        };

        panel.Children.Add(Labeled("角色编码 *", codeBox));
        panel.Children.Add(Labeled("角色名称 *", nameBox));
        panel.Children.Add(Labeled("数据范围 *", scopeCombo));

        var saveBtn = new Button
        {
            Content = "保存角色",
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Command = new AsyncRelayCommand(async () =>
            {
                string roleCode = form.RoleCode;
                bool saved = await Workbench.SaveNewRoleAsync();
                _shell?.ShowToast(Workbench.SystemNotice, saved ? "Success" : "Warning");
                if (!saved)
                {
                    return;
                }

                _shell?.CloseDrawer();
                SelectedRole = Workbench.RolesList.FirstOrDefault(x =>
                                   string.Equals(x.RoleCode, roleCode, StringComparison.OrdinalIgnoreCase))
                               ?? Workbench.RolesList.FirstOrDefault();
            })
        };
        saveBtn.Classes.Add("Primary");
        panel.Children.Add(saveBtn);

        _shell?.OpenDrawer("新建角色", "创建后可继续配置该角色的资源级权限", panel, 460);
    }

    private void OpenEditRoleDrawer(RoleAdminItemModel role)
    {
        var panel = new StackPanel { Spacing = 10 };
        var nameBox = new TextBox { Text = role.RoleName };
        var scopeItems = new[]
        {
            new ScopeOption("ALL_CAMPUS", "全院"),
            new ScopeOption("OPD_DEPT_SCOPE", "门诊科室"),
            new ScopeOption("IPD_DEPT_AND_BORROWED", "住院科室及借床病区"),
            new ScopeOption("NURSING_WARD_SCOPE", "护理病区"),
            new ScopeOption("MEDTECH_EXEC_SCOPE", "医技执行科室"),
            new ScopeOption("PHARMACY_SCOPE", "药房与药库"),
            new ScopeOption("DEPT_ONLY", "所属科室")
        };
        var scopeCombo = new ComboBox
        {
            ItemsSource = scopeItems,
            SelectedItem = scopeItems.FirstOrDefault(x => x.Value == role.DataScope) ?? scopeItems[0],
            DisplayMemberBinding = new Avalonia.Data.Binding(nameof(ScopeOption.Label)),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var activeCheck = new CheckBox { Content = "允许登录并启用此角色", IsChecked = role.IsActive };
        panel.Children.Add(Labeled("角色编码", new TextBox { Text = role.RoleCode, IsReadOnly = true }));
        panel.Children.Add(Labeled("角色名称 *", nameBox));
        panel.Children.Add(Labeled("数据范围 *", scopeCombo));
        panel.Children.Add(activeCheck);

        var saveBtn = new Button
        {
            Content = "保存修改",
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Command = new AsyncRelayCommand(async () =>
            {
                string name = nameBox.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    _shell?.ShowToast("角色名称不能为空。", "Warning");
                    return;
                }

                string scope = (scopeCombo.SelectedItem as ScopeOption)?.Value ?? role.DataScope;
                bool active = activeCheck.IsChecked == true;
                bool saved = await Workbench.UpdateRoleAsync(role, name, scope, active);
                _shell?.ShowToast(Workbench.SystemNotice, saved ? "Success" : "Warning");
                if (saved)
                {
                    _shell?.CloseDrawer();
                    RaiseRoleProperties();
                }
            })
        };
        saveBtn.Classes.Add("Primary");
        panel.Children.Add(saveBtn);
        _shell?.OpenDrawer("编辑角色", role.RoleName, panel, 460);
    }

    private async Task OpenAddPermissionDrawerAsync()
    {
        RoleAdminItemModel? role = SelectedRole;
        if (role == null)
        {
            _shell?.ShowToast("请先选择需要配置权限的角色。", "Warning");
            return;
        }

        var panel = new StackPanel { Spacing = 12 };
        var keyBox = new TextBox
        {
            PlaceholderText = "如 OPD.ConsultView.BtnCancelVisit",
            FontFamily = new FontFamily("Consolas")
        };
        var policyItems = new[]
        {
            new PolicyOption(3, "完全授权"),
            new PolicyOption(2, "只读"),
            new PolicyOption(1, "脱敏"),
            new PolicyOption(0, "仅隐藏")
        };
        var policyCombo = new ComboBox
        {
            ItemsSource = policyItems,
            SelectedIndex = 0,
            DisplayMemberBinding = new Avalonia.Data.Binding(nameof(PolicyOption.Label)),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var errorText = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };

        panel.Children.Add(new TextBlock
        {
            Text = $"角色：{role.RoleName} ({role.RoleCode})",
            FontSize = 12,
            FontWeight = FontWeight.SemiBold
        });
        panel.Children.Add(Labeled("资源键 *", keyBox));
        panel.Children.Add(Labeled("授权级别 *", policyCombo));
        panel.Children.Add(new TextBlock
        {
            Text = "资源键需与客户端实际检查的权限键一致。已有同名键会提示重复，不会覆盖现有配置。",
            FontSize = 12,
            Foreground = ThemeBrush("SemiColorText2"),
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(errorText);

        var saveBtn = new Button
        {
            Content = "添加并保存",
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Command = new AsyncRelayCommand(async () =>
            {
                string key = keyBox.Text?.Trim() ?? string.Empty;
                string? error = ValidateNewPermissionKey(key);
                if (error != null)
                {
                    errorText.Text = error;
                    errorText.IsVisible = true;
                    return;
                }

                errorText.IsVisible = false;
                int policy = (policyCombo.SelectedItem as PolicyOption)?.Value ?? 3;
                var item = new PermissionMatrixItemModel { ResourceKey = key, ResourceName = key };
                bool saved = await Workbench.SaveRolePermissionPolicyAsync(role.RoleId, item, policy);
                _shell?.ShowToast(Workbench.SystemNotice, saved ? "Success" : "Warning");
                if (!saved)
                {
                    return;
                }

                _shell?.CloseDrawer();
                await LoadSelectedRolePermissionsAsync();
            })
        };
        saveBtn.Classes.Add("Primary");
        panel.Children.Add(saveBtn);

        _shell?.OpenDrawer("新增角色权限", "添加服务端可持久化的资源键与授权级别", panel, 500);
        keyBox.Focus();
    }

    private string? ValidateNewPermissionKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "请输入资源键。";
        }
        if (!PermissionKeyPattern.IsMatch(key))
        {
            return "资源键需以字母开头，只可包含字母、数字、点、下划线和连字符，长度 3 至 160。";
        }
        if (_rolePermissions.Any(x => string.Equals(x.ResourceKey, key, StringComparison.OrdinalIgnoreCase)))
        {
            return "该角色已配置此资源键，请直接调整现有行的授权级别。";
        }
        return null;
    }

    private static IBrush? ThemeBrush(string key)
    {
        if (Application.Current?.TryGetResource(key, null, out object? value) == true)
        {
            return value as IBrush;
        }
        return null;
    }

    private sealed record ScopeOption(string Value, string Label);
    private sealed record PolicyOption(int Value, string Label);

    private static StackPanel Labeled(string label, Control control) => new()
    {
        Spacing = 4,
        Children =
        {
            new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeight.SemiBold },
            control
        }
    };
}
