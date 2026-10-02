using Lore.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lore.Views;

public sealed partial class DailyView : UserControl
{
    private DailyViewModel _viewModel = new();

    public DailyViewModel ViewModel
    {
        get => _viewModel;
        set
        {
            if (ReferenceEquals(_viewModel, value)) return;
            _viewModel = value;
            Bindings.Update();
        }
    }

    public DailyView()
    {
        InitializeComponent();
    }

    // x:Bind helpers.
    public bool Not(bool b) => !b;
    public Visibility HasText(string? s) => string.IsNullOrEmpty(s) ? Visibility.Collapsed : Visibility.Visible;
}
