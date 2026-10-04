using Lore.Models;
using Lore.ViewModels;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace Lore.Views;

public sealed partial class SynastryView : UserControl
{
    private SynastryViewModel _viewModel = new();

    public SynastryViewModel ViewModel
    {
        get => _viewModel;
        set
        {
            if (ReferenceEquals(_viewModel, value)) return;
            _viewModel = value;
            value.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SynastryViewModel.Comparison))
                    WheelCanvas.Invalidate();
            };
            Bindings.Update();
        }
    }

    public SynastryView()
    {
        InitializeComponent();
    }

    // ── Partner picker ────────────────────────────────────────────────────────

    private void PartnerBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            sender.ItemsSource = ViewModel.Suggest(sender.Text);
    }

    // A suggestion was picked, or Enter pressed: take the pick, else the best match.
    private void PartnerBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var person = args.ChosenSuggestion as Celebrity ?? ViewModel.Suggest(args.QueryText).FirstOrDefault();
        if (person is null) return;

        ViewModel.Partner = person;
        sender.Text = "";
        sender.ItemsSource = null;
    }

    // A name clicked in the best or worst matches: compare with that person.
    private void Match_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SynastryMatch match)
            ViewModel.Partner = match.Person;
    }

    // ── Bi-wheel ──────────────────────────────────────────────────────────────

    private void WheelCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (ViewModel.Comparison is { } comparison)
            ChartRenderer.DrawBiWheel(args.DrawingSession, comparison, (float)sender.ActualWidth, (float)sender.ActualHeight);
        else
            args.DrawingSession.Clear(Color.FromArgb(255, 18, 18, 30));
    }

    private void WheelCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        WheelCanvas.Invalidate();
    }

    // x:Bind helpers.
    public bool Either(bool a, bool b) => a || b;

    // The pill colour of a match, from its "#RRGGBB".
    public static Microsoft.UI.Xaml.Media.SolidColorBrush BrushOf(string hex)
    {
        hex = hex.TrimStart('#');
        return new(Color.FromArgb(255,
            Convert.ToByte(hex[..2], 16), Convert.ToByte(hex.Substring(2, 2), 16), Convert.ToByte(hex.Substring(4, 2), 16)));
    }
    public Visibility VisIf(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HasText(string? s) => string.IsNullOrEmpty(s) ? Visibility.Collapsed : Visibility.Visible;
}
