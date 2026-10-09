using Lore.Models;
using Lore.Services;

namespace Lore.Tests;

// The worksheet: the chart's numbers and how they were arrived at.
public class WorksheetServiceTests
{
    private static Worksheet For(string id) => WorksheetService.Build(Repo.Charts.Calculate(Repo.Figure(id)));

    private static string Fact(Worksheet w, string label) => w.Facts.Single(f => f.Label == label).Value;

    [Fact]
    public void The_time_conversion_is_spelled_out()
    {
        // Federer, 08:40 on 8 Aug 1981 in Basel: Swiss summer time, UTC+2.
        var w = For("roger-federer");
        Assert.Equal("UTC+2 (CEST, Europe/Zurich)", Fact(w, "Time conversion"));
        Assert.Equal("1981-08-08 06:40:00 UT", Fact(w, "Universal Time"));

        // Leonardo: local mean time at Vinci, 43 min 45 s ahead of Greenwich.
        var leonardo = For("leonardo-da-vinci");
        Assert.StartsWith("UTC+0:43:45 (local mean time", Fact(leonardo, "Time conversion"));
    }

    [Fact]
    public void The_Suns_declination_is_within_the_tropics_and_has_the_right_sign()
    {
        // Federer was born in August (Sun north of the equator), Elvis in January (south).
        var summer = Repo.Charts.Calculate(Repo.Figure("roger-federer")).GetPlanet(Planet.Sun)!;
        var winter = Repo.Charts.Calculate(Repo.Figure("elvis-presley")).GetPlanet(Planet.Sun)!;
        Assert.InRange(summer.Declination, 10, 23.5);
        Assert.InRange(winter.Declination, -23.5, -10);
    }

    [Fact]
    public void Positions_are_given_to_the_second_and_never_rounded_into_the_next_sign()
    {
        Assert.Equal("29°59'59\" Taurus", WorksheetService.Dms(59.9999999));
        Assert.Equal("0°00'00\" Gemini", WorksheetService.Dms(60));
        Assert.Equal("12°30'00\" Aries", WorksheetService.Dms(12.5));
    }

    [Fact]
    public void A_timed_chart_lists_the_angles_the_Part_of_Fortune_and_twelve_cusps()
    {
        var w = For("elvis-presley");
        // 13 bodies + Ascendant + Midheaven + South Node, Descendant, IC, Vertex, Anti-Vertex,
        // Fortune, Spirit + the node and Lilith of the other kind
        Assert.Equal(24, w.Positions.Count);
        Assert.Equal(12, w.Cusps.Count);
        Assert.Equal(18, w.Points.Count); // the bodies, the two angles, the Vertex, Fortune and Spirit
        Assert.Contains(w.Aspects, a => a.B.IsAngle);
    }

    [Fact]
    public void The_Part_of_Fortune_reverses_by_night()
    {
        // Sun 10°, Moon 40°. A Sun at a lower longitude than the Ascendant has already
        // risen (day): Asc + Moon − Sun. One at a higher longitude has not (night):
        // Asc + Sun − Moon.
        NatalChart Chart(double asc) => new()
        {
            Celebrity = new Celebrity { Id = "t", Name = "T", BirthDate = "2000-01-01", BirthTime = "12:00", BirthTimeKnown = true },
            Planets =
            [
                new PlanetPosition { Planet = Planet.Sun, Longitude = 10 },
                new PlanetPosition { Planet = Planet.Moon, Longitude = 40 },
            ],
            Houses = [],
            Aspects = [],
            Ascendant = asc,
        };

        var day = Chart(60);
        Assert.True(day.IsDayChart);
        Assert.Equal(90, WorksheetService.PartOfFortune(day)!.Value, 9);

        var night = Chart(340);
        Assert.False(night.IsDayChart);
        Assert.Equal(310, WorksheetService.PartOfFortune(night)!.Value, 9);
    }

    [Fact]
    public void A_chart_with_no_birth_time_has_no_angles_houses_or_cusps()
    {
        var untimed = new Celebrity
        {
            Id = "t", Name = "T", BirthDate = "1990-06-21", BirthTimeKnown = false,
            BirthPlace = "London", Latitude = 51.5, Longitude = -0.13, TimeZoneId = "Europe/London",
        };
        var w = WorksheetService.Build(Repo.Charts.Calculate(untimed));

        Assert.Equal(16, w.Positions.Count); // 13 bodies + the South Node, which needs no time, + the other node and Lilith
        Assert.All(w.Positions, r => Assert.Equal("", r.House));
        Assert.DoesNotContain(w.Sections, x => x.Title is "Angles and houses" or "House rulers");
        Assert.Empty(w.Cusps);
        Assert.DoesNotContain(w.Aspects, a => a.A.IsAngle || a.B.IsAngle);
        Assert.Contains("time unknown", Fact(w, "Born"));
    }

    [Fact]
    public void The_text_export_carries_every_section()
    {
        string text = System.Text.Encoding.UTF8.GetString(WorksheetService.ToText(For("elvis-presley")));
        Assert.Contains("HOW THIS WAS CALCULATED", text);
        Assert.Contains("POSITIONS", text);
        Assert.Contains("HOUSE CUSPS", text);
        Assert.Contains("ASPECTS", text);
        foreach (string heading in new[] { "THE MOON'S PHASE", "DISTANCE FROM THE SUN", "DECLINATION", "ANGLES AND HOUSES",
                     "BALANCE", "ASPECTS IN SUM", "RULERS", "HOUSE RULERS", "DISPOSITORS" })
            Assert.Contains(heading, text);
    }

    [Fact]
    public void Reliability_and_source_are_shown_or_said_to_be_missing()
    {
        var w = For("elvis-presley");
        Assert.Equal("Rodden rating AA — from a birth certificate or birth record", Fact(w, "Reliability"));
        Assert.StartsWith("Astro-Databank", Fact(w, "Source"));

        var unrated = WorksheetService.Build(Repo.Charts.Calculate(new Celebrity
        {
            Id = "t", Name = "T", BirthDate = "1990-06-21", BirthTime = "12:00", BirthTimeKnown = true,
            BirthPlace = "London", Latitude = 51.5, Longitude = -0.13, TimeZoneId = "Europe/London",
        }));
        Assert.Equal("Not rated", Fact(unrated, "Reliability"));
        Assert.Equal("Not recorded", Fact(unrated, "Source"));
    }

    [Fact]
    public void The_worksheet_lays_out_as_a_PDF_for_timed_untimed_and_polar_charts()
    {
        Celebrity Person(bool timed, double latitude) => new()
        {
            Id = "t", Name = "T", BirthDate = "1990-06-21", BirthTime = timed ? "12:00" : null, BirthTimeKnown = timed,
            BirthPlace = "Somewhere", Latitude = latitude, Longitude = 18.96, TimeZoneId = "Europe/Oslo",
        };

        foreach (var person in new[] { Repo.Figure("elvis-presley"), Person(true, 69.65), Person(false, 51.5) })
        {
            var sensitivity = person.BirthTimeKnown
                ? TimeSensitivityService.Analyse(Repo.Charts, person, 60)
                : TimeSensitivityService.AnalyseDay(Repo.Charts, person);
            byte[] pdf = WorksheetPdf.ToPdf(WorksheetService.Build(Repo.Charts.Calculate(person)), sensitivity);
            Assert.True(pdf.Length > 2000);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        }
    }
}
