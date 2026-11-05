using System.Collections;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using HIS.Contracts;

namespace HIS.Client.Core;

public abstract class ClientModelBase : INotifyPropertyChanged, INotifyDataErrorInfo
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    private readonly Dictionary<string, List<string>> _errors = new();

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public bool HasErrors => _errors.Any(kv => kv.Value.Count > 0);

    public IEnumerable GetErrors(string? propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
            return _errors.Values.SelectMany(x => x);

        return _errors.TryGetValue(propertyName, out var errs) ? errs : Array.Empty<string>();
    }

    protected void SetError(string propertyName, string error)
    {
        if (!_errors.ContainsKey(propertyName))
            _errors[propertyName] = new List<string>();

        if (!_errors[propertyName].Contains(error))
        {
            _errors[propertyName].Add(error);
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        }
    }

    protected void ClearErrors(string? propertyName = null)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            _errors.Clear();
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(null));
        }
        else if (_errors.Remove(propertyName))
        {
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        }
    }
}

public abstract class ClientControllerBase<TModel> where TModel : ClientModelBase
{
    public TModel Model { get; }

    protected ClientControllerBase(TModel model)
    {
        Model = model;
    }
}

public interface IApiClient
{
    Task<TResponse?> GetAsync<TResponse>(string url, CancellationToken ct = default);
    Task<TResponse?> PostAsync<TResponse>(string url, object? body, CancellationToken ct = default);
    Task<TResponse?> PutAsync<TResponse>(string url, object? body, CancellationToken ct = default);
    Task<TResponse?> DeleteAsync<TResponse>(string url, CancellationToken ct = default);
    Task<byte[]> GetByteArrayAsync(string url, CancellationToken ct = default);
}

public interface IPlatformDeviceService
{
    Task<string> ReadIdCardAsync(CancellationToken ct = default);
    Task<string> ScanBarcodeAsync(CancellationToken ct = default);
    Task<string> CaptureHandwritingSignatureSvgAsync(CancellationToken ct = default);
    Task<bool> PrintOfdDocumentAsync(byte[] ofdBytes, CancellationToken ct = default);
}

public sealed class ClientContextModel : ClientModelBase
{
    private string _campusId = "CAMPUS-01";
    private DepartmentCategory _activeDeptCategory = DepartmentCategory.OPD;
    private string _activeDeptId = string.Empty;
    private string _activeDeptName = string.Empty;
    private string _activeWardId = string.Empty;
    private string _activeWardName = string.Empty;
    private string _userId = string.Empty;
    private string _displayName = string.Empty;
    private Dictionary<string, ControlAccessPolicy> _permissions = new(StringComparer.OrdinalIgnoreCase);

    public string CampusId { get => _campusId; set => SetField(ref _campusId, value); }
    public DepartmentCategory ActiveDeptCategory { get => _activeDeptCategory; set => SetField(ref _activeDeptCategory, value); }
    public string ActiveDeptId { get => _activeDeptId; set => SetField(ref _activeDeptId, value); }
    public string ActiveDeptName { get => _activeDeptName; set => SetField(ref _activeDeptName, value); }
    public string ActiveWardId { get => _activeWardId; set => SetField(ref _activeWardId, value); }
    public string ActiveWardName { get => _activeWardName; set => SetField(ref _activeWardName, value); }
    public string UserId { get => _userId; set => SetField(ref _userId, value); }
    public string DisplayName { get => _displayName; set => SetField(ref _displayName, value); }

    public Dictionary<string, ControlAccessPolicy> Permissions
    {
        get => _permissions;
        set => SetField(ref _permissions, value);
    }

    public ControlAccessPolicy GetPolicy(string controlKey)
    {
        return _permissions.TryGetValue(controlKey, out var p) ? p : ControlAccessPolicy.FullAccess;
    }
}

