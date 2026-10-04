using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lore.Models;
using Lore.Services;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Lore.ViewModels;

// State for the Synastry view: the selected chart, the person it is compared with, and
// the generated reading. Rebuilt (off the UI thread) whenever either person changes.
public sealed partial class SynastryViewModel : ObservableObject
{
    private const int MaxSuggestions = 12;

    private readonly ChartService? _charts;
    private readonly SynastryInterpreter? _interpreter;

    // Bumped on every rebuild; a calculation that finishes after a newer one has started
    // is discarded, so switching quickly between people never shows a stale comparison.
    private int _generation;

    // Everyone who can be picked as the partner (the bundled figures and My Charts).
    private IReadOnlyList<Celebrity> _people = [];

    public SynastryViewModel(ChartService? charts = null, SynastryInterpreter? interpreter = null)
    {
        _charts = charts;
        _interpreter = interpreter;
    }

    // The chart selected in the browse list: the first person, and the inner wheel.
    [ObservableProperty]
    public partial NatalChart? Chart { get; set; }

    // The person chosen to compare with: the second person, and the outer wheel.
    [ObservableProperty]
    public partial Celebrity? Partner { get; set; }

    // The two charts and the contacts between them, for the bi-wheel.
    [ObservableProperty]
    public partial Synastry? Comparison { get; set; }

    [ObservableProperty]
    public partial SynastryReading? Reading { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string ErrorText { get; set; } = "";

    public string Title => Reading?.Title ?? (Chart is null ? "" : $"{Chart.Celebrity.Name} — Synastry");

    // What to do next, shown in place of a reading until two different people are chosen.
    public string PromptText =>
        Chart is null ? "Select a chart from the list, then choose someone to compare it with." :
        Partner is null ? $"Choose someone to compare with {Chart.Celebrity.Name}: type a name in the box above." :
        Partner.Id == Chart.Celebrity.Id ? $"That is {Chart.Celebrity.Name}'s own chart. Choose someone else to compare it with." :
        "";

    public string PartnerText => Partner is null ? "" : $"Compared with {Partner.Name}";
    public bool HasPartner => Partner is not null;

    public string WheelCaption => Comparison is null ? "" :
        $"Inner wheel: {Comparison.First.Celebrity.Name}   ·   Outer wheel: {Comparison.Second.Celebrity.Name}";

    public string ToneText => Reading is null ? "" : $"The connection reads as {Reading.Tone.Label()}";
    public SolidColorBrush ToneBrush => new(ParseHex(Reading?.Tone.ColorHex() ?? "#9AA0A6"));

    public IReadOnlyList<DailySection> Sections => Reading?.Sections ?? [];
    public IReadOnlyList<string> Trace => Reading?.Trace ?? [];
    public string TraceHeader => Reading is null ? "" :
        $"Why this reading? — all {Reading.Aspects.Count} contacts between the two charts";

    // Called whenever the pool of people is rebuilt. A partner who has since been edited
    // is swapped for the saved version; one who has been deleted is dropped.
    public void SetPeople(IReadOnlyList<Celebrity> people)
    {
        _people = people;
        if (Partner is { } current)
        {
            var match = people.FirstOrDefault(p => p.Id == current.Id);
            if (!ReferenceEquals(match, current))
                Partner = match;
        }
    }

    // Names for the partner picker: those starting with the typed text first, then those
    // containing it. The selected chart's own person is left out.
    public IReadOnlyList<Celebrity> Suggest(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return [];

        string? self = Chart?.Celebrity.Id;
        return _people
            .Where(p => p.Id != self && p.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => p.Name)
            .Take(MaxSuggestions)
            .ToList();
    }

    [RelayCommand]
    private void ClearPartner() => Partner = null;

    partial void OnChartChanged(NatalChart? value) => Rebuild();

    partial void OnPartnerChanged(Celebrity? value)
    {
        OnPropertyChanged(nameof(PartnerText));
        OnPropertyChanged(nameof(HasPartner));
        Rebuild();
    }

    partial void OnComparisonChanged(Synastry? value) => OnPropertyChanged(nameof(WheelCaption));

    partial void OnReadingChanged(SynastryReading? value)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ToneText));
        OnPropertyChanged(nameof(ToneBrush));
        OnPropertyChanged(nameof(Sections));
        OnPropertyChanged(nameof(Trace));
        OnPropertyChanged(nameof(TraceHeader));
    }

    private async void Rebuild()
    {
        int generation = ++_generation;
        var chart = Chart;
        var partner = Partner;
        ErrorText = "";
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(PromptText));

        if (chart is null || partner is null || partner.Id == chart.Celebrity.Id ||
            _charts is null || _interpreter is null)
        {
            Comparison = null;
            Reading = null;
            IsBusy = false;
            return;
        }

        IsBusy = true;
        try
        {
            var (comparison, reading) = await Task.Run(() =>
            {
                var synastry = SynastryService.Compare(chart, _charts.Calculate(partner));
                return (synastry, _interpreter.Compose(synastry));
            });
            if (generation == _generation)
            {
                Comparison = comparison;
                Reading = reading;
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"Synastry for {chart.Celebrity.Name} and {partner.Name} failed: {ex}");
            if (generation == _generation)
            {
                Comparison = null;
                Reading = null;
                ErrorText = $"Could not build the synastry reading: {ex.Message}";
            }
        }
        finally
        {
            if (generation == _generation)
                IsBusy = false;
        }
    }

    private static Color ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        return Color.FromArgb(255,
            Convert.ToByte(hex[..2], 16),
            Convert.ToByte(hex.Substring(2, 2), 16),
            Convert.ToByte(hex.Substring(4, 2), 16));
    }
}
