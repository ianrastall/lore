using Lore.Models;
using NodaTime;
using NodaTime.TimeZones;

namespace Lore.Services;

// Finds a day's transits: the aspects the moving sky makes to the fixed points of a
// birth chart. This is the astronomy half of the daily horoscope — it reports what
// happens and when, and leaves what it means to DailyInterpreter.
//
// Three things distinguish this from the natal aspect pass in ChartService:
//   • roles are directional — a moving body against a natal point, so every mover is
//     tried against every natal point (including its own: returns), and the natal point
//     never moves;
//   • the whole local day is scanned, not one instant — the Moon covers 13° a day, so a
//     midnight or noon snapshot would miss contacts that form and pass in between;
//   • the orb is tight (1°, against 6–8° natally) so that a transit marks particular
//     days rather than a whole season.
public sealed class TransitService
{
    // A transit is in effect while the mover is within this many degrees of exact.
    public const double Orb = 1.0;

    private const double ExactTolerance = 0.001; // degrees; closer than this counts as exact
    private const double StepHours = 1.0;        // spacing of the day's sky samples
    private const int RefineIterations = 26;     // narrows a two-hour bracket to well under a second
    private const double GiveUpOrb = 3.0;        // stop looking for an exact date once this far out

    private readonly ChartService _charts;

    public TransitService(ChartService charts) => _charts = charts;

    // The zone whose calendar day a reading covers: the reader's own, wherever the
    // chart's owner was born.
    public static DateTimeZone LocalZone()
    {
        try { return DateTimeZoneProviders.Tzdb.GetSystemDefault(); }
        catch (DateTimeZoneNotFoundException) { return BclDateTimeZone.ForSystemDefault(); }
    }

    public DaySky Scan(NatalChart natal, DateOnly date, DateTimeZone zone)
    {
        // The day runs from one local midnight to the next. Both ends are resolved
        // separately, so a 23- or 25-hour daylight-saving day comes out right.
        var local = new LocalDate(date.Year, date.Month, date.Day);
        DateTime startUtc = zone.AtStartOfDay(local).ToDateTimeUtc();
        DateTime endUtc = zone.AtStartOfDay(local.PlusDays(1)).ToDateTimeUtc();
        double jd0 = SwissEphemeris.DateTimeToJulianDay(startUtc);
        double jd1 = SwissEphemeris.DateTimeToJulianDay(endUtc);

        int n = Math.Max(2, (int)Math.Ceiling((jd1 - jd0) * 24 / StepHours));
        var jds = new double[n + 1];
        var skies = new IReadOnlyList<PlanetPosition>[n + 1];
        for (int i = 0; i <= n; i++)
        {
            jds[i] = jd0 + (jd1 - jd0) * i / n;
            skies[i] = _charts.CalculateSky(jds[i]);
        }

        // One track of positions per body, for the bodies available at every sample.
        var tracks = new Dictionary<Planet, PlanetPosition[]>();
        foreach (var planet in Enum.GetValues<Planet>())
        {
            var track = new PlanetPosition[n + 1];
            bool complete = true;
            for (int i = 0; i <= n && complete; i++)
            {
                var p = skies[i].FirstOrDefault(x => x.Planet == planet);
                if (p is null) complete = false; else track[i] = p;
            }
            if (complete) tracks[planet] = track;
        }

        bool timed = natal.Celebrity.BirthTimeKnown;
        var targets = Targets(natal, timed);

        var events = new List<TransitEvent>();
        foreach (var (mover, track) in tracks)
            foreach (var (point, lon) in targets)
                foreach (var aspect in AspectTypeExtensions.Majors)
                    if (FindEvent(natal, timed, mover, track, jds, point, lon, aspect) is { } e)
                        events.Add(e);

        var (phase, phaseExact) = MoonPhaseFor(tracks, jds);
        var (moonSign, moonEnters, moonIngress) = MoonSignFor(tracks, jds);

        return new DaySky
        {
            Date = date,
            ZoneId = zone.Id,
            StartUtc = startUtc,
            EndUtc = endUtc,
            Midday = skies[n / 2],
            Events = events,
            Phase = phase,
            PhaseExactUtc = phaseExact,
            MoonSign = moonSign,
            MoonEnters = moonEnters,
            MoonIngressUtc = moonIngress,
            Stations = StationsFor(tracks),
        };
    }

    // ── Forecast ──────────────────────────────────────────────────────────────
    // Every pass a moving body makes within orb of a natal point over a stretch of days,
    // with the moments it enters orb, is exact, and leaves. The Moon is left out: it
    // touches every point in the chart several times a month, which is what the Daily
    // view is for. `includeFast` adds the Sun, Mercury, Venus and Mars to the slow movers.
    public IReadOnlyList<TransitPass> Forecast(
        NatalChart natal, DateOnly start, int days, DateTimeZone zone, bool includeFast = true)
    {
        var first = new LocalDate(start.Year, start.Month, start.Day);
        double jd0 = SwissEphemeris.DateTimeToJulianDay(zone.AtStartOfDay(first).ToDateTimeUtc());
        double jd1 = SwissEphemeris.DateTimeToJulianDay(zone.AtStartOfDay(first.PlusDays(days)).ToDateTimeUtc());

        bool timed = natal.Celebrity.BirthTimeKnown;
        var targets = Targets(natal, timed);
        var passes = new List<TransitPass>();

        foreach (var mover in Enum.GetValues<Planet>())
        {
            if (mover == Planet.Moon || (!includeFast && mover.IsPersonal())) continue;

            // Half-day samples for the quick planets, daily for the slow: in either case
            // the body moves less between samples than the 2° an orb spans, so no pass
            // can slip between two of them.
            double step = mover.IsPersonal() ? 0.5 : 1.0;
            int n = Math.Max(1, (int)Math.Ceiling((jd1 - jd0) / step));
            var jds = new double[n + 1];
            var lon = new double[n + 1];
            bool available = true;
            for (int i = 0; i <= n && available; i++)
            {
                jds[i] = Math.Min(jd0 + i * step, jd1);
                if (_charts.CalculateBody(jds[i], mover) is { } p) lon[i] = p.Longitude; else available = false;
            }
            if (!available) continue;

            foreach (var (point, natalLon) in targets)
                foreach (var aspect in AspectTypeExtensions.Majors)
                    foreach (double branch in Branches(natalLon, aspect.Angle()))
                    {
                        double D(int i) => Signed(lon[i] - branch);
                        for (int i = 0; i <= n; i++)
                        {
                            if (Math.Abs(D(i)) > Orb) continue;
                            int a = i;
                            while (i < n && Math.Abs(D(i + 1)) <= Orb) i++;
                            passes.Add(Pass(natal, timed, mover, point, natalLon, aspect, branch, jds, D, a, i));
                        }
                    }
        }

        passes.Sort((x, y) => x.PeakUtc.CompareTo(y.PeakUtc));
        return passes;
    }

    // One run of samples [a..b] inside the orb, refined at both ends and at each crossing.
    private TransitPass Pass(
        NatalChart natal, bool timed, Planet mover, NatalPoint point, double natalLon, AspectType aspect,
        double branch, double[] jds, Func<int, double> d, int a, int b)
    {
        int n = jds.Length - 1;
        double OutBy(double t) => OrbAt(mover, branch, t) - Orb; // negative inside the orb

        double? enter = a > 0 ? Bisect(OutBy, jds[a - 1], jds[a]) : null;
        double? leave = b < n ? Bisect(OutBy, jds[b], jds[b + 1]) : null;

        // Exact wherever the signed distance changes sign between neighbouring samples.
        var exact = new List<double>();
        for (int k = Math.Max(a - 1, 0); k <= Math.Min(b, n - 1); k++)
        {
            double x = d(k), y = d(k + 1);
            if (x == 0) exact.Add(jds[k]);
            else if ((x < 0) != (y < 0) && y != 0)
                exact.Add(Bisect(t => SignedAt(mover, branch, t), jds[k], jds[k + 1]));
            else if (y == 0 && k + 1 == n) exact.Add(jds[n]);
        }
        // A crossing found just outside the run's own ends belongs to it only if it
        // falls between entering and leaving.
        exact.RemoveAll(t => (enter is { } e && t < e) || (leave is { } l && t > l));

        double peakJd, minOrb;
        if (exact.Count > 0)
        {
            peakJd = exact[0];
            minOrb = 0;
        }
        else
        {
            int k = a;
            for (int i = a; i <= b; i++)
                if (Math.Abs(d(i)) < Math.Abs(d(k))) k = i;
            (peakJd, minOrb) = Minimise(mover, branch, jds[Math.Max(k - 1, 0)], jds[Math.Min(k + 1, n)]);
        }

        return new TransitPass
        {
            Mover = mover,
            Target = point,
            Aspect = aspect,
            EnterUtc = enter is { } en ? SwissEphemeris.JulianDayToDateTime(en) : null,
            LeaveUtc = leave is { } le ? SwissEphemeris.JulianDayToDateTime(le) : null,
            ExactUtc = exact.Select(SwissEphemeris.JulianDayToDateTime).ToList(),
            PeakUtc = SwissEphemeris.JulianDayToDateTime(peakJd),
            MinOrb = minOrb,
            // The mean node always runs backwards; that is not a retrograde period.
            MoverRetrograde = _charts.CalculateBody(peakJd, mover) is { IsRetrograde: true },
            TargetHouse = timed && !point.IsAngle ? natal.GetHouseForLongitude(natalLon) : null,
        };
    }

    // The natal points a transit can land on. Without a birth time the angles are
    // unknown and the Moon may be several degrees off, which a 1° orb cannot absorb —
    // so those are left out rather than read from a noon guess.
    private static List<(NatalPoint point, double lon)> Targets(NatalChart natal, bool timed)
    {
        var targets = new List<(NatalPoint, double)>();
        foreach (var p in natal.Planets)
        {
            if (!timed && p.Planet == Planet.Moon) continue;
            targets.Add((NatalPoint.Of(p.Planet), p.Longitude));
        }
        if (timed)
        {
            targets.Add((NatalPoint.Ascendant, natal.Ascendant));
            targets.Add((NatalPoint.Midheaven, natal.Midheaven));
        }
        return targets;
    }

    private TransitEvent? FindEvent(
        NatalChart natal, bool timed, Planet mover, PlanetPosition[] track, double[] jds,
        NatalPoint point, double natalLon, AspectType aspect)
    {
        int n = track.Length - 1;

        // The furthest the mover travels between two samples: a contact that forms and
        // passes between samples still leaves one of them within Orb + slack.
        double slack = 0;
        for (int i = 0; i < n; i++)
            slack = Math.Max(slack, Math.Abs(Signed(track[i + 1].Longitude - track[i].Longitude)));

        // An aspect is exact at one longitude (conjunction, opposition) or two (the
        // others: one either side of the natal point). Take whichever comes closest.
        double bestOrb = double.MaxValue, bestJd = 0, bestBranch = 0;
        foreach (double branch in Branches(natalLon, aspect.Angle()))
        {
            int k = 0;
            double kOrb = double.MaxValue;
            for (int i = 0; i <= n; i++)
            {
                double orb = Math.Abs(Signed(track[i].Longitude - branch));
                if (orb < kOrb) { kOrb = orb; k = i; }
            }
            if (kOrb > Orb + slack) continue;

            var (jd, refined) = Minimise(mover, branch, jds[Math.Max(k - 1, 0)], jds[Math.Min(k + 1, n)]);
            if (refined < bestOrb) { bestOrb = refined; bestJd = jd; bestBranch = branch; }
        }
        if (bestOrb > Orb) return null;

        var peak = _charts.CalculateBody(bestJd, mover);
        if (peak is null) return null;

        TransitPhase phase;
        DateTime? exactUtc;
        if (bestOrb < ExactTolerance)
        {
            phase = TransitPhase.Exact;
            exactUtc = SwissEphemeris.JulianDayToDateTime(bestJd);
        }
        else
        {
            double atStart = Math.Abs(Signed(track[0].Longitude - bestBranch));
            double atEnd = Math.Abs(Signed(track[n].Longitude - bestBranch));
            phase = atEnd < atStart ? TransitPhase.Building : TransitPhase.Easing;
            exactUtc = phase == TransitPhase.Building
                ? FindExact(mover, bestBranch, jds[n], +1)
                : FindExact(mover, bestBranch, jds[0], -1);
        }

        return new TransitEvent
        {
            Mover = mover,
            Target = point,
            Aspect = aspect,
            MinOrb = bestOrb,
            PeakUtc = SwissEphemeris.JulianDayToDateTime(bestJd),
            Phase = phase,
            ExactUtc = exactUtc,
            // The mean node always runs backwards; that is not a retrograde period.
            MoverRetrograde = peak.IsRetrograde && mover != Planet.NorthNode,
            MoverHouse = timed ? natal.GetHouseForLongitude(peak.Longitude) : null,
            TargetHouse = timed && !point.IsAngle ? natal.GetHouseForLongitude(natalLon) : null,
        };
    }

    private static IEnumerable<double> Branches(double natalLon, double angle)
    {
        yield return Normalize(natalLon + angle);
        if (angle is > 0 and < 180)
            yield return Normalize(natalLon - angle);
    }

    // Distance (unsigned) from the mover to an exact-aspect longitude at one instant.
    private double OrbAt(Planet mover, double branch, double jd) =>
        _charts.CalculateBody(jd, mover) is { } p ? Math.Abs(Signed(p.Longitude - branch)) : double.MaxValue;

    private double SignedAt(Planet mover, double branch, double jd) =>
        _charts.CalculateBody(jd, mover) is { } p ? Signed(p.Longitude - branch) : double.NaN;

    // Ternary search for the instant of closest approach inside a short bracket. Unlike
    // looking for a sign change, this also finds a closest approach that never becomes
    // exact (the mover turning retrograde just short), and one at the edge of the day.
    private (double jd, double orb) Minimise(Planet mover, double branch, double lo, double hi)
    {
        for (int i = 0; i < RefineIterations; i++)
        {
            double third = (hi - lo) / 3;
            double m1 = lo + third, m2 = hi - third;
            if (OrbAt(mover, branch, m1) <= OrbAt(mover, branch, m2)) hi = m2; else lo = m1;
        }
        double jd = (lo + hi) / 2;
        return (jd, OrbAt(mover, branch, jd));
    }

    // Walks outward from the day (forward for a building transit, back for an easing
    // one) to the nearest moment it is exact. Null if the mover drifts away or turns
    // around without getting there inside the search window.
    private DateTime? FindExact(Planet mover, double branch, double fromJd, int direction)
    {
        var (step, steps) = mover switch
        {
            Planet.Moon => (1.0 / 24, 12),   // an hour at a time, half a day out
            _ when mover.IsPersonal() => (0.25, 40),       // ten days out
            _ => (1.0, 240),                               // eight months out
        };

        double prevJd = fromJd;
        double prev = SignedAt(mover, branch, prevJd);
        for (int i = 1; i <= steps && !double.IsNaN(prev); i++)
        {
            double jd = fromJd + direction * step * i;
            double cur = SignedAt(mover, branch, jd);
            if (double.IsNaN(cur)) break;

            if (prev == 0) return SwissEphemeris.JulianDayToDateTime(prevJd);
            if ((prev < 0) != (cur < 0))
            {
                double root = Bisect(t => SignedAt(mover, branch, t), prevJd, jd);
                return SwissEphemeris.JulianDayToDateTime(root);
            }
            if (Math.Abs(cur) > GiveUpOrb) break;

            prevJd = jd;
            prev = cur;
        }
        return null;
    }

    // Root of f between a and b, given f changes sign across them.
    private static double Bisect(Func<double, double> f, double a, double b)
    {
        double fa = f(a);
        for (int i = 0; i < 40; i++)
        {
            double mid = (a + b) / 2;
            double fm = f(mid);
            if (double.IsNaN(fm)) break;
            if ((fa < 0) == (fm < 0)) { a = mid; fa = fm; } else b = mid;
        }
        return (a + b) / 2;
    }

    // The Moon's phase for the day: a New, Quarter or Full Moon if one falls inside it
    // (with its time), otherwise the in-between phase the Moon is in at midday.
    private (MoonPhase phase, DateTime? exactUtc) MoonPhaseFor(
        Dictionary<Planet, PlanetPosition[]> tracks, double[] jds)
    {
        if (!tracks.TryGetValue(Planet.Sun, out var sun) || !tracks.TryGetValue(Planet.Moon, out var moon))
            return (MoonPhase.NewMoon, null);

        int n = jds.Length - 1;
        double Elongation(int i) => Normalize(moon[i].Longitude - sun[i].Longitude);

        double start = Elongation(0);
        double travelled = Normalize(Elongation(n) - start);
        for (int quarter = 0; quarter < 4; quarter++)
        {
            double ahead = Normalize(quarter * 90 - start);
            if (ahead > travelled) continue;

            double target = quarter * 90;
            double jd = Bisect(t =>
            {
                var s = _charts.CalculateBody(t, Planet.Sun);
                var m = _charts.CalculateBody(t, Planet.Moon);
                return s is null || m is null ? double.NaN : Signed(m.Longitude - s.Longitude - target);
            }, jds[0], jds[n]);
            return ((MoonPhase)(quarter * 2), SwissEphemeris.JulianDayToDateTime(jd));
        }

        int between = (int)(Elongation(n / 2) / 90) % 4;
        return ((MoonPhase)(between * 2 + 1), null);
    }

    private (ZodiacSign sign, ZodiacSign? enters, DateTime? ingressUtc) MoonSignFor(
        Dictionary<Planet, PlanetPosition[]> tracks, double[] jds)
    {
        if (!tracks.TryGetValue(Planet.Moon, out var moon))
            return (ZodiacSign.Aries, null, null);

        int n = jds.Length - 1;
        ZodiacSign first = moon[0].Sign, last = moon[n].Sign;
        if (first == last) return (first, null, null);

        double boundary = (int)last * 30;
        double jd = Bisect(t => SignedAt(Planet.Moon, boundary, t), jds[0], jds[n]);
        return (first, last, SwissEphemeris.JulianDayToDateTime(jd));
    }

    // Planets whose motion reverses during the day. The Sun and Moon never do, and the
    // mean node and mean Lilith have no stations.
    private static List<Station> StationsFor(Dictionary<Planet, PlanetPosition[]> tracks)
    {
        var stations = new List<Station>();
        foreach (var (planet, track) in tracks)
        {
            if (planet is Planet.Sun or Planet.Moon or Planet.NorthNode or Planet.Lilith) continue;
            bool before = track[0].IsRetrograde, after = track[^1].IsRetrograde;
            if (before != after) stations.Add(new Station(planet, TurnsRetrograde: after));
        }
        return stations;
    }

    private static double Normalize(double degrees) => ((degrees % 360) + 360) % 360;

    // Difference folded into −180…+180, so 359° and 1° are 2° apart rather than 358°.
    private static double Signed(double degrees) => Normalize(degrees + 180) - 180;
}
