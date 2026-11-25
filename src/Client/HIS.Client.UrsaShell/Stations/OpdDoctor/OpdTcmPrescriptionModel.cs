using System;
using System.Collections.ObjectModel;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.Stations.OpdDoctor;

public sealed class TcmHerbItemModel : ClientModelBase
{
    private string _herbName = string.Empty;
    private decimal _singleDoseG = 10;
    private int _doseCount = 7;
    private string _specialInstructions = "常规";
    private decimal _unitPricePerG = 0.35m;

    public string HerbName
    {
        get => _herbName;
        set => SetField(ref _herbName, value, nameof(HerbName));
    }

    public decimal SingleDoseG
    {
        get => _singleDoseG;
        set
        {
            if (SetField(ref _singleDoseG, value, nameof(SingleDoseG)))
            {
                OnPropertyChanged(nameof(TotalWeightG));
                OnPropertyChanged(nameof(ItemTotal));
            }
        }
    }

    public int DoseCount
    {
        get => _doseCount;
        set
        {
            if (SetField(ref _doseCount, value, nameof(DoseCount)))
            {
                OnPropertyChanged(nameof(TotalWeightG));
                OnPropertyChanged(nameof(ItemTotal));
            }
        }
    }

    public string SpecialInstructions
    {
        get => _specialInstructions;
        set => SetField(ref _specialInstructions, value, nameof(SpecialInstructions));
    }

    public decimal UnitPricePerG
    {
        get => _unitPricePerG;
        set
        {
            if (SetField(ref _unitPricePerG, value, nameof(UnitPricePerG)))
            {
                OnPropertyChanged(nameof(ItemTotal));
            }
        }
    }

    public decimal TotalWeightG => SingleDoseG * DoseCount;
    public decimal ItemTotal => Math.Round(TotalWeightG * UnitPricePerG, 2);
}
