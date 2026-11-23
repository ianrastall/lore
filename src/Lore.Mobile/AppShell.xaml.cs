using Lore.Mobile.Views;

namespace Lore.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("edit", typeof(ChartFormPage));
        Routing.RegisterRoute("about", typeof(AboutPage));
    }

    // To the wheel of the chart just chosen.
    public static Task ShowChartAsync() => Current.GoToAsync("//chart/wheel");
}
