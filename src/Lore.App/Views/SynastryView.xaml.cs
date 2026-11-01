using Lore.Models;
using Lore.ViewModels;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
            _viewModel.PropertyChanged -= OnViewModelChanged;
            _viewModel = value;
            value.PropertyChanged += OnViewModelChanged;
            Bindings.Update();
        }
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SynastryViewModel.Comparison) or nameof(SynastryViewModel.DavisonShown)
            or nameof(SynastryViewModel.DavisonChart) or nameof(SynastryViewModel.TrioShown))
            WheelCanvas.Invalidate();
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

    // A name clicked in the matches: compare with that person.
    private void Match_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SynastryMatch match)
            ViewModel.Partner = match.Person;
    }

    private void CompareWith_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Celebrity person })
            ViewModel.CompareWith(person);
    }

    // On to the Trine view with this person as the second, to look for a third.
    private void StartTrio_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Celebrity person })
            ViewModel.AddAsThird(person);
    }

    // ── The third person ──────────────────────────────────────────────────────

    private void ThirdBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            sender.ItemsSource = ViewModel.SuggestThird(sender.Text);
    }

    private void ThirdBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var person = args.ChosenSuggestion as Celebrity ?? ViewModel.SuggestThird(args.QueryText).FirstOrDefault();
        if (person is null) return;

        ViewModel.Third = person;
        sender.Text = "";
        sender.ItemsSource = null;
    }

    private void Third_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TrioMatch match)
            ViewModel.Third = match.Person;
    }

    // A row of the finder: both of the others at once.
    private void Trio_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TrioFind found)
            ViewModel.Choose(found);
    }

    // ── The wheel ─────────────────────────────────────────────────────────────

    private void WheelCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        // The Davison chart is drawn like any other, without a birth chart's verdict rim.
        if (ViewModel.DavisonShown && ViewModel.DavisonChart is { } davison)
            ChartRenderer.Draw(args.DrawingSession, davison, (float)sender.ActualWidth, (float)sender.ActualHeight, showVerdict: false);
        else if (ViewModel.TrioShown)
            ChartRenderer.DrawTrioWheel(args.DrawingSession, ViewModel.TrioReading!.Trio, (float)sender.ActualWidth, (float)sender.ActualHeight);
        else if (ViewModel.Comparison is { } comparison)
            ChartRenderer.DrawBiWheel(args.DrawingSession, comparison, (float)sender.ActualWidth, (float)sender.ActualHeight);
        else
            args.DrawingSession.Clear(ChartRenderer.Background);
    }

    private void WheelCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        WheelCanvas.Invalidate();
    }

    // (The two buttons show the choice itself, not whether a Davison chart is there to
    // be shown, so being set from the view model never changes it back.)
    private void ComparisonRadio_Checked(object sender, RoutedEventArgs e)
    {
        ViewModel.ShowDavison = false;
        ViewModel.ShowTrine = false;
    }

    private void TrineRadio_Checked(object sender, RoutedEventArgs e) => ViewModel.ShowTrine = true;
    private void DavisonRadio_Checked(object sender, RoutedEventArgs e) => ViewModel.ShowDavison = true;

    // x:Bind helpers.
    public static IReadOnlyList<string> Sorts => SynastryViewModel.ResultSorts;
    public bool AnyOf(bool a, bool b, bool c) => a || b || c;
    public bool Neither(bool a, bool b) => !a && !b;
    public Visibility VisIfBoth(bool a, bool b) => a && b ? Visibility.Visible : Visibility.Collapsed;

    // The pill colour of a match or of the reading's tone, from its "#RRGGBB".
    public static Microsoft.UI.Xaml.Media.SolidColorBrush BrushOf(string hex) => HexBrushConverter.ToBrush(hex);
    public Microsoft.UI.Xaml.Media.SolidColorBrush ToneBrush(string hex) => HexBrushConverter.ToBrush(hex);
    public Visibility VisIf(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HasText(string? s) => string.IsNullOrEmpty(s) ? Visibility.Collapsed : Visibility.Visible;
    public bool IsNotEmpty(string? s) => !string.IsNullOrEmpty(s);
}
