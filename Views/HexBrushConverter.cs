using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Lore.Views;

// Converts a "#RRGGBB" or "#AARRGGBB" hex string to a SolidColorBrush for x:Bind.
// Empty/blank → transparent, so an un-scored or Ordinary entry shows no highlight.
public sealed class HexBrushConverter : IValueConverter
{
    public object Convert(object value, System.Type targetType, object parameter, string language)
    {
        var hex = (value as string)?.TrimStart('#');
        if (string.IsNullOrEmpty(hex))
            return new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        byte a = 255;
        int i = 0;
        if (hex.Length == 8) { a = System.Convert.ToByte(hex.Substring(0, 2), 16); i = 2; }
        byte r = System.Convert.ToByte(hex.Substring(i, 2), 16);
        byte g = System.Convert.ToByte(hex.Substring(i + 2, 2), 16);
        byte b = System.Convert.ToByte(hex.Substring(i + 4, 2), 16);
        return new SolidColorBrush(Color.FromArgb(a, r, g, b));
    }

    public object ConvertBack(object value, System.Type targetType, object parameter, string language)
        => throw new System.NotSupportedException();
}
