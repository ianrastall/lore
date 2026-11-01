using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lore.Models;

namespace Lore.ViewModels;

// One entry in the level box. Level is null for no limit; otherwise a synastry level (+2
// to -2) or, for three people, a TrioTone as a number.
public sealed record LevelChoice(string Text, int? Level)
{
    public override string ToString() => Text;
}

// The whole of every search, to be queried: the matches for one chart, everyone tried as
// the third of two, and the trios found among a person's connections. One search box and
// one set of filters serve all three, since only one list is on screen at a time.
public sealed partial class SynastryViewModel
{
    public const string BestFirst = "Best match", HardestFirst = "Hardest first", BestKnown = "Best known",
                        LeastKnown = "Least known", ByName = "Name", OldestFirst = "Oldest first", YoungestFirst = "Youngest first";

    public static IReadOnlyList<string> ResultSorts { get; } =
        [BestFirst, HardestFirst, BestKnown, LeastKnown, ByName, OldestFirst, YoungestFirst];

    private static readonly IReadOnlyList<LevelChoice> PairLevels =
    [
        new("Any level", null),
        .. new[] { SynastryTone.Soulmates, SynastryTone.Harmonious, SynastryTone.Mixed, SynastryTone.Challenging, SynastryTone.Adversaries }
            .Select(t => new LevelChoice($"{t.Label()} ({t.LevelText()})", t.Level())),
    ];

    private static readonly IReadOnlyList<LevelChoice> TrioLevels =
    [
        new("Any level", null),
        .. new[] { TrioTone.Golden, TrioTone.Harmonious, TrioTone.Mixed, TrioTone.Strained }
            .Select(t => new LevelChoice(t.Label(), (int)t)),
    ];

    public IReadOnlyList<LevelChoice> LevelChoices => ShowTrine ? TrioLevels : PairLevels;

    // Name, birthplace or anything in the biography: "cyclist", "Glasgow", "1921".
    [ObservableProperty]
    public partial string ResultSearch { get; set; } = "";

    [ObservableProperty]
    public partial int? ResultLevel { get; set; }

    [ObservableProperty]
    public partial bool ContemporariesOnly { get; set; }

    [ObservableProperty]
    public partial string ResultSort { get; set; } = BestFirst;

    // ResultLevel as the box binds it. A box empties itself while its list is replaced, and
    // sets this to null: that is not a choice, and is ignored.
    public object? SelectedLevel
    {
        get => LevelChoices.FirstOrDefault(c => c.Level == ResultLevel) ?? LevelChoices[0];
        set { if (value is LevelChoice choice) ResultLevel = choice.Level; }
    }

    // ResultSort likewise.
    public object? SelectedSort
    {
        get => ResultSort;
        set { if (value is string sort) ResultSort = sort; }
    }

    [ObservableProperty]
    public partial IReadOnlyList<SynastryMatch> ShownMatches { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<TrioMatch> ShownThirds { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<TrioFind> ShownTrios { get; set; } = [];

    public bool ResultsShown => ShowMatches || FinderShown || ThirdsShown;

    public string ResultsHeading => ThirdsShown ? ThirdsHeading : FinderShown ? FinderHeading : MatchesHeading;

    public string ResultsSummary => ThirdsShown ? ThirdsSummary : FinderShown ? FinderSummary : MatchesSummary;

    public string ResultsNote => ThirdsShown
        ? "Every other chart tried as the third of these two. Golden needs no pair in difficulty and a strong grand trine through all three charts. The scale is Lore's own: astrology has no agreed score for three people. Select a name to read the three together."
        : FinderShown
        ? "Pairs from this person's first 100 matches among the people the search and filters allow, tried as trios with them. Golden needs no pair in difficulty and a strong grand trine through all three charts. The scale is Lore's own: astrology has no agreed score for three people. Select a row to read the three together."
        : "Every chart in the library, and every chart of your own, compared with this one. Each is placed on a five-step scale, from +2 to −2, by the share of the contacts between the two charts that are easy ones (trines, sextiles and the kinder conjunctions), each counted by how close and how personal it is. The scale is Lore's own: astrology has no agreed compatibility score. Select a name to read the comparison, or right-click it to start a trio.";

    public string ResultsCount
    {
        get
        {
            var (shown, total) = ThirdsShown ? (ShownThirds.Count, ThirdMatches.Count)
                               : FinderShown ? (ShownTrios.Count, Trios.Count)
                               : (ShownMatches.Count, Matches.Count);
            return total == 0 ? "" : $"Showing {shown:n0} of {total:n0}";
        }
    }

    public bool FiltersSet => ResultSearch.Length > 0 || ResultLevel is not null || ContemporariesOnly ||
                              ResultSort != BestFirst || FinderScope != EveryScope;

    [RelayCommand]
    private void ClearFilters()
    {
        ResultSearch = "";
        ResultLevel = null;
        ContemporariesOnly = false;
        ResultSort = BestFirst;
        FinderScope = EveryScope;
    }

    partial void OnResultSearchChanged(string value) => WhoChanged();

    partial void OnContemporariesOnlyChanged(bool value) => WhoChanged();

    partial void OnResultLevelChanged(int? value)
    {
        OnPropertyChanged(nameof(SelectedLevel));
        RefreshResults();
    }

    partial void OnResultSortChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedSort));
        RefreshResults();
    }

    // The people allowed have changed, and they are also whom the finder pairs up.
    private void WhoChanged()
    {
        RefreshResults();
        RebuildFinder();
    }

    private bool Allowed(Celebrity person)
    {
        if (FinderScope != EveryScope && person.Category != FinderScope) return false;
        string text = ResultSearch.Trim();
        return text.Length == 0 ||
               person.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
               person.BirthPlace.Contains(text, StringComparison.OrdinalIgnoreCase) ||
               person.Bio.Contains(text, StringComparison.OrdinalIgnoreCase) ||
               person.BirthDate.StartsWith(text, StringComparison.Ordinal);
    }

    private void RefreshResults()
    {
        ShownMatches = Sorted(
            Matches.Where(m => Allowed(m.Person) && (!ContemporariesOnly || m.Contemporaries) &&
                               (ResultLevel is null || m.Compatibility.Level == ResultLevel)),
            m => m.Person, m => Fame(m.Person),
            hardest => hardest.OrderBy(m => m.Compatibility.Level).ThenByDescending(m => m.Contemporaries).ThenBy(m => m.Compatibility.Share));

        ShownThirds = Sorted(
            ThirdMatches.Where(m => Allowed(m.Person) && (!ContemporariesOnly || m.Contemporaries) &&
                                    (ResultLevel is null || (int)m.Tone == ResultLevel)),
            m => m.Person, m => Fame(m.Person),
            hardest => hardest.OrderByDescending(m => m.Tone).ThenByDescending(m => m.Contemporaries).ThenBy(m => m.Share));

        // The finder has already paired only the people allowed.
        ShownTrios = Sorted(
            Trios.Where(t => ResultLevel is null || (int)t.Tone == ResultLevel),
            t => t.Second, t => Math.Min(Fame(t.Second), Fame(t.Third)),
            hardest => hardest.OrderByDescending(t => t.Tone).ThenByDescending(t => t.Contemporaries).ThenBy(t => t.Share));

        foreach (string name in new[] { nameof(ResultsCount), nameof(FiltersSet) })
            OnPropertyChanged(name);
    }

    // The reader's own charts have no sitelinks, and count as the best known of all.
    private static int Fame(Celebrity person) => person.Sitelinks ?? int.MaxValue;

    private List<T> Sorted<T>(IEnumerable<T> items, Func<T, Celebrity> person, Func<T, int> fame,
        Func<IEnumerable<T>, IEnumerable<T>> hardest) => ResultSort switch
    {
        HardestFirst => hardest(items).ToList(),
        BestKnown => items.OrderByDescending(fame).ToList(),
        LeastKnown => items.OrderBy(fame).ToList(),
        ByName => items.OrderBy(x => person(x).Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
        OldestFirst => items.OrderBy(x => person(x).BirthDate, StringComparer.Ordinal).ToList(),
        YoungestFirst => items.OrderByDescending(x => person(x).BirthDate, StringComparer.Ordinal).ToList(),
        _ => items.ToList(),   // each search comes best first
    };

    // Everything that shows which of the lists is on screen.
    private void ResultsChanged()
    {
        foreach (string name in new[] { nameof(ResultsShown), nameof(ResultsHeading), nameof(ResultsSummary), nameof(ResultsNote), nameof(ResultsCount) })
            OnPropertyChanged(name);
    }
}
