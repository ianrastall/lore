using System.ComponentModel;
using Lore.Models;
using Lore.ViewModels;

namespace Lore.Mobile.Views;

public partial class WheelPage : ContentPage
{
    private readonly ChartViewModel _vm = App.Main.ChartVM;
    private readonly ToolbarItem _edit, _delete;

    public WheelPage()
    {
        InitializeComponent();
        BindingContext = _vm;
        Wheel.Draw = (canvas, w, h, px) =>
        {
            if (_vm.Chart is not { } chart) return false;
            WheelRenderer.Draw(canvas, chart, w, h, px, _vm.SelectedPlanet?.Planet);
            return true;
        };
        _edit = new ToolbarItem { Text = "Edit", Order = ToolbarItemOrder.Secondary };
        _edit.Clicked += async (_, _) => await Shell.Current.GoToAsync("edit?existing=true");
        _delete = new ToolbarItem { Text = "Delete", Order = ToolbarItemOrder.Secondary };
        _delete.Clicked += OnDelete;

        _vm.PropertyChanged += OnChartChanged;
        App.Main.PropertyChanged += OnMainChanged;
        Refresh();
        RefreshToolbar();
    }

    private void OnChartChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChartViewModel.Chart) or nameof(ChartViewModel.SelectedPlanet))
            MainThread.BeginInvokeOnMainThread(Refresh);
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedCelebrity))
            MainThread.BeginInvokeOnMainThread(RefreshToolbar);
    }

    private void Refresh()
    {
        Wheel.Redraw();
        PlanetList.Children.Clear();
        if (_vm.Chart is not { } chart) return;
        foreach (var p in chart.Planets)
            PlanetList.Children.Add(PlanetRow(p, p.Planet == _vm.SelectedPlanet?.Planet));
    }

    private View PlanetRow(PlanetPosition p, bool selected)
    {
        var row = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(36), new ColumnDefinition(GridLength.Star)],
            Padding = new Thickness(4, 6),
            BackgroundColor = selected ? (Application.Current?.RequestedTheme == AppTheme.Light
                ? Color.FromArgb("#EAE6DF") : Color.FromArgb("#2B2824")) : Colors.Transparent,
        };
        row.Add(new Label { Text = GlyphText.Apply(p.PlanetSymbol), FontSize = 20, VerticalOptions = LayoutOptions.Center });
        var text = new VerticalStackLayout
        {
            Children =
            {
                new Label { Text = p.PlanetName, FontAttributes = FontAttributes.Bold },
                new Label { Text = GlyphText.Apply(p.FormatPosition()), Style = (Style)Application.Current!.Resources["Secondary"] },
            },
        };
        row.Add(text, 1);
        row.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => _vm.SelectedPlanet = selected ? null : p),
        });
        SemanticProperties.SetDescription(row, $"{p.PlanetName}, {p.FormatPosition()}{(selected ? ", selected" : "")}");
        return row;
    }

    private void RefreshToolbar()
    {
        ToolbarItems.Remove(_edit);
        ToolbarItems.Remove(_delete);
        if (!App.Main.SelectedIsCustom) return;
        ToolbarItems.Add(_edit);
        ToolbarItems.Add(_delete);
    }

    private async void OnDelete(object? sender, EventArgs e)
    {
        if (App.Main.SelectedCelebrity is not { } person) return;
        bool sure = await DisplayAlertAsync("Delete chart", $"Delete {person.Name}'s chart? This cannot be undone.", "Delete", "Keep");
        if (!sure) return;
        await App.Main.DeleteSelectedCommand.ExecuteAsync(null);
        await Shell.Current.GoToAsync("//charts/list");
    }
}
