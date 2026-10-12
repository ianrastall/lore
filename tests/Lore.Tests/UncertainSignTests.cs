using Lore.Models;
using Lore.Services;
using NodaTime;

namespace Lore.Tests;

// A chart with no birth time is placed for noon, and a body that changed sign that day
// may not have been in the sign it is shown in. Every reading that speaks of a body in
// its sign is told which those are by the same scan of the day, and none of them reads
// the noon guess as the fact: the same untimed chart, through each reading in turn.
public class UncertainSignTests
{
    private static readonly ChartInterpreter Report = new(Repo.Data("interpretations.json"));
    private static readonly MirrorInterpreter Mirror = new(Repo.Data("mirror.json"), Report);
    private static readonly InventoryService Inventory = new(Repo.Data("inventory.json"));

    // Chicago, 1 January 1990, time unknown: the Moon went from Aquarius into Pisces
    // that morning, and noon has it in Pisces.
    private static Celebrity Chicago(string date = "1990-01-01", bool timed = false) => new()
    {
        Id = "user-1", Name = "Sam Example", Category = UserChartService.MyChartsCategory,
        BirthDate = date, BirthTime = timed ? "12:00" : null, BirthTimeKnown = timed,
        BirthPlace = "Chicago", Latitude = 41.8781, Longitude = -87.6298, TimeZoneId = "America/Chicago",
    };

    // Answers that mark the Moon out (so there is something it would be compared on).
    private static InventoryProfile Marked()
    {
        var answers = Enumerable.Repeat(3, 120).ToArray();
        foreach (var item in Inventory.Instrument.Items)
        {
            bool high = item.Facet[0] is 'N' or 'A' or 'E';
            answers[item.N - 1] = high == item.Reversed ? 1 : 5;
        }
        return Inventory.Score(answers, "Sam Example", new DateOnly(2026, 10, 10));
    }

    private static string AllText(MirrorReading r) =>
        string.Join("\n", r.Sections.SelectMany(s => s.Items).Select(i => $"{i.Title}\n{i.Meta}\n{i.Text}")) + "\n" +
        string.Join("\n", r.ReportParagraphs.Concat(r.OverviewLines.Values).Concat(r.AspectLines.Values)
            .Concat(r.ElementLines.Values).Concat(r.ModalityLines.Values).Concat(r.Planets.Select(p => p.ReportLine)));

    [Fact]
    public void The_scan_of_the_day_names_every_body_that_changed_sign_and_no_other()
    {
        var person = Chicago();
        var chart = Repo.Charts.Calculate(person);
        var day = TimeSensitivityService.AnalyseDay(Repo.Charts, person)!;

        Assert.Equal(ZodiacSign.Pisces, chart.GetPlanet(Planet.Moon)!.Sign);   // the noon guess
        Assert.Equal([ZodiacSign.Aquarius, ZodiacSign.Pisces], day.SignChanges[Planet.Moon]);
        Assert.Equal(day.MoonSigns, day.SignChanges[Planet.Moon]);
        Assert.Equal(day.Changes.Count, day.SignChanges.Count);

        Assert.Equal(day.SignChanges[Planet.Moon], TimeSensitivity.SignsInDoubt(chart, day, Planet.Moon));
        Assert.Null(TimeSensitivity.SignsInDoubt(chart, day, Planet.Sun));     // Capricorn all day
        // With no scan to hand, the Moon is not taken on trust; the slow bodies are.
        Assert.Empty(TimeSensitivity.SignsInDoubt(chart, null, Planet.Moon)!);
        Assert.Null(TimeSensitivity.SignsInDoubt(chart, null, Planet.Saturn));
        // A timed chart has nothing in doubt.
        Assert.Null(TimeSensitivity.SignsInDoubt(Repo.Charts.Calculate(Chicago(timed: true)), null, Planet.Moon));
    }

    [Fact]
    public void The_chart_and_answers_reading_does_not_read_a_Moon_sign_that_is_not_known()
    {
        var person = Chicago();
        var chart = Repo.Charts.Calculate(person);
        var day = TimeSensitivityService.AnalyseDay(Repo.Charts, person)!;
        var profile = Marked();

        var reading = Mirror.Compose(chart, profile, day);
        var moon = reading.Planets.Single(p => p.Planet == Planet.Moon);
        string text = AllText(reading);

        // Named as the two it may be, compared with neither.
        Assert.Equal([ZodiacSign.Aquarius, ZodiacSign.Pisces], moon.SignsInDoubt);
        Assert.Empty(moon.Comparisons);
        Assert.Contains("not known (Aquarius or Pisces, depending on the hour)", moon.AgainstSign);
        var item = reading.Sections.Single(s => s.Heading == "How each planet is being lived").Items.Single(i => i.Title?.Contains("Moon") == true);
        Assert.StartsWith("The chart: Moon in Aquarius or Pisces, depending on the hour of birth, the planet of", item.Text);
        Assert.DoesNotContain("Moon in Pisces", text);
        Assert.DoesNotContain("the Moon in Pisces", text);
        Assert.DoesNotContain("the Moon in Aquarius would", text);
        Assert.False(reading.OverviewLines.ContainsKey("Moon"));

        // Nor its aspects, which seven degrees either way may make or unmake; nor the
        // balance of elements, which counts it by its sign.
        Assert.DoesNotContain(reading.AspectLines.Keys, k => k.Split('|').Contains("Moon"));
        Assert.Empty(reading.ElementLines);
        Assert.Empty(reading.ModalityLines);
        Assert.Contains(reading.Sections[0].Items, i => i.Text.Contains("a planet that changed sign on the day is not read by its sign"));

        // A planet whose sign is not in doubt is read as it always was.
        var sun = reading.Planets.Single(p => p.Planet == Planet.Sun);
        Assert.Null(sun.SignsInDoubt);
        Assert.Contains("Sun in Capricorn", text);

        // Without the scan the Moon is still not read for noon: only less can be said.
        var blind = Mirror.Compose(chart, profile);
        Assert.Empty(blind.Planets.Single(p => p.Planet == Planet.Moon).SignsInDoubt!);
        Assert.Contains("Moon, its sign not known without a birth time", AllText(blind));
        Assert.DoesNotContain("Moon in Pisces", AllText(blind));

        // The same person with a birth time: nothing in doubt, and the Moon read in full.
        var timedChart = Repo.Charts.Calculate(Chicago(timed: true));
        var timed = Mirror.Compose(timedChart, profile);
        Assert.Null(timed.Planets.Single(p => p.Planet == Planet.Moon).SignsInDoubt);
        Assert.Contains("Moon in Pisces", AllText(timed));
    }

    [Fact]
    public void A_Moon_that_kept_its_sign_all_day_is_read_by_it_even_without_a_birth_time()
    {
        // The next day the Moon was in Pisces from midnight to midnight.
        var person = Chicago("1990-01-02");
        var day = TimeSensitivityService.AnalyseDay(Repo.Charts, person)!;
        Assert.False(day.SignChanges.ContainsKey(Planet.Moon));

        var reading = Mirror.Compose(Repo.Charts.Calculate(person), Marked(), day);
        Assert.Null(reading.Planets.Single(p => p.Planet == Planet.Moon).SignsInDoubt);
        Assert.Contains("The chart: Moon in Pisces, the planet of", AllText(reading));
        // Its place within the sign is still not known, so its aspects are still left out.
        Assert.DoesNotContain(reading.AspectLines.Keys, k => k.Split('|').Contains("Moon"));
    }

    [Fact]
    public void Every_reading_of_the_same_untimed_chart_leaves_the_uncertain_Moon_unread()
    {
        var person = Chicago();
        var chart = Repo.Charts.Calculate(person);
        var day = TimeSensitivityService.AnalyseDay(Repo.Charts, person)!;
        var answers = Mirror.Compose(chart, Marked(), day);

        // The Report: says the Moon sign cannot be given, and takes nothing from the
        // answers that leans on a sign for it.
        var report = Report.Interpret(chart, day.MoonSigns, answers, day.SignChanges);
        var overview = report.Single(s => s.Heading == "Overview").Paragraphs;
        Assert.Contains(overview, p => p.StartsWith("The Moon changed sign on the day Sam was born — it was in Aquarius or Pisces"));
        string moonLine = report.Single(s => s.Heading == "The Planets").Paragraphs.Single(p => p.Contains(" Moon in "));
        Assert.Contains("This is its place at noon: on the day it was in Aquarius or Pisces", moonLine);
        Assert.DoesNotContain(report.SelectMany(s => s.Paragraphs), p => p.Contains("than the Moon in") || p.Contains("that the Moon in"));

        // The Daily, the Forecast, the Timing reading and a comparison with someone else
        // all leave the natal Moon of an untimed chart out altogether.
        var transits = new TransitService(Repo.Charts);
        var date = new DateOnly(2026, 10, 12);
        Assert.DoesNotContain(transits.Scan(chart, date, DateTimeZone.Utc).Events, e => e.Target == NatalPoint.Of(Planet.Moon));
        Assert.DoesNotContain(transits.Forecast(chart, date, 91, DateTimeZone.Utc), p => p.Target == NatalPoint.Of(Planet.Moon));
        var timing = new TimingService(Repo.Charts).Compose(chart, date, DateTimeZone.Utc);
        Assert.Contains(timing.Sections.SelectMany(s => s.Items), i => i.Meta?.Contains("the Moon and the angles are left out") == true);
        var other = Repo.Charts.Calculate(Repo.Figure("elvis-presley"));
        Assert.DoesNotContain(SynastryService.Compare(other, chart).Aspects, a => a.Second == NatalPoint.Of(Planet.Moon));
    }

    [Fact]
    public void A_Sun_that_changed_sign_that_day_is_treated_as_the_Moon_is()
    {
        // 20 March 1990: the Sun entered Aries in the afternoon, Chicago time.
        var person = Chicago("1990-03-20");
        var chart = Repo.Charts.Calculate(person);
        var day = TimeSensitivityService.AnalyseDay(Repo.Charts, person)!;
        Assert.Equal([ZodiacSign.Pisces, ZodiacSign.Aries], day.SignChanges[Planet.Sun]);

        var reading = Mirror.Compose(chart, Marked(), day);
        var sun = reading.Planets.Single(p => p.Planet == Planet.Sun);
        Assert.Equal(day.SignChanges[Planet.Sun], sun.SignsInDoubt);
        Assert.Empty(sun.Comparisons);
        Assert.False(reading.OverviewLines.ContainsKey("Sun"));
        Assert.Contains("The chart: Sun in Pisces or Aries, depending on the hour of birth", AllText(reading));

        var report = Report.Interpret(chart, day.MoonSigns, reading, day.SignChanges);
        var overview = report.Single(s => s.Heading == "Overview").Paragraphs;
        Assert.Contains(overview, p => p.StartsWith("The Sun changed sign on the day Sam was born — it was in Pisces or Aries"));
        Assert.Contains("This is its place at noon: on the day it was in Pisces or Aries",
            report.Single(s => s.Heading == "The Planets").Paragraphs.Single(p => p.Contains(" Sun in ")));

        // The heading over the chart names both, as it does for the Moon.
        var vm = new ViewModels.ChartViewModel(Report, Repo.Charts);
        vm.Show(chart, day);
        Assert.Contains("☉ Sun Pisces or Aries", vm.BigThreeText);
    }

    [Fact]
    public void The_view_passes_the_scan_on_so_the_reading_on_screen_is_the_careful_one()
    {
        string dir = Path.Combine(Path.GetTempPath(), "lore-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var person = Chicago();
            var chart = Repo.Charts.Calculate(person);
            var day = TimeSensitivityService.AnalyseDay(Repo.Charts, person)!;
            var store = new InventoryStore(dir);
            store.Save(person.Id, [new InventorySitting { Started = "2026-10-10", Completed = "2026-10-10", Answers = Enumerable.Repeat(4, 120).ToArray() }]);

            var vm = new ViewModels.InventoryViewModel(Inventory, store, Mirror);
            vm.ShowChart(chart, day);
            Assert.Equal(day.SignChanges[Planet.Moon], vm.Reading!.Planets.Single(p => p.Planet == Planet.Moon).SignsInDoubt);
            Assert.Contains("Aquarius or Pisces", System.Text.Encoding.UTF8.GetString(vm.ReadingText()));

            // A scan that came with one chart is never applied to another.
            vm.Chart = Repo.Charts.Calculate(Chicago("1990-01-02"));
            Assert.Empty(vm.Reading!.Planets.Single(p => p.Planet == Planet.Moon).SignsInDoubt!);
            store.Flush();
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
