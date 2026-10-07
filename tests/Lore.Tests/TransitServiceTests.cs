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

    // ── Void-of-course Moon ───────────────────────────────────────────────────

    private static readonly Planet[] CourseBodies =
    [
        Planet.Sun, Planet.Mercury, Planet.Venus, Planet.Mars, Planet.Jupiter,
        Planet.Saturn, Planet.Uranus, Planet.Neptune, Planet.Pluto,
    ];

    private static double Lon(DateTime utc, Planet p) =>
        Repo.Charts.CalculateBody(SwissEphemeris.DateTimeToJulianDay(utc), p)!.Longitude;

    // How far the Moon is from the nearest exact major aspect to a body, in degrees.
    private static double OffAspect(DateTime utc, Planet body)
    {
        double apart = Repo.ArcMinutesBetween(Lon(utc, Planet.Moon), Lon(utc, body)) / 60;
        return new[] { 0.0, 60, 90, 120, 180 }.Min(a => Math.Abs(apart - a));
    }

    [Fact]
    public void A_void_Moon_runs_from_its_last_aspect_in_a_sign_to_the_next_sign()
    {
        double from = SwissEphemeris.DateTimeToJulianDay(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        var voids = new TransitService(Repo.Charts).VoidOfCourse(from, from + 30);

        // The Moon changes sign thirteen times in thirty days, give or take one.
        Assert.InRange(voids.Count, 12, 14);

        for (int i = 0; i < voids.Count; i++)
        {
            var v = voids[i];
            Assert.True(v.StartUtc < v.EndUtc);
            if (i > 0) Assert.True(voids[i - 1].EndUtc < v.StartUtc);

            // It ends as the Moon crosses into the next sign…
            Assert.Equal((ZodiacSign)(((int)v.Sign + 1) % 12), v.Enters);
            Assert.True(Repo.ArcMinutesBetween(Lon(v.EndUtc, Planet.Moon), (int)v.Enters * 30) < 0.01);
            Assert.Equal(v.Sign, ZodiacSignExtensions.FromLongitude(Lon(v.StartUtc, Planet.Moon)));

            // …begins at an exact aspect to the body named…
            Assert.NotNull(v.LastPlanet);
            Assert.True(OffAspect(v.StartUtc, v.LastPlanet!.Value) < 0.0005, $"{v.StartUtc:u} {v.LastPlanet}");
            double apart = Repo.ArcMinutesBetween(Lon(v.StartUtc, Planet.Moon), Lon(v.StartUtc, v.LastPlanet.Value)) / 60;
            Assert.Equal(v.LastAspect!.Value.Angle(), apart, 3);

            // …and in between the Moon reaches no other: sampled every five minutes, its
            // distance from the nearest exact aspect to each body never touches zero
            // (it would show as a dip to within the 0.05° the Moon covers in that time).
            for (var t = v.StartUtc.AddMinutes(10); t < v.EndUtc.AddMinutes(-5); t = t.AddMinutes(5))
                foreach (var body in CourseBodies)
                    Assert.True(OffAspect(t, body) > 0.03, $"{t:u} Moon reaches an aspect to {body} inside a void");
        }
    }

    [Fact]
    public void The_days_void_ends_at_the_Moons_sign_change()
    {
        var sky = Scan(new DateOnly(2026, 10, 2));
        var v = Assert.Single(sky.Voids);
        Assert.Equal(ZodiacSign.Gemini, v.Sign);
        Assert.Equal(ZodiacSign.Cancer, v.Enters);
        Assert.True(Math.Abs((v.EndUtc - sky.MoonIngressUtc!.Value).TotalSeconds) < 2);

        // The same for everyone: a chart with no birth time sees the same void.
        Assert.Equal(sky.Voids, Scan(new DateOnly(2026, 10, 2), timed: false).Voids);
    }

    [Fact]
    public void A_day_the_Moon_stays_in_course_has_no_void()
    {
        // Every day in a month either overlaps a void or does not, and the ones that
        // do not are days with no sign change.
        var quiet = Enumerable.Range(0, 30).Select(i => Scan(new DateOnly(2026, 10, 1).AddDays(i)))
            .Where(s => s.Voids.Count == 0).ToList();
        Assert.NotEmpty(quiet);
        Assert.All(quiet, s => Assert.Null(s.MoonEnters));
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
