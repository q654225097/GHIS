using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HIS.Client.Core;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Shell.Controls;

/// <summary>
/// 通用真实 BO 动态扩展表单嵌入面板：
/// 只需绑定 EntityType（如 IPD_PRE_ADMISSION / CHK_ORDER / MTC_DIAGNOSTIC_REPORT / OPD_VISIT）与 EntityId，
/// 即可自动拉取管理员为该 BO 配置的动态表单字段、回显已存值，并一键保存持久化到 cfg_entity_field_values 与 BO 主表 ext_data_json。
/// </summary>
public sealed class BoDynamicFormPanel : UserControl
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static readonly StyledProperty<string> EntityTypeProperty =
        AvaloniaProperty.Register<BoDynamicFormPanel, string>(nameof(EntityType), string.Empty);

    public static readonly StyledProperty<string?> EntityIdProperty =
        AvaloniaProperty.Register<BoDynamicFormPanel, string?>(nameof(EntityId));

    private sealed class BoFormInstanceEnvelope
    {
        public string EntityType { get; set; } = string.Empty;
        public string BoDisplayName { get; set; } = string.Empty;
        public string EntityId { get; set; } = string.Empty;
        public List<BoFormGroupDto> Forms { get; set; } = new();
    }

    private sealed class BoFormGroupDto
    {
        public string SchemaId { get; set; } = string.Empty;
        public string FormCode { get; set; } = string.Empty;
        public string FormName { get; set; } = string.Empty;
        public List<BoFormFieldItemDto> Fields { get; set; } = new();
    }

    private sealed class BoFormFieldItemDto
    {
        public string FieldCode { get; set; } = string.Empty;
        public string FieldLabel { get; set; } = string.Empty;
        public string DataType { get; set; } = "STRING";
        public string UrsaControlType { get; set; } = "TextBox";
        public string? UnitText { get; set; }
        public string? DictCode { get; set; }
        public string Value { get; set; } = string.Empty;
        public List<DictOptDto> DictOptions { get; set; } = new();
    }

    private sealed class DictOptDto
    {
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public override string ToString() => ItemName;
    }

    private readonly StackPanel _rootPanel = new() { Spacing = 8 };
    private readonly TextBlock _headerText = new() { FontWeight = FontWeight.Bold, FontSize = 13, Text = "BO 动态扩展表单" };
    private readonly TextBlock _statusText = new() { FontSize = 11, Foreground = Brushes.Gray };
    private readonly StackPanel _fieldsContainer = new() { Spacing = 6 };
    private readonly Button _saveButton = new() { Content = "保存 BO 扩展字段", Height = 30 };
    private readonly List<(BoFormFieldItemDto Meta, Control Editor)> _activeEditors = new();

    public BoDynamicFormPanel()
    {
        _saveButton.Classes.Add("Primary");
        _saveButton.Click += async (_, _) => await SaveValuesAsync();

        var hdr = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(_headerText, 0);
        Grid.SetColumn(_saveButton, 1);
        hdr.Children.Add(_headerText);
        hdr.Children.Add(_saveButton);

        _rootPanel.Children.Add(hdr);
        _rootPanel.Children.Add(_fieldsContainer);
        _rootPanel.Children.Add(_statusText);

        Content = new Border
        {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.Parse("#CBD5E1")),
            Child = _rootPanel
        };
    }

    public string EntityType
    {
        get => GetValue(EntityTypeProperty);
        set => SetValue(EntityTypeProperty, value);
    }

    public string? EntityId
    {
        get => GetValue(EntityIdProperty);
        set => SetValue(EntityIdProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EntityTypeProperty || change.Property == EntityIdProperty)
        {
            _ = ReloadAsync();
        }
    }

    public async Task ReloadAsync()
    {
        _fieldsContainer.Children.Clear();
        _activeEditors.Clear();

        if (string.IsNullOrWhiteSpace(EntityType) || string.IsNullOrWhiteSpace(EntityId))
        {
            _headerText.Text = $"BO 动态扩展表单 ({EntityType})";
            _statusText.Text = "请选择一条具体业务记录以加载其动态扩展字段。";
            _saveButton.IsEnabled = false;
            return;
        }

        _statusText.Text = "正在加载绑定到此 BO 的动态扩展字段...";
        _saveButton.IsEnabled = false;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            string url = $"/api/v4/config/bo/instance/{Uri.EscapeDataString(EntityType)}/{Uri.EscapeDataString(EntityId)}";
            var res = await http.GetFromJsonAsync<ClientApiEnvelope<BoFormInstanceEnvelope>>(url, JsonOpts);
            if (res?.Code != 0 || res.Data == null)
            {
                _statusText.Text = res?.Message ?? "未加载到 BO 扩展表单。";
                return;
            }

            _headerText.Text = $"BO 动态扩展表单 — {res.Data.BoDisplayName} ({res.Data.EntityType})";
            if (res.Data.Forms.Count == 0)
            {
                _statusText.Text = $"当前 BO [{res.Data.BoDisplayName}] 尚未绑定已发布的动态表单，管理员可在「动态表单配置」中新建并绑定到 {res.Data.EntityType}。";
                return;
            }

            foreach (var form in res.Data.Forms)
            {
                _fieldsContainer.Children.Add(new TextBlock
                {
                    Text = $"▸ {form.FormName} ({form.FormCode})",
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(Color.Parse("#165DFF")),
                    Margin = new Thickness(0, 4, 0, 2)
                });

                foreach (var f in form.Fields)
                {
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), ColumnSpacing = 8 };
                    var lbl = new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(f.UnitText) ? f.FieldLabel : $"{f.FieldLabel} ({f.UnitText})",
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Control editor;
                    if (string.Equals(f.DataType, "BOOLEAN", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(f.UrsaControlType, "CheckBox", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(f.UrsaControlType, "ToggleSwitch", StringComparison.OrdinalIgnoreCase))
                    {
                        editor = new CheckBox
                        {
                            IsChecked = string.Equals(f.Value, "true", StringComparison.OrdinalIgnoreCase) || f.Value == "1",
                            Content = f.FieldLabel
                        };
                    }
                    else if (!string.IsNullOrWhiteSpace(f.DictCode) && f.DictOptions is { Count: > 0 })
                    {
                        var cb = new ComboBox
                        {
                            ItemsSource = f.DictOptions,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            PlaceholderText = $"请选择 {f.FieldLabel}（引用字典 {f.DictCode}）"
                        };
                        cb.SelectedItem = f.DictOptions.FirstOrDefault(o =>
                            string.Equals(o.ItemName, f.Value, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(o.ItemCode, f.Value, StringComparison.OrdinalIgnoreCase));
                        editor = cb;
                    }
                    else
                    {
                        editor = new TextBox
                        {
                            Text = f.Value ?? string.Empty,
                            PlaceholderText = $"输入 {f.FieldLabel}"
                        };
                    }

                    Grid.SetColumn(lbl, 0);
                    Grid.SetColumn(editor, 1);
                    row.Children.Add(lbl);
                    row.Children.Add(editor);
                    _fieldsContainer.Children.Add(row);
                    _activeEditors.Add((f, editor));
                }
            }

            _saveButton.IsEnabled = _activeEditors.Count > 0;
            _statusText.Text = $"已加载 {res.Data.Forms.Count} 套绑定表单，共 {_activeEditors.Count} 个扩展字段（保存后同步回写 BO 主表 ext_data_json）。";
        }
        catch (Exception ex)
        {
            _statusText.Text = $"加载 BO 扩展字段失败：{ex.Message}";
        }
    }

    private async Task SaveValuesAsync()
    {
        if (string.IsNullOrWhiteSpace(EntityType) || string.IsNullOrWhiteSpace(EntityId) || _activeEditors.Count == 0)
            return;

        _saveButton.IsEnabled = false;
        _statusText.Text = "正在保存并同步回写 BO 主表...";
        try
        {
            var list = new List<object>();
            foreach (var (meta, editor) in _activeEditors)
            {
                string raw = editor switch
                {
                    CheckBox cb => cb.IsChecked == true ? "true" : "false",
                    ComboBox cmb => (cmb.SelectedItem as DictOptDto)?.ItemName ?? cmb.SelectedItem?.ToString() ?? string.Empty,
                    TextBox tb => tb.Text ?? string.Empty,
                    _ => string.Empty
                };

                long? valInt = null;
                decimal? valDec = null;
                if (string.Equals(meta.DataType, "INT", StringComparison.OrdinalIgnoreCase) && long.TryParse(raw, out long iVal))
                    valInt = iVal;
                else if (string.Equals(meta.DataType, "DECIMAL", StringComparison.OrdinalIgnoreCase) && decimal.TryParse(raw, out decimal dVal))
                    valDec = dVal;

                list.Add(new
                {
                    FieldCode = meta.FieldCode,
                    ValString = raw,
                    ValInt = valInt,
                    ValDecimal = valDec,
                    ValTimeMs = (long?)null
                });
            }

            using var http = AuthenticatedHttpClientFactory.Create();
            string url = $"/api/v4/config/bo/instance/{Uri.EscapeDataString(EntityType)}/{Uri.EscapeDataString(EntityId)}/values";
            var resp = await http.PostAsJsonAsync(url, new { FormCode = (string?)null, Values = list });
            var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
            _statusText.Text = env?.Code == 0
                ? (env.Message ?? "已保存并同步回写 BO 主表。")
                : (env?.Message ?? "保存失败");
        }
        catch (Exception ex)
        {
            _statusText.Text = $"保存异常：{ex.Message}";
        }
        finally
        {
            _saveButton.IsEnabled = true;
        }
    }
}
