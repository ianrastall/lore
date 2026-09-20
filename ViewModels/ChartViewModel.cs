using CommunityToolkit.Mvvm.ComponentModel;
using Lore.Models;
using Lore.Services;

namespace Lore.ViewModels;

public sealed partial class ChartViewModel : ObservableObject
{
    private readonly ChartInterpreter? _interpreter;

    public ChartViewModel(ChartInterpreter? interpreter = null)
    {
        _interpreter = interpreter;
    }

    [ObservableProperty]
    public partial NatalChart? Chart { get; set; }

    [ObservableProperty]
    public partial PlanetPosition? SelectedPlanet { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ReportSection> ReportSections { get; set; } = [];

    public string Title => Chart is null ? "" : Chart.Celebrity.Name;

    public string SubTitle => Chart is null ? "" :
        $"{Chart.Celebrity.BirthDate}  ·  {Chart.Celebrity.BirthPlace}" +
        (Chart.Celebrity.BirthTimeKnown ? $"  ·  {Chart.Celebrity.BirthTime}" : "  ·  time unknown");

    public string AscendantText => Chart is null ? "" :
        $"ASC {ZodiacSignExtensions.FromLongitude(Chart.Ascendant).Symbol()} " +
        $"{ZodiacSignExtensions.FromLongitude(Chart.Ascendant).Name()}";

    public string MidheavenText => Chart is null ? "" :
        $"MC {ZodiacSignExtensions.FromLongitude(Chart.Midheaven).Symbol()} " +
        $"{ZodiacSignExtensions.FromLongitude(Chart.Midheaven).Name()}";

    partial void OnChartChanged(NatalChart? value)
    {
        SelectedPlanet = null;
        ReportSections = (_interpreter is not null && value is not null)
            ? _interpreter.Interpret(value)
            : [];
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(SubTitle));
        OnPropertyChanged(nameof(AscendantText));
        OnPropertyChanged(nameof(MidheavenText));
    }
}
