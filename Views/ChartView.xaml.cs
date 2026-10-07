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
            _viewModel.PropertyChanged -= OnViewModelChanged;
            _viewModel = value;
            value.PropertyChanged += OnViewModelChanged;
            Bindings.Update();
        }
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        ChartCanvas.Invalidate();

    public ChartView()
    {
        InitializeComponent();
    }

    private void ChartCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (ViewModel.Chart is { } chart)
            ChartRenderer.Draw(args.DrawingSession, chart, (float)sender.ActualWidth, (float)sender.ActualHeight,
                (PlanetsList.SelectedItem as Lore.Models.PlanetPosition)?.Planet);
        else
            args.DrawingSession.Clear(Color.FromArgb(255, 18, 18, 30));
    }

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ChartCanvas.Invalidate();
    }

    // x:Bind helpers.
    public Visibility VisIf(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
    public Visibility VisIfNot(bool b) => b ? Visibility.Collapsed : Visibility.Visible;
    public Microsoft.UI.Xaml.Media.SolidColorBrush BrushOf(string hex) => HexBrushConverter.ToBrush(hex);

    private void PlanetsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ChartCanvas.Invalidate();
    }
}
