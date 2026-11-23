using System.Globalization;
using System.Text;

namespace Lore.Mobile.Views;

// Text with astrological glyphs in it, made safe for Android: the zodiac signs (and a
// few others) are drawn as colour emoji unless each is followed by U+FE0E, which asks
// for the plain text form.
public sealed class GlyphText : IValueConverter
{
    public static string Apply(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        StringBuilder? sb = null;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            bool emojiCapable = c is >= '☀' and <= '⛿';
            bool alreadyMarked = i + 1 < text.Length && text[i + 1] is '︎' or '️';
            if (emojiCapable && !alreadyMarked)
            {
                sb ??= new StringBuilder(text, 0, i, text.Length + 16);
                sb.Append(c).Append('︎');
            }
            else sb?.Append(c);
        }
        return sb?.ToString() ?? text;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            string s => Apply(s),
            IEnumerable<string> lines => string.Join("\n", lines.Select(Apply)),
            _ => "",
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// "#RRGGBB" or "#AARRGGBB", as the core gives its colours, to a colour.
public sealed class HexColor : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && Color.TryParse(s, out var color) ? color : Colors.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// The view models' PickerDate (a DateTimeOffset at noon, for Windows' date picker) to
// and from the date MAUI's DatePicker speaks.
public sealed class PickerDate : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTimeOffset d ? d.Date : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTime d ? new DateTimeOffset(d.Date.AddHours(12)) : null;
}

// The Synastry prompt as the core words it for Windows, less the Charts list's + button,
// which the phone does not have.
public sealed class PhonePrompt : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as string ?? "").Replace("press + beside a name in the Charts list, type a name in the box above, or pick",
                                        "type a name in the box above or pick", StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class HasText : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && !string.IsNullOrWhiteSpace(s);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class IsNotNull : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class Not : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}
