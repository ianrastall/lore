using Lore.Models;
using Lore.Services;

namespace Lore.Tests;

// The birth instant everything else is calculated from.
public class BirthTimeResolverTests
{
    private static DateTime Utc(string id) => BirthTimeResolver.ToUtc(Repo.Figure(id));

    [Fact]
    public void Modern_daylight_saving_is_applied()
    {
        // Federer, 08:40 on 8 Aug 1981 in Basel: Swiss summer time, UTC+2.
        Assert.Equal(new DateTime(1981, 8, 8, 6, 40, 0), Utc("roger-federer"));
    }

    [Fact]
    public void Pre_standard_time_births_use_the_birthplace_local_mean_time()
    {
        // Leonardo, 21:40 local mean time at Vinci (10°55' E): 43 min 45 s ahead of UTC.
        // The zone (Europe/Rome) would give Rome's mean time instead, 6 minutes out.
        Assert.Equal(new DateTime(1452, 4, 23, 20, 56, 15), Utc("leonardo-da-vinci"));

        // Franklin D. Roosevelt, 20:45 at Hyde Park (73°56' W), 1882 -- a year before
        // US railway time: 4 h 55 min 44 s behind UTC, not New York City's mean time.
        var fdr = Utc("franklin-d-roosevelt");
        Assert.True(Math.Abs((fdr - new DateTime(1882, 1, 31, 1, 40, 44)).TotalSeconds) < 60, fdr.ToString("u"));
    }

    [Fact]
    public void Nationwide_city_mean_times_are_kept()
    {
        // Simone Weil, Paris 1909: Paris Mean Time was France's legal time (UTC+0:09:21).
        Assert.Equal(new DateTime(1909, 2, 3, 4, 50, 39), Utc("simone-weil"));
        // Borges, Buenos Aires 1899: Córdoba Mean Time, Argentina's legal time (UTC-4:16:48).
        Assert.Equal(new DateTime(1899, 8, 24, 7, 46, 48), Utc("jorge-luis-borges"));
    }

    [Fact]
    public void Indian_railway_times_before_1906_count_as_local_mean_time()
    {
        // 07:11 at Porbandar (69°36' E) in 1869: local time, not Calcutta railway time
        // (which would put a chart 75 minutes out).
        var porbandar = new Celebrity
        {
            Id = "test", Name = "Test", Category = "Test", BirthDate = "1869-10-02", BirthTime = "07:11",
            BirthTimeKnown = true, BirthPlace = "Porbandar, India", Latitude = 21.6417, Longitude = 69.6,
            TimeZoneId = "Asia/Kolkata",
        };
        Assert.Equal(new DateTime(1869, 10, 2, 2, 32, 36), BirthTimeResolver.ToUtc(porbandar));
    }

    [Fact]
    public void A_documented_standard_time_overrides_the_city_zone_history()
    {
        // Armstrong, Wapakoneta, Aug 1930: the birth record is in EST, though New York
        // was on daylight time that summer.
        Assert.Equal(new DateTime(1930, 8, 5, 5, 31, 0), Utc("neil-armstrong"));
    }

    [Fact]
    public void Describe_reports_local_mean_time_for_an_old_birth()
    {
        var (hours, label, zone) = BirthTimeResolver.Describe(null, 43.7833, 10.9167, 1452, 4, 23, 21, 40);
        Assert.Equal(10.9167 / 15, hours, 3);
        Assert.Contains("local mean time", label);
        Assert.Equal("Europe/Rome", zone);
    }

    [Fact]
    public void An_offset_the_user_set_overrides_the_time_zone()
    {
        // London, July 1990: the zone says BST (UTC+1); the user insists on UTC+0.
        var c = new Celebrity
        {
            Id = "t", Name = "T", BirthDate = "1990-07-01", BirthTime = "12:00", BirthTimeKnown = true,
            Latitude = 51.5, Longitude = -0.13, TimeZoneId = "Europe/London", UtcOffsetHours = 0,
        };
        Assert.Equal(new DateTime(1990, 7, 1, 11, 0, 0), BirthTimeResolver.ToUtc(c));

        var fixedOffset = new Celebrity
        {
            Id = "t", Name = "T", BirthDate = "1990-07-01", BirthTime = "12:00", BirthTimeKnown = true,
            Latitude = 51.5, Longitude = -0.13, TimeZoneId = "Europe/London", UtcOffsetHours = 0,
            UtcOffsetFixed = true,
        };
        Assert.Equal(new DateTime(1990, 7, 1, 12, 0, 0), BirthTimeResolver.ToUtc(fixedOffset));
    }

    [Fact]
    public void Clock_change_times_are_pointed_out()
    {
        // London, 28 Oct 1990: 01:30 happened twice. 25 Mar 1990: 01:30 never happened.
        string Label(int month, int day, int hour) =>
            BirthTimeResolver.Describe("Europe/London", 51.5, -0.13, 1990, month, day, hour, 30).label;

        Assert.Contains("happened twice", Label(10, 28, 1));
        Assert.Contains("skipped", Label(3, 25, 1));
        Assert.DoesNotContain("clock time", Label(7, 1, 12));
    }

    [Fact]
    public void An_Old_Style_date_is_converted_to_the_Gregorian_calendar()
    {
        // Isaac Newton: 25 December 1642 Old Style is 4 January 1643 (ten days' difference
        // in the 17th century). Shakespeare's 23 April 1564 is 3 May.
        Assert.Equal(new DateOnly(1643, 1, 4), BirthTimeResolver.GregorianDate(new DateOnly(1642, 12, 25), julian: true));
        Assert.Equal(new DateOnly(1564, 5, 3), BirthTimeResolver.GregorianDate(new DateOnly(1564, 4, 23), julian: true));
        // Thirteen days by the 20th century: Russia's 25 October 1917 is 7 November.
        Assert.Equal(new DateOnly(1917, 11, 7), BirthTimeResolver.GregorianDate(new DateOnly(1917, 10, 25), julian: true));
        Assert.Equal(new DateOnly(1642, 12, 25), BirthTimeResolver.GregorianDate(new DateOnly(1642, 12, 25), julian: false));

        Celebrity Newton(string date, bool julian) => new()
        {
            Id = "t", Name = "T", BirthDate = date, JulianCalendar = julian, BirthTime = "01:38", BirthTimeKnown = true,
            Latitude = 52.81, Longitude = -0.63, TimeZoneId = "Europe/London",
        };
        Assert.Equal(BirthTimeResolver.ToUtc(Newton("1643-01-04", false)), BirthTimeResolver.ToUtc(Newton("1642-12-25", true)));
        Assert.Equal("1642-12-25 O.S.", Newton("1642-12-25", true).BirthDateLabel);

        // The Sun was at about 13° Capricorn, not the 3° a Gregorian reading of the date gives.
        var sun = Repo.Charts.Calculate(Newton("1642-12-25", true)).GetPlanet(Planet.Sun)!;
        Assert.Equal(ZodiacSign.Capricorn, sun.Sign);
        Assert.InRange(sun.DegreeInSign, 13, 14.5);
    }

    [Theory]
    [InlineData("th-TH")] // Thai Buddhist calendar: year 2533 for 1990
    [InlineData("fa-IR")] // Persian calendar
    [InlineData("ar-SA")] // Umm al-Qura calendar
    [InlineData("en-US")]
    public void A_stored_date_means_the_same_whatever_calendar_Windows_uses(string cultureName)
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(cultureName);
            var c = new Celebrity
            {
                Id = "t", Name = "T", BirthDate = "1990-01-01", BirthTime = "12:00", BirthTimeKnown = true,
                Latitude = 51.5, Longitude = -0.13, TimeZoneId = "Europe/London",
            };
            Assert.Equal(new DateOnly(1990, 1, 1), c.GetBirthDate());
            Assert.Equal(new DateTime(1990, 1, 1, 12, 0, 0), BirthTimeResolver.ToUtc(c));
            Assert.Equal("1990-01-01 12:00:00 UT",
                WorksheetService.Build(Repo.Charts.Calculate(c)).Facts.Single(f => f.Label == "Universal Time").Value);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void The_birth_day_is_the_real_local_day_even_when_the_clocks_change()
    {
        Celebrity London(string date) => new()
        {
            Id = "t", Name = "T", BirthDate = date, BirthTimeKnown = false,
            Latitude = 51.5, Longitude = -0.13, TimeZoneId = "Europe/London",
        };

        // An ordinary summer day: BST midnight to midnight is 23:00 to 23:00 UT.
        var (start, end, clock) = BirthTimeResolver.LocalDay(London("1990-07-01"));
        Assert.Equal(new DateTime(1990, 6, 30, 23, 0, 0), start);
        Assert.Equal(24, (end - start).TotalHours);
        Assert.Equal("00:00", clock(start));
        Assert.Equal("12:30", clock(start.AddHours(12.5)));

        // 25 March 1990, clocks forward: 23 hours. 28 October 1990, clocks back: 25.
        var spring = BirthTimeResolver.LocalDay(London("1990-03-25"));
        Assert.Equal(23, (spring.endUtc - spring.startUtc).TotalHours);
        Assert.Equal("00:00", spring.clock(spring.startUtc));
        Assert.Equal("23:59", spring.clock(spring.endUtc.AddMinutes(-1)));
        var autumn = BirthTimeResolver.LocalDay(London("1990-10-28"));
        Assert.Equal(25, (autumn.endUtc - autumn.startUtc).TotalHours);

        Assert.Equal(690, TimeSensitivityService.AnalyseDay(Repo.Charts, London("1990-03-25"))!.Minutes);
        Assert.Equal(750, TimeSensitivityService.AnalyseDay(Repo.Charts, London("1990-10-28"))!.Minutes);
    }
}
