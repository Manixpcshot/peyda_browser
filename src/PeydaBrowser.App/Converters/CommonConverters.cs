using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace PeydaBrowser.App.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var visible = value is true;
        if (Invert) visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        (value is Visibility.Visible) != Invert;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, string language) => value is not true;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    /// <summary>False: visible when value is null. True (Invert): visible when value is not null/non-empty.</summary>
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isNull = value is null || (value is string s && s.Length == 0);
        var visible = Invert ? !isNull : isNull;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>Formats DateTimeOffset/DateTime values with the current culture.</summary>
public sealed class DateTimeTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var format = parameter as string ?? "g";
        return value switch
        {
            DateTimeOffset dto => dto.ToLocalTime().ToString(format, System.Globalization.CultureInfo.CurrentCulture),
            DateTime dt => dt.ToLocalTime().ToString(format, System.Globalization.CultureInfo.CurrentCulture),
            _ => value?.ToString() ?? string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
