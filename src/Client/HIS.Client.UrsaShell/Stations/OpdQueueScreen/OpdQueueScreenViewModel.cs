using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.OpdQueueScreen;

public sealed class OpdScreenCallingModel : ClientModelBase
{
    public string RoomName { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string TicketNo { get; set; } = string.Empty;
}

public sealed class OpdScreenWaitingModel : ClientModelBase
{
    public string PatientName { get; set; } = string.Empty;
    public string TicketNo { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
}

public sealed class OpdScreenPassedModel : ClientModelBase
{
    public string PatientName { get; set; } = string.Empty;
    public string TicketNo { get; set; } = string.Empty;
}

public sealed class OpdQueueScreenViewModel : ClientModelBase, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly ShellViewModel? _shell;
    private readonly DispatcherTimer _clockTimer;
    private string _deptName = "门诊内科诊区";
    private string _deptId = "DEPT-OPD-01";
    private string _currentTimeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    private string _currentCallTicket = "--";
    private string _currentCallPatient = "暂无呼叫";
    private string _currentCallRoom = "--";
    private string _currentCallDoctor = "--";
    private string _ttsAnnouncement = "欢迎前往导医台取号排队就诊";
    private bool _isAlertFlashing;

    public OpdQueueScreenViewModel(ShellViewModel? shell = null)
    {
        _shell = shell;
        RefreshCommand = new AsyncRelayCommand(RefreshScreenDataAsync);

        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += (s, e) =>
        {
            CurrentTimeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        };
        _clockTimer.Start();

        ClinicalAlertHubClient.Current.OnOpdQueueCalled += HandleOpdQueueCalled;
        _ = RefreshScreenDataAsync();
    }

    public string DeptName
    {
        get => _deptName;
        set => SetField(ref _deptName, value, nameof(DeptName));
    }

    public string CurrentTimeText
    {
        get => _currentTimeText;
        set => SetField(ref _currentTimeText, value, nameof(CurrentTimeText));
    }

    public string CurrentCallTicket
    {
        get => _currentCallTicket;
        set => SetField(ref _currentCallTicket, value, nameof(CurrentCallTicket));
    }

    public string CurrentCallPatient
    {
        get => _currentCallPatient;
        set => SetField(ref _currentCallPatient, value, nameof(CurrentCallPatient));
    }

    public string CurrentCallRoom
    {
        get => _currentCallRoom;
        set => SetField(ref _currentCallRoom, value, nameof(CurrentCallRoom));
    }

    public string CurrentCallDoctor
    {
        get => _currentCallDoctor;
        set => SetField(ref _currentCallDoctor, value, nameof(CurrentCallDoctor));
    }

    public string TtsAnnouncement
    {
        get => _ttsAnnouncement;
        set => SetField(ref _ttsAnnouncement, value, nameof(TtsAnnouncement));
    }

    public bool IsAlertFlashing
    {
        get => _isAlertFlashing;
        set => SetField(ref _isAlertFlashing, value, nameof(IsAlertFlashing));
    }

    public ObservableCollection<OpdScreenCallingModel> CallingList { get; } = new();
    public ObservableCollection<OpdScreenWaitingModel> WaitingList { get; } = new();
    public ObservableCollection<OpdScreenPassedModel> PassedList { get; } = new();

    public ICommand RefreshCommand { get; }

    private void HandleOpdQueueCalled(ClinicalAlertPayload payload)
    {
        Dispatcher.UIThread.Post(() =>
        {
            CurrentCallTicket = $"#{payload.QueueNo}";
            CurrentCallPatient = MaskName(payload.PatientName ?? "患者");
            CurrentCallRoom = payload.ClinicRoomName ?? "就诊室";
            CurrentCallDoctor = payload.DoctorName ?? "医师";
            TtsAnnouncement = payload.TtsAnnouncement ?? $"请 {CurrentCallTicket} {CurrentCallPatient} 到 {CurrentCallRoom} 就诊";
            IsAlertFlashing = true;

            var flashTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            flashTimer.Tick += (s, e) =>
            {
                IsAlertFlashing = false;
                flashTimer.Stop();
            };
            flashTimer.Start();

            _ = RefreshScreenDataAsync();
        });
    }

    private static string MaskName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "--";
        if (name.Length <= 1) return name;
        if (name.Length == 2) return name[0] + "*";
        return name[0] + "*" + name[^1];
    }

    public async Task RefreshScreenDataAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var activeDept = SessionContext.Current.ActiveDeptId;
            if (!string.IsNullOrWhiteSpace(activeDept))
            {
                _deptId = activeDept;
            }

            var envelope = await http.GetFromJsonAsync<ClientApiEnvelope<ScreenDisplayDto>>(
                $"/api/v4/opd/queue/screen/{Uri.EscapeDataString(_deptId)}", JsonOptions);

            if (envelope is { Code: 200, Data: not null })
            {
                var data = envelope.Data;
                DeptName = string.IsNullOrWhiteSpace(data.DeptName) ? "门诊候诊大屏" : data.DeptName;

                CallingList.Clear();
                foreach (var item in data.CallingList)
                {
                    CallingList.Add(new OpdScreenCallingModel
                    {
                        RoomName = item.RoomName,
                        DoctorName = item.DoctorName,
                        PatientName = item.PatientName,
                        TicketNo = item.TicketNo
                    });
                }

                if (CallingList.Count > 0 && CurrentCallTicket == "--")
                {
                    var first = CallingList[0];
                    CurrentCallTicket = first.TicketNo;
                    CurrentCallPatient = first.PatientName;
                    CurrentCallRoom = first.RoomName;
                    CurrentCallDoctor = first.DoctorName;
                }

                WaitingList.Clear();
                foreach (var item in data.WaitingList)
                {
                    WaitingList.Add(new OpdScreenWaitingModel
                    {
                        PatientName = item.PatientName,
                        TicketNo = item.TicketNo,
                        DoctorName = item.DoctorName
                    });
                }

                PassedList.Clear();
                foreach (var item in data.PassedList)
                {
                    PassedList.Add(new OpdScreenPassedModel
                    {
                        PatientName = item.PatientName,
                        TicketNo = item.TicketNo
                    });
                }
            }
        }
        catch
        {
            // Fail quietly on network hiccup during screen poll
        }
    }

    public void Dispose()
    {
        _clockTimer.Stop();
        ClinicalAlertHubClient.Current.OnOpdQueueCalled -= HandleOpdQueueCalled;
    }

    private sealed record ScreenDisplayDto(
        string DeptId,
        string DeptName,
        OpdScreenCallingItemDto[] CallingList,
        OpdScreenWaitingItemDto[] WaitingList,
        OpdScreenPassedItemDto[] PassedList
    );

    private sealed record OpdScreenCallingItemDto(string RoomName, string DoctorName, string PatientName, string TicketNo);
    private sealed record OpdScreenWaitingItemDto(string PatientName, string TicketNo, string DoctorName);
    private sealed record OpdScreenPassedItemDto(string PatientName, string TicketNo);
}
