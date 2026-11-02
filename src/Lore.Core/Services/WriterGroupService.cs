using System.Numerics;
using Lore.Models;

namespace Lore.Services;

// Global discovery using exactly the existing Synastry and Trine measures/order.
// Age, birthplace, field and historical eligibility are enforced before ranking.
public sealed class WriterGroupService(ChartService charts)
{
    private sealed record Points((NatalPoint point, double lon)[] Values)
    {
        public double[] Weights { get; } = Values.Select(v => SynastryScoring.PointWeight(v.point)).ToArray();
        public bool[] Personal { get; } = Values.Select(v => !SynastryScoring.IsGenerational(v.point)).ToArray();
    }
    private sealed record Edge(Compatibility Compatibility, bool Contemporaries, ushort[] Trines);
    private readonly record struct Hit(int A, int B, int C, double Distance, double Trine, double Share, string Tone);
    private readonly record struct Rank(int Band, bool Contemporaries, double Trine, double Share, string Key) : IComparable<Rank>
    {
        public int CompareCore(Rank other)
        {
            int c = Band.CompareTo(other.Band); if (c != 0) return c;
            c = Contemporaries.CompareTo(other.Contemporaries); if (c != 0) return c;
            c = Trine.CompareTo(other.Trine); return c == 0 ? Share.CompareTo(other.Share) : c;
        }
        public int CompareTo(Rank other)
        {
            int c = CompareCore(other);
            return c == 0 ? StringComparer.Ordinal.Compare(other.Key, Key) : c;
        }
    }

    public WriterGroupSearch Search(IEnumerable<Celebrity> library, WriterGroupQuery query,
        CancellationToken cancel = default, IProgress<string>? progress = null)
    {
        query = query with { AsOf = query.AsOf ?? DateOnly.FromDateTime(DateTime.Today) };
        var pinned = charts.With(charts.Settings);
        progress?.Report("Finding historical figures in the same field, within ten years and the birthplace distance limit…");
        var graph = WriterEligibility.Build(library, query, cancel);
        var points = new Points?[graph.People.Count];
        var used = new HashSet<int>();
        for (int a = 0; a < graph.People.Count; a++) foreach (int b in graph.Neighbors[a].Keys) { used.Add(a); used.Add(b); }
        int skipped = graph.Skipped;
        foreach (int i in used.Order())
        {
            cancel.ThrowIfCancellationRequested();
            try { points[i] = new Points(SynastryService.Points(pinned.Calculate(graph.People[i])).ToArray()); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { skipped++; Diagnostics.Log($"Writers skipped chart {graph.People[i].Id}: {ex.Message}"); }
        }
        var edges = graph.People.Select(_ => new Dictionary<int, Edge>()).ToArray();
        var best = new PriorityQueue<Hit, Rank>();
        long pairs = 0, trios = 0, matches = 0;
        void Keep(Hit hit, Rank rank)
        {
            matches++;
            if (best.Count == query.Keep && rank.CompareCore(best.PeekPriority()) < 0) return;
            string name = graph.People[hit.A].Name + " / " + graph.People[hit.B].Name +
                (hit.C >= 0 ? " / " + graph.People[hit.C].Name : "") + "|" + graph.People[hit.A].Id + "/" + graph.People[hit.B].Id +
                (hit.C >= 0 ? "/" + graph.People[hit.C].Id : "");
            rank = rank with { Key = name };
            if (best.Count < query.Keep) best.Enqueue(hit, rank);
            else if (rank.CompareTo(best.PeekPriority()) > 0) best.DequeueEnqueue(hit, rank);
        }
        for (int a = 0; a < graph.People.Count; a++)
        {
            cancel.ThrowIfCancellationRequested();
            if (points[a] is not { } pa) continue;
            foreach (var (b, distance) in graph.Neighbors[a])
            {
                if (points[b] is not { } pb) continue;
                var edge = Assess(pa, pb, Lifetimes.Overlap(graph.People[a], graph.People[b]));
                edges[a].Add(b, edge); pairs++;
                if (query.Size == 2 && (query.PairLevel is null || edge.Compatibility.Level == query.PairLevel))
                    Keep(new Hit(a, b, -1, distance, 0, edge.Compatibility.Share, edge.Compatibility.Tone.Label()),
                        new Rank(edge.Compatibility.Level, edge.Contemporaries, 0, edge.Compatibility.Share, ""));
                if ((pairs & 1023) == 0)
                { cancel.ThrowIfCancellationRequested(); progress?.Report($"Compared {pairs:N0} of {graph.PairCount:N0} eligible pairs…"); }
            }
        }
        if (query.Size == 3)
            for (int a = 0; a < graph.People.Count; a++)
            {
                cancel.ThrowIfCancellationRequested();
                foreach (var (b, ab) in edges[a]) foreach (var (c, bc) in edges[b])
                {
                    if (!edges[a].TryGetValue(c, out var ac)) continue;
                    trios++;
                    if ((trios & 16383) == 0)
                    { cancel.ThrowIfCancellationRequested(); progress?.Report($"Assessed {trios:N0} eligible trios; retaining the best {query.Keep:N0}…"); }
                    double trine = StrongestTrine(points[a]!, points[b]!, points[c]!, ab, ac, bc);
                    var tone = TrioService.Assess(ab.Compatibility, ac.Compatibility, bc.Compatibility, trine);
                    if (query.TrioTone is { } requested && tone != requested) continue;
                    double share = (ab.Compatibility.Share + ac.Compatibility.Share + bc.Compatibility.Share) / 3;
                    Keep(new Hit(a, b, c, Math.Max(graph.Neighbors[a][b], Math.Max(graph.Neighbors[a][c], graph.Neighbors[b][c])),
                        trine, share, tone.Label()), new Rank(-(int)tone, ab.Contemporaries && ac.Contemporaries && bc.Contemporaries, trine, share, ""));
                }
            }
        cancel.ThrowIfCancellationRequested();
        var results = best.UnorderedItems.OrderByDescending(x => x.Priority).Select(x =>
        {
            var h = x.Element;
            Celebrity[] members = h.C < 0 ? [graph.People[h.A], graph.People[h.B]] : [graph.People[h.A], graph.People[h.B], graph.People[h.C]];
            return new WriterGroup(members, h.Share, h.Distance, h.Trine, h.Tone, query.DeathCutoff);
        }).ToArray();
        return new WriterGroupSearch(results, graph.People.Count, pairs, trios, matches, skipped);
    }

    public static Compatibility AssessPair(NatalChart a, NatalChart b) => Assess(
        new Points(SynastryService.Points(a).ToArray()), new Points(SynastryService.Points(b).ToArray()), false).Compatibility;

    private static Edge Assess(Points a, Points b, bool contemporaries)
    {
        double flow = 0, tension = 0;
        var trines = new ushort[a.Values.Length];
        for (int i = 0; i < a.Values.Length; i++) for (int j = 0; j < b.Values.Length; j++)
        {
            var (pa, la) = a.Values[i]; var (pb, lb) = b.Values[j];
            double angle = Separation(la, lb); AspectType type; double orb, allowed;
            if (angle <= 6) { type = AspectType.Conjunction; orb = angle; allowed = 6; }
            else if (Math.Abs(angle - 60) <= 4) { type = AspectType.Sextile; orb = Math.Abs(angle - 60); allowed = 4; }
            else if (Math.Abs(angle - 90) <= 6) { type = AspectType.Square; orb = Math.Abs(angle - 90); allowed = 6; }
            else if (Math.Abs(angle - 120) <= 6) { type = AspectType.Trine; orb = Math.Abs(angle - 120); allowed = 6; trines[i] |= (ushort)(1 << j); }
            else if (Math.Abs(angle - 180) <= 6) { type = AspectType.Opposition; orb = Math.Abs(angle - 180); allowed = 6; }
            else continue;
            if (!a.Personal[i] && !b.Personal[j]) continue;
            double weight = a.Weights[i] * b.Weights[j] * (1 - 0.7 * orb / allowed);
            bool Has(Planet p) => (!pa.IsAngle && pa.Body == p) || (!pb.IsAngle && pb.Body == p);
            bool easy = type is AspectType.Trine or AspectType.Sextile || type == AspectType.Conjunction && (Has(Planet.Venus) || Has(Planet.Jupiter));
            bool hard = type is AspectType.Square or AspectType.Opposition || type == AspectType.Conjunction && !easy &&
                (Has(Planet.Mars) || Has(Planet.Saturn) || Has(Planet.Pluto));
            if (easy) flow += weight; else if (hard) tension += weight; else flow += weight / 2;
        }
        return new Edge(SynastryScoring.AssessWeights(flow, tension), contemporaries, trines);
    }

    private static double StrongestTrine(Points a, Points b, Points c, Edge ab, Edge ac, Edge bc)
    {
        double best = 0;
        for (int i = 0; i < a.Values.Length; i++)
        {
            uint bm = ab.Trines[i];
            while (bm != 0)
            {
                int j = BitOperations.TrailingZeroCount(bm); bm &= bm - 1;
                uint cm = (uint)(ac.Trines[i] & bc.Trines[j]);
                while (cm != 0)
                {
                    int k = BitOperations.TrailingZeroCount(cm); cm &= cm - 1;
                    if ((a.Personal[i] ? 1 : 0) + (b.Personal[j] ? 1 : 0) + (c.Personal[k] ? 1 : 0) < 2) continue;
                    double widest = Math.Max(Math.Abs(Separation(a.Values[i].lon, b.Values[j].lon) - 120),
                        Math.Max(Math.Abs(Separation(a.Values[i].lon, c.Values[k].lon) - 120), Math.Abs(Separation(b.Values[j].lon, c.Values[k].lon) - 120)));
                    best = Math.Max(best, (1 - 0.7 * widest / 6) * a.Weights[i] * b.Weights[j] * c.Weights[k]);
                }
            }
        }
        return best;
    }
    private static double Separation(double a, double b) { double diff = Math.Abs(a - b) % 360; return diff > 180 ? 360 - diff : diff; }
}

internal static class WriterQueueExtensions
{
    public static TPriority PeekPriority<TElement, TPriority>(this PriorityQueue<TElement, TPriority> queue)
    { queue.TryPeek(out _, out var priority); return priority!; }
}
