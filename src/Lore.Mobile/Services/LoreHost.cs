using Lore.Services;
using Lore.ViewModels;

namespace Lore.Mobile.Services;

// Builds Lore's engine as the Windows app does (Lore.App's App.BuildMainWindow), from
// the files ContentFiles has unpacked. Loading the library is a separate step
// (LoadLibraryAsync), so the app can be shown while the list fills in.
public static class LoreHost
{
    private static ContentFiles.Folders? _folders;

    public static async Task<MainViewModel> BuildAsync()
    {
        var folders = _folders = await ContentFiles.EnsureAsync();
        string Data(string name) => Path.Combine(folders.Content, name);

        // The services read their files as they are made, so they are made off the UI thread.
        var (mainVm, settings) = await Task.Run(() =>
        {
            var celebSvc = new CelebrityService();
            var settings = new SettingsService();
            var chartSvc = new ChartService(folders.Ephemeris) { Settings = settings.Load() };
            var interpreter = new ChartInterpreter(Data("interpretations.json"));
            var cities = new CityService();
            var transits = new TransitService(chartSvc);
            var daily = new DailyInterpreter(Data("daily.json"));
            var synastry = new SynastryInterpreter(Data("synastry.json"));
            var vm = new MainViewModel(celebSvc, chartSvc, interpreter, new UserChartService(), cities, new HospitalService(),
                transits, daily, synastry, settings,
                new DavisonInterpreter(Data("davison.json")),
                new InventoryService(Data("inventory.json")), new InventoryStore(),
                new MirrorInterpreter(Data("mirror.json"), interpreter),
                new CharacterService(Data("character.json")), new LifeAreaService(Data("lifeareas.json")),
                new WritersViewModel(chartSvc, new WriterInterpreter(chartSvc, interpreter, synastry), cities, new WriterStore()),
                new SkyInterpreter(Data("sky.json"), daily));
            return (vm, settings);
        });
        Settings = settings;
        return mainVm;
    }

    // Reads the figures, the person's own charts and the cities, and scores every chart
    // for the list's marks. The list shows "Loading data…" until it is done.
    public static Task LoadLibraryAsync(MainViewModel main)
    {
        var folders = _folders ?? throw new InvalidOperationException("BuildAsync first.");
        string Data(string name) => Path.Combine(folders.Content, name);
        return main.InitializeAsync(Data("celebrities.json"), Data("cities.json"), Data("hospitals.json"));
    }

    // Written out when Android sends the app to the background, where it may be ended
    // without warning (see App.OnSleep).
    public static SettingsService? Settings { get; private set; }
}
