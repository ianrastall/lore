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
    private const int ConnectionsTried = 100;  // how many of a person's best matches the finder tries in pairs
    private const int TriosShown = 24;
    public const string EveryScope = "Everyone";

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
    private readonly LatestOnly _latestFinder = new();

    public SynastryViewModel(ChartService? charts = null, SynastryInterpreter? interpreter = null,
        DavisonInterpreter? davison = null)
    {
        _charts = charts;
        _interpreter = interpreter;
        _davison = davison;
    }

    // Anyone's answers, if they have any and they are to shape what is said; set by the
    // main view model. Asked on a background thread.
    public Func<NatalChart, MirrorReading?>? AnswersOf { get; set; }

    // The selected person's own, so that a change in them rebuilds what is on screen.
    [ObservableProperty]
    public partial MirrorReading? Answers { get; set; }

    partial void OnAnswersChanged(MirrorReading? oldValue, MirrorReading? newValue)
    {
        if (Chart is not { } chart || (oldValue?.IsFor(chart) != true && newValue?.IsFor(chart) != true)) return;
        Rebuild();
        RebuildMatches();
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
    public bool FinderShown => ShowTrine && Chart is not null && Partner is null;
    public bool HasChart => Chart is not null;

    // The finder: pairs of the selected person's top connections, as trios with them.
    [ObservableProperty]
    public partial IReadOnlyList<TrioFind> Trios { get; set; } = [];

    public IReadOnlyList<TrioFind> BestTrios => Trios.Take(TriosShown).ToList();

    // Whom the finder looks among: everyone, or one category of the list.
    [ObservableProperty]
    public partial string FinderScope { get; set; } = EveryScope;

    partial void OnFinderScopeChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedScope));
        RebuildFinder();
    }

    // The list the box offers, kept as it is unless the categories themselves change, since
    // a box given a new list forgets what was selected in it.
    public IReadOnlyList<string> FinderScopes { get; private set; } = [EveryScope];

    // FinderScope as the box binds it. A box empties itself while its list is replaced, and
    // sets this to null: that is not a choice, and is ignored.
    public object? SelectedScope
    {
        get => FinderScope;
        set { if (value is string scope) FinderScope = scope; }
    }

    private void RefreshScopes()
    {
        IReadOnlyList<string> scopes = [EveryScope, .. _people.Select(p => p.Category).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        if (scopes.SequenceEqual(FinderScopes)) return;
        FinderScopes = scopes;
        OnPropertyChanged(nameof(FinderScopes));
        // The last chart of a category may have gone, and the choice with it.
        if (!scopes.Contains(FinderScope)) FinderScope = EveryScope;
        OnPropertyChanged(nameof(SelectedScope));
    }

    // Said wherever a list's scores have been moved by the selected person's answers, so
    // that nobody takes them for the charts' own.
    private bool ScoredAsLived => Chart is { } chart && Answers?.IsFor(chart) == true;
    private string AsLivedNote => ScoredAsLived ? ", taking the answers into account" : "";

    private IEnumerable<SynastryMatch> InScope =>
        FinderScope == EveryScope ? Matches : Matches.Where(m => m.Person.Category == FinderScope);

    public string FinderHeading => Chart is null ? "" : $"Best tri-synastries for {Chart.Celebrity.Name}";

    public string FinderSummary
    {
        get
        {
            if (Trios.Count == 0) return "";
            int golden = Trios.Count(t => t.Tone == TrioTone.Golden);
            string among = FinderScope == EveryScope ? "" : $" in {FinderScope}";
            int connections = Math.Min(ConnectionsTried, InScope.Count());
            return $"Tried {Trios.Count:n0} {(Trios.Count == 1 ? "trio" : "trios")} from {Chart?.Celebrity.Name}'s top {connections} connections{among}{AsLivedNote}: {golden:n0} Golden.";
        }
    }
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
            return $"Tried {ThirdMatches.Count} charts as the third{AsLivedNote}: {golden} Golden.";
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
        Partner is null && ShowTrine ? "" :
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

    public bool ShowMatches => Chart is not null && Partner is null && Matches.Count > 0 && !ShowTrine;

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
            return $"Compared with {Matches.Count} charts{AsLivedNote}:  {string.Join("  ·  ", counts)}";
        }
    }

    public string PartnerText => Partner is null ? "" : $"Compared with {Partner.Name}";
    public bool HasPartner => Partner is not null;

    public string WheelCaption => Comparison is null ? "" :
        DavisonShown ? $"The Davison chart of {Comparison.First.Celebrity.Name} and {Comparison.Second.Celebrity.Name}" :
        TrioShown ? "Rings from the inside: " + string.Join(", ", TrioReading!.Names) :
        $"Inner wheel: {Comparison.First.Celebrity.Name}   ·   Outer wheel: {Comparison.Second.Celebrity.Name}";

    public string ToneText => TrioShown
        ? $"The three read as {TrioReading!.Trio.Tone.Label()}" +
          (TrioReading.Trio.LivedTone is { } lived ? $"  ·  by the answers, {lived.Label()}" : "")
        : Reading is null ? "" :
        $"The connection reads as {Reading.Tone.Label()} ({Reading.Tone.LevelText()})" +
        (Reading.AsLived is { } asLived ? $"  ·  by the answers, {asLived.Tone.Label()} ({asLived.Tone.LevelText()})" : "");
    public string ToneColorHex => TrioShown ? TrioReading!.Trio.RankedTone.ColorHex() : (Reading?.AsLived?.Tone ?? Reading?.Tone)?.ColorHex() ?? "#9AA0A6";

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
        RefreshScopes();
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

    // A row of the finder chosen: both people at once.
    public void Choose(TrioFind found)
    {
        Third = found.Third;
        Partner = found.Second;
    }

    partial void OnChartChanged(NatalChart? value)
    {
        OnPropertyChanged(nameof(MatchesHeading));
        OnPropertyChanged(nameof(FinderHeading));
        OnPropertyChanged(nameof(HasChart));
        OnPropertyChanged(nameof(FinderShown));
        Rebuild();
        RebuildMatches();
    }

    partial void OnPartnerChanged(Celebrity? value)
    {
        OnPropertyChanged(nameof(PartnerText));
        OnPropertyChanged(nameof(HasPartner));
        OnPropertyChanged(nameof(ShowMatches));
        if (value is null) Third = null;   // the trio goes with the partner
        Rebuild();
        RebuildFinder();
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
        OnPropertyChanged(nameof(ShowMatches));
        TrioChanged();
        RebuildThirds();
        RebuildFinder();
    }

    partial void OnTriosChanged(IReadOnlyList<TrioFind> value)
    {
        OnPropertyChanged(nameof(BestTrios));
        OnPropertyChanged(nameof(FinderSummary));
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
                     nameof(TrioShown), nameof(ThirdsShown), nameof(FinderShown), nameof(ReadingShown), nameof(ComparisonShown),
                     nameof(PromptText),
                     nameof(Title), nameof(ToneText), nameof(ToneColorHex), nameof(Sections), nameof(WheelCaption),
                 })
            OnPropertyChanged(name);
    }

    // The finder, off the UI thread: every pair of the top connections as a trio with the
    // selected chart. It runs only while the Trine view is open and nobody has been chosen.
    private async void RebuildFinder()
    {
        var chart = Chart;
        var connections = InScope.Take(ConnectionsTried).Select(m => m.Person).ToList();
        var answersOf = AnswersOf;
        Trios = [];
        if (!ShowTrine || !IsActive || Partner is not null || chart is null || connections.Count < 2 || _charts is null)
        {
            _latestFinder.Cancel();
            return;
        }

        var charts = _charts.With(chart.Settings);
        await _latestFinder.Run(
            cancel => TrioService.FindTrios(charts, chart, connections, cancel, answersOf),
            busy => IsFindingThird = busy,
            found => Trios = found,
            ex => Diagnostics.Log($"Tri-synastry search for chart {chart.Celebrity.Id} failed: {ex}"));
    }

    // The search for a third, off the UI thread, for whichever two are chosen. It runs
    // only while the Trine view is the one open and nobody has been chosen yet.
    private async void RebuildThirds()
    {
        var comparison = Comparison;
        var people = _people;
        var answersOf = AnswersOf;
        ThirdMatches = [];
        if (!ShowTrine || !IsActive || Third is not null || comparison is null || _charts is null)
        {
            _latestThirds.Cancel();
            IsFindingThird = false;
            return;
        }

        var charts = _charts.With(comparison.First.Settings);
        await _latestThirds.Run(
            cancel => TrioService.Rank(charts, comparison.First, comparison.Second, people, cancel, answersOf),
            busy => IsFindingThird = busy,
            matches => ThirdMatches = matches,
            ex => Diagnostics.Log($"Search for a third for {comparison.First.Celebrity.Id} failed: {ex}"));
    }

    // The three charts and their reading, off the UI thread.
    private async void RebuildTrio()
    {
        var comparison = Comparison;
        var third = Third;
        var answersOf = AnswersOf;
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
                MirrorReading?[]? answers = answersOf is null ? null : [answersOf(comparison.First), answersOf(comparison.Second), answersOf(chart)];
                return _interpreter.ComposeTrio(TrioService.Compare(comparison.First, comparison.Second, chart, answers));
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
        RebuildFinder();
    }

    // Compares the selected chart with everyone else (off the UI thread: a chart is
    // calculated for each person). A search overtaken by a newer one is discarded.
    private async void RebuildMatches()
    {
        var chart = Chart;
        var people = _people;
        var answersOf = AnswersOf;
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
            cancel => SynastryScoring.Rank(charts, chart, people, cancel, answersOf),
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
        var answersOf = AnswersOf;
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
                return (synastry, _interpreter.Compose(synastry, answersOf?.Invoke(chart), answersOf?.Invoke(other)), _davison?.Compose(synastry));
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
