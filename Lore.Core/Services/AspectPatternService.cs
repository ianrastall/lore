using Lore.Models;

namespace Lore.Services;

// Detects the classic multi-body configurations from the chart's positions and
// its already-computed aspects: stellium, grand trine, kite, grand cross, T-square,
// mystic rectangle and yod. A Yod needs the quincunx, so it is only found when
// the minor aspects are switched on. Kept separate from the interpreter so the
// detection is testable and the report layer only has to render the result.
//
// The Ascendant and Midheaven take part alongside the bodies (in a chart with a
// birth time), so a T-square can have the Ascendant at its apex. A stellium is
// bodies only, and the two angles are never aspected to each other.
public static class AspectPatternService
{
    private const int StelliumMin = 3;

    public static IReadOnlyList<AspectPattern> Detect(NatalChart chart)
    {
        var signs = chart.Planets.ToDictionary(p => NatalPoint.Of(p.Planet), p => p.Sign);
        var points = chart.Planets.Select(p => NatalPoint.Of(p.Planet)).ToList();
        if (chart.Timed)
        {
            foreach (var angle in new[] { NatalPoint.Ascendant, NatalPoint.Midheaven })
            {
                points.Add(angle);
                signs[angle] = ZodiacSignExtensions.FromLongitude(chart.LongitudeOf(angle) ?? 0);
            }
        }
        var aspect = BuildAspectLookup(chart, points);

        var patterns = new List<AspectPattern>();
        patterns.AddRange(Stelliums(chart));
        var trines = GrandTrines(points, aspect, signs).ToList();
        var kites = Kites(points, aspect, trines);
        // A grand trine that is the body of a kite is told as the kite.
        patterns.AddRange(trines.Where(t => !kites.Any(k => t.Points.All(k.Points.Contains))));
        patterns.AddRange(kites);
        var crosses = GrandCrosses(points, aspect, signs);
        patterns.AddRange(crosses);
        patterns.AddRange(TSquares(points, aspect, signs, crosses));
        patterns.AddRange(MysticRectangles(points, aspect));
        patterns.AddRange(Yods(points, aspect));
        return patterns;
    }

    // ── Stellium: 3+ bodies sharing a sign ────────────────────────────────────
    private static IEnumerable<AspectPattern> Stelliums(NatalChart chart)
    {
        return chart.Planets
            .GroupBy(p => p.Sign)
            .Where(g => g.Count() >= StelliumMin)
            .Select(g => new AspectPattern
            {
                Type = PatternType.Stellium,
                Sign = g.Key,
                Element = g.Key.GetElement(),
                Modality = g.Key.GetModality(),
                Points = g.OrderBy(p => p.DegreeInSign).Select(p => NatalPoint.Of(p.Planet)).ToList()
            });
    }

    // ── Grand Trine: three points in mutual trine ─────────────────────────────
    private static IEnumerable<AspectPattern> GrandTrines(
        List<NatalPoint> points, Dictionary<(int, int), AspectType> aspect,
        Dictionary<NatalPoint, ZodiacSign> signs)
    {
        for (int i = 0; i < points.Count; i++)
            for (int j = i + 1; j < points.Count; j++)
                for (int k = j + 1; k < points.Count; k++)
                {
                    if (Is(aspect, i, j, AspectType.Trine)
                        && Is(aspect, i, k, AspectType.Trine)
                        && Is(aspect, j, k, AspectType.Trine))
                    {
                        yield return new AspectPattern
                        {
                            Type = PatternType.GrandTrine,
                            Points = new[] { points[i], points[j], points[k] },
                            Element = Shared(new[] { i, j, k }.Select(x => signs[points[x]].GetElement()))
                        };
                    }
                }
    }

    // ── Kite: a grand trine, and a fourth point opposite one corner of it and
    //    sextile the other two ─────────────────────────────────────────────────
    private static List<AspectPattern> Kites(
        List<NatalPoint> points, Dictionary<(int, int), AspectType> aspect, List<AspectPattern> trines)
    {
        var found = new List<AspectPattern>();
        foreach (var trine in trines)
        {
            var corners = trine.Points.Select(x => points.IndexOf(x)).ToArray();
            for (int d = 0; d < points.Count; d++)
            {
                if (corners.Contains(d)) continue;
                foreach (int tail in corners)
                {
                    if (!Is(aspect, d, tail, AspectType.Opposition) ||
                        !corners.Where(c => c != tail).All(c => Is(aspect, d, c, AspectType.Sextile))) continue;
                    found.Add(new AspectPattern
                    {
                        Type = PatternType.Kite,
                        // The corner the fourth point opposes comes first.
                        Points = [points[tail], .. trine.Points.Where(x => x != points[tail]), points[d]],
                        Apex = points[d],
                        Element = trine.Element,
                    });
                }
            }
        }
        return found;
    }

    // ── Mystic Rectangle: two oppositions whose ends are joined by two sextiles
    //    and two trines ───────────────────────────────────────────────────────
    private static List<AspectPattern> MysticRectangles(
        List<NatalPoint> points, Dictionary<(int, int), AspectType> aspect)
    {
        var found = new List<AspectPattern>();
        for (int a = 0; a < points.Count; a++)
            for (int b = a + 1; b < points.Count; b++)
                for (int c = b + 1; c < points.Count; c++)
                    for (int d = c + 1; d < points.Count; d++)
                    {
                        int[] q = { a, b, c, d };
                        (int o1, int o2, int o3, int o4)[] pairings =
                        {
                            (0, 1, 2, 3), (0, 2, 1, 3), (0, 3, 1, 2)
                        };
                        foreach (var (o1, o2, o3, o4) in pairings)
                        {
                            if (!Is(aspect, q[o1], q[o2], AspectType.Opposition) ||
                                !Is(aspect, q[o3], q[o4], AspectType.Opposition)) continue;
                            bool Sides(AspectType near, AspectType far) =>
                                Is(aspect, q[o1], q[o3], near) && Is(aspect, q[o2], q[o4], near) &&
                                Is(aspect, q[o1], q[o4], far) && Is(aspect, q[o2], q[o3], far);
                            if (!Sides(AspectType.Sextile, AspectType.Trine) && !Sides(AspectType.Trine, AspectType.Sextile)) continue;
                            found.Add(new AspectPattern
                            {
                                Type = PatternType.MysticRectangle,
                                // Each opposition's two ends side by side.
                                Points = [points[q[o1]], points[q[o2]], points[q[o3]], points[q[o4]]],
                            });
                            break;
                        }
                    }
        return found;
    }

    // ── Grand Cross: two oppositions joined by four squares ───────────────────
    private static List<AspectPattern> GrandCrosses(
        List<NatalPoint> points, Dictionary<(int, int), AspectType> aspect,
        Dictionary<NatalPoint, ZodiacSign> signs)
    {
        var found = new List<AspectPattern>();
        for (int a = 0; a < points.Count; a++)
            for (int b = a + 1; b < points.Count; b++)
                for (int c = b + 1; c < points.Count; c++)
                    for (int d = c + 1; d < points.Count; d++)
                    {
                        int[] q = { a, b, c, d };
                        // Three ways to split four points into two opposition pairs.
                        (int o1, int o2, int o3, int o4)[] pairings =
                        {
                            (0, 1, 2, 3), (0, 2, 1, 3), (0, 3, 1, 2)
                        };
                        foreach (var (o1, o2, o3, o4) in pairings)
                        {
                            if (Is(aspect, q[o1], q[o2], AspectType.Opposition)
                                && Is(aspect, q[o3], q[o4], AspectType.Opposition)
                                && Is(aspect, q[o1], q[o3], AspectType.Square)
                                && Is(aspect, q[o1], q[o4], AspectType.Square)
                                && Is(aspect, q[o2], q[o3], AspectType.Square)
                                && Is(aspect, q[o2], q[o4], AspectType.Square))
                            {
                                found.Add(new AspectPattern
                                {
                                    Type = PatternType.GrandCross,
                                    Points = q.Select(i => points[i]).ToList(),
                                    Modality = Shared(q.Select(i => signs[points[i]].GetModality()))
                                });
                                break; // one grand cross per set of four
                            }
                        }
                    }
        return found;
    }

    // ── T-Square: an opposition with a third point square to both ends ────────
    private static IEnumerable<AspectPattern> TSquares(
        List<NatalPoint> points, Dictionary<(int, int), AspectType> aspect,
        Dictionary<NatalPoint, ZodiacSign> signs, List<AspectPattern> crosses)
    {
        for (int i = 0; i < points.Count; i++)
            for (int j = i + 1; j < points.Count; j++)
            {
                if (!Is(aspect, i, j, AspectType.Opposition)) continue;
                for (int a = 0; a < points.Count; a++)
                {
                    if (a == i || a == j) continue;
                    if (Is(aspect, a, i, AspectType.Square)
                        && Is(aspect, a, j, AspectType.Square))
                    {
                        var trio = new[] { points[i], points[j], points[a] };
                        // Skip a T-square that is merely one arm of a detected grand cross.
                        if (crosses.Any(x => trio.All(x.Points.Contains))) continue;
                        yield return new AspectPattern
                        {
                            Type = PatternType.TSquare,
                            Points = trio,
                            Apex = points[a],
                            Modality = Shared(trio.Select(x => signs[x].GetModality()))
                        };
                    }
                }
            }
    }

    // ── Yod: two points in sextile, each quincunx the same third point ────────
    private static IEnumerable<AspectPattern> Yods(
        List<NatalPoint> points, Dictionary<(int, int), AspectType> aspect)
    {
        for (int i = 0; i < points.Count; i++)
            for (int j = i + 1; j < points.Count; j++)
            {
                if (!Is(aspect, i, j, AspectType.Sextile)) continue;
                for (int a = 0; a < points.Count; a++)
                {
                    if (a == i || a == j) continue;
                    if (Is(aspect, a, i, AspectType.Quincunx) && Is(aspect, a, j, AspectType.Quincunx))
                        yield return new AspectPattern
                        {
                            Type = PatternType.Yod,
                            Points = new[] { points[i], points[j], points[a] },
                            Apex = points[a],
                        };
                }
            }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    // The element or modality every point of a pattern has in common. Null when they
    // differ: an aspect can hold across a sign boundary (29° Aries trine 1° Virgo), and
    // the figure is then real but belongs to no one element or modality.
    private static T? Shared<T>(IEnumerable<T> values) where T : struct, Enum
    {
        var distinct = values.Distinct().ToList();
        return distinct.Count == 1 ? distinct[0] : null;
    }

    // Every aspect in the chart, keyed by the positions of its two points in `points`.
    private static Dictionary<(int, int), AspectType> BuildAspectLookup(NatalChart chart, List<NatalPoint> points)
    {
        var map = new Dictionary<(int, int), AspectType>();
        void Add(NatalPoint a, NatalPoint b, AspectType type)
        {
            int i = points.IndexOf(a), j = points.IndexOf(b);
            if (i >= 0 && j >= 0) map[Key(i, j)] = type; // one aspect per pair
        }

        foreach (var asp in chart.Aspects)
            Add(NatalPoint.Of(asp.PlanetA), NatalPoint.Of(asp.PlanetB), asp.Type);
        foreach (var asp in chart.AngleAspects)
            Add(NatalPoint.Of(asp.Planet), asp.Angle, asp.Type);
        return map;
    }

    private static (int, int) Key(int a, int b) => a <= b ? (a, b) : (b, a);

    private static bool Is(Dictionary<(int, int), AspectType> map, int a, int b, AspectType type) =>
        map.TryGetValue(Key(a, b), out var t) && t == type;
}
