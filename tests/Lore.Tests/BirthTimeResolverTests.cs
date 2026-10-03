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
}
