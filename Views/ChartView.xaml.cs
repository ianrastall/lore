using Lore.ViewModels;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace Lore.Views;

public sealed partial class ChartView : UserControl
{
    private ChartViewModel _viewModel = new();

    public ChartViewModel ViewModel
    {
        get => _viewModel;
        set
        {
            if (ReferenceEquals(_viewModel, value)) return;
            _viewModel = value;
            value.PropertyChanged += (_, _) => ChartCanvas.Invalidate();
            Bindings.Update();
        }
    }

    public ChartView()
    {
        InitializeComponent();
    }

    private void ChartCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (ViewModel.Chart is { } chart)
            ChartRenderer.Draw(args.DrawingSession, chart, (float)sender.ActualWidth, (float)sender.ActualHeight);
        else
            args.DrawingSession.Clear(Color.FromArgb(255, 18, 18, 30));
    }

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ChartCanvas.Invalidate();
    }

    private void PlanetsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ChartCanvas.Invalidate();
    }
}
