using Lore.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Lore.Views;

public sealed partial class ReportView : UserControl
{
    private ChartViewModel _viewModel = new();

    public ChartViewModel ViewModel
    {
        get => _viewModel;
        set
        {
            if (ReferenceEquals(_viewModel, value)) return;
            _viewModel = value;
            if (value is not null)
                value.PropertyChanged += (_, _) => Bindings.Update();
            Bindings.Update();
        }
    }

    public ReportView()
    {
        InitializeComponent();
    }
}
