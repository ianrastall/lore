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
    public const double GoldenWeight = 0.5;

    // The weight a figure other than a grand trine needs to be spoken of. At these orbs
    // nearly every trio has a T-square of some sort, so as with Golden the bar is on the
    // strongest. Against 4,000 random trios from the library this keeps about one trio in
    // four with a T-square, one in ten with a yod and one in twenty with a meeting point.
    private const double FigureWeight = 0.5;

    // How far a yod's two quincunxes may be from exact. The figure is a narrow one: minor
    // aspects are held tighter than major ones, here as in a birth chart.
    private const double YodOrb = 2.0;

    // `answers`: each person's, in the same order, where they have any. The pairs and the
    // trio are then also assessed as lived; their tones from the charts do not change.
    // `figures`: false leaves out the figures other than grand trines, which only a
    // reading needs and which would take a search through a thousand trios three times over.
    public static Trio Compare(NatalChart first, NatalChart second, NatalChart third,
        IReadOnlyList<MirrorReading?>? answers = null, bool figures = true)
    {
        NatalChart[] charts = [first, second, third];
        MirrorReading? Of(int person)
        {
            var a = answers?.ElementAtOrDefault(person);
            return a is not null && a.IsFor(charts[person]) ? a : null;
        }
        (int, int)[] pairs = [(0, 1), (0, 2), (1, 2)];
        var compared = pairs.Select(p =>
        {
            var contacts = SynastryService.Compare(charts[p.Item1], charts[p.Item2]);
            var (a, b) = (Of(p.Item1), Of(p.Item2));
            return new TrioPair(p.Item1, p.Item2, contacts, SynastryScoring.Assess(contacts.Aspects))
            {
                AsLived = a is null && b is null ? null : SynastryScoring.Assess(contacts.Aspects, a, b),
                Contemporaries = Lifetimes.Overlap(charts[p.Item1].Celebrity, charts[p.Item2].Celebrity),
            };
        }).ToList();

        var grandTrines = GrandTrines(charts);
        double strongest = grandTrines.Count > 0 ? grandTrines[0].Weight : 0;
        bool lived = compared.Any(p => p.AsLived is not null);
        return new Trio
        {
            Charts = charts,
            Pairs = compared,
            GrandTrines = grandTrines,
            Tone = Assess(compared.Select(p => p.Compatibility).ToList(), strongest),
            Figures = figures ? Figures(charts) : [],
            Answers = [Of(0), Of(1), Of(2)],
            LivedTone = lived ? Assess(compared.Select(p => p.AsLived ?? p.Compatibility).ToList(), strongest) : null,
        };
    }

    // How many of each element each person's chart holds, by the ten planets, and by the
    // Ascendant where it is known. The Moon of an untimed chart may be in the next sign.
    public static IReadOnlyList<(Element Element, int[] Counts)> ElementsOf(Trio trio) =>
        Enum.GetValues<Element>().Select(element => (element, trio.Charts.Select(chart =>
                chart.Planets.Count(p => p.Planet <= Planet.Pluto && (chart.Timed || p.Planet != Planet.Moon) && p.Sign.GetElement() == element) +
                (chart.Timed && ZodiacSignExtensions.FromLongitude(chart.Ascendant).GetElement() == element ? 1 : 0))
            .ToArray())).ToList();

    // No pair in difficulty and more ease than not makes a trio Harmonious; a strong enough
    // grand trine through all three makes that Golden. A pair in difficulty rules both
    // out, however easy the other two; they can only keep the trio Mixed.
    private static TrioTone Assess(IReadOnlyList<Compatibility> pairs, double goldenWeight)
        => Assess(pairs[0], pairs[1], pairs[2], goldenWeight);

    internal static TrioTone Assess(Compatibility a, Compatibility b, Compatibility c, double goldenWeight)
    {
        int total = a.Level + b.Level + c.Level;
        if (Math.Min(a.Level, Math.Min(b.Level, c.Level)) < 0) return total < 0 ? TrioTone.Strained : TrioTone.Mixed;
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

    private static double TrineOrb(TrioPoint a, TrioPoint b) => Off(a, b, AspectType.Trine);

    // How far two points are from an exact aspect.
    private static double Off(TrioPoint a, TrioPoint b, AspectType type)
    {
        double diff = Math.Abs(a.Longitude - b.Longitude) % 360;
        return Math.Abs((diff > 180 ? 360 - diff : diff) - type.Angle());
    }

    private static List<TrioFigure> Figures(NatalChart[] charts)
    {
        var points = charts.Select((chart, person) => SynastryService.Points(chart)
            .Select(p => new TrioPoint(person, p.point, p.lon)).ToList()).ToList();
        var found = new List<TrioFigure>();

        void Add(TrioFigureKind kind, TrioPoint a, TrioPoint b, TrioPoint c, int? focus, double widest, double orb)
        {
            TrioPoint[] corners = [a, b, c];
            if (corners.Count(x => !SynastryScoring.IsGenerational(x.Point)) < PersonalCorners) return;
            double closeness = 1 - 0.7 * widest / orb;
            double weight = corners.Aggregate(closeness, (w, x) => w * SynastryScoring.PointWeight(x.Point));
            if (weight >= FigureWeight) found.Add(new TrioFigure(kind, corners, focus, widest, weight));
        }

        // All three in one place.
        double meet = SynastryService.Orb(AspectType.Conjunction);
        foreach (var a in points[0])
            foreach (var b in points[1])
            {
                double ab = Off(a, b, AspectType.Conjunction);
                if (ab > meet) continue;
                foreach (var c in points[2])
                {
                    double widest = Math.Max(ab, Math.Max(Off(a, c, AspectType.Conjunction), Off(b, c, AspectType.Conjunction)));
                    if (widest <= meet) Add(TrioFigureKind.Meeting, a, b, c, null, widest, meet);
                }
            }

        // Each person in turn at the apex of a T-square and of a yod.
        double opposed = SynastryService.Orb(AspectType.Opposition), square = SynastryService.Orb(AspectType.Square),
               sextile = SynastryService.Orb(AspectType.Sextile);
        foreach (var (x, y, apex) in new[] { (0, 1, 2), (0, 2, 1), (1, 2, 0) })
            foreach (var a in points[x])
                foreach (var b in points[y])
                {
                    double opp = Off(a, b, AspectType.Opposition), sex = Off(a, b, AspectType.Sextile);
                    if (opp > opposed && sex > sextile) continue;
                    foreach (var c in points[apex])
                    {
                        if (opp <= opposed)
                        {
                            double widest = Math.Max(opp, Math.Max(Off(a, c, AspectType.Square), Off(b, c, AspectType.Square)));
                            if (Off(a, c, AspectType.Square) <= square && Off(b, c, AspectType.Square) <= square)
                                Add(TrioFigureKind.TSquare, a, b, c, apex, widest, opposed);
                        }
                        else
                        {
                            double widest = Math.Max(Off(a, c, AspectType.Quincunx), Off(b, c, AspectType.Quincunx));
                            if (widest <= YodOrb) Add(TrioFigureKind.Yod, a, b, c, apex, Math.Max(widest, sex), sextile);
                        }
                    }
                }

        return found.OrderByDescending(f => f.Weight).ToList();
    }

    // Every pair of `connections` tried as a trio with `chart`, best first, in the same
    // order as Rank. The connections are the person's own best matches, so this looks
    // only among people who already suit them, and each still has to suit the other.
    // They are cut from the head of SynastryScoring.Rank, which already puts the person's
    // contemporaries ahead within a level, so the preference is in the cast before anyone
    // is paired here, and again in the sort below.
    // `answersOf`: each person's answers, if any: the trios are then ranked as lived.
    public static List<TrioFind> FindTrios(ChartService charts, NatalChart chart, IReadOnlyList<Celebrity> connections,
        CancellationToken cancel = default, Func<NatalChart, MirrorReading?>? answersOf = null)
    {
        var cast = new List<NatalChart>();
        foreach (var person in connections)
        {
            cancel.ThrowIfCancellationRequested();
            try { cast.Add(charts.Calculate(person)); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Diagnostics.Log($"Trio finder skipped chart {person.Id}: {ex.Message}");
            }
        }

        var found = new List<TrioFind>();
        for (int i = 0; i < cast.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            for (int j = i + 1; j < cast.Count; j++)
            {
                var trio = Compare(chart, cast[i], cast[j], Answers(answersOf, chart, cast[i], cast[j]), figures: false);
                found.Add(new TrioFind(cast[i].Celebrity, cast[j].Celebrity, trio.RankedTone, trio.GoldenWeight, trio.Share, trio.GrandTrines.Count, trio.Contemporaries));
            }
        }

        return found
            .OrderBy(f => f.Tone)
            .ThenByDescending(f => f.Contemporaries)
            .ThenByDescending(f => f.GoldenWeight)
            .ThenByDescending(f => f.Share)
            .ThenBy(f => f.Name, StringComparer.Ordinal)
            .ToList();
    }

    // Only the reader's own charts have answers, so everyone else costs nothing here.
    private static MirrorReading?[]? Answers(Func<NatalChart, MirrorReading?>? answersOf, params NatalChart[] charts) =>
        answersOf is null ? null : charts.Select(c => c.Celebrity.Category == UserChartService.MyChartsCategory ? answersOf(c) : null).ToArray();

    // Everyone in `people` as the third of a trio with the two given, best first: Golden
    // ahead of the rest, within a tone the trios whose lifetimes all overlap, then by
    // grand-trine weight and the easy share. The two themselves are left out, and so is
    // anyone whose chart cannot be calculated.
    public static List<TrioMatch> Rank(ChartService charts, NatalChart first, NatalChart second,
        IEnumerable<Celebrity> people, CancellationToken cancel = default, Func<NatalChart, MirrorReading?>? answersOf = null)
    {
        var matches = new List<TrioMatch>();
        foreach (var person in people)
        {
            cancel.ThrowIfCancellationRequested();
            if (person.Id == first.Celebrity.Id || person.Id == second.Celebrity.Id) continue;
            try
            {
                var third = charts.Calculate(person);
                var trio = Compare(first, second, third, Answers(answersOf, first, second, third), figures: false);
                matches.Add(new TrioMatch(person, trio.RankedTone, trio.GoldenWeight, trio.Share, trio.GrandTrines.Count, trio.Contemporaries));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Diagnostics.Log($"Trio match for chart {person.Id} skipped: {ex.Message}");
            }
        }

        return matches
            .OrderBy(m => m.Tone)
            .ThenByDescending(m => m.Contemporaries)
            .ThenByDescending(m => m.GoldenWeight)
            .ThenByDescending(m => m.Share)
            .ThenBy(m => m.Person.Name, StringComparer.Ordinal)
            .ToList();
    }
}
