using System.ComponentModel;
using Lore.Models;
using Lore.ViewModels;

namespace Lore.Mobile.Views;

public partial class TimingPage : ContentPage
{
    private readonly TimingViewModel _vm = App.Main.TimingVM;

    public TimingPage()
    {
        InitializeComponent();
        BindingContext = _vm;
        Wheel.Draw = (canvas, w, h, px) =>
        {
            if (_vm.ReturnChart is not { } chart) return false;
            WheelRenderer.Draw(canvas, chart, w, h, px, showVerdict: false);
            return true;
        };
        _vm.PropertyChanged += OnChanged;
        Refresh();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TimingViewModel.Reading) or nameof(TimingViewModel.Chart))
            MainThread.BeginInvokeOnMainThread(Refresh);
    }

    private void Refresh()
    {
        NoChart.IsVisible = _vm.Chart is null;
        Body.IsVisible = _vm.Chart is not null;
        Wheel.Redraw();
    }

    private void OnCityTyped(object? sender, TextChangedEventArgs e)
    {
        CityResults.Children.Clear();
        foreach (var city in _vm.Suggest(e.NewTextValue).Take(6))
            CityResults.Children.Add(CityChoice(city));
    }

    private View CityChoice(City city)
    {
        var label = new Label { Text = city.Display, Padding = new Thickness(8, 10) };
        label.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() =>
            {
                _vm.CastFor(city);
                CitySearch.Text = "";
                CityResults.Children.Clear();
                CitySearch.Unfocus();
            }),
        });
        return label;
    }
}
