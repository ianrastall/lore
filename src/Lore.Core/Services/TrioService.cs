using Lore.Models;

namespace Lore.Services;

// Sets three charts together. Each pair is compared as any two charts are, and the three
// are searched for grand trines running through all of them: one point from each person,
// each trine with both of the others. A trine has no standard three-person score, so
// the measure here is Lore's own.
public static class TrioService
{
    // A grand trine with two slow points in it links everyone born in those years with
    // anyone; it needs at least this many corners of its own.
    private const int PersonalCorners = 2;

    // The weight of grand trine that makes a trio Golden, once its pairs qualify. Any
    // grand trine would be no mark at all: at this orb nine trios in ten have one. This
    // bar asks for personal points close to exact (a Sun, Moon and Venus within about 3°).
    // Set against 20,000 random trios from the library: about 3% read as Golden, 26%
    // Harmonious, 34% Mixed and 37% Strained. TrioServiceTests holds those proportions.
    private const double GoldenWeight = 0.5;

    public static Trio Compare(NatalChart first, NatalChart second, NatalChart third)
    {
        NatalChart[] charts = [first, second, third];
        (int, int)[] pairs = [(0, 1), (0, 2), (1, 2)];
        var compared = pairs.Select(p =>
        {
            var contacts = SynastryService.Compare(charts[p.Item1], charts[p.Item2]);
            return new TrioPair(p.Item1, p.Item2, contacts, SynastryScoring.Assess(contacts.Aspects));
        }).ToList();

        var grandTrines = GrandTrines(charts);
        return new Trio
        {
            Charts = charts,
            Pairs = compared,
            GrandTrines = grandTrines,
            Tone = Assess(compared, grandTrines.Count > 0 ? grandTrines[0].Weight : 0),
        };
    }

    // No pair in difficulty and more ease than not makes a trio Harmonious; a strong enough
    // grand trine through all three makes that Golden. A pair in difficulty rules both
    // out, however easy the other two; they can only keep the trio Mixed.
    private static TrioTone Assess(IReadOnlyList<TrioPair> pairs, double goldenWeight)
    {
        var levels = pairs.Select(p => p.Compatibility.Level).ToList();
        int total = levels.Sum();
        if (levels.Min() < 0) return total < 0 ? TrioTone.Strained : TrioTone.Mixed;
        if (total < 1) return TrioTone.Mixed;
        return goldenWeight >= GoldenWeight ? TrioTone.Golden : TrioTone.Harmonious;
    }

    private static List<TrioGrandTrine> GrandTrines(NatalChart[] charts)
    {
        var points = charts.Select((chart, person) => SynastryService.Points(chart)
            .Select(p => new TrioPoint(person, p.point, p.lon)).ToList()).ToList();
        double orb = SynastryService.Orb(AspectType.Trine);

        var found = new List<TrioGrandTrine>();
        foreach (var a in points[0])
            foreach (var b in points[1])
            {
                double ab = TrineOrb(a, b);
                if (ab > orb) continue;
                foreach (var c in points[2])
                {
                    double widest = Math.Max(ab, Math.Max(TrineOrb(a, c), TrineOrb(b, c)));
                    if (widest > orb) continue;
                    TrioPoint[] corners = [a, b, c];
                    if (corners.Count(x => !SynastryScoring.IsGenerational(x.Point)) < PersonalCorners) continue;

                    double closeness = 1 - 0.7 * widest / orb;
                    double weight = corners.Aggregate(closeness, (w, x) => w * SynastryScoring.PointWeight(x.Point));
                    found.Add(new TrioGrandTrine(corners, widest, weight));
                }
            }
        return found.OrderByDescending(g => g.Weight).ToList();
    }

    private static double TrineOrb(TrioPoint a, TrioPoint b)
    {
        double diff = Math.Abs(a.Longitude - b.Longitude) % 360;
        return Math.Abs((diff > 180 ? 360 - diff : diff) - AspectType.Trine.Angle());
    }

    // Everyone in `people` as the third of a trio with the two given, best first: Golden
    // ahead of the rest, then by grand-trine weight and the easy share. The two
    // themselves are left out, and so is anyone whose chart cannot be calculated.
    public static List<TrioMatch> Rank(ChartService charts, NatalChart first, NatalChart second,
        IEnumerable<Celebrity> people, CancellationToken cancel = default)
    {
        var matches = new List<TrioMatch>();
        foreach (var person in people)
        {
            cancel.ThrowIfCancellationRequested();
            if (person.Id == first.Celebrity.Id || person.Id == second.Celebrity.Id) continue;
            try
            {
                var trio = Compare(first, second, charts.Calculate(person));
                matches.Add(new TrioMatch(person, trio.Tone, trio.GoldenWeight, trio.Share, trio.GrandTrines.Count));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Diagnostics.Log($"Trio match for chart {person.Id} skipped: {ex.Message}");
            }
        }

        return matches
            .OrderBy(m => m.Tone)
            .ThenByDescending(m => m.GoldenWeight)
            .ThenByDescending(m => m.Share)
            .ThenBy(m => m.Person.Name, StringComparer.Ordinal)
            .ToList();
    }
}
