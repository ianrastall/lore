using Lore.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lore.Views;

public sealed partial class ForecastView : UserControl
{
    private ForecastViewModel _viewModel = new();

    public ForecastViewModel ViewModel
    {
        get => _viewModel;
        set
        {
            if (ReferenceEquals(_viewModel, value)) return;
            _viewModel = value;
            Bindings.Update();
        }
    }

    public ForecastView()
    {
        InitializeComponent();
    }

    // x:Bind helpers.
    public Visibility HasText(string? s) => string.IsNullOrEmpty(s) ? Visibility.Collapsed : Visibility.Visible;
    public bool IsNotEmpty(string? s) => !string.IsNullOrEmpty(s);
}
