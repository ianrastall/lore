using Lore.Models;

namespace Lore.Services;

// Detects the classic multi-body configurations from the chart's positions and
// its already-computed major aspects. Only patterns expressible with Lore's five
// major aspects are found: a Yod, for instance, needs the quincunx, which Lore
// does not track. Kept separate from the interpreter so the detection is
// testable and the report layer only has to render the result.
public static class AspectPatternService
{
    private const int StelliumMin = 3;

    public static IReadOnlyList<AspectPattern> Detect(NatalChart chart)
    {
        var signs = chart.Planets.ToDictionary(p => p.Planet, p => p.Sign);
        var aspect = BuildAspectLookup(chart);
        var bodies = chart.Planets.Select(p => p.Planet).ToList();

        var patterns = new List<AspectPattern>();
        patterns.AddRange(Stelliums(chart));
        patterns.AddRange(GrandTrines(bodies, aspect, signs));
        var crosses = GrandCrosses(bodies, aspect, signs);
        patterns.AddRange(crosses);
        patterns.AddRange(TSquares(bodies, aspect, signs, crosses));
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
                Planets = g.OrderBy(p => p.DegreeInSign).Select(p => p.Planet).ToList()
            });
    }

    // ── Grand Trine: three bodies in mutual trine ─────────────────────────────
    private static IEnumerable<AspectPattern> GrandTrines(
        List<Planet> bodies, Dictionary<(Planet, Planet), AspectType> aspect,
        Dictionary<Planet, ZodiacSign> signs)
    {
        for (int i = 0; i < bodies.Count; i++)
            for (int j = i + 1; j < bodies.Count; j++)
                for (int k = j + 1; k < bodies.Count; k++)
                {
                    if (Is(aspect, bodies[i], bodies[j], AspectType.Trine)
                        && Is(aspect, bodies[i], bodies[k], AspectType.Trine)
                        && Is(aspect, bodies[j], bodies[k], AspectType.Trine))
                    {
                        yield return new AspectPattern
                        {
                            Type = PatternType.GrandTrine,
                            Planets = new[] { bodies[i], bodies[j], bodies[k] },
                            Element = signs[bodies[i]].GetElement()
                        };
                    }
                }
    }

    // ── Grand Cross: two oppositions joined by four squares ───────────────────
    private static List<AspectPattern> GrandCrosses(
        List<Planet> bodies, Dictionary<(Planet, Planet), AspectType> aspect,
        Dictionary<Planet, ZodiacSign> signs)
    {
        var found = new List<AspectPattern>();
        for (int a = 0; a < bodies.Count; a++)
            for (int b = a + 1; b < bodies.Count; b++)
                for (int c = b + 1; c < bodies.Count; c++)
                    for (int d = c + 1; d < bodies.Count; d++)
                    {
                        Planet[] q = { bodies[a], bodies[b], bodies[c], bodies[d] };
                        // Three ways to split four bodies into two opposition pairs.
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
                                    Planets = q,
                                    Modality = signs[q[0]].GetModality()
                                });
                                break; // one grand cross per set of four
                            }
                        }
                    }
        return found;
    }

    // ── T-Square: an opposition with a third body square to both ends ─────────
    private static IEnumerable<AspectPattern> TSquares(
        List<Planet> bodies, Dictionary<(Planet, Planet), AspectType> aspect,
        Dictionary<Planet, ZodiacSign> signs, List<AspectPattern> crosses)
    {
        for (int i = 0; i < bodies.Count; i++)
            for (int j = i + 1; j < bodies.Count; j++)
            {
                if (!Is(aspect, bodies[i], bodies[j], AspectType.Opposition)) continue;
                for (int a = 0; a < bodies.Count; a++)
                {
                    Planet apex = bodies[a];
                    if (apex == bodies[i] || apex == bodies[j]) continue;
                    if (Is(aspect, apex, bodies[i], AspectType.Square)
                        && Is(aspect, apex, bodies[j], AspectType.Square))
                    {
                        var trio = new[] { bodies[i], bodies[j], apex };
                        // Skip a T-square that is merely one arm of a detected grand cross.
                        if (crosses.Any(x => trio.All(x.Planets.Contains))) continue;
                        yield return new AspectPattern
                        {
                            Type = PatternType.TSquare,
                            Planets = trio,
                            Apex = apex,
                            Modality = signs[apex].GetModality()
                        };
                    }
                }
            }
    }

    // ── helpers ───────────────────────────────────────────────────────────────
    private static Dictionary<(Planet, Planet), AspectType> BuildAspectLookup(NatalChart chart)
    {
        var map = new Dictionary<(Planet, Planet), AspectType>();
        foreach (var asp in chart.Aspects)
            map[Key(asp.PlanetA, asp.PlanetB)] = asp.Type; // one aspect per pair
        return map;
    }

    private static (Planet, Planet) Key(Planet a, Planet b) =>
        (int)a <= (int)b ? (a, b) : (b, a);

    private static bool Is(Dictionary<(Planet, Planet), AspectType> map, Planet a, Planet b, AspectType type) =>
        map.TryGetValue(Key(a, b), out var t) && t == type;
}
