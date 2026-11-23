using System.ComponentModel;
using Lore.Models;
using Lore.ViewModels;

namespace Lore.Mobile.Views;

public partial class SkyPage : ContentPage
{
    private readonly SkyViewModel _vm = App.Main.SkyVM;

    public SkyPage()
    {
        InitializeComponent();
        BindingContext = _vm;
        Wheel.Draw = (canvas, w, h, px) =>
        {
            if (_vm.Reading is not { } reading) return false;
            WheelRenderer.DrawSkyWheel(canvas, reading, w, h, px);
            return true;
        };
        _vm.PropertyChanged += OnChanged;
        WeekStrip.ChildAdded += (_, e) => Grid.SetColumn((BindableObject)e.Element, WeekStrip.Children.Count - 1);
    }

    // The sky is worked out only while it is on screen (MainViewModel.ShowSky).
    protected override void OnAppearing()
    {
        base.OnAppearing();
        App.Main.ShowSky = true;
        Wheel.Redraw();
    }

    protected override void OnDisappearing()
    {
        App.Main.ShowSky = false;
        base.OnDisappearing();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SkyViewModel.Reading))
            MainThread.BeginInvokeOnMainThread(Wheel.Redraw);
    }

    private void OnDayTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is SkyDayTone day) _vm.ShowDay(day.Date);
    }

    private void OnToggleTrace(object? sender, EventArgs e) => TraceText.IsVisible = !TraceText.IsVisible;
}
