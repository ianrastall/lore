using Lore.Models;

namespace Lore.Services;

// Weighs the contacts between two charts: which ones a reading should spend its few slots
// on, and how the easy and hard ones balance overall. Western astrology has no agreed
// compatibility score, so this is Lore's own, and deliberately simple: every personal
// contact counts for or against by how close it is and how personal its two points are.
public static class SynastryScoring
{
    // ── One contact ───────────────────────────────────────────────────────────

    // An editorial priority: closer contacts, more personal points and harder-edged
    // aspects rank higher.
    public static double Priority(SynastryAspect x)
    {
        double closeness = 1 - 0.7 * Math.Min(1, x.Orb / SynastryService.Orb(x.Type));
        return PointWeight(x.First) * PointWeight(x.Second) * AspectWeight(x.Type) * closeness;
    }

    private static double PointWeight(NatalPoint p) => p.Kind switch
    {
        NatalPointKind.Ascendant => 0.9,
        NatalPointKind.Midheaven => 0.5,
        _ => p.Body switch
        {
            Planet.Sun or Planet.Moon => 1.0,
            Planet.Venus or Planet.Mars => 0.9,
            Planet.Mercury => 0.75,
            Planet.Saturn => 0.7,
            Planet.Jupiter => 0.65,
            Planet.Pluto => 0.5,
            Planet.Uranus or Planet.Neptune or Planet.NorthNode => 0.45,
            Planet.Chiron => 0.4,
            _ => 0.3
        }
    };

    private static double AspectWeight(AspectType a) => a switch
    {
        AspectType.Conjunction => 1.0,
        AspectType.Opposition or AspectType.Square => 0.9,
        AspectType.Trine => 0.85,
        _ => 0.6
    };

    // Two slow bodies in aspect (her Neptune, his Pluto) link everyone born in the same
    // years, so they say nothing about these two people in particular.
    public static bool IsGenerational(SynastryAspect x) => IsGenerational(x.First) && IsGenerational(x.Second);

    private static bool IsGenerational(NatalPoint p) =>
        !p.IsAngle && p.Body is Planet.Uranus or Planet.Neptune or Planet.Pluto
                                or Planet.NorthNode or Planet.Chiron or Planet.Lilith;

    // ── Two charts ────────────────────────────────────────────────────────────

    // Where the five bands fall, as the share of the weight that is on the easy side.
    // They are set against every pair in the bundled library (22,366 of them), not at
    // fixed distances from an even split: sextiles and trines between them cover more of
    // the circle than squares and oppositions, so the typical pair is a little over half
    // easy (the median share is 0.54). Measured from there, about 7% of pairs read as
    // Soulmates, 18% Harmonious, 50% Mixed, 18% Challenging and 7% Adversaries.
    private const double SoulmatesShare = 0.665;
    private const double HarmoniousShare = 0.595;
    private const double ChallengingShare = 0.48;
    private const double AdversariesShare = 0.41;

    // The two outer bands also need this much weight behind them in all, so that two
    // charts joined by a couple of stray contacts (as can happen when neither has a birth
    // time) are not called Soulmates on the strength of one trine. A typical pair of
    // timed charts carries about 14.
    private const double WeightForExtremes = 6.0;

    // The balance of easy and hard across every personal contact, each counted by how
    // much it matters. (Not just the ones a reading shows: those are chosen in fixed
    // numbers of each kind, which would make every pair of charts come out Mixed.)
    public static Compatibility Assess(IEnumerable<SynastryAspect> aspects)
    {
        double flow = 0, tension = 0;
        foreach (var x in aspects)
        {
            if (IsGenerational(x)) continue;

            // The ranking favours hard aspects (they are the more noticeable); take that
            // back out here, or the same bias would tip every pair toward Challenging.
            double weight = Priority(x) / AspectWeight(x.Type);
            switch (x.Tone)
            {
                case TransitTone.Flow: flow += weight; break;
                case TransitTone.Tension: tension += weight; break;
                default:
                    // A conjunction takes its colour from the planets meeting; between
                    // neutral points it binds more than it grates.
                    if (Involves(x, Planet.Venus, Planet.Jupiter)) flow += weight;
                    else if (Involves(x, Planet.Mars, Planet.Saturn, Planet.Pluto)) tension += weight;
                    else flow += weight / 2;
                    break;
            }
        }

        double total = flow + tension;
        if (total <= 0) return new Compatibility(SynastryTone.Light, 0, 0);

        double share = flow / total;
        bool weighty = total >= WeightForExtremes;
        var tone = share >= HarmoniousShare
                 ? (share >= SoulmatesShare && weighty ? SynastryTone.Soulmates : SynastryTone.Harmonious)
                 : share <= ChallengingShare
                 ? (share <= AdversariesShare && weighty ? SynastryTone.Adversaries : SynastryTone.Challenging)
                 : SynastryTone.Mixed;
        return new Compatibility(tone, flow, tension);
    }

    private static bool Involves(SynastryAspect x, params Planet[] bodies) =>
        (!x.First.IsAngle && bodies.Contains(x.First.Body)) || (!x.Second.IsAngle && bodies.Contains(x.Second.Body));

    // ── One chart against many ────────────────────────────────────────────────

    // Everyone in `people` compared with one chart, best match first: by level, and
    // within a level by the share of the weight that is on the easy side. The chart's own
    // person is left out, and so is anyone whose chart cannot be calculated.
    public static List<SynastryMatch> Rank(ChartService charts, NatalChart chart, IEnumerable<Celebrity> people,
        CancellationToken cancel = default)
    {
        var matches = new List<SynastryMatch>();
        foreach (var person in people)
        {
            cancel.ThrowIfCancellationRequested();
            if (person.Id == chart.Celebrity.Id) continue;
            try
            {
                var other = charts.Calculate(person);
                matches.Add(new SynastryMatch(person, Assess(SynastryService.Compare(chart, other).Aspects)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Diagnostics.Log($"Synastry match for {person.Name} skipped: {ex.Message}");
            }
        }

        return matches
            .OrderByDescending(m => m.Compatibility.Level)
            .ThenByDescending(m => m.Compatibility.Share)
            .ThenBy(m => m.Person.Name, StringComparer.Ordinal)
            .ToList();
    }
}
