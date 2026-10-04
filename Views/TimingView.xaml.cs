using Lore.ViewModels;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace Lore.Views;

public sealed partial class TimingView : UserControl
{
    private TimingViewModel _viewModel = new();

    public TimingViewModel ViewModel
    {
        get => _viewModel;
        set
        {
            if (ReferenceEquals(_viewModel, value)) return;
            _viewModel = value;
            value.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TimingViewModel.ReturnChart))
                    WheelCanvas.Invalidate();
            };
            Bindings.Update();
        }
    }

    public TimingView()
    {
        InitializeComponent();
    }

    // The solar return chart, drawn like any other chart.
    private void WheelCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (ViewModel.ReturnChart is { } chart)
            ChartRenderer.Draw(args.DrawingSession, chart, (float)sender.ActualWidth, (float)sender.ActualHeight, showVerdict: false);
        else
            args.DrawingSession.Clear(Color.FromArgb(255, 18, 18, 30));
    }

    private void WheelCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => WheelCanvas.Invalidate();

    // x:Bind helpers.
    public Visibility VisIf(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HasText(string? s) => string.IsNullOrEmpty(s) ? Visibility.Collapsed : Visibility.Visible;
}
