using System;
using System.Globalization;
using System.Windows.Data;

namespace GrayMatch.Wpf;

/// <summary>
/// Negates a rotation angle for rendering. The native matcher reports angles in
/// OpenCV convention (positive = counterclockwise on screen), but WPF's
/// RotateTransform uses the opposite convention (positive = clockwise). The green
/// result box must therefore rotate by the NEGATED angle to visually match the
/// detected target. The angle text keeps the true (un-negated) value.
/// </summary>
public class AngleSignConverter : IValueConverter
{
    public object? Convert(object? value, Type? targetType, object? parameter, CultureInfo? culture)
    {
        if (value is double d) return -d;
        if (value is int i) return (double)-i;
        return 0.0;
    }

    public object? ConvertBack(object? value, Type? targetType, object? parameter, CultureInfo? culture)
    {
        if (value is double d) return -d;
        return 0.0;
    }
}
