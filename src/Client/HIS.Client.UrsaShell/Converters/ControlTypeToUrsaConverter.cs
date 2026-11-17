using System;
using System.Globalization;
using Avalonia.Data.Converters;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Converters;

public class ControlTypeToUrsaConverter : IValueConverter
{
    public static readonly ControlTypeToUrsaConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string str)
        {
            return DynamicFormFieldModel.MapControlTypeToUrsa(str);
        }
        return "u:TextBox";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string str)
        {
            return DynamicFormFieldModel.MapUrsaToControlType(str);
        }
        return "SINGLE_TEXT";
    }
}
