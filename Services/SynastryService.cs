using Lore.Models;

namespace Lore.Services;

// Compares two birth charts: the aspects each person's points make to the other's, and
// the houses each person's planets fall in. This is the astronomy half of synastry — it
// reports what the contacts are and leaves what they mean to the interpreter.
//
// It differs from the natal aspect pass in ChartService in two ways:
//   • every point in one chart is tried against every point in the other, including the
//     same body (her Sun, his Sun) and the angles;
//   • the orb is a little tighter (6°, and 4° for a sextile, against 8° and 6° natally),
//     since two whole charts laid over each other make far more contacts than one.
public static class SynastryService
{
    // How much narrower than a natal aspect's orb a contact between two charts must be.
    private const double OrbTightening = 2.0;

    public static double Orb(AspectType type) => type.Orb() - OrbTightening;

    public static Synastry Compare(NatalChart first, NatalChart second)
    {
        var aspects = new List<SynastryAspect>();
        foreach (var (pointA, lonA) in Points(first))
            foreach (var (pointB, lonB) in Points(second))
            {
                double angle = AngleBetween(lonA, lonB);
                foreach (var type in AspectTypeExtensions.Majors)
                {
                    double orb = Math.Abs(angle - type.Angle());
                    if (orb <= Orb(type))
                    {
                        aspects.Add(new SynastryAspect { First = pointA, Second = pointB, Type = type, Orb = orb });
                        break; // one aspect per pair
                    }
                }
            }
        aspects.Sort((a, b) => a.Orb.CompareTo(b.Orb));

        return new Synastry
        {
            First = first,
            Second = second,
            Aspects = aspects,
            FirstInSecondHouses = Overlay(first, second),
            SecondInFirstHouses = Overlay(second, first),
        };
    }

    // The points a chart brings to the comparison. Without a birth time the angles are
    // unknown and the noon Moon may be more than 6° off — as wide as the orb itself — so
    // those are left out rather than guessed at (the same rule the daily transits use).
    private static List<(NatalPoint point, double lon)> Points(NatalChart chart)
    {
        bool timed = chart.Celebrity.BirthTimeKnown;
        var points = new List<(NatalPoint, double)>();
        foreach (var p in chart.Planets)
        {
            if (!timed && p.Planet == Planet.Moon) continue;
            points.Add((NatalPoint.Of(p.Planet), p.Longitude));
        }
        if (timed)
        {
            points.Add((NatalPoint.Ascendant, chart.Ascendant));
            points.Add((NatalPoint.Midheaven, chart.Midheaven));
        }
        return points;
    }

    // The guest's planets placed in the host's houses, which only exist if the host has
    // a birth time.
    private static List<HouseOverlay> Overlay(NatalChart guest, NatalChart host)
    {
        var overlays = new List<HouseOverlay>();
        if (!host.Celebrity.BirthTimeKnown) return overlays;

        bool guestTimed = guest.Celebrity.BirthTimeKnown;
        foreach (var p in guest.Planets)
        {
            if (!guestTimed && p.Planet == Planet.Moon) continue;
            overlays.Add(new HouseOverlay(p.Planet, host.GetHouseForLongitude(p.Longitude)));
        }
        return overlays;
    }

    private static double AngleBetween(double lonA, double lonB)
    {
        double diff = Math.Abs(lonA - lonB) % 360;
        return diff > 180 ? 360 - diff : diff;
    }
}
