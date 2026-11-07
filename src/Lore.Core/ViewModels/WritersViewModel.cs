using CommunityToolkit.Mvvm.ComponentModel;
using Lore.Models;
using Lore.Services;

namespace Lore.ViewModels;

public sealed partial class WritersViewModel : ObservableObject
{
    private readonly ChartService? _charts;
    private readonly WriterInterpreter? _interpreter;
    private readonly WriterStore? _store;
    private readonly LatestOnly _search = new(), _reading = new();
    private IReadOnlyList<Celebrity> _people = [];
    private ChartSettings _resultSettings = ChartSettings.Default;
    private bool _loaded;
    private int _request;
    private Task _saving = Task.CompletedTask;
    private WriterBrief? _unsavedSnapshot;

    public WritersViewModel(ChartService? charts = null, WriterInterpreter? interpreter = null,
        CityService? cities = null, WriterStore? store = null)
    {
        _charts = charts; _interpreter = interpreter; Cities = cities ?? new(); _store = store;
    }
    public CityService Cities { get; }

    [ObservableProperty] public partial IReadOnlyList<WriterCandidate> Candidates { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<WriterGroup> Groups { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<WriterBrief> Saved { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<string> Categories { get; set; } = ["All categories"];
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial bool IsSaving { get; set; }
    [ObservableProperty] public partial string Error { get; set; } = "";
    [ObservableProperty] public partial string Summary { get; set; } = "Choose a place and a period, or search famous pairs and trios.";
    [ObservableProperty] public partial string BriefName { get; set; } = "Untitled character";
    [ObservableProperty] public partial string SaveProblem { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBrief), nameof(Sections), nameof(BirthNames), nameof(CanOpenSynastry))]
    public partial WriterBrief? Brief { get; set; }
    public bool HasBrief => Brief is not null;
    public bool CanOpenSynastry => Brief is { Kind: "Pair", Births.Count: 2 } or { Kind: "Trio", Births.Count: 3 };
    public IReadOnlyList<DailySection> Sections => Brief?.Sections ?? [];
    public IReadOnlyList<string> BirthNames => Brief?.Births.Select(b => b.Person.Name).ToArray() ?? [];

    public void SetPeople(IReadOnlyList<Celebrity> people)
    {
        _people = people.Where(p => p.Category != UserChartService.MyChartsCategory).ToArray();
        Categories = ["All categories", .. _people.Select(p => p.Category).Distinct().Order()];
        Invalidate();
    }

    public void Invalidate()
    {
        _request++; _search.Cancel(); _reading.Cancel(); IsBusy = false;
        Candidates = []; Groups = [];
        Summary = "Choose the filters and start a search. Existing briefs retain their original calculation settings.";
    }

    public async Task InitializeAsync()
    {
        if (_loaded || _store is null) return;
        _loaded = true;
        await ReloadSavedAsync();
    }

    public async Task ReloadSavedAsync()
    {
        if (_store is null) return;
        try { Saved = (await _store.LoadAsync()).OrderByDescending(b => b.CreatedUtc).ToArray(); Error = ""; }
        catch (Exception ex) { Error = "Saved Writers work could not be read; its file was left untouched. " + ex.Message; }
    }

    public async Task FindCharactersAsync(WriterCharacterQuery query)
    {
        if (_charts is null) { Error = "Writers is not initialized."; return; }
        CancelReading(); Error = ""; Candidates = []; Groups = [];
        _resultSettings = _charts.Settings;
        var service = new WriterCharacterService(_charts.With(_resultSettings));
        int request = ++_request;
        var progress = new Progress<string>(s => { if (request == _request) Summary = s; });
        await _search.Run(c => service.Find(query, c, progress), b => IsBusy = b,
            found => { Candidates = found; Summary = found.Count == 0 ? "No moments meet these anchor constraints. Change the period, place or anchor." :
                $"{found.Count:N0} birth anchors found. Choose one to read a character brief."; },
            ex => Error = ex.Message);
    }

    public async Task FindGroupsAsync(WriterGroupQuery query)
    {
        if (_charts is null) { Error = "Writers is not initialized."; return; }
        query = query with { AsOf = query.AsOf ?? DateOnly.FromDateTime(DateTime.Today) };
        CancelReading(); Error = ""; Candidates = []; Groups = [];
        _resultSettings = _charts.Settings;
        var service = new WriterGroupService(_charts.With(_resultSettings));
        int request = ++_request;
        var progress = new Progress<string>(s => { if (request == _request) Summary = s; });
        await _search.Run(c => service.Search(_people, query, c, progress), b => IsBusy = b,
            found => { Groups = found.Results; Summary = found.Summary + $" Same field; deaths by {query.DeathCutoff:yyyy-MM-dd}; age gap ≤10 years; birthplaces ≤{query.DistanceKm:0.#} km." +
                (found.Matches == 0 ? " No groups qualify; widen the birthplace distance or other optional filters." : ""); },
            ex => Error = ex.Message);
    }

    private void CancelReading() { _reading.Cancel(); Brief = null; }
    public void ClearSelection() => CancelReading();
    public void Cancel()
    {
        _request++; _search.Cancel(); _reading.Cancel(); IsBusy = false; Summary = "Search cancelled. No partial results are presented as a complete search.";
    }

    public async Task SelectCandidateAsync(WriterCandidate candidate)
    {
        if (_interpreter is null) return;
        var settings = _resultSettings;
        Error = "";
        await _reading.Run(_ => _interpreter.Character(candidate, settings), _ => { }, ShowBrief, ex => Error = ex.Message);
    }

    public async Task SelectGroupAsync(WriterGroup group)
    {
        if (_interpreter is null) return;
        var settings = _resultSettings;
        Error = "";
        await _reading.Run(_ => _interpreter.Group(group, settings), _ => { }, ShowBrief, ex => Error = ex.Message);
    }

    public void ShowBrief(WriterBrief brief)
    {
        _reading.Cancel();
        Brief = brief; BriefName = brief.Title;
    }

    public async Task SaveAsync()
    {
        if (_store is null || Brief is not { } brief || IsSaving) return;
        if (string.IsNullOrWhiteSpace(BriefName)) { Error = "Give the brief a name before saving it."; return; }
        var snapshot = brief with { Title = BriefName.Trim() };
        _unsavedSnapshot = snapshot;
        IsSaving = true; Error = ""; SaveProblem = "";
        _saving = SaveCoreAsync(snapshot);
        await _saving;
    }

    private async Task SaveCoreAsync(WriterBrief snapshot)
    {
        try
        {
            Saved = (await _store!.SaveAsync(snapshot)).OrderByDescending(b => b.CreatedUtc).ToArray();
            _unsavedSnapshot = null;
            if (Brief?.Id == snapshot.Id) Brief = snapshot;
            Summary = $"Saved {snapshot.Title}.";
        }
        catch (Exception ex) { SaveProblem = Error = "The brief was not saved. " + ex.Message; }
        finally { IsSaving = false; }
    }

    public async Task<bool> FinishSaveAsync() { await _saving; return SaveProblem.Length == 0; }

    public async Task RetrySaveAsync()
    {
        if (IsSaving || _unsavedSnapshot is not { } snapshot) return;
        IsSaving = true; SaveProblem = ""; Error = "";
        _saving = SaveCoreAsync(snapshot);
        await _saving;
    }

    public async Task<NatalChart?> PreviewAsync(int index)
    {
        if (_charts is null || Brief is not { } brief || index < 0 || index >= brief.Births.Count) return null;
        var birth = brief.Births[index];
        try { return await Task.Run(() => _charts.With(brief.Settings).CalculateAt(birth.Person, birth.Utc)); }
        catch (Exception ex) { Error = ex.Message; return null; }
    }
}
