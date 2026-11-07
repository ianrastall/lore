using Lore.Models;
using Lore.ViewModels;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lore.Views;

public sealed partial class SkyView : UserControl
{
    private SkyViewModel _viewModel = new();

    // True while the week strip's selection is being set from the date, so that it is
    // not taken for the reader choosing a day.
    private bool _selecting;

    public SkyViewModel ViewModel
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

    public SkyView()
    {
        InitializeComponent();
        _viewModel.PropertyChanged += OnViewModelChanged;
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SkyViewModel.Reading):
                WheelCanvas.Invalidate();
                break;
            case nameof(SkyViewModel.Week):
            case nameof(SkyViewModel.Date):
                // After the list has made its items for a new week.
                DispatcherQueue.TryEnqueue(SelectDate);
                break;
        }
    }

    private void SelectDate()
    {
        _selecting = true;
        try { WeekList.SelectedItem = ViewModel.Week.FirstOrDefault(d => d.Date == ViewModel.Date); }
        finally { _selecting = false; }
    }

    private void WeekList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_selecting && WeekList.SelectedItem is SkyDayTone day) ViewModel.ShowDay(day.Date);
    }

    // ── Sky wheel ─────────────────────────────────────────────────────────────

    private void WheelCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (ViewModel.Reading is { } reading && reading.Date == ViewModel.Date)
            ChartRenderer.DrawSkyWheel(args.DrawingSession, reading, (float)sender.ActualWidth, (float)sender.ActualHeight);
        else
            args.DrawingSession.Clear(ChartRenderer.Background);
    }

    private void WheelCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => WheelCanvas.Invalidate();

    // x:Bind helpers.
    public bool Not(bool b) => !b;
    public Microsoft.UI.Xaml.Media.SolidColorBrush BrushOf(string hex) => HexBrushConverter.ToBrush(hex);
    public Visibility HasText(string? s) => string.IsNullOrEmpty(s) ? Visibility.Collapsed : Visibility.Visible;
    public bool IsNotEmpty(string? s) => !string.IsNullOrEmpty(s);
}
