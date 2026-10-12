using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lore.Models;
using Lore.Services;
using System.Globalization;

namespace Lore.ViewModels;

// One statement of the questionnaire as it is shown: its number and text, and which of
// the five choices is picked (−1 for none yet).
public sealed partial class InventoryQuestion : ObservableObject
{
    private readonly Action<InventoryQuestion> _answered;

    public InventoryQuestion(InventoryItem item, int answer, IReadOnlyList<string> choices, Action<InventoryQuestion> answered)
    {
        Number = item.N;
        Text = item.Text;
        Choices = choices;
        _answerIndex = answer is >= 1 and <= 5 ? answer - 1 : -1;
        _answered = answered;
    }

    public int Number { get; }
    public string Text { get; }
    public IReadOnlyList<string> Choices { get; }
    public string NumberText => $"{Number}.";

    private int _answerIndex;
    public int AnswerIndex
    {
        get => _answerIndex;
        set
        {
            // The list of choices clears its selection when it is rebuilt; an answer once
            // given is changed for another, never taken away.
            if (value < 0 || value > 4 || value == _answerIndex) return;
            _answerIndex = value;
            OnPropertyChanged();
            foreach (string choice in new[] { nameof(Is1), nameof(Is2), nameof(Is3), nameof(Is4), nameof(Is5) })
                OnPropertyChanged(choice);
            _answered(this);
        }
    }

    // The five choices as the five buttons the view shows for them. Unticking one (which
    // the buttons do to each other) is not an answer; ticking one is.
    public bool? Is1 { get => _answerIndex == 0; set { if (value == true) AnswerIndex = 0; } }
    public bool? Is2 { get => _answerIndex == 1; set { if (value == true) AnswerIndex = 1; } }
    public bool? Is3 { get => _answerIndex == 2; set { if (value == true) AnswerIndex = 2; } }
    public bool? Is4 { get => _answerIndex == 3; set { if (value == true) AnswerIndex = 3; } }
    public bool? Is5 { get => _answerIndex == 4; set { if (value == true) AnswerIndex = 4; } }

    public string Choice1 => Choices.ElementAtOrDefault(0) ?? "";
    public string Choice2 => Choices.ElementAtOrDefault(1) ?? "";
    public string Choice3 => Choices.ElementAtOrDefault(2) ?? "";
    public string Choice4 => Choices.ElementAtOrDefault(3) ?? "";
    public string Choice5 => Choices.ElementAtOrDefault(4) ?? "";

    // The buttons of one statement are a group of their own.
    public string GroupName => $"InventoryStatement{Number}";

    // 1 to 5, or 0 if not yet answered.
    public int Answer => _answerIndex + 1;
}

// Where the Inventory view stands for the chart selected.
public enum InventoryStage
{
    NoChart,       // nobody is selected
    NotOffered,    // a bundled figure: they cannot sit a questionnaire
    Unavailable,   // the questionnaire's own file is missing or damaged
    Introduction,  // not begun
    Asking,        // under way
    Profile,       // finished: the scored profile
}

// State for the Inventory view: the questionnaire for the person whose chart is selected,
// a page of statements at a time, and the profile once every statement is answered.
// Nothing here reads the chart beyond whose it is.
public sealed partial class InventoryViewModel : ObservableObject
{
    public const int PageSize = 10;

    private readonly InventoryService? _inventory;
    private readonly InventoryStore? _store;
    private readonly MirrorInterpreter? _mirror;

    // The sittings saved for the person on screen, oldest first, and the one under way.
    private List<InventorySitting> _sittings = [];
    private InventorySitting? _current;

    // The finished sitting the profile on screen was scored from.
    private InventorySitting? _shown;

    // The scan of the birth day that came with the chart on screen, if it has no birth
    // time (see ShowChart), and the chart it came with.
    private TimeSensitivity? _day;
    private NatalChart? _dayFor;

    // The window's thread, to come back to when the store reports from a background one.
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;

    public InventoryViewModel(InventoryService? inventory = null, InventoryStore? store = null,
        MirrorInterpreter? mirror = null)
    {
        _inventory = inventory;
        _store = store;
        _mirror = mirror;
        if (store is not null) store.StatusChanged += OnStoreChanged;
    }

    // Takes a chart with the scan of its birth day, which says which signs are in doubt
    // when there is no birth time; the chart-and-answers reading does not read those.
    public void ShowChart(NatalChart? chart, TimeSensitivity? day)
    {
        bool same = ReferenceEquals(Chart, chart) && !ReferenceEquals(_day, day);
        _day = day;
        _dayFor = chart;
        Chart = chart;
        if (same) Compose(); // the same chart, now with its scan
    }

    private TimeSensitivity? Day => ReferenceEquals(_dayFor, Chart) ? _day : null;

    [ObservableProperty]
    public partial NatalChart? Chart { get; set; }

    [ObservableProperty]
    public partial InventoryStage Stage { get; set; } = InventoryStage.NoChart;

    [ObservableProperty]
    public partial IReadOnlyList<InventoryQuestion> Questions { get; set; } = [];

    [ObservableProperty]
    public partial int Page { get; set; }

    [ObservableProperty]
    public partial InventoryProfile? Profile { get; set; }

    [ObservableProperty]
    public partial string Notice { get; set; } = "";

    // The profile set beside the chart: made whenever there is a profile, and made again
    // when the same person's chart is recalculated.
    [ObservableProperty]
    public partial MirrorReading? Reading { get; set; }

    // With a profile on screen: false shows the profile itself, true the reading that
    // sets it beside the chart.
    [ObservableProperty]
    public partial bool ShowReading { get; set; }

    // Whether the answers also shape the wording of the Report, Daily and Forecast
    // readings (MainViewModel passes the reading on to them, and remembers the choice).
    [ObservableProperty]
    public partial bool ShapeReadings { get; set; } = true;

    public bool HasReading => Reading is not null;
    public bool ReadingShown => IsProfile && ShowReading && Reading is not null;
    public bool ProfileShown => IsProfile && !ReadingShown;
    public IReadOnlyList<DailySection> ReadingSections => Reading?.Sections ?? [];

    // Under the title: what is on screen. The profile takes nothing from the chart; the
    // chart-and-answers reading is made of both, and says so.
    public string HeaderText => ReadingShown
        ? "The personality profile set beside the birth chart: what the chart says of each planet, and what the answers say " +
          "of the part of life it stands for. The pairing of the two is Lore's own, and the chart is not changed by it."
        : "A standard personality questionnaire and the profile it gives. It is not astrology: nothing here is taken from the chart.";

    partial void OnReadingChanged(MirrorReading? value) => ReadingModeChanged();
    partial void OnShowReadingChanged(bool value) => ReadingModeChanged();
    partial void OnStageChanged(InventoryStage value) => ReadingModeChanged();

    private void ReadingModeChanged()
    {
        OnPropertyChanged(nameof(HasReading));
        OnPropertyChanged(nameof(ReadingShown));
        OnPropertyChanged(nameof(ProfileShown));
        OnPropertyChanged(nameof(ReadingSections));
        OnPropertyChanged(nameof(HeaderText));
    }

    // Sets the profile on screen beside the chart on screen; nothing if either is missing.
    private void Compose()
    {
        try
        {
            Reading = _mirror is { IsAvailable: true } && Chart is { } chart && Profile is { } profile
                ? _mirror.Compose(chart, profile, Day) : null;
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"The chart-and-answers reading for chart {Chart?.Celebrity.Id} could not be made: {ex}");
            Reading = null;
        }
    }

    // The reading on screen as plain text, for the export.
    public byte[] ReadingText() => Reading is { } reading ? MirrorInterpreter.ToText(reading) : [];

    // The text export of the profile (or, with `reading`, of the chart-and-answers
    // reading) as it stands now for `chart`: the finished bytes, so that nothing chosen
    // or changed afterwards can alter what is written. Null if what is on screen is not
    // this chart's, or there is no profile (or reading) for it.
    public byte[]? ExportText(NatalChart chart, bool reading)
    {
        if (!ReferenceEquals(Chart, chart) || _inventory is null || Profile is not { } profile) return null;
        if (profile.Name != chart.Celebrity.Name) return null;
        if (!reading) return _inventory.ToText(profile);
        return Reading is { } made && made.IsFor(chart) && made.Name == chart.Celebrity.Name ? MirrorInterpreter.ToText(made) : null;
    }

    private Celebrity? Person => Chart?.Celebrity;
    private int Count => _inventory?.Count ?? 0;

    public string Title => Person is null ? "Personality inventory" : $"{Person.Name} — Personality inventory";

    public bool IsNoChart => Stage == InventoryStage.NoChart;
    public bool IsNotOffered => Stage == InventoryStage.NotOffered;
    public bool IsUnavailable => Stage == InventoryStage.Unavailable;
    public bool IsIntroduction => Stage == InventoryStage.Introduction;
    public bool IsAsking => Stage == InventoryStage.Asking;
    public bool IsProfile => Stage == InventoryStage.Profile;

    public string NotOfferedText => Person is null ? "" :
        $"{Person.Name} is one of the figures Lore comes with, and cannot answer a questionnaire. " +
        "The inventory is for the charts under My Charts: add your own with Add chart, select it, and come back here.";

    // The questionnaire's own instruction, and what it is.
    public string Prompt => _inventory?.Instrument.Instrument.Prompt ?? "";
    public string AboutText => _inventory is null ? "" :
        $"The {_inventory.Instrument.Instrument.Name} is a standard, public-domain personality questionnaire: {Count} short statements, " +
        "each answered on a five-point scale from very inaccurate to very accurate. It takes fifteen to twenty-five minutes, " +
        "and can be left and taken up again: every answer is saved as it is given. At the end Lore shows a profile of five broad " +
        "traits and thirty narrower facets, each set beside the answers of a large group of other people who took it on the internet.";
    public string PrivacyText =>
        "The answers are kept on this PC only, in a file of their own beside your saved charts. They are not part of a " +
        "My Charts export, and nothing is sent anywhere. They can be deleted at any time.";
    public string AstrologyText =>
        "The questionnaire has nothing to do with astrology and the profile is not drawn from the chart. It is here to be set beside the chart, not to be explained by it.";
    public string Caution => InventoryService.Caution;
    public string EstimateText => InventoryService.EstimateNote;
    public string NormsText => _inventory is null ? "" : "Reference figures: " + _inventory.Instrument.Instrument.Norms;
    public string SourceText => _inventory is null ? "" :
        $"Questionnaire: {_inventory.Instrument.Instrument.Author}. {_inventory.Instrument.Instrument.Licence}";

    // ── Asking ────────────────────────────────────────────────────────────────

    public int PageCount => Count == 0 ? 0 : (Count + PageSize - 1) / PageSize;
    public int Answered => _current is null || _inventory is null ? 0 : _inventory.Answered(_current.Answers);
    public double Progress => Count == 0 ? 0 : 100.0 * Answered / Count;
    public string ProgressText => $"{Answered} of {Count} answered";
    public string PageText => Count == 0 ? "" :
        $"Statements {Page * PageSize + 1} to {Math.Min(Count, (Page + 1) * PageSize)} of {Count}";
    public bool CanGoBack => Page > 0;
    public bool CanGoOn => Page < PageCount - 1;
    public bool IsLastPage => PageCount > 0 && Page == PageCount - 1;
    public bool HasNotice => Notice.Length > 0;

    // ── Profile ───────────────────────────────────────────────────────────────

    public IReadOnlyList<DomainScore> Domains => Profile?.Domains ?? [];
    public string TakenText
    {
        get
        {
            if (Profile is null) return "";
            int earlier = _sittings.Count(s => s.IsComplete) - 1;
            return $"Answered on {Profile.TakenOn.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}" +
                   (earlier > 0 ? $"  ·  {earlier} earlier {(earlier == 1 ? "sitting is" : "sittings are")} kept" : "");
        }
    }

    // The profile on screen as plain text, for the export.
    public byte[] ProfileText() => _inventory is not null && Profile is { } profile ? _inventory.ToText(profile) : [];

    partial void OnChartChanged(NatalChart? oldValue, NatalChart? newValue)
    {
        // The same person's chart, calculated again: where they had got to is kept.
        if (oldValue?.Celebrity.Id == newValue?.Celebrity.Id && newValue is not null && Stage != InventoryStage.NoChart)
        {
            // The profile carries the person's name: renamed, it is scored again under
            // the new one, so that the heading, the profile and its export agree.
            if (Profile is { } profile && _shown is { } shown && _inventory is not null &&
                profile.Name != newValue.Celebrity.Name)
                Profile = _inventory.Score(shown.Answers, newValue.Celebrity.Name, profile.TakenOn);
            Refresh();
            Compose(); // the houses or the dignities may have changed with a setting
            return;
        }
        Load();
    }

    // ── Whether the answers have reached the file ─────────────────────────────

    // Said, in the view, while answers given here are not in the file: empty otherwise.
    public string SaveProblem => _store is { State: InventorySaveState.Failed } store
        ? (store.Problem ?? "The latest answers could not be saved.") +
          " What has been answered or deleted in this window is kept here while Lore stays open, but it is not in the file yet: " +
          "anything deleted is still there, and anything answered would be lost if Lore were closed now. " +
          "It is tried again with the next answer, or now with Try again."
        : "";
    public bool HasSaveProblem => SaveProblem.Length > 0;

    [RelayCommand]
    private async Task RetrySave()
    {
        if (_store is not null) await _store.RetryAsync();
    }

    // The store reports from the thread its writes run on.
    private void OnStoreChanged()
    {
        if (_ui is not null && !ReferenceEquals(SynchronizationContext.Current, _ui)) _ui.Post(_ => StoreChanged(), null);
        else StoreChanged();
    }

    private void StoreChanged()
    {
        OnPropertyChanged(nameof(SaveProblem));
        OnPropertyChanged(nameof(HasSaveProblem));
        // The answers file could not be read and now can be.
        if (Stage == InventoryStage.Unavailable && _inventory is { IsAvailable: true } && _store is { IsReadable: true }) Load();
    }

    // Why nothing is offered: the questionnaire's own file, or the file of answers.
    public string UnavailableText => _inventory is not { IsAvailable: true }
        ? "The questionnaire could not be read (Data\\inventory.json is missing or damaged). Reinstalling Lore will put it back."
        : (_store?.Problem ?? "The answers already given could not be read.") +
          " Until they can be, the questionnaire is not offered here, so that nothing is saved over them.";

    // Works out where the person on screen stands: no sitting, one under way, or a
    // finished one to show.
    private void Load()
    {
        _sittings = [];
        _current = null;
        _shown = null;
        Profile = null;
        Reading = null;
        Questions = [];
        Notice = "";

        if (Person is not { } person)
            Stage = InventoryStage.NoChart;
        else if (person.Category != UserChartService.MyChartsCategory)
            Stage = InventoryStage.NotOffered;
        else if (_inventory is not { IsAvailable: true })
            Stage = InventoryStage.Unavailable;
        else
        {
            _sittings = _store?.Get(person.Id) ?? [];
            // Answers from a file that does not fit this questionnaire are not guessed at.
            _sittings.RemoveAll(s => s.Answers.Length != Count);
            _current = _sittings.LastOrDefault(s => !s.IsComplete);
            // The file of answers is there and cannot be read: what this person has
            // already answered is not known, and beginning afresh would save over it.
            if (_store is { IsReadable: false })
            {
                _sittings = [];
                _current = null;
                Stage = InventoryStage.Unavailable;
            }
            else if (_current is not null)
                Ask(FirstUnansweredPage());
            else if (_sittings.LastOrDefault(s => s.IsComplete) is { } done)
                Show(done);
            else
                Stage = InventoryStage.Introduction;
        }
        Refresh();
    }

    [RelayCommand]
    private void Begin()
    {
        if (Person is null || _inventory is not { IsAvailable: true }) return;
        // Not while the answers already given cannot be read: a new sitting would be
        // saved in place of whatever is there.
        if (_store is { IsReadable: false }) return;
        _current = new InventorySitting
        {
            Started = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Answers = new int[Count],
        };
        _sittings.Add(_current);
        Save();
        Ask(0);
    }

    // The same again, some other day: the finished sittings are kept.
    [RelayCommand]
    private void TakeAgain() => Begin();

    [RelayCommand]
    private void Previous()
    {
        if (CanGoBack) Ask(Page - 1);
    }

    [RelayCommand]
    private void Next()
    {
        if (CanGoOn) Ask(Page + 1);
    }

    // Scores the sitting if every statement is answered; otherwise goes to the first
    // that is not, and says so.
    [RelayCommand]
    private void Finish()
    {
        if (_current is null || _inventory is null) return;
        if (_inventory.FirstUnanswered(_current.Answers) is { } missing)
        {
            Ask((missing - 1) / PageSize);
            int left = Count - Answered;
            Notice = $"{left} {(left == 1 ? "statement is" : "statements are")} still to be answered; the first is number {missing}.";
            return;
        }
        _current.Completed = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Save();
        var done = _current;
        _current = null;
        Show(done);
        Refresh();
    }

    // Gives up the sitting under way and goes back to the last finished one, if any.
    [RelayCommand]
    private void Abandon()
    {
        if (_current is null) return;
        _sittings.Remove(_current);
        _current = null;
        Save();
        Load();
    }

    // Removes every answer saved for this person. They go from this window at once;
    // the task says whether they have gone from the file too (SaveProblem says so if not).
    public async Task<bool> DeleteAnswersAsync()
    {
        if (Person is not { } person) return false;
        var removed = _store?.RemoveAsync(person.Id);
        Load();
        return removed is null || await removed;
    }

    private void Ask(int page)
    {
        if (_current is null || _inventory is null) return;
        Stage = InventoryStage.Asking;
        _shown = null;
        Profile = null;
        Reading = null;
        Notice = "";
        Page = Math.Clamp(page, 0, Math.Max(0, PageCount - 1));
        var answers = _current.Answers;
        Questions = _inventory.Instrument.Items
            .Skip(Page * PageSize).Take(PageSize)
            .Select(i => new InventoryQuestion(i, answers[i.N - 1], _inventory.Instrument.Choices, OnAnswered))
            .ToList();
        Refresh();
    }

    private void OnAnswered(InventoryQuestion question)
    {
        if (_current is null) return;
        _current.Answers[question.Number - 1] = question.Answer;
        Save();
        if (HasNotice && _inventory?.FirstUnanswered(_current.Answers) is null) Notice = "";
        OnPropertyChanged(nameof(Answered));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(ProgressText));
    }

    private void Show(InventorySitting done)
    {
        if (_inventory is null || Person is not { } person) return;
        var taken = DateOnly.TryParseExact(done.Completed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : DateOnly.FromDateTime(DateTime.Now);
        Questions = [];
        _shown = done;
        Profile = _inventory.Score(done.Answers, person.Name, taken);
        Stage = InventoryStage.Profile;
        Compose();
    }

    private int FirstUnansweredPage() =>
        _current is null || _inventory?.FirstUnanswered(_current.Answers) is not { } n ? 0 : (n - 1) / PageSize;

    private void Save()
    {
        if (Person is { } person) _store?.Save(person.Id, _sittings);
    }

    partial void OnNoticeChanged(string value) => OnPropertyChanged(nameof(HasNotice));

    private void Refresh()
    {
        foreach (string name in new[]
                 {
                     nameof(Title), nameof(IsNoChart), nameof(IsNotOffered), nameof(IsUnavailable), nameof(IsIntroduction),
                     nameof(IsAsking), nameof(IsProfile), nameof(NotOfferedText), nameof(Answered), nameof(Progress),
                     nameof(ProgressText), nameof(PageText), nameof(CanGoBack), nameof(CanGoOn), nameof(IsLastPage),
                     nameof(Domains), nameof(TakenText), nameof(UnavailableText), nameof(HeaderText),
                     nameof(SaveProblem), nameof(HasSaveProblem),
                 })
            OnPropertyChanged(name);
    }
}
