using System.ComponentModel;

namespace Lore.Views;

// The size of the long-form text in the readings, which the reader sets from the View
// menu (style standard, section 4). One instance, so that every reading on screen changes
// together; the templates reach it with {x:Bind views:ReadingText.Current.FontSize}.
public sealed class ReadingText : INotifyPropertyChanged
{
    public static ReadingText Current { get; } = new();

    // Size and line height of each step, smallest first.
    private static readonly (double Size, double Line)[] Steps = [(14, 20), (16, 26), (18, 30), (20, 32)];

    public const int DefaultStep = 1;

    private int _step = DefaultStep;

    public int Step
    {
        get => _step;
        set
        {
            int step = Math.Clamp(value, 0, Steps.Length - 1);
            if (step == _step) return;
            _step = step;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FontSize)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LineHeight)));
        }
    }

    public double FontSize => Steps[_step].Size;
    public double LineHeight => Steps[_step].Line;

    public event PropertyChangedEventHandler? PropertyChanged;
}
