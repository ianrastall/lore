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
            _viewModel.PropertyChanged -= OnViewModelChanged;
            _viewModel = value;
            value.PropertyChanged += OnViewModelChanged;
            Bindings.Update();
        }
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DailyViewModel.Reading))
            WheelCanvas.Invalidate();
    }

    public DailyView()
    {
        InitializeComponent();
    }

    // ── Transit wheel ─────────────────────────────────────────────────────────

    // The reading is only drawn if it is this chart's.
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
    public Microsoft.UI.Xaml.Media.SolidColorBrush BrushOf(string hex) => HexBrushConverter.ToBrush(hex);
    public Visibility HasText(string? s) => string.IsNullOrEmpty(s) ? Visibility.Collapsed : Visibility.Visible;
}
