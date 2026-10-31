using Lore.Models;
using NodaTime;

namespace Lore.Services;

// The Forecast: every pass a moving body makes within orb of a natal point over a
// stretch of days.
public sealed partial class TransitService
{
    // ── Forecast ──────────────────────────────────────────────────────────────
    // Every pass a moving body makes within orb of a natal point over a stretch of days,
    // with the moments it enters orb, is exact, and leaves. The Moon is left out: it
    // touches every point in the chart several times a month, which is what the Daily
    // view is for. `includeFast` adds the Sun, Mercury, Venus and Mars to the slow movers.
    public IReadOnlyList<TransitPass> Forecast(
        NatalChart natal, DateOnly start, int days, DateTimeZone zone, bool includeFast = true,
        CancellationToken cancel = default)
    {
        if (!_pinned) return For(natal).Forecast(natal, start, days, zone, includeFast, cancel);

        var first = new LocalDate(start.Year, start.Month, start.Day);
        double jd0 = SwissEphemeris.DateTimeToJulianDay(BirthTimeResolver.StartOfDay(zone, first).ToDateTimeUtc());
        double jd1 = SwissEphemeris.DateTimeToJulianDay(BirthTimeResolver.StartOfDay(zone, first.PlusDays(days)).ToDateTimeUtc());

        bool timed = natal.Celebrity.BirthTimeKnown;
        var targets = Targets(natal, timed);
        var passes = new List<TransitPass>();

        foreach (var mover in Enum.GetValues<Planet>())
        {
            if (mover == Planet.Moon || (!includeFast && mover.IsPersonal())) continue;
            cancel.ThrowIfCancellationRequested();

            double step = SampleStep(mover);
            int n = Math.Max(1, (int)Math.Ceiling((jd1 - jd0) / step));
            var jds = new double[n + 1];
            var lon = new double[n + 1];
            var speed = new double[n + 1];
            bool available = true;
            for (int i = 0; i <= n && available; i++)
            {
                jds[i] = Math.Min(jd0 + i * step, jd1);
                if (_charts.CalculateBody(jds[i], mover) is { } p) { lon[i] = p.Longitude; speed[i] = p.SpeedLongitude; }
                else available = false;
            }
            if (!available) continue;

            foreach (var (point, natalLon) in targets)
            {
                cancel.ThrowIfCancellationRequested();
                foreach (var aspect in AspectTypeExtensions.Majors)
                    foreach (double branch in Branches(natalLon, aspect.Angle()))
                    {
                        double D(int i) => Signed(lon[i] - branch);

                        // The moment the body stations between samples i and i + 1, if it
                        // does and is outside the orb when it does. Both samples may be
                        // inside the orb with the body swinging out and back in between:
                        // that is the end of one pass and the start of another.
                        double? StationOutside(int i)
                        {
                            if ((speed[i] < 0) == (speed[i + 1] < 0)) return null;
                            double station = Bisect(t => SpeedAt(mover, t), jds[i], jds[i + 1]);
                            return OrbAt(mover, branch, station) > Orb ? station : null;
                        }

                        double? enteredAfter = null; // the station the last run ended at
                        for (int i = 0; i <= n; i++)
                        {
                            if (Math.Abs(D(i)) > Orb) continue;
                            int a = i;
                            double? leftBefore = null;
                            while (i < n && Math.Abs(D(i + 1)) <= Orb && (leftBefore = StationOutside(i)) is null) i++;
                            passes.Add(Pass(natal, timed, mover, point, natalLon, aspect, branch, jds, speed, D, a, i,
                                enteredAfter, leftBefore));
                            enteredAfter = leftBefore;
                        }

                        // The opposite case, which sampling can also miss: the body stations
                        // between two samples, reaching into the orb and turning back before
                        // the next one. Where it changes direction with both samples just
                        // outside the orb, look for the closest approach in between.
                        for (int i = 0; i < n; i++)
                        {
                            if ((speed[i] < 0) == (speed[i + 1] < 0)) continue;
                            double x = Math.Abs(D(i)), y = Math.Abs(D(i + 1));
                            if (x <= Orb || y <= Orb || Math.Min(x, y) > Orb + 1) continue;

                            var (peakJd, minOrb) = Minimise(mover, branch, jds[i], jds[i + 1]);
                            if (minOrb > Orb) continue;
                            double OutBy(double t) => OrbAt(mover, branch, t) - Orb;
                            passes.Add(new TransitPass
                            {
                                Mover = mover,
                                Target = point,
                                Aspect = aspect,
                                EnterUtc = SwissEphemeris.JulianDayToDateTime(Bisect(OutBy, jds[i], peakJd)),
                                LeaveUtc = SwissEphemeris.JulianDayToDateTime(Bisect(OutBy, peakJd, jds[i + 1])),
                                ExactUtc = minOrb < ExactTolerance ? [SwissEphemeris.JulianDayToDateTime(peakJd)] : [],
                                PeakUtc = SwissEphemeris.JulianDayToDateTime(peakJd),
                                MinOrb = minOrb < ExactTolerance ? 0 : minOrb,
                                MoverRetrograde = _charts.CalculateBody(peakJd, mover) is { IsRetrograde: true },
                                TargetHouse = timed && !point.IsAngle ? natal.GetHouseForLongitude(natalLon) : null,
                            });
                        }
                    }
            }
        }

        passes.Sort((x, y) => x.PeakUtc.CompareTo(y.PeakUtc));
        return passes;
    }


    // One run of samples [a..b] inside the orb, refined at both ends and at each crossing.
    // `enteredAfter` and `leftBefore` are given when the run is bounded not by a sample
    // outside the orb but by a station outside it, between two samples that are inside.
    private TransitPass Pass(
        NatalChart natal, bool timed, Planet mover, NatalPoint point, double natalLon, AspectType aspect,
        double branch, double[] jds, double[] speed, Func<int, double> d, int a, int b,
        double? enteredAfter = null, double? leftBefore = null)
    {
        int n = jds.Length - 1;
        double OutBy(double t) => OrbAt(mover, branch, t) - Orb; // negative inside the orb

        double? enter = enteredAfter is { } turnedIn ? Bisect(OutBy, turnedIn, jds[a])
            : a > 0 ? Bisect(OutBy, jds[a - 1], jds[a]) : null;
        double? leave = leftBefore is { } turnedOut ? Bisect(OutBy, jds[b], turnedOut)
            : b < n ? Bisect(OutBy, jds[b], jds[b + 1]) : null;

        // Exact wherever the signed distance changes sign between neighbouring samples.
        var exact = new List<double>();
        for (int k = Math.Max(a - 1, 0); k <= Math.Min(b, n - 1); k++)
        {
            double x = d(k), y = d(k + 1);
            if (x == 0) exact.Add(jds[k]);
            else if ((x < 0) != (y < 0) && y != 0)
                exact.Add(Bisect(t => SignedAt(mover, branch, t), jds[k], jds[k + 1]));
            else if (y == 0 && k + 1 == n) exact.Add(jds[n]);
            else if (y != 0 && (speed[k] < 0) != (speed[k + 1] < 0))
            {
                // On the same side at both samples, but the body turned round in between:
                // it may have crossed the exact point, stationed just beyond it, and
                // crossed back. It is furthest over where it stations, so look there.
                double station = Bisect(t => SpeedAt(mover, t), jds[k], jds[k + 1]);
                double beyond = SignedAt(mover, branch, station);
                if (!double.IsNaN(beyond) && beyond != 0 && (beyond < 0) != (x < 0))
                {
                    exact.Add(Bisect(t => SignedAt(mover, branch, t), jds[k], station));
                    exact.Add(Bisect(t => SignedAt(mover, branch, t), station, jds[k + 1]));
                }
            }
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
}
