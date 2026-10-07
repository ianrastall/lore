using Lore.Models;
using Lore.Services;
using NodaTime;

namespace Lore.Tests;

// Solar returns, profections, secondary progressions, solar arc and relocation.
public class TimingServiceTests
{
    private static readonly TimingService Timing = new(Repo.Charts, house => $"topic {house}");
    private static readonly ReturnPlace Tokyo = new("Tokyo, Japan", 35.6895, 139.6917);

    private static DailySection Section(TimingReading reading, string headingStart) =>
        reading.Sections.Single(s => s.Heading.StartsWith(headingStart));
    private static NatalChart Chart(string id = "albert-einstein") => Repo.Charts.Calculate(Repo.Figure(id));

    private static double Apart(double a, double b)
    {
        double d = Math.Abs(a - b) % 360;
        return d > 180 ? 360 - d : d;
    }

    [Theory]
    [InlineData("albert-einstein", 1905)]
    [InlineData("albert-einstein", 2026)]
    [InlineData("elvis-presley", 1956)]
    [InlineData("roger-federer", 2003)]
    public void At_a_solar_return_the_Sun_is_back_at_its_birth_place_near_the_birthday(string id, int year)
    {
        var natal = Chart(id);
        var solar = Timing.SolarReturn(natal, year)!;

        double natalSun = natal.GetPlanet(Planet.Sun)!.Longitude;
        Assert.True(Apart(natalSun, solar.Chart.GetPlanet(Planet.Sun)!.Longitude) < 0.00002); // within a second of time
        Assert.Equal(year, solar.Utc.Year);

        var born = natal.CalculatedForUtc;
        var birthday = new DateTime(year, born.Month, born.Day, born.Hour, born.Minute, 0);
        Assert.InRange((solar.Utc - birthday).TotalDays, -1.5, 1.5);

        // Cast for the birthplace, with angles of its own.
        Assert.Equal(12, solar.Chart.Houses.Count);
        Assert.True(Apart(solar.Chart.Ascendant, natal.Ascendant) > 0 || year == born.Year);
    }

    [Fact]
    public void A_return_cast_for_another_place_keeps_its_moment_and_takes_that_places_angles()
    {
        var natal = Chart(); // Einstein, born in Ulm
        var tokyo = new ReturnPlace("Tokyo, Japan", 35.6895, 139.6917);
        var home = Timing.SolarReturn(natal, 1922)!;
        var away = Timing.SolarReturn(natal, 1922, tokyo)!;

        // The same instant and the same sky…
        Assert.Equal(home.Utc, away.Utc);
        Assert.Equal(home.Chart.GetPlanet(Planet.Moon)!.Longitude, away.Chart.GetPlanet(Planet.Moon)!.Longitude, 9);
        Assert.Null(home.Place);
        Assert.Same(tokyo, away.Place);

        // …under a different horizon: the angles are those of a chart cast in Tokyo then.
        Assert.True(Apart(home.Chart.Ascendant, away.Chart.Ascendant) > 30);
        var bornThere = Repo.Charts.CalculateAt(new Celebrity
        {
            Id = "t", Name = "T", BirthDate = "1922-03-14", BirthTimeKnown = true,
            BirthPlace = tokyo.Name, Latitude = tokyo.Latitude, Longitude = tokyo.Longitude,
        }, away.Utc);
        Assert.Equal(bornThere.Ascendant, away.Chart.Ascendant, 9);
        Assert.Equal(bornThere.Midheaven, away.Chart.Midheaven, 9);
        Assert.Equal(bornThere.Houses.Select(h => h.Longitude), away.Chart.Houses.Select(h => h.Longitude));

        // The Midheaven turns with the longitude: about 130° of it between Ulm and Tokyo.
        double turned = ((away.Chart.Armc - home.Chart.Armc) % 360 + 360) % 360;
        Assert.Equal(tokyo.Longitude - natal.Celebrity.Longitude, turned, 6);
    }

    [Fact]
    public void The_reading_says_which_place_the_return_was_cast_for()
    {
        var natal = Chart();
        var asOf = new DateOnly(2026, 10, 4);

        var home = Timing.Compose(natal, asOf, DateTimeZone.Utc);
        Assert.Contains($"cast for the birthplace, {natal.Celebrity.BirthPlace}", home.Sections[0].Items[0].Meta);

        var away = Timing.Compose(natal, asOf, DateTimeZone.Utc, new ReturnPlace("Tokyo, Japan", 35.6895, 139.6917));
        Assert.Contains("cast for Tokyo, Japan, not the birthplace", away.Sections[0].Items[0].Meta);
        Assert.Contains("those of the place chosen", away.Sections[0].Items[0].Text);
        Assert.NotEqual(home.Sections[0].Items[1].Text, away.Sections[0].Items[1].Text); // the angles

        // The progressions have nothing to do with the place.
        Assert.Equal(Section(home, "Secondary progressions").Items.Select(i => i.Text),
            Section(away, "Secondary progressions").Items.Select(i => i.Text));
        Assert.Contains("cast for Tokyo, Japan", System.Text.Encoding.UTF8.GetString(TimingService.ToText(away)));
    }

    [Fact]
    public void The_return_in_force_is_the_latest_one_on_or_before_the_date()
    {
        var natal = Chart(); // born 14 March
        Assert.Equal(2025, Timing.SolarReturnInForce(natal, new DateOnly(2026, 2, 1), DateTimeZone.Utc)!.Utc.Year);
        Assert.Equal(2026, Timing.SolarReturnInForce(natal, new DateOnly(2026, 6, 1), DateTimeZone.Utc)!.Utc.Year);
    }

    [Theory]
    [InlineData(14)]   // far east of Greenwich: already tomorrow
    [InlineData(0)]
    [InlineData(-12)]  // far west: still yesterday
    public void The_return_takes_over_on_the_readers_own_calendar_day(int offsetHours)
    {
        var natal = Chart();
        var zone = DateTimeZone.ForOffset(Offset.FromHours(offsetHours));
        var returns = Timing.SolarReturn(natal, 2026)!.Utc;
        var local = DateOnly.FromDateTime(returns.AddHours(offsetHours));

        Assert.Equal(2026, Timing.SolarReturnInForce(natal, local, zone)!.Utc.Year);
        Assert.Equal(2025, Timing.SolarReturnInForce(natal, local.AddDays(-1), zone)!.Utc.Year);
    }

    [Fact]
    public void An_Ascendant_can_be_recovered_from_its_Midheaven()
    {
        // The geometry the progressed Ascendant relies on, checked where the answer is
        // known: every figure's own Midheaven must give back that figure's own Ascendant.
        foreach (var f in Repo.Figures)
        {
            var chart = Repo.Charts.Calculate(f);
            double jd = SwissEphemeris.DateTimeToJulianDay(chart.CalculatedForUtc);
            double asc = Repo.Charts.AscendantFor(chart.Midheaven, jd, f.Latitude)!.Value;
            Assert.True(Apart(asc, chart.Ascendant) < 0.0001, $"{f.Name}: {asc} vs {chart.Ascendant}");
        }
    }

    [Fact]
    public void Progressed_to_the_birth_date_the_chart_is_the_birth_chart()
    {
        var natal = Chart();
        var born = DateOnly.FromDateTime(natal.CalculatedForUtc);
        var p = Timing.Progress(natal, born);

        Assert.InRange(p.AgeYears, -0.01, 0.01);
        Assert.All(p.Points, x => Assert.True(Apart(x.Longitude, x.NatalLongitude) < 0.05, x.Point.Name));
    }

    [Fact]
    public void A_day_for_a_year()
    {
        var natal = Chart(); // Einstein, 14 March 1879
        var p = Timing.Progress(natal, new DateOnly(1905, 3, 14));

        // Twenty-six years on, the sky of twenty-six days after birth.
        Assert.InRange(p.AgeYears, 25.99, 26.01);
        Assert.InRange((p.ProgressedUtc - natal.CalculatedForUtc).TotalDays, 25.99, 26.01);

        // The Sun moves about a degree a day, so about 26 degrees; the Midheaven by the same arc.
        var sun = p.Get(NatalPoint.Of(Planet.Sun))!;
        double arc = ((sun.Longitude - sun.NatalLongitude) % 360 + 360) % 360;
        Assert.InRange(arc, 25, 27);
        var mc = p.Get(NatalPoint.Midheaven)!;
        Assert.True(Apart(mc.Longitude, natal.Midheaven + arc) < 1e-9);
        Assert.NotNull(p.Get(NatalPoint.Ascendant));

        // The progressed positions are simply the sky at that instant.
        double moonThen = Repo.Charts.CalculateBody(SwissEphemeris.DateTimeToJulianDay(p.ProgressedUtc), Planet.Moon)!.Longitude;
        Assert.True(Apart(moonThen, p.Get(NatalPoint.Of(Planet.Moon))!.Longitude) < 1e-6);

        Assert.All(p.Aspects, a => Assert.InRange(a.Orb, 0, TimingService.Orb));
        Assert.Equal(p.Aspects.OrderBy(a => a.Orb).Select(a => a.Orb), p.Aspects.Select(a => a.Orb));
    }

    [Fact]
    public void Without_a_birth_time_there_is_no_return_and_no_progressed_Moon_or_angles()
    {
        var untimed = Repo.Charts.Calculate(new Celebrity
        {
            Id = "t", Name = "T", BirthDate = "1990-06-21", BirthTimeKnown = false,
            BirthPlace = "London", Latitude = 51.5, Longitude = -0.13, TimeZoneId = "Europe/London",
        });
        Assert.Null(Timing.SolarReturn(untimed, 2026));

        var p = Timing.Progress(untimed, new DateOnly(2026, 10, 4));
        Assert.DoesNotContain(p.Points, x => x.Point.IsAngle || x.Point == NatalPoint.Of(Planet.Moon));
        Assert.DoesNotContain(p.Aspects, a => a.Natal.IsAngle || a.Natal == NatalPoint.Of(Planet.Moon));

        var reading = Timing.Compose(untimed, new DateOnly(2026, 10, 4), DateTimeZone.Utc);
        Assert.Null(reading.Return);
        Assert.Contains("needs a birth time", reading.Sections[0].Items[0].Text);
    }

    [Fact]
    public void The_reading_has_both_sections_and_saves_as_text()
    {
        var reading = Timing.Compose(Chart(), new DateOnly(2026, 10, 4), DateTimeZone.Utc);
        Assert.Equal("Solar return for 2026–2027", reading.Sections[0].Heading);
        Assert.Equal(
            ["Solar return for", "Annual profection for age", "Secondary progressions for", "Solar arc directions"],
            reading.Sections.Select(s => new[] { "Solar return for", "Annual profection for age", "Secondary progressions for", "Solar arc directions" }
                .Single(s.Heading.StartsWith)));
        Assert.Contains(Section(reading, "Secondary progressions").Items, i => i.Title!.StartsWith("Progressed lunar phase: "));

        string text = System.Text.Encoding.UTF8.GetString(TimingService.ToText(reading));
        Assert.Contains("SOLAR RETURN FOR 2026", text);
        Assert.Contains("Where the planets fall in the return chart", text);
        Assert.Contains("is Lord of the Year", text);
        Assert.Contains("SOLAR ARC DIRECTIONS", text);
    }

    // ── Annual profection ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(1879, 3, 14, 0, 1)]    // the day of birth: the first house
    [InlineData(1880, 3, 13, 0, 1)]    // the day before the first birthday
    [InlineData(1880, 3, 14, 1, 2)]    // the first birthday
    [InlineData(1891, 3, 14, 12, 1)]   // twelve years on, back to the first
    [InlineData(1905, 6, 30, 26, 3)]
    [InlineData(1955, 4, 18, 76, 5)]
    public void A_profection_moves_on_one_house_each_birthday(int y, int m, int d, int age, int house)
    {
        var natal = Chart(); // Einstein, 14 March 1879, Cancer rising
        var p = Timing.Profect(natal, new DateOnly(y, m, d))!;

        Assert.Equal(age, p.Age);
        Assert.Equal(house, p.House);
        Assert.Equal(new DateOnly(1879 + age, 3, 14), p.From);
        Assert.Equal(new DateOnly(1880 + age, 3, 14), p.Until);

        // The sign is that many whole signs on from the rising sign, and its traditional
        // ruler is the Lord of the Year, found in the birth chart.
        var rising = ZodiacSignExtensions.FromLongitude(natal.Ascendant);
        Assert.Equal(ZodiacSign.Cancer, rising);
        Assert.Equal((ZodiacSign)(((int)rising + house - 1) % 12), p.Sign);
        Assert.Equal(DignityService.RulerOf(p.Sign), p.Lord);
        Assert.Equal(natal.GetPlanet(p.Lord)!.Sign, p.LordSign);
        Assert.Equal(natal.GetHouseForLongitude(natal.GetPlanet(p.Lord)!.Longitude), p.LordHouse);
    }

    [Fact]
    public void A_leap_day_birthday_falls_on_the_first_of_March_in_other_years()
    {
        var natal = Repo.Charts.Calculate(new Celebrity
        {
            Id = "t", Name = "T", BirthDate = "1996-02-29", BirthTime = "10:00", BirthTimeKnown = true,
            BirthPlace = "London", Latitude = 51.5, Longitude = -0.13, TimeZoneId = "Europe/London",
        });
        Assert.Equal(0, Timing.Profect(natal, new DateOnly(1997, 2, 28))!.Age);
        Assert.Equal(1, Timing.Profect(natal, new DateOnly(1997, 3, 1))!.Age);
        Assert.Equal(3, Timing.Profect(natal, new DateOnly(2000, 2, 28))!.Age);
        Assert.Equal(4, Timing.Profect(natal, new DateOnly(2000, 2, 29))!.Age);
        Assert.Equal(new DateOnly(2001, 3, 1), Timing.Profect(natal, new DateOnly(2000, 2, 29))!.Until);
    }

    [Fact]
    public void There_is_no_profection_before_the_birth_or_without_a_birth_time()
    {
        Assert.Null(Timing.Profect(Chart(), new DateOnly(1879, 3, 13)));
        Assert.DoesNotContain(Timing.Compose(Chart(), new DateOnly(1870, 1, 1), DateTimeZone.Utc).Sections,
            s => s.Heading.StartsWith("Annual profection"));

        var untimed = Repo.Charts.Calculate(Demo.Person(timed: false));
        Assert.Null(Timing.Profect(untimed, new DateOnly(2026, 10, 4)));
        var reading = Timing.Compose(untimed, new DateOnly(2026, 10, 4), DateTimeZone.Utc);
        Assert.Contains("rising sign is unknown", Section(reading, "Annual profection").Items[0].Text);
    }

    [Fact]
    public void The_profection_is_written_out_with_what_the_houses_are_about()
    {
        var reading = Timing.Compose(Chart(), new DateOnly(2026, 10, 4), DateTimeZone.Utc);
        var p = reading.Profection!;
        var items = Section(reading, "Annual profection").Items;

        Assert.Equal($"Age {p.Age}: a {ChartInterpreter.Ordinal(p.House)}-house year", items[0].Title);
        Assert.Contains($"(topic {p.House})", items[0].Text);
        Assert.Contains($"{p.Lord.Name()} is Lord of the Year", items[1].Title);
        Assert.Contains($"(topic {p.LordHouse})", items[1].Text);
    }

    // ── Solar arc ─────────────────────────────────────────────────────────────

    [Fact]
    public void Solar_arc_moves_every_point_by_as_far_as_the_progressed_Sun_has_gone()
    {
        var natal = Chart();
        var progression = Timing.Progress(natal, new DateOnly(1905, 3, 14));
        var arc = TimingService.Direct(natal, progression)!;

        var sun = progression.Get(NatalPoint.Of(Planet.Sun))!;
        Assert.InRange(arc.Arc, 25, 27);
        Assert.True(Apart(sun.NatalLongitude + arc.Arc, sun.Longitude) < 1e-9);

        // All thirteen bodies and both angles, each exactly the arc further on.
        Assert.Equal(natal.Planets.Count + 2, arc.Points.Count);
        Assert.All(arc.Points, p => Assert.True(Apart(p.NatalLongitude + arc.Arc, p.Longitude) < 1e-9, p.Point.Name));
        Assert.True(Apart(natal.GetPlanet(Planet.Pluto)!.Longitude + arc.Arc, arc.Points.Single(p => p.Point == NatalPoint.Of(Planet.Pluto)).Longitude) < 1e-9);

        // The directed Midheaven is the progressed one: Lore progresses it by solar arc.
        Assert.True(Apart(progression.Get(NatalPoint.Midheaven)!.Longitude, arc.Points.Single(p => p.Point == NatalPoint.Midheaven).Longitude) < 1e-9);

        // Contacts are real, within orb, closest first, and never a point to itself.
        Assert.All(arc.Aspects, a =>
        {
            Assert.NotEqual(a.Progressed, a.Natal);
            double moved = arc.Points.Single(p => p.Point == a.Progressed).Longitude;
            Assert.Equal(a.Orb, Math.Abs(Apart(moved, natal.LongitudeOf(a.Natal)!.Value) - a.Type.Angle()), 9);
            Assert.InRange(a.Orb, 0, TimingService.Orb);
        });
        Assert.Equal(arc.Aspects.OrderBy(a => a.Orb).Select(a => a.Orb), arc.Aspects.Select(a => a.Orb));
    }

    [Fact]
    public void A_solar_arc_contact_is_found_in_the_year_it_falls_due()
    {
        // Somewhere in a long life some directed point reaches some natal one; and a
        // contact found in one year is closer or gone, not unchanged, a year later.
        var natal = Chart();
        var years = Enumerable.Range(1880, 76)
            .Select(y => TimingService.Direct(natal, Timing.Progress(natal, new DateOnly(y, 3, 14)))!)
            .ToList();
        Assert.Contains(years, a => a.Aspects.Count > 0);
        Assert.Contains(years, a => a.Aspects.Count == 0 || a.Aspects[0].Orb > 0.2);
    }

    [Fact]
    public void There_is_no_solar_arc_before_the_birth_and_no_Moon_or_angles_without_a_time()
    {
        Assert.Null(TimingService.Direct(Chart(), Timing.Progress(Chart(), new DateOnly(1870, 1, 1))));

        var untimed = Repo.Charts.Calculate(Demo.Person(timed: false));
        var arc = TimingService.Direct(untimed, Timing.Progress(untimed, new DateOnly(2026, 10, 4)))!;
        Assert.DoesNotContain(arc.Points, p => p.Point.IsAngle || p.Point == NatalPoint.Of(Planet.Moon));
    }

    // ── Relocation ────────────────────────────────────────────────────────────

    [Fact]
    public void A_relocated_chart_keeps_the_planets_and_takes_the_new_places_angles()
    {
        var natal = Chart(); // Einstein, born in Ulm
        var there = Timing.Relocate(natal, Tokyo)!.Chart;

        Assert.Equal(natal.CalculatedForUtc, there.CalculatedForUtc);
        Assert.Equal(natal.Planets.Select(p => p.Longitude), there.Planets.Select(p => p.Longitude));
        Assert.Equal(natal.Aspects.Count, there.Aspects.Count);

        Assert.True(Apart(natal.Ascendant, there.Ascendant) > 30);
        double turned = ((there.Armc - natal.Armc) % 360 + 360) % 360;
        Assert.Equal(Tokyo.Longitude - natal.Celebrity.Longitude, turned, 6);

        // Relocated to the birthplace itself, nothing changes.
        var home = Timing.Relocate(natal, new ReturnPlace("Ulm", natal.Celebrity.Latitude, natal.Celebrity.Longitude))!.Chart;
        Assert.Equal(natal.Ascendant, home.Ascendant, 9);
        Assert.Equal(natal.Houses.Select(h => h.Longitude), home.Houses.Select(h => h.Longitude));
    }

    [Fact]
    public void The_reading_relocates_the_chart_only_when_a_place_is_chosen()
    {
        var natal = Chart();
        var asOf = new DateOnly(2026, 10, 4);
        Assert.Null(Timing.Compose(natal, asOf, DateTimeZone.Utc).Relocation);
        Assert.Null(Timing.Relocate(Repo.Charts.Calculate(Demo.Person(timed: false)), Tokyo));

        var away = Timing.Compose(natal, asOf, DateTimeZone.Utc, Tokyo);
        var section = away.Sections[^1];
        Assert.Equal("Birth chart relocated to Tokyo, Japan", section.Heading);
        Assert.Contains("at the birthplace", section.Items[1].Text);
        Assert.Equal(natal.Planets.Count, section.Items[2].Text.Split('\n').Length);

        // The profection and solar arc have nothing to do with the place.
        Assert.Equal(Timing.Compose(natal, asOf, DateTimeZone.Utc).Profection, away.Profection);
    }
}
