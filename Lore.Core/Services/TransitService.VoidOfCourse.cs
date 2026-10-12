using Lore.Models;
using NodaTime;

namespace Lore.Services;

// The stretches in which the Moon makes no further major aspect before leaving its sign.
public sealed partial class TransitService
{
    // ── Void-of-course Moon ───────────────────────────────────────────────────

    // The bodies whose aspects keep the Moon "in course": the Sun and the planets. The
    // node, Chiron and Lilith are not counted, by the usual modern convention.
    private static readonly Planet[] CourseBodies =
    [
        Planet.Sun, Planet.Mercury, Planet.Venus, Planet.Mars, Planet.Jupiter,
        Planet.Saturn, Planet.Uranus, Planet.Neptune, Planet.Pluto,
    ];

    // The void-of-course stretches overlapping the time between two Julian days. This
    // has nothing to do with any birth chart: it is the same for everyone.
    public IReadOnlyList<VoidOfCourse> VoidOfCourse(double fromJd, double toJd)
    {
        // The Moon is never more than about two and a half days in a sign, so three
        // days either side takes in the whole of every stay that overlaps the stretch.
        const double Margin = 3;
        double lo = fromJd - Margin;
        int n = (int)Math.Ceiling((toJd + Margin - lo) * 24);
        double At(int i) => lo + i / 24.0;

        var moon = new double[n + 1];
        for (int i = 0; i <= n; i++)
        {
            if (_charts.CalculateBody(At(i), Planet.Moon) is not { } m) return [];
            moon[i] = m.Longitude;
        }

        var ingresses = new List<(double Jd, ZodiacSign Sign)>();
        for (int i = 0; i < n; i++)
        {
            var next = ZodiacSignExtensions.FromLongitude(moon[i + 1]);
            if (next == ZodiacSignExtensions.FromLongitude(moon[i])) continue;
            double boundary = (int)next * 30;
            ingresses.Add((Bisect(t => SignedAt(Planet.Moon, boundary, t), At(i), At(i + 1)), next));
        }

        // Every exact major aspect from the Moon to one of the bodies. The Moon gains on
        // all of them all the time, so its lead over each only ever grows, and an aspect
        // is exact where that lead passes 0°, 60°, 90°, 120° or 180° either side.
        var aspects = new List<(double Jd, Planet Body, AspectType Type)>();
        foreach (var body in CourseBodies)
        {
            var lead = new double[n + 1];
            bool available = true;
            for (int i = 0; i <= n && available; i++)
            {
                if (_charts.CalculateBody(At(i), body) is { } p) lead[i] = Normalize(moon[i] - p.Longitude);
                else available = false;
            }
            if (!available) continue;

            for (int i = 0; i < n; i++)
            {
                double gained = Signed(lead[i + 1] - lead[i]);
                foreach (var type in AspectTypeExtensions.Majors)
                    foreach (double angle in Branches(0, type.Angle()))
                    {
                        double ahead = Normalize(angle - lead[i]);
                        if (ahead <= 0 || ahead > gained) continue;
                        double jd = Bisect(t =>
                            _charts.CalculateBody(t, Planet.Moon) is { } m && _charts.CalculateBody(t, body) is { } p
                                ? Signed(m.Longitude - p.Longitude - angle) : double.NaN,
                            At(i), At(i + 1));
                        aspects.Add((jd, body, type));
                    }
            }
        }

        var voids = new List<VoidOfCourse>();
        for (int k = 0; k + 1 < ingresses.Count; k++)
        {
            var (entered, sign) = ingresses[k];
            var (left, enters) = ingresses[k + 1];
            if (left <= fromJd || entered >= toJd) continue;

            var inSign = aspects.Where(a => a.Jd >= entered && a.Jd < left).ToList();
            var last = inSign.Count > 0 ? inSign.MaxBy(a => a.Jd) : ((double Jd, Planet Body, AspectType Type)?)null;
            double start = last?.Jd ?? entered;
            if (start >= toJd) continue;
            voids.Add(new VoidOfCourse(
                SwissEphemeris.JulianDayToDateTime(start), SwissEphemeris.JulianDayToDateTime(left),
                sign, enters, last?.Body, last?.Type));
        }
        return voids;
    }
}
