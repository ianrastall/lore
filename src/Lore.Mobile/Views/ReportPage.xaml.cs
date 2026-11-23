namespace Lore.Mobile.Views;

public partial class ReportPage : ContentPage
{
    public ReportPage()
    {
        InitializeComponent();
        BindingContext = App.Main.ChartVM;
    }
}
