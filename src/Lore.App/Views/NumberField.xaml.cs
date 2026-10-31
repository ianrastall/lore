using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Lore.Views;

// A labelled number that is typed, not spun. The text is read as a number with this
// PC's own decimal separator or with a point. An entry that cannot be read, or is out of
// range, is kept as typed with a line under it saying what is wanted; ValueChanged fires
// only for an accepted change made by the person at the keyboard.
public sealed partial class NumberField : UserControl
{
    public event EventHandler? ValueChanged;

    public double Minimum { get; set; } = double.NegativeInfinity;
    public double Maximum { get; set; } = double.PositiveInfinity;

    private double _accepted = double.NaN;

    public NumberField()
    {
        InitializeComponent();
    }

    public string Header
    {
        get => Box.Header as string ?? "";
        set
        {
            Box.Header = value;
            AutomationProperties.SetName(Box, value);
        }
    }

    // What the box holds now, or NaN if that is not an acceptable number. Read from the
    // text, so a form that is submitted without the box losing focus still gets it.
    public double Value
    {
        get => Parse(Box.Text) is { } value && InRange(value) ? value : double.NaN;
        set
        {
            _accepted = value;
            Box.Text = double.IsNaN(value) ? "" : value.ToString("0.####", CultureInfo.CurrentCulture);
            Problem.Visibility = Visibility.Collapsed;
        }
    }

    private bool InRange(double value) => value >= Minimum && value <= Maximum;

    private static double? Parse(string text)
    {
        text = text.Trim();
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double local)) return local;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double plain)) return plain;
        return null;
    }

    private void Commit()
    {
        double value = Value;
        if (double.IsNaN(value))
        {
            bool bounded = !double.IsInfinity(Minimum) && !double.IsInfinity(Maximum);
            ProblemText.Text = bounded ? $"Enter a number from {Minimum:0.####} to {Maximum:0.####}." : "Enter a number.";
            Problem.Visibility = Visibility.Visible;
            return;
        }
        Problem.Visibility = Visibility.Collapsed;
        if (value == _accepted) return;
        _accepted = value;
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Box_LostFocus(object sender, RoutedEventArgs e) => Commit();

    private void Box_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) Commit();
    }
}
