using System.Globalization;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class VitalSignModel : ClientModelBase
{
    private string _recordedAt = string.Empty;
    private string _temperature = string.Empty;
    private string _coolingTemperature = string.Empty;
    private string _pulse = string.Empty;
    private string _heartRate = string.Empty;
    private string _respiration = string.Empty;
    private string _bloodPressure = string.Empty;
    private string _spo2 = string.Empty;
    private string _measureType = "AXILLARY";
    private string _stoolCount = string.Empty;
    private string _urineOutput = string.Empty;
    private string _totalIntake = string.Empty;
    private string _totalOutput = string.Empty;
    private string _weightKg = string.Empty;
    private string _abdominalGirth = string.Empty;
    private string _milestoneEvent = string.Empty;
    private string _nurseName = string.Empty;
    private bool _isAbnormal;

    public string RecordedAt
    {
        get => _recordedAt;
        set => SetField(ref _recordedAt, value);
    }

    public string Temperature
    {
        get => _temperature;
        set => SetField(ref _temperature, value);
    }

    public string CoolingTemperature
    {
        get => _coolingTemperature;
        set => SetField(ref _coolingTemperature, value);
    }

    public string Pulse
    {
        get => _pulse;
        set => SetField(ref _pulse, value);
    }

    public string HeartRate
    {
        get => _heartRate;
        set => SetField(ref _heartRate, value);
    }

    public string Respiration
    {
        get => _respiration;
        set => SetField(ref _respiration, value);
    }

    public string BloodPressure
    {
        get => _bloodPressure;
        set => SetField(ref _bloodPressure, value);
    }

    public string Spo2
    {
        get => _spo2;
        set => SetField(ref _spo2, value);
    }

    public string MeasureType
    {
        get => _measureType;
        set => SetField(ref _measureType, value);
    }

    public string StoolCount
    {
        get => _stoolCount;
        set => SetField(ref _stoolCount, value);
    }

    public string UrineOutput
    {
        get => _urineOutput;
        set => SetField(ref _urineOutput, value);
    }

    public string TotalIntake
    {
        get => _totalIntake;
        set => SetField(ref _totalIntake, value);
    }

    public string TotalOutput
    {
        get => _totalOutput;
        set => SetField(ref _totalOutput, value);
    }

    public string WeightKg
    {
        get => _weightKg;
        set => SetField(ref _weightKg, value);
    }

    public string AbdominalGirth
    {
        get => _abdominalGirth;
        set => SetField(ref _abdominalGirth, value);
    }

    public string MilestoneEvent
    {
        get => _milestoneEvent;
        set => SetField(ref _milestoneEvent, value);
    }

    public string NurseName
    {
        get => _nurseName;
        set => SetField(ref _nurseName, value);
    }

    public bool IsAbnormal
    {
        get => _isAbnormal;
        set => SetField(ref _isAbnormal, value);
    }

    public double TemperatureValue =>
        double.TryParse(Temperature, NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? t : 0;

    public double CoolingTemperatureValue =>
        double.TryParse(CoolingTemperature, NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? t : 0;

    public double PulseValue =>
        double.TryParse(Pulse, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : 0;

    public double HeartRateValue =>
        double.TryParse(HeartRate, NumberStyles.Float, CultureInfo.InvariantCulture, out var hr) ? hr : PulseValue;

    public string MeasureTypeText => MeasureType switch
    {
        "ORAL" => "口温",
        "RECTAL" => "肛温",
        _ => "腋温"
    };

    public string StatusText => IsAbnormal ? "异常" : "正常";

    public string StatusClass => IsAbnormal ? "Danger" : "Success";

    public string StatusBrush => IsAbnormal ? "#DC2626" : "#059669";

    public string Summary =>
        $"{MeasureTypeText} {Temperature}℃{(string.IsNullOrWhiteSpace(CoolingTemperature) ? "" : $" (复测 {CoolingTemperature}℃)")} · P {Pulse} · R {Respiration} · BP {BloodPressure} · SpO₂ {Spo2}%";
}
