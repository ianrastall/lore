using Lore.ViewModels;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace Lore.Views;

public sealed partial class DailyView : UserControl
{
    private DailyViewModel _viewModel = new();

    public DailyViewModel ViewModel
    {
        get => _viewModel;
        set
        {
            if (ReferenceEquals(_viewModel, value)) return;
            _viewModel = value;
            value.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DailyViewModel.Reading))
                    WheelCanvas.Invalidate();
            };
            Bindings.Update();
        }
    }

    public DailyView()
    {
        InitializeComponent();
    }

    // ── Transit wheel ─────────────────────────────────────────────────────────

    // The reading is only drawn once it is this chart's: for a moment after another
    // person is picked, the last one's reading is still up while the new one is worked out.
    private void WheelCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (ViewModel.Chart is { } chart && ViewModel.Reading is { } reading && reading.Name == chart.Celebrity.Name)
            ChartRenderer.DrawTransitWheel(args.DrawingSession, chart, reading, (float)sender.ActualWidth, (float)sender.ActualHeight);
        else
            args.DrawingSession.Clear(Color.FromArgb(255, 18, 18, 30));
    }

    private void WheelCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => WheelCanvas.Invalidate();

    // x:Bind helpers.
    public bool Not(bool b) => !b;
    public Visibility HasText(string? s) => string.IsNullOrEmpty(s) ? Visibility.Collapsed : Visibility.Visible;
}
