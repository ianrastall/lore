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

    public InventoryViewModel(InventoryService? inventory = null, InventoryStore? store = null,
        MirrorInterpreter? mirror = null)
    {
        _inventory = inventory;
        _store = store;
        _mirror = mirror;
    }

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

    public bool HasReading => Reading is not null;
    public bool ReadingShown => IsProfile && ShowReading && Reading is not null;
    public bool ProfileShown => IsProfile && !ReadingShown;
    public IReadOnlyList<DailySection> ReadingSections => Reading?.Sections ?? [];

    partial void OnReadingChanged(MirrorReading? value) => ReadingModeChanged();
    partial void OnShowReadingChanged(bool value) => ReadingModeChanged();
    partial void OnStageChanged(InventoryStage value) => ReadingModeChanged();

    private void ReadingModeChanged()
    {
        OnPropertyChanged(nameof(HasReading));
        OnPropertyChanged(nameof(ReadingShown));
        OnPropertyChanged(nameof(ProfileShown));
        OnPropertyChanged(nameof(ReadingSections));
    }

    // Sets the profile on screen beside the chart on screen; nothing if either is missing.
    private void Compose()
    {
        try
        {
            Reading = _mirror is { IsAvailable: true } && Chart is { } chart && Profile is { } profile
                ? _mirror.Compose(chart, profile) : null;
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"The chart-and-answers reading for chart {Chart?.Celebrity.Id} could not be made: {ex}");
            Reading = null;
        }
    }

    // The reading on screen as plain text, for the export.
    public byte[] ReadingText() => Reading is { } reading ? MirrorInterpreter.ToText(reading) : [];

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
        "traits and thirty narrower facets, each set beside the answers of a large group of other people.";
    public string PrivacyText =>
        "The answers are kept on this PC only, in a file of their own beside your saved charts. They are not part of a " +
        "My Charts export, and nothing is sent anywhere. They can be deleted at any time.";
    public string AstrologyText =>
        "The questionnaire has nothing to do with astrology and the profile is not drawn from the chart. It is here to be set beside the chart, not to be explained by it.";
    public string Caution => InventoryService.Caution;
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
            OnPropertyChanged(nameof(Title));
            Compose(); // the houses or the dignities may have changed with a setting
            return;
        }
        Load();
    }

    // Works out where the person on screen stands: no sitting, one under way, or a
    // finished one to show.
    private void Load()
    {
        _sittings = [];
        _current = null;
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
            if (_current is not null)
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

    // Removes every answer saved for this person.
    public void DeleteAnswers()
    {
        if (Person is not { } person) return;
        _store?.Remove(person.Id);
        Load();
    }

    private void Ask(int page)
    {
        if (_current is null || _inventory is null) return;
        Stage = InventoryStage.Asking;
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
                     nameof(Domains), nameof(TakenText),
                 })
            OnPropertyChanged(name);
    }
}
