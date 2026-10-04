using Lore.Models;
using Lore.Services;
using NodaTime;

namespace Lore.Tests;

// Solar returns and secondary progressions.
public class TimingServiceTests
{
    private static readonly TimingService Timing = new(Repo.Charts);
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
    public void The_return_in_force_is_the_latest_one_on_or_before_the_date()
    {
        var natal = Chart(); // born 14 March
        Assert.Equal(2025, Timing.SolarReturnInForce(natal, new DateOnly(2026, 2, 1))!.Utc.Year);
        Assert.Equal(2026, Timing.SolarReturnInForce(natal, new DateOnly(2026, 6, 1))!.Utc.Year);
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
        Assert.StartsWith("Secondary progressions for ", reading.Sections[1].Heading);
        Assert.Contains(reading.Sections[1].Items, i => i.Title!.StartsWith("Progressed lunar phase: "));

        string text = System.Text.Encoding.UTF8.GetString(TimingService.ToText(reading));
        Assert.Contains("SOLAR RETURN FOR 2026", text);
        Assert.Contains("Where the planets fall in the return chart", text);
    }
}
