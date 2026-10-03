using Lore.Models;
using Lore.Services;
using NodaTime;

namespace Lore.Tests;

// The daily transit scan, checked against an independent calculation (Codex's worked
// example for the demo chart on 2 October 2026, Chicago time).
public class TransitServiceTests
{
    private static readonly DateTimeZone Chicago = DateTimeZoneProviders.Tzdb["America/Chicago"];

    private static DaySky Scan(DateOnly date, bool timed = true)
    {
        var natal = Repo.Charts.Calculate(Demo.Person(timed));
        return new TransitService(Repo.Charts).Scan(natal, date, Chicago);
    }

    private static TransitEvent Find(DaySky sky, Planet mover, AspectType aspect, string target) =>
        sky.Events.Single(e => e.Mover == mover && e.Aspect == aspect && e.Target.Name == target);

    [Fact]
    public void Finds_a_lunar_contact_that_forms_and_perfects_between_midnight_and_noon()
    {
        var sky = Scan(new DateOnly(2026, 10, 2));
        var moon = Find(sky, Planet.Moon, AspectType.Conjunction, "Sun");
        Assert.Equal(TransitPhase.Exact, moon.Phase);
        // 05:06:13 CDT
        Assert.True(Math.Abs((moon.PeakUtc - new DateTime(2026, 10, 2, 10, 6, 13)).TotalSeconds) < 5);
    }

    [Fact]
    public void Exact_times_and_closest_orbs_match_the_reference()
    {
        var sky = Scan(new DateOnly(2026, 10, 2));
        var jupiter = Find(sky, Planet.Jupiter, AspectType.Trine, "Midheaven");
        Assert.True(Math.Abs((jupiter.PeakUtc - new DateTime(2026, 10, 3, 4, 0, 17)).TotalSeconds) < 5);

        Assert.Equal(0.292860, Find(sky, Planet.Uranus, AspectType.Conjunction, "Mercury").MinOrb, 4);
        Assert.Equal(0.318939, Find(sky, Planet.Saturn, AspectType.Conjunction, "Mars").MinOrb, 4);
        Assert.Equal(0.305309, Find(sky, Planet.Venus, AspectType.Sextile, "Uranus").MinOrb, 4);
        Assert.Equal(0.863838, Find(sky, Planet.Sun, AspectType.Square, "Uranus").MinOrb, 4);
    }

    [Fact]
    public void Every_reported_transit_is_within_orb()
    {
        var sky = Scan(new DateOnly(2026, 10, 2));
        Assert.NotEmpty(sky.Events);
        Assert.All(sky.Events, e => Assert.InRange(e.MinOrb, 0, TransitService.Orb));
    }

    [Fact]
    public void A_daylight_saving_day_is_23_hours_long()
    {
        var sky = Scan(new DateOnly(2026, 3, 8));
        Assert.Equal(TimeSpan.FromHours(23), sky.EndUtc - sky.StartUtc);
    }

    [Fact]
    public void Without_a_birth_time_the_angles_and_natal_Moon_are_left_out()
    {
        var sky = Scan(new DateOnly(2026, 10, 2), timed: false);
        Assert.DoesNotContain(sky.Events, e => e.Target.IsAngle || e.Target.Name == "Moon");
        Assert.All(sky.Events, e => Assert.Null(e.TargetHouse));
    }

    [Fact]
    public void Moon_sign_change_is_found_and_timed()
    {
        var sky = Scan(new DateOnly(2026, 10, 2));
        Assert.Equal(ZodiacSign.Gemini, sky.MoonSign);
        Assert.Equal(ZodiacSign.Cancer, sky.MoonEnters);
        Assert.NotNull(sky.MoonIngressUtc);
    }
}
