using Lore.Models;

namespace Lore.Mobile.Views;

public partial class ChartsPage : ContentPage
{
    public ChartsPage()
    {
        InitializeComponent();
        BindingContext = App.Main;
    }

    private async void OnPersonTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not Celebrity person) return;
        App.Main.SelectedCelebrity = person;
        await AppShell.ShowChartAsync();
    }

    private async void OnNewChart(object? sender, EventArgs e) => await Shell.Current.GoToAsync("edit");

    private async void OnAbout(object? sender, EventArgs e) => await Shell.Current.GoToAsync("about");
}
