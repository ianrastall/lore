using System.ComponentModel;
using Lore.ViewModels;

namespace Lore.Mobile.Views;

public partial class DailyPage : ContentPage
{
    private readonly DailyViewModel _vm = App.Main.DailyVM;

    public DailyPage()
    {
        InitializeComponent();
        BindingContext = _vm;
        Wheel.Draw = (canvas, w, h, px) =>
        {
            if (_vm.Chart is not { } chart || _vm.Reading is not { } reading) return false;
            WheelRenderer.DrawTransitWheel(canvas, chart, reading, w, h, px);
            return true;
        };
        _vm.PropertyChanged += OnChanged;
        Refresh();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DailyViewModel.Reading) or nameof(DailyViewModel.Chart))
            MainThread.BeginInvokeOnMainThread(Refresh);
    }

    private void Refresh()
    {
        NoChart.IsVisible = _vm.Chart is null;
        Body.IsVisible = _vm.Chart is not null;
        Wheel.Redraw();
    }

    private void OnToggleTrace(object? sender, EventArgs e) => TraceText.IsVisible = !TraceText.IsVisible;
}
