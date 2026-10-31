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
    private readonly DavisonInterpreter? _davison;

    // Everyone who can be picked as the partner (the bundled figures and My Charts).
    private IReadOnlyList<Celebrity> _people = [];


    // The comparison with the chosen partner, and the search of everyone against the
    // selected chart: of each, one at a time and the latest asked for, so switching
    // quickly between people never shows a stale one and a search overtaken is stopped.
    private readonly LatestOnly _latest = new();
    private readonly LatestOnly _latestMatches = new();
    private readonly LatestOnly _latestTrio = new();
    private readonly LatestOnly _latestThirds = new();

    public SynastryViewModel(ChartService? charts = null, SynastryInterpreter? interpreter = null,
        DavisonInterpreter? davison = null)
    {
        _charts = charts;
        _interpreter = interpreter;
        _davison = davison;
    }

    // The chart selected in the browse list: the first person, and the inner wheel.
    [ObservableProperty]
    public partial NatalChart? Chart { get; set; }

    // False while the Synastry view is not the one open. The search compares the chart
    // with everyone in the library, so it is not run then; it runs when the view is next
    // opened. (The comparison with a chosen partner is one chart, and is always made.)
    [ObservableProperty]
    public partial bool IsActive { get; set; } = true;

    // The chart or the list of people changed while the view was closed.
    private bool _matchesStale;

    partial void OnIsActiveChanged(bool value)
    {
        if (value && _matchesStale) RebuildMatches();
    }

    // The person chosen to compare with: the second person, and the outer wheel.
    [ObservableProperty]
    public partial Celebrity? Partner { get; set; }

    // The two charts and the contacts between them, for the bi-wheel.
    [ObservableProperty]
    public partial Synastry? Comparison { get; set; }

    [ObservableProperty]
    public partial SynastryReading? Reading { get; set; }

    // The reading of the two people's Davison chart, the other thing the view can show.
    [ObservableProperty]
    public partial DavisonReading? Davison { get; set; }

    // False: the comparison of the two charts and the bi-wheel. True: the Davison
    // chart's reading and its wheel. Kept as it is when another person is chosen.
    [ObservableProperty]
    public partial bool ShowDavison { get; set; }

    // The Davison chart is what is on screen (it has been asked for and there is one).
    public bool DavisonShown => ShowDavison && Davison is not null;
    public bool ComparisonShown => !DavisonShown && !ShowTrine;
    public NatalChart? DavisonChart => Davison?.Chart;

    // True: the Trine view, the two people here and a third. Kept as it is when another
    // person is chosen, like ShowDavison, and only ever one of the two.
    [ObservableProperty]
    public partial bool ShowTrine { get; set; }

    // The third person, and the reading of the three.
    [ObservableProperty]
    public partial Celebrity? Third { get; set; }

    [ObservableProperty]
    public partial TrioReading? TrioReading { get; set; }

    // The search: everyone else as the third of a trio with these two, best first.
    [ObservableProperty]
    public partial IReadOnlyList<TrioMatch> ThirdMatches { get; set; } = [];

    [ObservableProperty]
    public partial bool IsFindingThird { get; set; }

    public bool TrioShown => ShowTrine && TrioReading is not null;
    public bool ThirdsShown => ShowTrine && Comparison is not null && Third is null;
    public bool ReadingShown => Reading is not null && !ThirdsShown;
    public bool HasThird => Third is not null;
    public bool GoldenThirds => BestThirds.Any(m => m.Tone == TrioTone.Golden);

    public IReadOnlyList<TrioMatch> BestThirds => ThirdMatches.Take(MatchesShown).ToList();

    public string ThirdsHeading => Comparison is null ? "" :
        $"Best third for {Comparison.First.Celebrity.Name} and {Comparison.Second.Celebrity.Name}";

    public string ThirdsSummary
    {
        get
        {
            if (ThirdMatches.Count == 0) return "";
            int golden = ThirdMatches.Count(m => m.Tone == TrioTone.Golden);
            return $"Tried {ThirdMatches.Count} charts as the third: {golden} Golden.";
        }
    }

    public string ThirdText => Third is null ? "" : $"With {Third.Name}";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string ErrorText { get; set; } = "";

    public string Title => (TrioShown ? TrioReading!.Title : Reading?.Title) ?? (Chart is null ? "" : $"{Chart.Celebrity.Name} — Synastry");

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
        DavisonShown ? $"The Davison chart of {Comparison.First.Celebrity.Name} and {Comparison.Second.Celebrity.Name}" :
        TrioShown ? "Rings from the inside: " + string.Join(", ", TrioReading!.Names) + "   ·   Triangles: the strongest grand trines" :
        $"Inner wheel: {Comparison.First.Celebrity.Name}   ·   Outer wheel: {Comparison.Second.Celebrity.Name}";

    public string ToneText => TrioShown ? $"The three read as {TrioReading!.Trio.Tone.Label()}" : Reading is null ? "" :
        $"The connection reads as {Reading.Tone.Label()} ({Reading.Tone.LevelText()})";
    public string ToneColorHex => TrioShown ? TrioReading!.Trio.Tone.ColorHex() : Reading?.Tone.ColorHex() ?? "#9AA0A6";

    public IReadOnlyList<DailySection> Sections =>
        DavisonShown ? Davison!.Sections : TrioShown ? TrioReading!.Sections : ShowTrine ? [] : Reading?.Sections ?? [];
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
        if (Third is { } third)
        {
            var match = people.FirstOrDefault(p => p.Id == third.Id);
            if (!ReferenceEquals(match, third))
                Third = match;
        }
        RebuildMatches();
        RebuildThirds();
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

    // Names for the third person: as for the partner, leaving out both people already chosen.
    public IReadOnlyList<Celebrity> SuggestThird(string text) =>
        Suggest(text).Where(p => p.Id != Partner?.Id).ToList();

    [RelayCommand]
    private void ClearPartner() => Partner = null;

    [RelayCommand]
    private void ClearThird() => Third = null;

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

    partial void OnThirdChanged(Celebrity? value)
    {
        OnPropertyChanged(nameof(HasThird));
        OnPropertyChanged(nameof(ThirdText));
        TrioChanged();
        RebuildTrio();
    }

    partial void OnShowTrineChanged(bool value)
    {
        if (value) ShowDavison = false;
        TrioChanged();
        RebuildThirds();
    }

    partial void OnTrioReadingChanged(TrioReading? value) => TrioChanged();

    partial void OnThirdMatchesChanged(IReadOnlyList<TrioMatch> value)
    {
        OnPropertyChanged(nameof(BestThirds));
        OnPropertyChanged(nameof(ThirdsSummary));
        OnPropertyChanged(nameof(GoldenThirds));
    }

    // Everything on screen that depends on whether the three are being shown.
    private void TrioChanged()
    {
        foreach (string name in new[]
                 {
                     nameof(TrioShown), nameof(ThirdsShown), nameof(ReadingShown), nameof(ComparisonShown),
                     nameof(Title), nameof(ToneText), nameof(ToneColorHex), nameof(Sections), nameof(WheelCaption),
                 })
            OnPropertyChanged(name);
    }

    // The search for a third, off the UI thread, for whichever two are chosen. It runs
    // only while the Trine view is the one open and nobody has been chosen yet.
    private async void RebuildThirds()
    {
        var comparison = Comparison;
        var people = _people;
        ThirdMatches = [];
        if (!ShowTrine || !IsActive || Third is not null || comparison is null || _charts is null)
        {
            _latestThirds.Cancel();
            IsFindingThird = false;
            return;
        }

        var charts = _charts.With(comparison.First.Settings);
        await _latestThirds.Run(
            cancel => TrioService.Rank(charts, comparison.First, comparison.Second, people, cancel),
            busy => IsFindingThird = busy,
            matches => ThirdMatches = matches,
            ex => Diagnostics.Log($"Search for a third for {comparison.First.Celebrity.Id} failed: {ex}"));
    }

    // The three charts and their reading, off the UI thread.
    private async void RebuildTrio()
    {
        var comparison = Comparison;
        var third = Third;
        TrioReading = null;
        RebuildThirds();

        // The third is one of the two now being compared: nothing to read.
        if (third is null || comparison is null || _charts is null || _interpreter is null ||
            third.Id == comparison.First.Celebrity.Id || third.Id == comparison.Second.Celebrity.Id)
        {
            _latestTrio.Cancel();
            return;
        }

        await _latestTrio.Run(
            cancel =>
            {
                var chart = _charts.With(comparison.First.Settings).Calculate(third);
                cancel.ThrowIfCancellationRequested();
                return _interpreter.ComposeTrio(TrioService.Compare(comparison.First, comparison.Second, chart));
            },
            busy => IsFindingThird = busy,
            reading => TrioReading = reading,
            ex =>
            {
                Diagnostics.Log($"Trio with {third.Id} failed: {ex}");
                ErrorText = $"Could not build the reading of the three: {ex.Message}";
            });
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
        var chart = Chart;
        var people = _people;
        Matches = [];

        // Nobody to search for, or the view is closed (then it is made when next opened).
        bool nothing = chart is null || _charts is null || people.Count == 0;
        if (nothing || !IsActive)
        {
            _matchesStale = !nothing;
            _latestMatches.Cancel();
            IsSearching = false;
            return;
        }
        _matchesStale = false;

        // Everyone is cast with the settings the selected chart was cast with.
        var charts = _charts!.With(chart!.Settings);
        await _latestMatches.Run(
            cancel => SynastryScoring.Rank(charts, chart, people, cancel),
            busy => IsSearching = busy,
            matches => Matches = matches,
            ex => Diagnostics.Log($"Synastry search for chart {chart.Celebrity.Id} failed: {ex}"));
    }

    partial void OnComparisonChanged(Synastry? value)
    {
        OnPropertyChanged(nameof(WheelCaption));
        OnPropertyChanged(nameof(ThirdsHeading));
        TrioChanged();
        RebuildTrio();
    }

    partial void OnDavisonChanged(DavisonReading? value) => DavisonModeChanged();

    partial void OnShowDavisonChanged(bool value)
    {
        if (value) ShowTrine = false;
        DavisonModeChanged();
    }

    private void DavisonModeChanged()
    {
        OnPropertyChanged(nameof(DavisonShown));
        OnPropertyChanged(nameof(ComparisonShown));
        OnPropertyChanged(nameof(DavisonChart));
        OnPropertyChanged(nameof(Sections));
        OnPropertyChanged(nameof(WheelCaption));
    }

    partial void OnReadingChanged(SynastryReading? value)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ToneText));
        OnPropertyChanged(nameof(ToneColorHex));
        OnPropertyChanged(nameof(Sections));
        OnPropertyChanged(nameof(Trace));
        OnPropertyChanged(nameof(TraceHeader));
        TrioChanged();
    }

    private async void Rebuild()
    {
        var chart = Chart;
        var partner = Partner;
        ErrorText = "";

        // Either person has changed, so the comparison on screen is no longer theirs: it
        // goes at once, not when its replacement is ready.
        Comparison = null;
        Reading = null;
        Davison = null;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(PromptText));

        if (chart is null || partner is null || partner.Id == chart.Celebrity.Id ||
            _charts is null || _interpreter is null)
        {
            _latest.Cancel();
            IsBusy = false;
            return;
        }

        await _latest.Run(
            cancel =>
            {
                // The partner is cast with the settings the selected chart was cast with.
                var charts = _charts.With(chart.Settings);
                var other = charts.Calculate(partner);
                cancel.ThrowIfCancellationRequested();
                var between = charts.Davison(chart, other);
                try
                {
                    // The lunations and stations around its moment, for its worksheet.
                    between.Events = NatalEventsService.Compute(charts, between);
                }
                catch (Exception ex)
                {
                    Diagnostics.Log($"Events around the Davison chart of {chart.Celebrity.Id} and {partner.Id} could not be found: {ex}");
                }
                cancel.ThrowIfCancellationRequested();
                var synastry = SynastryService.Compare(chart, other, between);
                return (synastry, _interpreter.Compose(synastry), _davison?.Compose(synastry));
            },
            busy => IsBusy = busy,
            made =>
            {
                Comparison = made.Item1;
                Reading = made.Item2;
                Davison = made.Item3;
            },
            ex =>
            {
                Diagnostics.Log($"Synastry for charts {chart.Celebrity.Id} and {partner.Id} failed: {ex}");
                Comparison = null;
                Reading = null;
                Davison = null;
                ErrorText = $"Could not build the synastry reading: {ex.Message}";
            });
    }
}
