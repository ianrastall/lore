using System.ComponentModel;
using Lore.Models;
using Lore.ViewModels;

namespace Lore.Mobile.Views;

public partial class SynastryPage : ContentPage
{
    private readonly SynastryViewModel _vm = App.Main.SynastryVM;

    public SynastryPage()
    {
        InitializeComponent();
        SortPicker.ItemsSource = SynastryViewModel.ResultSorts.ToList();
        BindingContext = _vm;
        Wheel.Draw = (canvas, w, h, px) =>
        {
            if (_vm.DavisonShown && _vm.DavisonChart is { } davison)
                WheelRenderer.Draw(canvas, davison, w, h, px, showVerdict: false);
            else if (_vm.Comparison is { } comparison)
                WheelRenderer.DrawBiWheel(canvas, comparison, w, h, px);
            else return false;
            return true;
        };
        _vm.PropertyChanged += OnChanged;
        Refresh();
    }

    // The best matches are worked out only while this is on screen (MainViewModel.ShowSynastry).
    protected override void OnAppearing()
    {
        base.OnAppearing();
        App.Main.ShowSynastry = true;
        Refresh();
    }

    protected override void OnDisappearing()
    {
        App.Main.ShowSynastry = false;
        base.OnDisappearing();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SynastryViewModel.Comparison) or nameof(SynastryViewModel.Davison)
            or nameof(SynastryViewModel.ShowDavison) or nameof(SynastryViewModel.Chart)
            or nameof(SynastryViewModel.Partner)
            or nameof(SynastryViewModel.IsBusy) or nameof(SynastryViewModel.IsSearching))
            MainThread.BeginInvokeOnMainThread(Refresh);
    }

    private void Refresh()
    {
        bool chart = _vm.Chart is not null;
        NoChart.IsVisible = !chart;
        Finder.IsVisible = chart && !_vm.HasPartner;
        Reading.IsVisible = chart && _vm.HasPartner;
        bool busy = _vm.IsBusy || _vm.IsSearching;
        FinderBusy.IsRunning = FinderBusy.IsVisible = busy;
        ReadingBusy.IsRunning = ReadingBusy.IsVisible = busy;
        Wheel.Redraw();
    }

    private void OnPartnerTyped(object? sender, TextChangedEventArgs e)
    {
        PartnerResults.Children.Clear();
        foreach (var person in _vm.Suggest(e.NewTextValue ?? "").Take(6))
            PartnerResults.Children.Add(PersonChoice(person));
    }

    private View PersonChoice(Celebrity person)
    {
        var row = new VerticalStackLayout
        {
            Padding = new Thickness(8, 8),
            Children =
            {
                new Label { Text = person.Name, FontAttributes = FontAttributes.Bold },
                new Label { Text = $"{person.Category} · {person.BirthDateLabel}", Style = (Style)Application.Current!.Resources["Secondary"] },
            },
        };
        row.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => Choose(person)) });
        return row;
    }

    private void OnMatchTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is SynastryMatch match) Choose(match.Person);
    }

    private void Choose(Celebrity person)
    {
        _vm.CompareWith(person);
        PartnerSearch.Text = "";
        PartnerResults.Children.Clear();
        PartnerSearch.Unfocus();
    }

    private void OnToggleTrace(object? sender, EventArgs e) => TraceText.IsVisible = !TraceText.IsVisible;
}
