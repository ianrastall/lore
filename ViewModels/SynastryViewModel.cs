using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lore.Models;
using Lore.Services;

namespace Lore.ViewModels;

// State for the Synastry view: the selected chart, the person it is compared with, and
// the generated reading. Rebuilt (off the UI thread) whenever either person changes.
public sealed partial class SynastryViewModel : ObservableObject
{
    private const int MaxSuggestions = 12;
    private const int MatchesShown = 12;   // how many of the best, and of the worst, are listed

    private readonly ChartService? _charts;
    private readonly SynastryInterpreter? _interpreter;

    // Bumped on every rebuild; a calculation that finishes after a newer one has started
    // is discarded, so switching quickly between people never shows a stale comparison.
    private int _generation;

    // Everyone who can be picked as the partner (the bundled figures and My Charts).
    private IReadOnlyList<Celebrity> _people = [];

    // As _generation, for the search of everyone against the selected chart.
    private int _matchGeneration;

    // Stops a search a newer one has overtaken, rather than letting it run on.
    private CancellationTokenSource? _matchCancel;

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
        Partner is null ? $"Choose someone to compare with {Chart.Celebrity.Name}: type a name in the box above, or pick one of the matches below." :
        Partner.Id == Chart.Celebrity.Id ? $"That is {Chart.Celebrity.Name}'s own chart. Choose someone else to compare it with." :
        "";

    // The search: everyone else compared with the selected chart, best match first.
    // Shown, as its best and worst few, until someone is chosen to compare with.
    [ObservableProperty]
    public partial IReadOnlyList<SynastryMatch> Matches { get; set; } = [];

    [ObservableProperty]
    public partial bool IsSearching { get; set; }

    public IReadOnlyList<SynastryMatch> BestMatches => Matches.Take(MatchesShown).ToList();

    // Worst first. Never the same people as the best, however few charts there are.
    public IReadOnlyList<SynastryMatch> WorstMatches =>
        Matches.Skip(Math.Max(MatchesShown, Matches.Count - MatchesShown)).Reverse().ToList();

    public bool ShowMatches => Chart is not null && Partner is null && Matches.Count > 0;

    public string MatchesHeading => Chart is null ? "" : $"Best and worst matches for {Chart.Celebrity.Name}";

    // "Compared with 211 charts: 14 Soulmates (+2) · 39 Harmonious (+1) · …"
    public string MatchesSummary
    {
        get
        {
            if (Matches.Count == 0) return "";
            SynastryTone[] scale =
            [
                SynastryTone.Soulmates, SynastryTone.Harmonious, SynastryTone.Mixed,
                SynastryTone.Challenging, SynastryTone.Adversaries,
            ];
            var counts = scale.Select(t =>
                $"{Matches.Count(m => m.Compatibility.Level == t.Level())} {t.Label()} ({t.LevelText()})");
            return $"Compared with {Matches.Count} charts:  {string.Join("  ·  ", counts)}";
        }
    }

    public string PartnerText => Partner is null ? "" : $"Compared with {Partner.Name}";
    public bool HasPartner => Partner is not null;

    public string WheelCaption => Comparison is null ? "" :
        $"Inner wheel: {Comparison.First.Celebrity.Name}   ·   Outer wheel: {Comparison.Second.Celebrity.Name}";

    public string ToneText => Reading is null ? "" :
        $"The connection reads as {Reading.Tone.Label()} ({Reading.Tone.LevelText()})";
    public string ToneColorHex => Reading?.Tone.ColorHex() ?? "#9AA0A6";

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
        RebuildMatches();
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

    partial void OnChartChanged(NatalChart? value)
    {
        OnPropertyChanged(nameof(MatchesHeading));
        Rebuild();
        RebuildMatches();
    }

    partial void OnPartnerChanged(Celebrity? value)
    {
        OnPropertyChanged(nameof(PartnerText));
        OnPropertyChanged(nameof(HasPartner));
        OnPropertyChanged(nameof(ShowMatches));
        Rebuild();
    }

    partial void OnMatchesChanged(IReadOnlyList<SynastryMatch> value)
    {
        OnPropertyChanged(nameof(BestMatches));
        OnPropertyChanged(nameof(WorstMatches));
        OnPropertyChanged(nameof(MatchesSummary));
        OnPropertyChanged(nameof(ShowMatches));
    }

    // Compares the selected chart with everyone else (off the UI thread: a chart is
    // calculated for each person). A search overtaken by a newer one is discarded.
    private async void RebuildMatches()
    {
        int generation = ++_matchGeneration;
        _matchCancel?.Cancel();
        var cancel = _matchCancel = new CancellationTokenSource();
        var chart = Chart;
        var people = _people;
        Matches = [];

        if (chart is null || _charts is null || people.Count == 0)
        {
            IsSearching = false;
            return;
        }

        IsSearching = true;
        try
        {
            // Everyone is cast with the settings the selected chart was cast with.
            var charts = _charts.With(chart.Settings);
            var matches = await Task.Run(() => SynastryScoring.Rank(charts, chart, people, cancel.Token));
            if (generation == _matchGeneration)
                Matches = matches;
        }
        catch (OperationCanceledException)
        {
            // overtaken by a newer search
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"Synastry search for chart {chart.Celebrity.Id} failed: {ex}");
        }
        finally
        {
            if (generation == _matchGeneration)
                IsSearching = false;
        }
    }

    partial void OnComparisonChanged(Synastry? value) => OnPropertyChanged(nameof(WheelCaption));

    partial void OnReadingChanged(SynastryReading? value)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ToneText));
        OnPropertyChanged(nameof(ToneColorHex));
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

        // Either person has changed, so the comparison on screen is no longer theirs: it
        // goes at once, not when its replacement is ready.
        Comparison = null;
        Reading = null;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(PromptText));

        if (chart is null || partner is null || partner.Id == chart.Celebrity.Id ||
            _charts is null || _interpreter is null)
        {
            IsBusy = false;
            return;
        }

        IsBusy = true;
        try
        {
            var (comparison, reading) = await Task.Run(() =>
            {
                // The partner is cast with the settings the selected chart was cast with.
                var charts = _charts.With(chart.Settings);
                var other = charts.Calculate(partner);
                var synastry = SynastryService.Compare(chart, other, charts.Davison(chart, other));
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
            Diagnostics.Log($"Synastry for charts {chart.Celebrity.Id} and {partner.Id} failed: {ex}");
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
}
