using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.Admin;

public sealed class UserAdminStationViewModel : ClientModelBase, IShortcutTarget
{
    private readonly ShellViewModel? _shell;
    private string _searchKeyword = string.Empty;
    private bool _subscribed;

    public UserAdminStationViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        OpenCreateUserDrawerCommand = new RelayCommand(OpenCreateUserDrawer);
        ToggleUserStatusCommand = new RelayCommand(p => ConfirmToggleStatus(p as UserAdminItemModel));
        ResetPasswordCommand = new RelayCommand(p => ConfirmResetPassword(p as UserAdminItemModel));
        RefreshCommand = new RelayCommand(() =>
        {
            SearchKeyword = string.Empty;
            RebuildFiltered();
            _shell?.ShowToast($"已刷新账号列表（共 {TotalCount} 名用户）。", "Info");
        });

        RebuildFiltered();
    }

    public WorkbenchModel Workbench { get; }
    public ObservableCollection<UserAdminItemModel> FilteredUsers { get; } = new();
    public Action? FocusSearchAction { get; set; }

    public string SearchKeyword
    {
        get => _searchKeyword;
        set
        {
            if (SetField(ref _searchKeyword, value))
            {
                RebuildFiltered();
            }
        }
    }

    public int TotalCount => Workbench.UsersList.Count;
    public int ActiveCount => Workbench.UsersList.Count(u => u.IsActive);

    public ICommand OpenCreateUserDrawerCommand { get; }
    public ICommand ToggleUserStatusCommand { get; }
    public ICommand ResetPasswordCommand { get; }
    public ICommand RefreshCommand { get; }

    public void Attach()
    {
        if (!_subscribed)
        {
            Workbench.UsersList.CollectionChanged += OnUsersChanged;
            _subscribed = true;
        }
        RebuildFiltered();
        RaiseCounts();
    }

    public void Detach()
    {
        if (_subscribed)
        {
            Workbench.UsersList.CollectionChanged -= OnUsersChanged;
            _subscribed = false;
        }
    }

    public bool OnShortcut(string keyGesture)
    {
        switch (keyGesture)
        {
            case "F2":
                OpenCreateUserDrawer();
                return true;
            case "Ctrl+F":
                FocusSearchAction?.Invoke();
                return true;
            case "F5":
                RefreshCommand.Execute(null);
                return true;
            default:
                return false;
        }
    }

    private void OnUsersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildFiltered();
        RaiseCounts();
    }

    private void RebuildFiltered()
    {
        FilteredUsers.Clear();
        string kw = (SearchKeyword ?? string.Empty).Trim();
        foreach (var user in Workbench.UsersList)
        {
            if (!string.IsNullOrEmpty(kw))
            {
                bool hit = user.DisplayName.Contains(kw, StringComparison.OrdinalIgnoreCase)
                    || user.UserName.Contains(kw, StringComparison.OrdinalIgnoreCase)
                    || user.EmployeeNo.Contains(kw, StringComparison.OrdinalIgnoreCase)
                    || user.RoleName.Contains(kw, StringComparison.OrdinalIgnoreCase)
                    || user.DeptName.Contains(kw, StringComparison.OrdinalIgnoreCase);
                if (!hit) continue;
            }
            FilteredUsers.Add(user);
        }
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(ActiveCount));
    }

    private void OpenCreateUserDrawer()
    {
        Workbench.NewUserForm.Reset();
        Workbench.IsCreateUserModalOpen = false;

        var form = Workbench.NewUserForm;
        var panel = new StackPanel { Spacing = 10 };

        panel.Children.Add(new TextBlock { Text = "账号基本信息与默认工作身份", FontWeight = FontWeight.Bold, FontSize = 14 });

        var userNameBox = CreateBox("登录账号 (如 zhangsan)", form.UserName, v => form.UserName = v);
        var displayNameBox = CreateBox("员工姓名", form.DisplayName, v => form.DisplayName = v);
        var empNoBox = CreateBox("工号 (如 EMP021)", form.EmployeeNo, v => form.EmployeeNo = v);
        var passwordBox = CreateBox("设置初始密码", form.Password, v => form.Password = v);

        var roleItems = Workbench.RolesList.Count > 0
            ? Workbench.RolesList.ToArray()
            : Array.Empty<RoleAdminItemModel>();
        var roleCombo = new ComboBox
        {
            ItemsSource = roleItems,
            SelectedItem = roleItems.FirstOrDefault(r => r.RoleId == form.SelectedRole) ?? roleItems.FirstOrDefault(),
            DisplayMemberBinding = new Avalonia.Data.Binding(nameof(RoleAdminItemModel.RoleName)),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        roleCombo.SelectionChanged += (_, _) =>
        {
            if (roleCombo.SelectedItem is RoleAdminItemModel role) form.SelectedRole = role.RoleId;
        };

        var deptBox = CreateBox("所属科室名称", form.SelectedDept, v => form.SelectedDept = v);
        var deptHint = new TextBlock
        {
            Text = "科室将用于定义登录后的数据范围。",
            FontSize = 11,
            Foreground = ThemeBrushes.Text2
        };

        var prescribeBox = CreateBox("处方权分级 (0~3)", form.PrescribeLevel.ToString(), v =>
        {
            if (int.TryParse(v, out int level)) form.PrescribeLevel = level;
        });
        var surgeryBox = CreateBox("手术分级 (0~4)", form.SurgeryLevel.ToString(), v =>
        {
            if (int.TryParse(v, out int level)) form.SurgeryLevel = level;
        });
        var emrBox = CreateBox("病历审签分级 (0~3)", form.EmrSignLevel.ToString(), v =>
        {
            if (int.TryParse(v, out int level)) form.EmrSignLevel = level;
        });

        panel.Children.Add(Labeled("登录账号 *", userNameBox));
        panel.Children.Add(Labeled("员工姓名 *", displayNameBox));
        panel.Children.Add(Labeled("工号 *", empNoBox));
        panel.Children.Add(Labeled("初始密码 *", passwordBox));
        panel.Children.Add(Labeled("主角色 *", roleCombo));
        panel.Children.Add(Labeled("所属科室 *", deptBox));
        panel.Children.Add(deptHint);
        panel.Children.Add(Labeled("处方分级 / 手术分级 / 病历审签分级", new StackPanel
        {
            Spacing = 6,
            Children = { prescribeBox, surgeryBox, emrBox }
        }));

        var saveBtn = new Button
        {
            Content = "创建系统账号并授权",
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Command = new RelayCommand(async () =>
            {
                await Workbench.SaveNewUserAsync();
                RebuildFiltered();
                RaiseCounts();
                bool ok = !Workbench.SystemNotice.Contains("失败") && !Workbench.SystemNotice.Contains("必填") && !Workbench.SystemNotice.Contains("不能");
                _shell?.ShowToast(Workbench.SystemNotice, ok ? "Success" : "Warning");
                if (ok)
                {
                    _shell?.CloseDrawer();
                }
            })
        };
        saveBtn.Classes.Add("Primary");
        panel.Children.Add(saveBtn);

        _shell?.OpenDrawer("新建系统账号", "支持处方/手术/病历分级授权", panel, 460);
    }

    private void ConfirmToggleStatus(UserAdminItemModel? user)
    {
        if (user == null) return;
        string action = user.IsActive ? "停用" : "启用";
        _shell?.RequestConfirm(
            $"{action}用户账号",
            $"确定{action}【{user.DisplayName} ({user.EmployeeNo})】的系统登录权限？",
            $"确认{action}",
            async () =>
            {
                await Workbench.ToggleUserStatusAsync(user);
                RaiseCounts();
                _shell?.ShowToast(Workbench.SystemNotice, "Success");
            });
    }

    private void ConfirmResetPassword(UserAdminItemModel? user)
    {
        if (user == null) return;
        _shell?.RequestConfirm(
            "重置用户密码确认",
            $"确定将用户【{user.DisplayName} ({user.UserName})】的登录口令重置为初始默认密码？",
            "确认重置",
            async () =>
            {
                await Workbench.ResetUserPasswordAsync(user);
                _shell?.ShowToast(Workbench.SystemNotice, "Info");
            });
    }

    private static TextBox CreateBox(string placeholder, string initialValue, Action<string> onChanged)
    {
        var box = new TextBox { PlaceholderText = placeholder, Text = initialValue };
        box.TextChanged += (_, _) => onChanged(box.Text ?? string.Empty);
        return box;
    }

    private static StackPanel Labeled(string label, Control control) => new()
    {
        Spacing = 4,
        Children =
        {
            new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = ThemeBrushes.Text2 },
            control
        }
    };
}
