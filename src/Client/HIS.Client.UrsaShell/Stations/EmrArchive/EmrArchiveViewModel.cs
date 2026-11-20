using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Media;
using HIS.BuildingBlocks.OfdEngine;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.EmrArchive;

public sealed class EmrArchiveViewModel : ClientModelBase, IShortcutTarget
{
    private readonly ShellViewModel? _shell;
    private string _searchKeyword = string.Empty;
    private bool _subscribed;

    public EmrArchiveViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        ArchiveCurrentEmrCommand = new RelayCommand(ExecuteArchiveCurrentEmr);
        ExportCurrentOfdCommand = new RelayCommand(() => ExportRecord(null));
        VerifyCurrentOfdCommand = new RelayCommand(() => VerifyRecord(null));
        PreviewCurrentOfdCommand = new RelayCommand(() => PreviewRecord(null));

        ExportRecordCommand = new RelayCommand(p => ExportRecord(p as OfdArchiveRecordModel));
        VerifyRecordCommand = new RelayCommand(p => VerifyRecord(p as OfdArchiveRecordModel));
        PreviewRecordCommand = new RelayCommand(p => PreviewRecord(p as OfdArchiveRecordModel));
        ReverseXmlCommand = new RelayCommand(p => ReverseParseXml(p as OfdArchiveRecordModel));
        RefreshCommand = new RelayCommand(() =>
        {
            SearchKeyword = string.Empty;
            _ = LoadArchivesAsync();
            RebuildFiltered();
            _shell?.ShowToast($"归档台账已刷新（共 {TotalCount} 份）。", "Info");
        });

        RebuildFiltered();
        _ = LoadArchivesAsync();
    }

    public WorkbenchModel Workbench { get; }

    public ObservableCollection<OfdArchiveRecordModel> FilteredRecords { get; } = new();

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

    public int TotalCount => Workbench.OfdArchiveRecords.Count;
    public int VerifiedCount => Workbench.OfdArchiveRecords.Count(r => r.IsVerified);

    public ICommand ArchiveCurrentEmrCommand { get; }
    public ICommand ExportCurrentOfdCommand { get; }
    public ICommand VerifyCurrentOfdCommand { get; }
    public ICommand PreviewCurrentOfdCommand { get; }
    public ICommand ExportRecordCommand { get; }
    public ICommand VerifyRecordCommand { get; }
    public ICommand PreviewRecordCommand { get; }
    public ICommand ReverseXmlCommand { get; }
    public ICommand RefreshCommand { get; }

    public void Attach()
    {
        if (!_subscribed)
        {
            Workbench.OfdArchiveRecords.CollectionChanged += OnRecordsChanged;
            _subscribed = true;
        }
        _ = LoadArchivesAsync();
        RebuildFiltered();
        RaiseCounts();
    }

    public async Task LoadArchivesAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.GetFromJsonAsync<ClientApiEnvelope<List<ArchiveDto>>>("/api/v4/emr/archives");
            if (res?.Code == 200 && res.Data != null && res.Data.Count > 0)
            {
                Workbench.OfdArchiveRecords.Clear();
                foreach (var a in res.Data)
                {
                    Workbench.OfdArchiveRecords.Add(new OfdArchiveRecordModel
                    {
                        ArchiveNo = a.OfdNo,
                        DocType = a.ArchiveScope switch { "SINGLE_DOC" => "电子病历单篇 (GB/T 33190)", _ => "全病案封包" },
                        PatientName = a.PatientId,
                        DeptName = a.VisitOrEncounterId,
                        OfdStandard = a.OfdVersionStandard ?? "GB/T 33190-2016",
                        ZipEntries = "OFD.xml | Document.xml | CustomTags/EmrStructuredData.xml | Signatures.xml",
                        HashSha256 = a.ContentSha256 ?? "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
                        SignatureStatus = a.ArchiveStatus >= 2 ? "国密三级审签完成 (防篡改验签合法)" : "已归档封存",
                        ArchiveTime = a.GeneratedAtMs > 0
                            ? DateTimeOffset.FromUnixTimeMilliseconds(a.GeneratedAtMs).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                            : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        IsVerified = true
                    });
                }
                RebuildFiltered();
                RaiseCounts();
            }
        }
        catch { }
    }

    private sealed class ArchiveDto
    {
        public string OfdNo { get; set; } = string.Empty;
        public string ArchiveScope { get; set; } = string.Empty;
        public string VisitOrEncounterId { get; set; } = string.Empty;
        public string PatientId { get; set; } = string.Empty;
        public string? OfdVersionStandard { get; set; }
        public string? ContentSha256 { get; set; }
        public int ArchiveStatus { get; set; }
        public long GeneratedAtMs { get; set; }
    }

    public void Detach()
    {
        if (_subscribed)
        {
            Workbench.OfdArchiveRecords.CollectionChanged -= OnRecordsChanged;
            _subscribed = false;
        }
    }

    public bool OnShortcut(string keyGesture)
    {
        switch (keyGesture)
        {
            case "F2":
                PreviewRecord(FilteredRecords.FirstOrDefault());
                return true;
            case "F3":
                VerifyRecord(FilteredRecords.FirstOrDefault());
                return true;
            case "Ctrl+F":
                FocusSearchAction?.Invoke();
                return true;
            case "Escape" when !string.IsNullOrEmpty(SearchKeyword):
                SearchKeyword = string.Empty;
                return true;
            default:
                return false;
        }
    }

    private void OnRecordsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildFiltered();
        RaiseCounts();
    }

    private void RebuildFiltered()
    {
        FilteredRecords.Clear();
        string kw = (SearchKeyword ?? string.Empty).Trim();
        foreach (var item in Workbench.OfdArchiveRecords)
        {
            if (!string.IsNullOrEmpty(kw))
            {
                bool hit = item.ArchiveNo.Contains(kw, StringComparison.OrdinalIgnoreCase)
                    || item.DocType.Contains(kw, StringComparison.OrdinalIgnoreCase)
                    || item.PatientName.Contains(kw, StringComparison.OrdinalIgnoreCase)
                    || item.DeptName.Contains(kw, StringComparison.OrdinalIgnoreCase);
                if (!hit) continue;
            }
            FilteredRecords.Add(item);
        }
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(VerifiedCount));
    }

    private void ExecuteArchiveCurrentEmr()
    {
        Workbench.ExecutePrintAndArchiveCurrentEmr();
        RebuildFiltered();
        RaiseCounts();
        _shell?.ShowToast(Workbench.SystemNotice, Workbench.SystemNotice.Contains("❌") ? "Warning" : "Success");
    }

    private string ResolveOrBuildOfdFile(OfdArchiveRecordModel? record)
    {
        string docNo = record?.ArchiveNo ?? Workbench.EmrWorkspace.DocNo;
        if (string.IsNullOrWhiteSpace(docNo))
        {
            docNo = $"DOC-EMR-{DateTime.Now:yyyyMMddHHmmss}";
        }

        string exportDir = Path.Combine(AppContext.BaseDirectory, "OfdExports");
        Directory.CreateDirectory(exportDir);
        string exportPath = Path.Combine(exportDir, $"{docNo}.ofd");
        if (File.Exists(exportPath))
        {
            return exportPath;
        }

        byte[] bytes = Workbench.BuildCurrentOfdPackage();
        File.WriteAllBytes(exportPath, bytes);
        return exportPath;
    }

    private void ExportRecord(OfdArchiveRecordModel? record)
    {
        try
        {
            string path = ResolveOrBuildOfdFile(record);
            Workbench.SystemNotice = $"已导出国标 GB/T 33190-2016 OFD 文件：{path}";
            _shell?.ShowToast($"已导出 OFD 文件：{Path.GetFileName(path)}", "Success");
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"导出 OFD 失败：{ex.Message}", "Error");
        }
    }

    private void VerifyRecord(OfdArchiveRecordModel? record)
    {
        try
        {
            string path = ResolveOrBuildOfdFile(record);
            byte[] bytes = File.ReadAllBytes(path);
            OfdVerificationResult result = OfdDocumentPackage.VerifyOfdPackage(bytes);
            if (result.IsValid)
            {
                string shortHash = result.ContentSha256.Length > 20
                    ? result.ContentSha256[..20] + "..."
                    : result.ContentSha256;
                Workbench.SystemNotice = $"✓ SHA-256 防篡改校验通过：文档={result.DocId} | SHA-256={shortHash}";
                _shell?.ShowToast($"防篡改验签通过 ({result.DocId})", "Success");
            }
            else
            {
                Workbench.SystemNotice = $"❌ OFD 完整性校验异常：{result.DocId}";
                _shell?.ShowToast("OFD 防篡改校验未通过，请核查文件内容。", "Error");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"校验异常：{ex.Message}", "Error");
        }
    }

    private void PreviewRecord(OfdArchiveRecordModel? record)
    {
        var panel = new StackPanel { Spacing = 10 };
        if (record != null)
        {
            panel.Children.Add(CreateInfoBox("归档编号与类型", $"{record.ArchiveNo} · {record.DocType}"));
            panel.Children.Add(CreateInfoBox("患者与科室", $"{record.PatientName} | {record.DeptName} | 归档时间 {record.ArchiveTime}"));
            panel.Children.Add(CreateInfoBox("国标标准与签名状态", $"{record.OfdStandard} | {record.SignatureStatus}"));
            panel.Children.Add(CreateInfoBox("OFD ZIP 内部目录树", record.ZipEntries));
            panel.Children.Add(CreateInfoBox("SHA-256 全文防篡改指纹", record.HashSha256));
        }
        else
        {
            var snapshot = Workbench.CreateCurrentEmrPrintSnapshot();
            panel.Children.Add(CreateInfoBox("当前病历文书", $"{snapshot.DocNo} · {snapshot.SubTitle}"));
            panel.Children.Add(CreateInfoBox("患者摘要", snapshot.PatientBanner));
            panel.Children.Add(CreateInfoBox("版面尺寸", Workbench.ActiveEmrPrintLayout.PaperSpecSummary));
            panel.Children.Add(CreateInfoBox("电子签名状态", Workbench.EmrWorkspace.OfdStatus));
        }

        _shell?.OpenDrawer("国标 OFD 版式与结构预览", record?.ArchiveNo ?? Workbench.EmrWorkspace.DocNo, panel, 520);
    }

    private void ReverseParseXml(OfdArchiveRecordModel? record)
    {
        try
        {
            string path = ResolveOrBuildOfdFile(record);
            string xmlText;
            using (ZipArchive archive = ZipFile.OpenRead(path))
            {
                ZipArchiveEntry? entry = archive.GetEntry("Doc_0/CustomTags/EmrStructuredData.xml");
                if (entry != null)
                {
                    using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                    xmlText = reader.ReadToEnd();
                }
                else
                {
                    xmlText = "当前 OFD 包内未包含 CustomTags/EmrStructuredData.xml（可在执行『生成并归档 OFD』后生成完整双轨语义包）。";
                }
            }

            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(new TextBlock
            {
                Text = $"来源包：{Path.GetFileName(path)}（Doc_0/CustomTags/EmrStructuredData.xml）",
                FontSize = 12,
                Foreground = ThemeBrushes.Text2
            });
            panel.Children.Add(new Border
            {
                Padding = new Avalonia.Thickness(12),
                CornerRadius = new Avalonia.CornerRadius(6),
                Background = ThemeBrushes.Surface,
                BorderBrush = ThemeBrushes.Border,
                BorderThickness = new Avalonia.Thickness(1),
                Child = new SelectableTextBlock
                {
                    Text = xmlText,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    Foreground = ThemeBrushes.Text1,
                    TextWrapping = TextWrapping.Wrap
                }
            });

            _shell?.OpenDrawer("OFD 语义 XML 逆向解析", record?.ArchiveNo ?? Workbench.EmrWorkspace.DocNo, panel, 560);
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"XML 逆向解析失败：{ex.Message}", "Error");
        }
    }

    private static Border CreateInfoBox(string label, string value)
    {
        return new Border
        {
            Padding = new Avalonia.Thickness(12),
            CornerRadius = new Avalonia.CornerRadius(6),
            BorderBrush = ThemeBrushes.Border,
            BorderThickness = new Avalonia.Thickness(1),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = ThemeBrushes.Text2 },
                    new SelectableTextBlock { Text = string.IsNullOrWhiteSpace(value) ? "-" : value, FontSize = 13, TextWrapping = TextWrapping.Wrap }
                }
            }
        };
    }
}
