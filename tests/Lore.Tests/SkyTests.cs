using System.Text.Json;
using Lore.Models;
using Lore.Services;
using NodaTime;
using Xunit;

namespace Lore.Tests;

// The Sky view: the day's sky read with no birth chart, from the aspects the planets make
// to one another.
public sealed class SkyTests
{
    private static readonly TransitService Transits = new(Repo.Charts);
    private static readonly SkyInterpreter Interpreter = new(Repo.Data("sky.json"), new DailyInterpreter(Repo.Data("daily.json")));

    private static SkyDay Scan(int y, int m, int d, DateTimeZone? zone = null, HomePlace? home = null) =>
        Transits.ScanSky(new DateOnly(y, m, d), zone ?? DateTimeZone.Utc, home);

    [Fact]
    public void The_great_conjunction_of_2020_is_exact_on_its_day_and_reads_as_background()
    {
        var day = Scan(2020, 12, 21);
        var gc = Assert.Single(day.Aspects, a => a.First == Planet.Jupiter && a.Second == Planet.Saturn);
        Assert.Equal(AspectType.Conjunction, gc.Aspect);
        Assert.Equal(TransitPhase.Exact, gc.Phase);
        Assert.True(gc.IsBackground);
        // Exact at about 18:20 UT.
        Assert.InRange(gc.PeakUtc, new DateTime(2020, 12, 21, 17, 30, 0), new DateTime(2020, 12, 21, 19, 0, 0));

        var reading = Interpreter.Compose(day, DateTimeZone.Utc);
        var themes = Assert.Single(reading.Sections, s => s.Heading == "Longer-running themes");
        Assert.Contains(themes.Items, i => i.Title!.Contains("Jupiter conjunct ♄ Saturn") && i.Text.Contains("twenty-year"));
        Assert.True(gc.Shown);
    }

    [Fact]
    public void Every_aspect_found_is_within_orb_and_exact_ones_are_exact_at_their_peak()
    {
        foreach (var date in new[] { new DateOnly(2026, 11, 6), new DateOnly(1969, 7, 20), new DateOnly(2024, 4, 8) })
        {
            var day = Transits.ScanSky(date, DateTimeZone.Utc);
            Assert.True(day.Aspects.Count > 0, $"no aspects on {date}");
            Assert.DoesNotContain(day.Aspects, a => a.First == Planet.Moon && a.Second == Planet.Sun);
            foreach (var a in day.Aspects)
            {
                Assert.True(SkyAspect.Rank(a.First) < SkyAspect.Rank(a.Second));
                Assert.InRange(a.MinOrb, 0, TransitService.SkyOrb(a.First));
                double jd = SwissEphemeris.DateTimeToJulianDay(a.PeakUtc);
                double lead = Repo.Charts.CalculateBody(jd, a.First)!.Longitude - Repo.Charts.CalculateBody(jd, a.Second)!.Longitude;
                double sep = Math.Abs(((lead % 360) + 540) % 360 - 180);
                Assert.True(Math.Abs(sep - a.Aspect.Angle()) <= a.MinOrb + 1e-6, $"{a.First} {a.Aspect} {a.Second}: {sep}");
                if (a.Phase == TransitPhase.Exact)
                {
                    Assert.InRange(a.PeakUtc, day.Day.StartUtc, day.Day.EndUtc);
                    Assert.True(Math.Abs(sep - a.Aspect.Angle()) < 0.001);
                }
            }
        }
    }

    [Fact]
    public void The_days_sky_events_include_eclipses_stations_and_the_faster_planets_changing_sign()
    {
        var eclipse = Scan(2024, 4, 8);
        Assert.Contains(eclipse.Events, e => e.Kind == SkyEventKind.SolarEclipse);

        var station = Scan(2024, 4, 1);
        Assert.Contains(station.Events, e => e.Kind == SkyEventKind.Station && e.Body == Planet.Mercury && e.Retrograde);

        var equinox = Scan(2024, 3, 20);
        var ingress = Assert.Single(equinox.Events, e => e.Kind == SkyEventKind.Ingress && e.Body == Planet.Sun);
        Assert.Equal(ZodiacSign.Aries, ingress.Sign);
        Assert.InRange(ingress.Utc, new DateTime(2024, 3, 20, 2, 50, 0), new DateTime(2024, 3, 20, 3, 20, 0));

        var reading = Interpreter.Compose(equinox, DateTimeZone.Utc);
        var also = Assert.Single(reading.Sections, s => s.Heading == "Also in the sky");
        Assert.Contains(also.Items, i => i.Title == "☉ Sun enters Aries" && i.Text.Contains("Aries"));

        // A station is reported once, as a timed event, not again among the notes.
        var stationReading = Interpreter.Compose(station, DateTimeZone.Utc);
        Assert.Single(stationReading.Sections.SelectMany(s => s.Items), i => i.Title?.Contains("Mercury turns retrograde") == true);
    }

    [Fact]
    public void The_corpus_has_a_line_for_every_pair_and_tone_that_can_occur_and_no_others()
    {
        var corpus = JsonDocument.Parse(File.ReadAllText(Repo.Data("sky.json"))).RootElement;
        var pairs = corpus.GetProperty("pairs").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString());
        var possible = SkyInterpreter.PossibleKeys().ToList();
        Assert.Equal(127, possible.Count);
        Assert.Equal(possible.OrderBy(k => k), pairs.Keys.OrderBy(k => k));
        Assert.All(pairs.Values, v => Assert.False(string.IsNullOrWhiteSpace(v)));
        Assert.Equal(pairs.Count, pairs.Values.Distinct().Count());
        foreach (var tone in Enum.GetValues<DayTone>())
            Assert.False(string.IsNullOrWhiteSpace(corpus.GetProperty("dayTones").GetProperty(tone.ToString()).GetString()));
    }

    [Fact]
    public void Over_two_months_every_aspect_found_has_a_corpus_line()
    {
        var possible = SkyInterpreter.PossibleKeys().ToHashSet();
        for (var date = new DateOnly(2026, 1, 1); date < new DateOnly(2026, 3, 1); date = date.AddDays(1))
            foreach (var a in Transits.ScanSky(date, DateTimeZone.Utc, aspectsOnly: true).Aspects)
                Assert.Contains($"{a.First.Name()}|{a.Tone}|{a.Second.Name()}", possible);
    }

    [Fact]
    public void The_reading_is_deterministic_and_its_tone_matches_the_week_strip()
    {
        var zone = DateTimeZoneProviders.Tzdb["America/Chicago"];
        var first = Interpreter.Compose(Scan(2026, 11, 6, zone), zone);
        var again = Interpreter.Compose(Scan(2026, 11, 6, zone), zone);
        Assert.Equal(Flatten(first), Flatten(again));
        Assert.Equal(first.Trace, again.Trace);
        Assert.Equal(first.Tone, SkyInterpreter.ToneOf(Transits.ScanSky(new DateOnly(2026, 11, 6), zone, aspectsOnly: true)));

        Assert.Equal("The day at a glance", first.Sections[0].Heading);
        Assert.Contains(first.Sections, s => s.Heading == "Where the planets are");
        Assert.Contains(first.Sections[0].Items, i => i.Title?.Contains("Friday") == true && i.Title.Contains("Venus"));
        Assert.Equal(first.Aspects.Count, first.Trace.Count);
        Assert.Equal(first.Aspects.Count(a => a.Shown), first.Trace.Count(t => t.Contains("✓ in the reading")));
    }

    [Fact]
    public void The_Moons_contacts_are_the_exact_ones_in_time_order()
    {
        var day = Scan(2026, 11, 6);
        var reading = Interpreter.Compose(day, DateTimeZone.Utc);
        var lunar = day.Aspects.Where(a => a.IsLunar && a.Phase == TransitPhase.Exact).OrderBy(a => a.PeakUtc).ToList();
        Assert.NotEmpty(lunar);
        var section = Assert.Single(reading.Sections, s => s.Heading == "The Moon's contacts");
        Assert.Equal(lunar.Select(a => $"{a.First.Symbol()} {a.First.Name()} {a.Aspect.Verb()} {a.Second.Symbol()} {a.Second.Name()}"),
            section.Items.Where(i => i.HasTitle).Select(i => i.Title));
    }

    [Fact]
    public void A_day_with_nothing_beyond_the_Moon_reads_as_quiet()
    {
        var real = Scan(2026, 11, 6);
        var quiet = new SkyDay { Day = real.Day, Aspects = [], Events = [] };
        Assert.Equal(DayTone.Quiet, SkyInterpreter.ToneOf(quiet));
        var reading = Interpreter.Compose(quiet, DateTimeZone.Utc);
        Assert.Contains(reading.Sections.Single(s => s.Heading == "Today's aspects").Items, i => i.Text.Contains("no particular foreground"));
    }

    [Fact]
    public void A_home_place_adds_the_planetary_hours_and_the_exports_carry_the_reading()
    {
        var home = new HomePlace("London", 51.5074, -0.1278);
        var zone = DateTimeZoneProviders.Tzdb["Europe/London"];
        var reading = Interpreter.Compose(Transits.ScanSky(new DateOnly(2026, 6, 21), zone, home), zone);
        Assert.Contains(reading.Sections, s => s.Heading == "Planetary hours");

        string text = System.Text.Encoding.UTF8.GetString(SkyInterpreter.ToText(reading));
        Assert.Contains("Planetary mood", text);
        Assert.Contains("ALL ASPECTS IN EFFECT", text);
        Assert.Contains(reading.Sections[1].Items[0].Text, text);
        Assert.True(DailyExportService.SkyToPdf(reading, null).Length > 1000);
    }

    [Fact]
    public void Dates_outside_the_ephemeris_range_are_refused()
    {
        Assert.Throws<ArgumentException>(() => Transits.ScanSky(new DateOnly(1199, 12, 31), DateTimeZone.Utc));
        Assert.Throws<ArgumentException>(() => Transits.ScanSky(new DateOnly(2400, 1, 1), DateTimeZone.Utc));
    }

    private static string Flatten(SkyReading r) => string.Join("\n", r.Sections.SelectMany(s =>
        s.Items.Select(i => $"{s.Heading}|{i.Title}|{i.Meta}|{i.Text}")));

    [Fact]
    public void The_view_model_reads_the_day_and_its_week_only_while_open() => UiThread.Run(async () =>
    {
        var zone = DateTimeZoneProviders.Tzdb["America/Chicago"];
        var vm = new Lore.ViewModels.SkyViewModel(Transits, Interpreter, zone) { IsActive = false };
        vm.Date = new DateOnly(2026, 11, 6);
        await Task.Delay(50);
        Assert.Null(vm.Reading);
        Assert.Empty(vm.Week);

        async Task Settled()
        {
            for (int i = 0; vm.IsBusy || vm.Reading?.Date != vm.Date || vm.Week.Count == 0 ||
                            vm.Week[0].Date > vm.Date || vm.Week[^1].Date < vm.Date; i++)
            {
                Assert.True(i < 3000, "The Sky reading did not settle.");
                await Task.Delay(10);
            }
        }

        vm.IsActive = true;
        await Settled();
        Assert.Equal("", vm.ErrorText);
        Assert.Equal(7, vm.Week.Count);
        Assert.Equal(new DateOnly(2026, 11, 2), vm.Week[0].Date);   // the Monday
        Assert.Equal(vm.Reading!.Tone, vm.Week.Single(d => d.Date == vm.Date).Tone);
        Assert.Contains("The day reads as", vm.ToneText);
        Assert.NotEmpty(vm.Sections);

        // A day in the same week keeps the strip; the next week brings its own.
        var week = vm.Week;
        vm.ShowDay(new DateOnly(2026, 11, 8));
        await Settled();
        Assert.Same(week, vm.Week);
        vm.NextDayCommand.Execute(null);
        await Settled();
        Assert.Equal(new DateOnly(2026, 11, 9), vm.Week[0].Date);

        // A home place adds the planetary hours.
        vm.Home = new HomePlace("Chicago", 41.8781, -87.6298);
        await Task.Delay(20);
        await Settled();
        Assert.Contains(vm.Sections, s => s.Heading == "Planetary hours");

        vm.Date = new DateOnly(2400, 6, 1);
        await Task.Delay(20);
        Assert.Null(vm.Reading);
        Assert.Contains("between 1200 and 2399", vm.ErrorText);
    });
}
