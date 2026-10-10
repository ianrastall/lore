using System.Text.Json;
using Lore.Models;
using Lore.Services;
using Lore.ViewModels;

namespace Lore.Tests;

// The main view model's handling of selection: what is on screen and what may be
// exported while charts are being calculated, lists rebuilt and settings changed, all
// of which overlap. Each test runs on one thread with a message queue, as the window
// does, and stands in for the list box, which drops its selection whenever the list
// under it is replaced.
public sealed class ViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lore-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private async Task<MainViewModel> Start()
    {
        // A library of three keeps each rebuild of the list quick.
        Directory.CreateDirectory(_dir);
        string library = Path.Combine(_dir, "celebrities.json");
        File.WriteAllText(library, JsonSerializer.Serialize(
            new[] { "elvis-presley", "marilyn-monroe", "albert-einstein" }.Select(Repo.Figure)));

        var charts = new ChartService(Repo.Ephemeris); // its own: the tests change its settings
        var daily = new DailyInterpreter(Repo.Data("daily.json"));
        var vm = new MainViewModel(
            new CelebrityService(), charts, new ChartInterpreter(Repo.Data("interpretations.json")),
            new UserChartService(Path.Combine(_dir, "store")), new CityService(), new HospitalService(),
            new TransitService(charts), daily, new SynastryInterpreter(Repo.Data("synastry.json")));
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.DisplayedCelebrities)) vm.SelectedCelebrity = null;
        };
        await vm.InitializeAsync(library, Repo.Data("cities.json"), Repo.Data("hospitals.json"));
        return vm;
    }

    private static Celebrity Row(MainViewModel vm, string id) => vm.DisplayedCelebrities.Single(c => c.Id == id);

    private static async Task Settled(MainViewModel vm)
    {
        for (int i = 0; vm.IsLoading; i++)
        {
            Assert.True(i < 3000, "The view model did not settle.");
            await Task.Delay(10);
        }
    }

    private static Celebrity Own(string id, string name, string time) => new()
    {
        Id = id, Name = name, Category = UserChartService.MyChartsCategory,
        BirthDate = "1980-05-17", BirthTime = time, BirthTimeKnown = true,
        BirthPlace = "Leeds, United Kingdom", Latitude = 53.8, Longitude = -1.55,
        TimeZoneId = "Europe/London",
    };

    [Fact]
    public void Nothing_is_exported_while_the_chart_on_screen_is_the_previous_persons() => UiThread.Run(async () =>
    {
        var vm = await Start();
        vm.SelectedCelebrity = Row(vm, "elvis-presley");
        await Settled(vm);
        Assert.True(vm.ChartIsCurrent);

        vm.SelectedCelebrity = Row(vm, "marilyn-monroe");
        // Marilyn is selected and being calculated; Elvis is still what is drawn.
        Assert.Equal("elvis-presley", vm.ChartVM.Chart!.Celebrity.Id);
        Assert.False(vm.ChartIsCurrent);

        await Settled(vm);
        Assert.Equal("marilyn-monroe", vm.ChartVM.Chart!.Celebrity.Id);
        Assert.True(vm.ChartIsCurrent);

        // Typing in the search box drops the selection but not the chart, which is
        // still the current one.
        vm.SearchText = "zzz";
        Assert.Null(vm.SelectedCelebrity);
        Assert.True(vm.ChartIsCurrent);
    });

    [Fact]
    public void A_chart_that_cannot_be_calculated_does_not_leave_the_last_one_on_screen() => UiThread.Run(async () =>
    {
        var vm = await Start();
        vm.SelectedCelebrity = Row(vm, "elvis-presley");
        await Settled(vm);

        vm.SelectedCelebrity = new Celebrity { Id = "broken", Name = "Broken", BirthDate = "not a date" };
        await Settled(vm);

        Assert.Null(vm.ChartVM.Chart);
        Assert.Null(vm.DailyVM.Chart);
        Assert.StartsWith("Chart error", vm.StatusMessage);
    });

    [Fact]
    public void Settings_changed_twice_in_quick_succession_end_with_the_last_ones_on_screen() => UiThread.Run(async () =>
    {
        var vm = await Start();
        vm.SelectedCelebrity = Row(vm, "elvis-presley");
        await Settled(vm);

        // The second change arrives while the list is still being rescored for the first.
        var first = vm.ApplySettingsAsync(new ChartSettings(HouseSystem.Koch, NodeType.True, LilithType.True));
        var second = vm.ApplySettingsAsync(new ChartSettings(HouseSystem.Equal));
        await Task.WhenAll(first, second);
        await Settled(vm);

        Assert.Equal("elvis-presley", vm.SelectedCelebrity?.Id);
        Assert.Equal(new ChartSettings(HouseSystem.Equal), vm.ChartVM.Chart!.Settings);
        Assert.Equal("Equal houses", vm.ChartVM.Chart.HouseSystemLabel);
        Assert.True(vm.ChartIsCurrent);
    });

    [Fact]
    public void A_chart_renamed_out_of_the_search_that_found_it_is_still_shown() => UiThread.Run(async () =>
    {
        var vm = await Start();
        await vm.AddCustomChartAsync(Own("user-1", "Original Person", "12:00"));
        await Settled(vm);
        vm.SearchText = "Original";
        vm.SelectedCelebrity = Assert.Single(vm.DisplayedCelebrities);
        await Settled(vm);

        await vm.UpdateCustomChartAsync(Own("user-1", "Renamed Person", "13:00"));
        await Settled(vm);

        Assert.Equal("Renamed Person", vm.SelectedCelebrity?.Name);
        Assert.Equal("", vm.SearchText);
        Assert.Equal("13:00", vm.ChartVM.Chart!.Celebrity.BirthTime);
    });

    [Fact]
    public void A_chart_added_while_searching_for_someone_else_is_selected() => UiThread.Run(async () =>
    {
        var vm = await Start();
        vm.SearchText = "Elvis";
        vm.SelectedCelebrity = Row(vm, "elvis-presley");
        await Settled(vm);

        await vm.AddCustomChartAsync(Own("user-1", "New Person", "12:00"));
        await Settled(vm);

        Assert.Equal("New Person", vm.SelectedCelebrity?.Name);
        Assert.Equal("user-1", vm.ChartVM.Chart!.Celebrity.Id);
    });

    [Fact]
    public void The_Synastry_view_switches_between_the_comparison_and_the_Davison_chart() => UiThread.Run(async () =>
    {
        var charts = new ChartService(Repo.Ephemeris);
        var vm = new SynastryViewModel(charts, new SynastryInterpreter(Repo.Data("synastry.json")),
            new DavisonInterpreter(Repo.Data("davison.json")));
        async Task Built()
        {
            for (int i = 0; vm.IsBusy; i++)
            {
                Assert.True(i < 3000, "The synastry view did not settle.");
                await Task.Delay(10);
            }
        }

        vm.Chart = charts.Calculate(Repo.Figure("elvis-presley"));
        vm.Partner = Repo.Figure("marilyn-monroe");
        await Built();

        // The comparison first: its sections, and no Davison chart on the wheel.
        Assert.True(vm.ComparisonShown);
        Assert.Equal("At a glance", vm.Sections[0].Heading);
        Assert.StartsWith("Inner wheel: Elvis Presley", vm.WheelCaption);
        Assert.NotNull(vm.DavisonChart);
        Assert.NotNull(vm.DavisonChart!.Events); // looked up, for its worksheet

        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        vm.ShowDavison = true;
        Assert.True(vm.DavisonShown);
        Assert.Equal("The relationship's own chart", vm.Sections[0].Heading);
        Assert.Equal("The Davison chart of Elvis Presley and Marilyn Monroe", vm.WheelCaption);
        Assert.Contains(nameof(SynastryViewModel.Sections), changed);
        Assert.Contains(nameof(SynastryViewModel.DavisonShown), changed);

        // The choice is kept when someone else is picked, and nothing of the last
        // pair is shown while the new one is worked out.
        vm.Partner = Repo.Figure("albert-einstein");
        Assert.Empty(vm.Sections);
        await Built();
        Assert.True(vm.DavisonShown);
        Assert.Equal("Elvis Presley & Albert Einstein", vm.Davison!.Title);

        // With nobody to compare with there is no Davison chart to show.
        vm.Partner = null;
        Assert.False(vm.DavisonShown);
        Assert.Null(vm.DavisonChart);
    });

    [Fact]
    public void The_place_a_return_is_cast_for_is_kept_when_the_same_persons_chart_is_recalculated() => UiThread.Run(async () =>
    {
        var charts = new ChartService(Repo.Ephemeris);
        var timing = new TimingViewModel(new TimingService(charts));
        async Task Built()
        {
            for (int i = 0; timing.IsBusy; i++)
            {
                Assert.True(i < 3000, "The timing view did not settle.");
                await Task.Delay(10);
            }
        }
        var london = new ReturnPlace("London", 51.5, -0.13);

        timing.Chart = charts.Calculate(Repo.Figure("elvis-presley"));
        timing.Place = london;

        // The same person again, with other houses; and with no chart in between.
        timing.Chart = charts.With(new ChartSettings(HouseSystem.WholeSign)).Calculate(Repo.Figure("elvis-presley"));
        Assert.Equal(london, timing.Place);
        timing.Chart = null;
        timing.Chart = charts.Calculate(Repo.Figure("elvis-presley"));
        Assert.Equal(london, timing.Place);
        await Built();
        Assert.Equal(london, timing.Reading!.Return!.Place);

        // Somebody else: the place was Elvis's.
        timing.Chart = charts.Calculate(Repo.Figure("marilyn-monroe"));
        Assert.Null(timing.Place);
        await Built();
    });
}

// Runs a test on one thread that also takes the continuations of everything it awaits,
// in turn, as a window's thread does.
internal static class UiThread
{
    public static void Run(Func<Task> test)
    {
        var previous = SynchronizationContext.Current;
        var queue = new Queue();
        SynchronizationContext.SetSynchronizationContext(queue);
        try
        {
            var running = test();
            running.ContinueWith(_ => queue.Close(), TaskScheduler.Default);
            queue.Pump();
            running.GetAwaiter().GetResult();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private sealed class Queue : SynchronizationContext
    {
        private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback Work, object? State)> _posted = [];

        public override void Post(SendOrPostCallback d, object? state)
        {
            // Work still arriving after the test has finished has nobody to run it.
            try { _posted.Add((d, state)); }
            catch (InvalidOperationException) { }
        }

        public void Close() => _posted.CompleteAdding();

        public void Pump()
        {
            foreach (var (work, state) in _posted.GetConsumingEnumerable())
                work(state);
        }
    }
}
