using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HIS.Client.Core;

public class ClinicalAlertPayload
{
    public string? EventType { get; set; }
    public string? EncounterId { get; set; }
    public string? PatientName { get; set; }
    public string? Item { get; set; }
    public string? Value { get; set; }
    public string? Message { get; set; }
    public string? BedCode { get; set; }
    public string? Severity { get; set; }
    public long Timestamp { get; set; }
}

public sealed class ClinicalAlertHubClient : IDisposable
{
    private static ClinicalAlertHubClient? _instance;
    public static ClinicalAlertHubClient Current => _instance ??= new ClinicalAlertHubClient();

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private bool _isStarted;

    public event Action<ClinicalAlertPayload>? OnCriticalValue;
    public event Action<ClinicalAlertPayload>? OnOrderChanged;
    public event Action<ClinicalAlertPayload>? OnTimelinessAlert;
    public event Action<ClinicalAlertPayload>? OnMonitoringAlarm;
    public event Action<ClinicalAlertPayload>? OnPrescriptionComment;
    public event Action<ClinicalAlertPayload>? OnCarePlanReminder;
    public event Action<bool>? ConnectionStateChanged;

    public bool IsConnected => _webSocket?.State == WebSocketState.Open;

    public void Start(string? baseAddress = null)
    {
        if (_isStarted) return;
        _isStarted = true;
        _cts = new CancellationTokenSource();
        _ = RunConnectionLoopAsync(baseAddress ?? ApiEndpoints.BaseAddress, _cts.Token);
    }

    public void Stop()
    {
        _isStarted = false;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        if (_webSocket != null)
        {
            try { _webSocket.Dispose(); } catch { }
            _webSocket = null;
        }
        ConnectionStateChanged?.Invoke(false);
    }

    private async Task RunConnectionLoopAsync(string baseAddress, CancellationToken ct)
    {
        var uriBuilder = new UriBuilder(baseAddress);
        uriBuilder.Scheme = uriBuilder.Scheme == "https" ? "wss" : "ws";
        uriBuilder.Path = "/hubs/clinical-alerts";

        var token = SessionContext.Current.AccessToken;
        if (!string.IsNullOrWhiteSpace(token))
        {
            uriBuilder.Query = $"access_token={Uri.EscapeDataString(token)}";
        }

        while (!ct.IsCancellationRequested && _isStarted)
        {
            try
            {
                _webSocket = new ClientWebSocket();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {token}");
                }

                await _webSocket.ConnectAsync(uriBuilder.Uri, ct);
                ConnectionStateChanged?.Invoke(true);

                // Send SignalR Handshake
                var handshake = "{\"protocol\":\"json\",\"version\":1}\x1e";
                var handshakeBytes = Encoding.UTF8.GetBytes(handshake);
                await _webSocket.SendAsync(new ArraySegment<byte>(handshakeBytes), WebSocketMessageType.Text, true, ct);

                var buffer = new byte[8192];
                while (_webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            break;
                        }
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    ms.Seek(0, SeekOrigin.Begin);
                    var text = Encoding.UTF8.GetString(ms.ToArray());
                    ProcessSignalRMessages(text);
                }
            }
            catch
            {
                // Reconnect delay
            }
            finally
            {
                ConnectionStateChanged?.Invoke(false);
                if (_webSocket != null)
                {
                    try { _webSocket.Dispose(); } catch { }
                    _webSocket = null;
                }
            }

            try
            {
                await Task.Delay(5000, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void ProcessSignalRMessages(string rawText)
    {
        var messages = rawText.Split('\x1e', StringSplitOptions.RemoveEmptyEntries);
        foreach (var msg in messages)
        {
            try
            {
                using var doc = JsonDocument.Parse(msg);
                var root = doc.RootElement;
                if (root.TryGetProperty("type", out var typeProp) && typeProp.GetInt32() == 1) // Invocation
                {
                    string target = root.GetProperty("target").GetString() ?? string.Empty;
                    if (root.TryGetProperty("arguments", out var args) && args.GetArrayLength() > 0)
                    {
                        var arg = args[0];
                        var payload = new ClinicalAlertPayload
                        {
                            EventType = target,
                            EncounterId = arg.TryGetProperty("EncounterId", out var e) ? e.GetString() : null,
                            PatientName = arg.TryGetProperty("PatientName", out var p) ? p.GetString() : null,
                            Item = arg.TryGetProperty("Item", out var i) ? i.GetString() : null,
                            Value = arg.TryGetProperty("Value", out var v) ? v.GetString() : null,
                            Message = arg.TryGetProperty("Message", out var m) ? m.GetString() : null,
                            BedCode = arg.TryGetProperty("BedCode", out var b) ? b.GetString() : null,
                            Severity = arg.TryGetProperty("Severity", out var s) ? s.GetString() : null,
                            Timestamp = arg.TryGetProperty("Timestamp", out var t) ? t.GetInt64() : 0
                        };

                        TriggerEvent(target, payload);
                    }
                }
            }
            catch { }
        }
    }

    private void TriggerEvent(string target, ClinicalAlertPayload payload)
    {
        switch (target)
        {
            case nameof(OnCriticalValue):
            case "CriticalValuePublished":
                OnCriticalValue?.Invoke(payload);
                break;
            case nameof(OnOrderChanged):
            case "MedicalOrderChanged":
                OnOrderChanged?.Invoke(payload);
                break;
            case nameof(OnTimelinessAlert):
            case "EmrTimelinessAlert":
                OnTimelinessAlert?.Invoke(payload);
                break;
            case nameof(OnMonitoringAlarm):
            case "MonitoringAlarm":
                OnMonitoringAlarm?.Invoke(payload);
                break;
            case nameof(OnPrescriptionComment):
            case "PrescriptionCommentReceived":
                OnPrescriptionComment?.Invoke(payload);
                break;
            case nameof(OnCarePlanReminder):
            case "CarePlanReminder":
                OnCarePlanReminder?.Invoke(payload);
                break;
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
