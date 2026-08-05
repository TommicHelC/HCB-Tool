using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace HCB.RevitAddin.Features.VelocitySizer.UI;

public sealed class VelocityToBrushConverter : IValueConverter
{
    private static readonly (double Min, double Max, Brush Brush)[] Ranges =
    [
        (1.0d, 2.0d, CreateBrush(169, 209, 142)), // #A9D18E
        (2.0d, 2.5d, CreateBrush(198, 224, 180)), // #C6E0B4
        (2.5d, 4.0d, CreateBrush(248, 203, 173)), // #F8CBAD
        (5.6d, 6.2d, CreateBrush(218, 238, 243)), // #DAEEF3
        (6.2d, 6.6d, CreateBrush(183, 222, 232)), // #B7DEE8
        (6.6d, 7.0d, CreateBrush(184, 204, 228))  // #B8CCE4
    ];

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (!TryGetVelocity(value, out double velocity))
        {
            return Brushes.Transparent;
        }

        foreach ((double min, double max, Brush brush) in Ranges)
        {
            // dolna granica wlacznie, gorna wylacznie; ostatni zakres domyka 7.0
            bool inRange = velocity >= min && (velocity < max || (Math.Abs(max - 7.0d) < 1e-9 && velocity <= max));
            if (inRange)
            {
                return brush;
            }
        }

        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static bool TryGetVelocity(object value, out double velocity)
    {
        switch (value)
        {
            case double doubleValue:
                velocity = doubleValue;
                return true;
            case float floatValue:
                velocity = floatValue;
                return true;
            case string text when double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed):
                velocity = parsed;
                return true;
            default:
                velocity = 0d;
                return false;
        }
    }

    private static SolidColorBrush CreateBrush(byte r, byte g, byte b)
    {
        SolidColorBrush brush = new(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
