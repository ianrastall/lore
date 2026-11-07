using Lore.Models;
using NodaTime;

namespace Lore.Services;

// The day's sky with no birth chart: the aspects the moving planets make to one another,
// and what the sky itself does (lunations, stations, changes of sign). This is the
// astronomy half of the Sky view; SkyInterpreter writes the reading.
public sealed partial class TransitService
{
    // The ten planets the Sky view reads. The node, Chiron and Lilith are left out: the
    // world-day corpus is written for the planets.
    public static readonly IReadOnlyList<Planet> SkyPlanets =
    [
        Planet.Moon, Planet.Sun, Planet.Mercury, Planet.Venus, Planet.Mars,
        Planet.Jupiter, Planet.Saturn, Planet.Uranus, Planet.Neptune, Planet.Pluto,
    ];

    // The faster planets whose change of sign is marked on the day it happens. The slow
    // planets' come from the sky calendar; the Moon's from the day's own scan.
    private static readonly Planet[] FastIngress = [Planet.Sun, Planet.Mercury, Planet.Venus, Planet.Mars];

    // How close two planets must come to an exact aspect for it to count on the day.
    // The Moon's contacts keep the Daily view's 1°, so they still mark particular hours.
    // Between the other planets 2°: wide enough that an aspect forming or passing over a
    // few days is part of the day's weather, as a reading of the world sky normally takes
    // it, and still tight enough to change from week to week.
    public static double SkyOrb(Planet first) => first == Planet.Moon ? Orb : 2.0;

    // `aspectsOnly`: just the planets' aspects and the Moon, without the sky calendar,
    // the void-of-course search or the planetary hours; for the week strip.
    public SkyDay ScanSky(DateOnly date, DateTimeZone zone, HomePlace? home = null, bool aspectsOnly = false,
        CancellationToken cancel = default)
    {
        if (!_pinned)
            return new TransitService(_charts.With(_charts.Settings), pinned: true).ScanSky(date, zone, home, aspectsOnly, cancel);
        if (date.Year < 1200 || date.Year >= 2400)
            throw new ArgumentException("Choose a date within 1200–2399.");

        var s = SampleDay(date, zone, cancel);
        var aspects = new List<SkyAspect>();
        for (int i = 0; i < SkyPlanets.Count; i++)
            for (int j = i + 1; j < SkyPlanets.Count; j++)
            {
                cancel.ThrowIfCancellationRequested();
                Planet first = SkyPlanets[i], second = SkyPlanets[j];
                // The Sun–Moon aspects are the lunar phases, reported as such.
                if (first == Planet.Moon && second == Planet.Sun) continue;
                if (!s.Tracks.ContainsKey(first) || !s.Tracks.ContainsKey(second)) continue;
                foreach (var aspect in AspectTypeExtensions.Majors)
                    if (FindSkyAspect(first, second, aspect, s) is { } found)
                        aspects.Add(found);
            }

        var day = DayOf(s, date, zone, [], aspectsOnly ? null : home, voids: !aspectsOnly);
        var events = new List<SkyEvent>();
        if (!aspectsOnly)
        {
            int n = s.Jds.Length - 1;
            events.AddRange(SkyCalendarUtc(null, s.Jds[0], s.Jds[n], cancel));
            events.AddRange(FastIngresses(s));
            events.Sort((x, y) => x.Utc.CompareTo(y.Utc));
        }

        return new SkyDay { Day = day, Aspects = aspects, Events = events };
    }

    // The Sun, Mercury, Venus or Mars changing sign during the day.
    private IEnumerable<SkyEvent> FastIngresses(DaySamples s)
    {
        int n = s.Jds.Length - 1;
        foreach (var planet in FastIngress)
        {
            if (!s.Tracks.TryGetValue(planet, out var track)) continue;
            for (int i = 0; i < n; i++)
            {
                var (a, b) = (track[i], track[i + 1]);
                if (a.Sign == b.Sign) continue;
                bool backwards = (int)b.Sign == ((int)a.Sign + 11) % 12;
                double boundary = (int)(backwards ? a.Sign : b.Sign) * 30;
                double jd = Bisect(t => SignedAt(planet, boundary, t), s.Jds[i], s.Jds[i + 1]);
                yield return new SkyEvent(SkyEventKind.Ingress, SwissEphemeris.JulianDayToDateTime(jd), planet, boundary, b.Sign)
                {
                    Retrograde = backwards,
                };
            }
        }
    }

    // ── Aspects between two moving planets ────────────────────────────────────
    // As FindEvent, except that both ends move: what is followed is the first planet's
    // lead over the second (−180…+180), and an aspect is exact where that lead equals
    // the aspect's angle, either way round.

    private SkyAspect? FindSkyAspect(Planet first, Planet second, AspectType aspect, DaySamples s)
    {
        var (a, b, jds) = (s.Tracks[first], s.Tracks[second], s.Jds);
        int n = jds.Length - 1;
        double Lead(int i) => Signed(a[i].Longitude - b[i].Longitude);

        // The furthest the lead changes between two samples, as FindEvent's slack.
        double slack = 0;
        for (int i = 0; i < n; i++)
            slack = Math.Max(slack, Math.Abs(Signed(Lead(i + 1) - Lead(i))));

        double angle = aspect.Angle();
        double[] branches = angle is > 0 and < 180 ? [angle, -angle] : [angle];

        double bestOrb = double.MaxValue, bestJd = 0, bestBranch = 0;
        foreach (double branch in branches)
        {
            int k = 0;
            double kOrb = double.MaxValue;
            for (int i = 0; i <= n; i++)
            {
                double orb = Math.Abs(Signed(Lead(i) - branch));
                if (orb < kOrb) { kOrb = orb; k = i; }
            }
            if (kOrb > SkyOrb(first) + slack) continue;

            double lo = jds[Math.Max(k - 1, 0)], hi = jds[Math.Min(k + 1, n)];
            for (int i = 0; i < RefineIterations; i++)
            {
                double third = (hi - lo) / 3;
                double m1 = lo + third, m2 = hi - third;
                if (PairOrbAt(first, second, branch, m1) <= PairOrbAt(first, second, branch, m2)) hi = m2; else lo = m1;
            }
            double jd = (lo + hi) / 2, refined = PairOrbAt(first, second, branch, jd);
            if (refined < bestOrb) { bestOrb = refined; bestJd = jd; bestBranch = branch; }
        }
        if (bestOrb > SkyOrb(first)) return null;

        TransitPhase phase;
        DateTime? exactUtc;
        if (bestOrb < ExactTolerance)
        {
            phase = TransitPhase.Exact;
            exactUtc = SwissEphemeris.JulianDayToDateTime(bestJd);
        }
        else
        {
            double atStart = Math.Abs(Signed(Lead(0) - bestBranch));
            double atEnd = Math.Abs(Signed(Lead(n) - bestBranch));
            phase = atEnd < atStart ? TransitPhase.Building : TransitPhase.Easing;
            exactUtc = PairExact(first, second, bestBranch, phase == TransitPhase.Building ? jds[n] : jds[0],
                phase == TransitPhase.Building ? +1 : -1);
        }

        var p1 = _charts.CalculateBody(bestJd, first);
        var p2 = _charts.CalculateBody(bestJd, second);
        return new SkyAspect
        {
            First = first,
            Second = second,
            Aspect = aspect,
            MinOrb = bestOrb,
            PeakUtc = SwissEphemeris.JulianDayToDateTime(bestJd),
            Phase = phase,
            ExactUtc = exactUtc,
            FirstRetrograde = p1?.IsRetrograde == true,
            SecondRetrograde = p2?.IsRetrograde == true,
        };
    }

    // The first planet's lead over the second, less the aspect's angle (−180…+180).
    private double PairSignedAt(Planet first, Planet second, double branch, double jd) =>
        _charts.CalculateBody(jd, first) is { } p && _charts.CalculateBody(jd, second) is { } q
            ? Signed(Signed(p.Longitude - q.Longitude) - branch) : double.NaN;

    private double PairOrbAt(Planet first, Planet second, double branch, double jd) =>
        PairSignedAt(first, second, branch, jd) is var d && double.IsNaN(d) ? double.MaxValue : Math.Abs(d);

    // As FindExact, for a pair: walks outward from the day to the nearest exact moment.
    private DateTime? PairExact(Planet first, Planet second, double branch, double fromJd, int direction)
    {
        var (step, steps) = first switch
        {
            Planet.Moon => (1.0 / 24, 12),
            _ when first.IsPersonal() => (0.25, 40),
            _ => (1.0, 240),
        };

        double prevJd = fromJd;
        double prev = PairSignedAt(first, second, branch, prevJd);
        for (int i = 1; i <= steps && !double.IsNaN(prev); i++)
        {
            double jd = fromJd + direction * step * i;
            double cur = PairSignedAt(first, second, branch, jd);
            if (double.IsNaN(cur)) break;

            if (prev == 0) return SwissEphemeris.JulianDayToDateTime(prevJd);
            // A sign change near ±180 is the lead wrapping round, not an exact aspect.
            if ((prev < 0) != (cur < 0) && Math.Abs(prev - cur) < 90)
            {
                double root = Bisect(t => PairSignedAt(first, second, branch, t), prevJd, jd);
                return SwissEphemeris.JulianDayToDateTime(root);
            }
            if (Math.Abs(cur) > GiveUpOrb) break;

            prevJd = jd;
            prev = cur;
        }
        return null;
    }
}
