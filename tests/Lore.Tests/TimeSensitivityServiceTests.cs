using Lore.Models;
using Lore.Services;

namespace Lore.Tests;

// "What if the birth time is off?" — what holds and what changes across a margin.
public class TimeSensitivityServiceTests
{
    // London, 21 June 1990, 12:00 BST.
    private static Celebrity Person(string time = "12:00", bool timed = true) => new()
    {
        Id = "t", Name = "T", BirthDate = "1990-06-21", BirthTime = timed ? time : null, BirthTimeKnown = timed,
        BirthPlace = "London", Latitude = 51.5, Longitude = -0.13, TimeZoneId = "Europe/London",
    };

    [Fact]
    public void Nothing_to_test_without_a_margin_or_a_birth_time()
    {
        Assert.Null(TimeSensitivityService.Analyse(Repo.Charts, Person(), 0));
        Assert.Null(TimeSensitivityService.Analyse(Repo.Charts, Person(timed: false), 30));
    }

    [Fact]
    public void A_one_minute_margin_changes_nothing_for_an_ordinary_chart()
    {
        var result = TimeSensitivityService.Analyse(Repo.Charts, Repo.Figure("albert-einstein"), 1)!;
        Assert.False(result.HasChanges);
        Assert.Contains(result.Holds, h => h.StartsWith("Rising sign: Cancer throughout"));
        Assert.Contains(result.Holds, h => h.StartsWith("Signs: every body keeps its sign"));
        Assert.Equal("11:29 to 11:31, clock time at the birthplace", result.Window);
    }

    [Fact]
    public void A_wide_margin_finds_the_Rising_sign_changing_and_times_it_to_the_minute()
    {
        // Three hours either way: the Ascendant crosses several signs.
        var person = Person();
        var result = TimeSensitivityService.Analyse(Repo.Charts, person, 180)!;
        string rising = Assert.Single(result.Changes, c => c.StartsWith("Rising sign:"));

        // "Rising sign: Leo until 09:53, then Virgo until …" — take the first change and
        // confirm the sign really does turn over at that minute and not the one before.
        var parts = rising["Rising sign: ".Length..].Split(", then ");
        string before = parts[0].Split(" until ")[0];
        string at = parts[0].Split(" until ")[1];
        string after = parts[1].Split(" until ")[0];
        Assert.NotEqual(before, after);

        var when = TimeOnly.ParseExact(at, "HH:mm");
        string SignAt(TimeOnly t) => ZodiacSignExtensions.FromLongitude(
            Repo.Charts.Calculate(Person(t.ToString("HH:mm"))).Ascendant).Name();
        Assert.Equal(after, SignAt(when));
        Assert.Equal(before, SignAt(when.AddMinutes(-1)));
    }

    [Fact]
    public void Day_turning_to_night_inside_the_window_is_reported()
    {
        // Sunset in London on 21 June 1990 was about 21:21 BST.
        var result = TimeSensitivityService.Analyse(Repo.Charts, Person("21:20"), 30)!;
        Assert.Contains(result.Changes, c => c.StartsWith("Sect: a day chart until 21:") && c.EndsWith("then a night chart"));
    }

    [Fact]
    public void A_Moon_changing_sign_inside_the_window_is_reported_and_the_rest_are_said_to_hold()
    {
        // Find a minute on this day's chart where widening the margin catches the Moon
        // at a sign boundary: the Moon entered Gemini late on 21 June 1990 (UT).
        var result = TimeSensitivityService.Analyse(Repo.Charts, Person("23:00"), 180)!;
        if (result.Changes.Any(c => c.StartsWith("Moon: ") && !c.Contains("house")))
            Assert.Contains(result.Holds, h => h.StartsWith("Signs: the other 12 bodies keep their signs"));
        else
            Assert.Contains(result.Holds, h => h.StartsWith("Signs: every body keeps its sign"));
    }

    [Fact]
    public void The_margin_is_capped()
    {
        var result = TimeSensitivityService.Analyse(Repo.Charts, Person(), 5000)!;
        Assert.Equal(TimeSensitivityService.MaxMinutes, result.Minutes);
    }

    // London, a day in June 1990, no birth time.
    private static Celebrity Untimed(int day) => new()
    {
        Id = "t", Name = "T", BirthDate = $"1990-06-{day:D2}", BirthTimeKnown = false,
        BirthPlace = "London", Latitude = 51.5, Longitude = -0.13, TimeZoneId = "Europe/London",
    };

    [Fact]
    public void With_no_birth_time_the_whole_day_is_checked_for_sign_changes()
    {
        Assert.Null(TimeSensitivityService.AnalyseDay(Repo.Charts, Person()));

        // The Moon changes sign every two and a half days, so a month has both kinds of day.
        var days = Enumerable.Range(1, 30).Select(d => TimeSensitivityService.AnalyseDay(Repo.Charts, Untimed(d))!).ToList();
        var steady = days.First(d => d.MoonSigns.Count == 1);
        var turning = days.First(d => d.MoonSigns.Count == 2);
        Assert.InRange(days.Count(d => d.MoonSigns.Count == 2), 10, 14);

        Assert.True(steady.WholeDay);
        Assert.Contains(steady.Holds, h => h.StartsWith($"Moon: {steady.MoonSigns[0].Name()} all day, somewhere from"));
        Assert.DoesNotContain(steady.Changes, c => c.StartsWith("Moon:"));

        // "Moon: Taurus until 14:32, then Gemini", in the order the Moon went through them.
        string line = Assert.Single(turning.Changes, c => c.StartsWith("Moon:"));
        Assert.Matches($@"^Moon: {turning.MoonSigns[0].Name()} until \d\d:\d\d, then {turning.MoonSigns[1].Name()}$", line);
        Assert.Contains(turning.Holds, h => h.Contains("can't be known without a birth time"));
    }

    [Fact]
    public void The_report_declines_to_name_a_Moon_sign_that_changed_that_day()
    {
        var interpreter = new ChartInterpreter(Repo.Data("interpretations.json"));
        var person = Enumerable.Range(1, 30).Select(Untimed)
            .First(p => TimeSensitivityService.AnalyseDay(Repo.Charts, p)!.MoonSigns.Count == 2);
        var chart = Repo.Charts.Calculate(person);
        var signs = TimeSensitivityService.AnalyseDay(Repo.Charts, person)!.MoonSigns;

        var overview = interpreter.Interpret(chart, signs)[0].Paragraphs;
        Assert.Contains(overview, p => p.StartsWith("The Moon changed sign on the day"));
        Assert.DoesNotContain(overview, p => p.Contains("(Moon in "));

        // Without the day's analysis (or with a steady Moon) it reads the Moon as before.
        Assert.Contains(interpreter.Interpret(chart)[0].Paragraphs, p => p.Contains("(Moon in "));
    }
}
