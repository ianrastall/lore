using Lore.Models;

namespace Lore.Services;

// Takes the further measurements of a birth chart (see NatalMetrics). Pure arithmetic on
// an already-calculated chart: nothing here touches the ephemeris.
public static class NatalMetricsService
{
    // How close two declinations must be to count as a parallel or contra-parallel.
    public const double ParallelOrb = 1.0;

    private static readonly Planet[] TenPlanets =
    [
        Planet.Sun, Planet.Moon, Planet.Mercury, Planet.Venus, Planet.Mars,
        Planet.Jupiter, Planet.Saturn, Planet.Uranus, Planet.Neptune, Planet.Pluto,
    ];

    public static NatalMetrics Compute(NatalChart chart) => new()
    {
        Points = Points(chart),
        Moon = Lunar(chart),
        Solar = Solar(chart),
        Obliquity = chart.Obliquity,
        OutOfBounds = OutOfBounds(chart),
        Parallels = Parallels(chart.Planets),
        Angularity = Angularity(chart),
        Distribution = Distribution(chart),
        Aspects = Aspects(chart),
        Rulers = Rulers(chart),
    };

    // ── Further points ────────────────────────────────────────────────────────

    private static List<DerivedPoint> Points(NatalChart chart)
    {
        var points = new List<DerivedPoint>();
        if (chart.GetPlanet(Planet.NorthNode) is { } node)
            points.Add(new("South Node", "☋", Normalize(node.Longitude + 180)));
        if (!chart.Timed) return points;

        points.Add(new("Descendant", "↓", Normalize(chart.Ascendant + 180)));
        points.Add(new("Imum Coeli", "IC", Normalize(chart.Midheaven + 180)));
        points.Add(new("Vertex", "Vx", Normalize(chart.Vertex)));
        if (Lot(chart, spirit: false) is { } fortune) points.Add(new("Part of Fortune", "⊗", fortune));
        if (Lot(chart, spirit: true) is { } spirit) points.Add(new("Part of Spirit", "⊕", spirit));
        return points;
    }

    // The Lots of Fortune and Spirit, reversed by sect as the traditional sources give
    // them. Fortune is Ascendant + Moon − Sun by day and Ascendant + Sun − Moon by night;
    // Spirit is the other way about.
    public static double? Lot(NatalChart chart, bool spirit)
    {
        if (!chart.Timed ||
            chart.GetPlanet(Planet.Sun) is not { } sun || chart.GetPlanet(Planet.Moon) is not { } moon)
            return null;

        double arc = chart.IsDayChart != spirit ? moon.Longitude - sun.Longitude : sun.Longitude - moon.Longitude;
        return Normalize(chart.Ascendant + arc);
    }

    // ── Moon and Sun ──────────────────────────────────────────────────────────

    private static LunarPhase? Lunar(NatalChart chart)
    {
        if (chart.GetPlanet(Planet.Sun) is not { } sun || chart.GetPlanet(Planet.Moon) is not { } moon)
            return null;

        double elongation = Normalize(moon.Longitude - sun.Longitude);
        // The true angle between them allows for the Moon's latitude (the Sun has none
        // to speak of); the lit fraction of the disc follows from it.
        double cos = Math.Cos(Radians(moon.Latitude)) * Math.Cos(Radians(elongation));
        return new LunarPhase(elongation, PhaseAt(elongation), elongation < 180, (1 - cos) / 2);
    }

    // The eighth of the cycle an elongation falls in, each centred on its exact phase.
    public static MoonPhase PhaseAt(double elongation) =>
        (MoonPhase)((int)Math.Floor(Normalize(elongation + 22.5) / 45) % 8);

    private static List<SolarRelation> Solar(NatalChart chart)
    {
        var result = new List<SolarRelation>();
        if (chart.GetPlanet(Planet.Sun) is not { } sun) return result;

        foreach (var planet in TenPlanets.Skip(1))
        {
            if (chart.GetPlanet(planet) is not { } p) continue;
            double ahead = Normalize(p.Longitude - sun.Longitude);
            double separation = ahead > 180 ? 360 - ahead : ahead;
            var condition = planet <= Planet.Saturn ? DignityService.SolarConditionAt(separation) : SolarCondition.None;
            result.Add(new SolarRelation(planet, separation, ahead < 180, condition));
        }
        return result;
    }

    // ── Declination ───────────────────────────────────────────────────────────

    private static List<OutOfBounds> OutOfBounds(NatalChart chart)
    {
        if (chart.Obliquity is not { } limit) return [];
        return chart.Planets
            .Where(p => p.HasEquatorial && Math.Abs(p.Declination) > limit)
            .Select(p => new OutOfBounds(p.Planet, p.Declination, Math.Abs(p.Declination) - limit))
            .ToList();
    }

    // A parallel needs both bodies on the same side of the equator, a contra-parallel on
    // opposite sides. A body whose declination is unknown takes no part.
    public static List<DeclinationContact> Parallels(IReadOnlyList<PlanetPosition> planets)
    {
        const double step = 1.0 / 1440; // one minute, as for the aspects in longitude
        var result = new List<DeclinationContact>();
        var known = planets.Where(p => p.HasEquatorial).ToList();

        for (int i = 0; i < known.Count; i++)
        for (int j = i + 1; j < known.Count; j++)
        {
            var (a, b) = (known[i], known[j]);
            bool contra = Math.Sign(a.Declination) * Math.Sign(b.Declination) < 0;
            double Orb(double da, double db) => contra ? Math.Abs(da + db) : Math.Abs(da - db);

            double orb = Orb(a.Declination, b.Declination);
            if (orb > ParallelOrb) continue;
            double next = Orb(a.Declination + a.SpeedDeclination * step, b.Declination + b.SpeedDeclination * step);
            result.Add(new DeclinationContact(a.Planet, b.Planet, contra, orb, next < orb));
        }

        result.Sort((x, y) => x.Orb.CompareTo(y.Orb));
        return result;
    }

    // ── Angles and houses ─────────────────────────────────────────────────────

    private static List<Angularity> Angularity(NatalChart chart)
    {
        if (!chart.Timed || chart.Houses.Count < 12) return [];

        (string name, double lon)[] angles =
        [
            ("Ascendant", chart.Ascendant), ("Midheaven", chart.Midheaven),
            ("Descendant", Normalize(chart.Ascendant + 180)), ("Imum Coeli", Normalize(chart.Midheaven + 180)),
        ];

        return chart.Planets.Select(p =>
        {
            var nearest = angles.MinBy(a => Separation(p.Longitude, a.lon));
            int house = chart.GetHouseForLongitude(p.Longitude);
            return new Angularity(p.Planet, nearest.name, Separation(p.Longitude, nearest.lon),
                house, Normalize(p.Longitude - chart.GetHouse(house).Longitude));
        }).ToList();
    }

    // ── Balance ───────────────────────────────────────────────────────────────

    public static Dictionary<Element, int> ElementCounts(NatalChart chart)
    {
        var counts = Enum.GetValues<Element>().ToDictionary(e => e, _ => 0);
        foreach (var p in chart.Planets) counts[p.Sign.GetElement()]++;
        return counts;
    }

    public static Dictionary<Modality, int> ModalityCounts(NatalChart chart)
    {
        var counts = Enum.GetValues<Modality>().ToDictionary(m => m, _ => 0);
        foreach (var p in chart.Planets) counts[p.Sign.GetModality()]++;
        return counts;
    }

    private static Distribution Distribution(NatalChart chart)
    {
        var elements = ElementCounts(chart);
        var modalities = ModalityCounts(chart);
        int positive = elements[Element.Fire] + elements[Element.Air];

        var houses = chart.Timed && chart.Houses.Count == 12
            ? chart.Planets.Select(p => chart.GetHouseForLongitude(p.Longitude)).ToList()
            : [];
        int In(params int[] set) => houses.Count(set.Contains);

        return new Distribution
        {
            Total = chart.Planets.Count,
            Elements = elements.Select(kv => new Tally(kv.Key.ToString(), kv.Value)).ToList(),
            Modalities = modalities.Select(kv => new Tally(kv.Key.ToString(), kv.Value)).ToList(),
            Polarities =
            [
                new("Positive (fire and air)", positive),
                new("Negative (earth and water)", chart.Planets.Count - positive),
            ],
            HouseTypes = houses.Count == 0 ? [] :
            [
                new("Angular (1, 4, 7, 10)", In(1, 4, 7, 10)),
                new("Succedent (2, 5, 8, 11)", In(2, 5, 8, 11)),
                new("Cadent (3, 6, 9, 12)", In(3, 6, 9, 12)),
            ],
            Hemispheres = houses.Count == 0 ? [] :
            [
                new("Upper (houses 7–12)", In(7, 8, 9, 10, 11, 12)),
                new("Lower (houses 1–6)", In(1, 2, 3, 4, 5, 6)),
                new("Eastern (houses 10–3)", In(10, 11, 12, 1, 2, 3)),
                new("Western (houses 4–9)", In(4, 5, 6, 7, 8, 9)),
            ],
        };
    }

    // ── Aspects in sum ────────────────────────────────────────────────────────

    private static AspectSummary Aspects(NatalChart chart)
    {
        var aspects = chart.Aspects;
        int Touching(Planet p, Func<Aspect, bool> which) =>
            aspects.Count(a => (a.PlanetA == p || a.PlanetB == p) && which(a));

        return new AspectSummary
        {
            ByType = aspects.GroupBy(a => a.Type).OrderBy(g => g.Key)
                .Select(g => new Tally(g.Key.Name(), g.Count())).ToList(),
            Applying = aspects.Count(a => a.IsApplying),
            Separating = aspects.Count(a => !a.IsApplying),
            Closest = aspects.MinBy(a => a.Orb),
            PerBody = chart.Planets
                .Select(p => new Tally(p.PlanetName, Touching(p.Planet, _ => true)))
                .OrderByDescending(t => t.Count).ToList(),
            Unaspected = TenPlanets
                .Where(p => chart.GetPlanet(p) is not null && Touching(p, a => a.Type.IsMajor()) == 0)
                .ToList(),
        };
    }

    // ── Rulers ────────────────────────────────────────────────────────────────

    private static Planet? ModernRuler(ZodiacSign sign) => sign switch
    {
        ZodiacSign.Scorpio  => Planet.Pluto,
        ZodiacSign.Aquarius => Planet.Uranus,
        ZodiacSign.Pisces   => Planet.Neptune,
        _ => null
    };

    private static Rulership Rulers(NatalChart chart)
    {
        var dispositions = chart.Planets.Select(p => new Disposition(
            p.Planet, p.Sign, DignityService.RulerOf(p.Sign),
            DignityService.TermRuler(p.Sign, p.DegreeInSign),
            DignityService.FaceRuler(p.Sign, p.DegreeInSign),
            Chain(chart, p.Planet))).ToList();

        // Where every chain ends: a planet in its own sign, or a ring of planets in each
        // other's. One ending shared by all, and that a single planet, is a final dispositor.
        var inDomicile = dispositions.Where(d => d.SignRuler == d.Planet).Select(d => d.Planet).ToList();
        var loops = new List<IReadOnlyList<Planet>>();
        foreach (var d in dispositions)
        {
            // A chain that closes on a ring ends by naming a planet it has already passed.
            var chain = d.Chain;
            int first = chain.ToList().IndexOf(chain[^1]);
            if (first == chain.Count - 1) continue;                     // the chain broke off: a body is missing
            var ring = chain.Skip(first).Take(chain.Count - 1 - first).ToList();
            if (ring.Count < 2) continue;                               // ended on a planet in its own sign
            if (!loops.Any(l => l.Count == ring.Count && l.All(ring.Contains)))
                loops.Add(ring);
        }

        var asc = chart.Timed ? ZodiacSignExtensions.FromLongitude(chart.Ascendant) : (ZodiacSign?)null;
        return new Rulership
        {
            ChartRuler = asc is { } rising ? DignityService.RulerOf(rising) : null,
            ModernChartRuler = asc is { } r ? ModernRuler(r) : null,
            Houses = chart.Timed
                ? chart.Houses.Select(h =>
                {
                    var ruler = DignityService.RulerOf(h.Sign);
                    var at = chart.GetPlanet(ruler);
                    return new HouseRuler(h.House, h.Sign, ruler, at?.Sign,
                        at is null ? null : chart.GetHouseForLongitude(at.Longitude));
                }).ToList()
                : [],
            Dispositions = dispositions,
            InDomicile = inDomicile,
            FinalDispositor = inDomicile.Count == 1 && loops.Count == 0
                              && dispositions.All(d => d.Chain[^1] == inDomicile[0]) ? inDomicile[0] : null,
            Loops = loops,
        };
    }

    // From a body to the ruler of its sign, to the ruler of that planet's sign, and so on:
    // the last entry repeats an earlier one (a planet in its own sign repeats itself).
    private static List<Planet> Chain(NatalChart chart, Planet start)
    {
        var chain = new List<Planet> { start };
        while (chart.GetPlanet(chain[^1]) is { } at)
        {
            var ruler = DignityService.RulerOf(at.Sign);
            bool seen = chain.Contains(ruler);
            chain.Add(ruler);
            if (seen) break;
        }
        return chain;
    }

    // ── Arithmetic ────────────────────────────────────────────────────────────

    private static double Normalize(double degrees) => ((degrees % 360) + 360) % 360;

    private static double Radians(double degrees) => degrees * Math.PI / 180;

    private static double Separation(double a, double b)
    {
        double diff = Math.Abs(a - b) % 360;
        return diff > 180 ? 360 - diff : diff;
    }
}
