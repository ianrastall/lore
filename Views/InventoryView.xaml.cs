using Lore.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lore.Views;

public sealed partial class InventoryView : UserControl
{
    private InventoryViewModel _viewModel = new();

    public InventoryViewModel ViewModel
    {
        get => _viewModel;
        set
        {
            if (ReferenceEquals(_viewModel, value)) return;
            _viewModel.PropertyChanged -= OnViewModelChanged;
            _viewModel = value;
            value.PropertyChanged += OnViewModelChanged;
            Bindings.Update();
        }
    }

    public InventoryView()
    {
        InitializeComponent();
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // A new page of statements starts at its top.
        if (e.PropertyName == nameof(InventoryViewModel.Questions))
            QuestionScroller.ChangeView(null, 0, null, disableAnimation: true);
    }

    // Both of these throw answers away, so each asks first.

    private async void Abandon_Click(object sender, RoutedEventArgs e)
    {
        if (await Confirm("Discard this sitting?",
                "The answers given so far in this sitting are deleted. Any sitting already finished is kept.", "Discard"))
            ViewModel.AbandonCommand.Execute(null);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (await Confirm("Delete the answers?",
                "Every answer saved for this person is deleted, with the profile made from them. It can't be undone.", "Delete"))
            ViewModel.DeleteAnswers();
    }

    private bool _asking;

    private async Task<bool> Confirm(string title, string text, string yes)
    {
        if (_asking) return false; // only one dialog can be open at a time
        _asking = true;
        try
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = text,
                PrimaryButtonText = yes,
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                RequestedTheme = ElementTheme.Dark,
                XamlRoot = XamlRoot,
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            _asking = false;
        }
    }

    private void ProfileRadio_Checked(object sender, RoutedEventArgs e) => ViewModel.ShowReading = false;
    private void ReadingRadio_Checked(object sender, RoutedEventArgs e) => ViewModel.ShowReading = true;

    // x:Bind helpers.
    public Visibility VisIf(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
    public bool Not(bool b) => !b;
}
