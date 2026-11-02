using Lore.Models;
using System.Globalization;

namespace Lore.Services;

// Forward adjacency lists in birth-date order. Triangles, rather than chains, are
// the trios: every edge must pass both of Ian's hard requirements.
public sealed class WriterEligibility
{
    public required IReadOnlyList<Celebrity> People { get; init; }
    public required IReadOnlyList<Dictionary<int, double>> Neighbors { get; init; }
    public int Skipped { get; init; }
    public long PairCount => Neighbors.Sum(n => (long)n.Count);

    public static double Distance(Celebrity a, Celebrity b)
    {
        const double rad = Math.PI / 180;
        double lat = (b.Latitude - a.Latitude) * rad, lon = (b.Longitude - a.Longitude) * rad;
        double h = Math.Pow(Math.Sin(lat / 2), 2) + Math.Cos(a.Latitude * rad) *
            Math.Cos(b.Latitude * rad) * Math.Pow(Math.Sin(lon / 2), 2);
        return 6371.0088 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(h, 0, 1)));
    }

    public static bool WithinAge(DateOnly a, DateOnly b) =>
        (a <= b ? b <= a.AddYears(10) : a <= b.AddYears(10));

    public static WriterEligibility Build(IEnumerable<Celebrity> library, WriterGroupQuery query,
        CancellationToken cancel = default)
    {
        if (query.Size is not (2 or 3) || !double.IsFinite(query.DistanceKm) ||
            query.DistanceKm is < 1 or > 1000 || query.FromYear < 1200 || query.ToYear > 2400 ||
            query.FromYear > query.ToYear || query.Keep is < 1 or > 100_000 || query.MinimumSitelinks is < 0 or > 1000 ||
            query.AsOf is { Year: < 51 } ||
            query.PairLevel is < -2 or > 2 || query.TrioTone is { } tone && !Enum.IsDefined(tone))
            throw new ArgumentException("Choose two or three people, 1–1,000 km, and birth years within 1200–2400.");

        var terms = query.Search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var valid = new List<(Celebrity Person, DateOnly Date)>();
        int skipped = 0;
        foreach (var person in library.DistinctBy(p => p.Id))
        {
            cancel.ThrowIfCancellationRequested();
            if (person.Category == UserChartService.MyChartsCategory || person.Category == "Fictional") continue;
            if (string.IsNullOrWhiteSpace(person.Category)) { skipped++; continue; }
            if (!DiedBy(person, query.DeathCutoff)) continue;
            if (query.Category.Length > 0 && person.Category != query.Category) continue;
            if (person.Sitelinks.GetValueOrDefault() < query.MinimumSitelinks) continue;
            string haystack = $"{person.Name} {person.BirthPlace} {person.Bio} {person.Category}";
            if (!terms.All(t => haystack.Contains(t, StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                var date = BirthTimeResolver.GregorianDate(person);
                if (date > DateOnly.ParseExact(person.DeathDate!, "yyyy-MM-dd", CultureInfo.InvariantCulture)) { skipped++; continue; }
                if (date.Year < query.FromYear || date.Year > query.ToYear) continue;
                // Unknown coordinates must be absent/invalid in the data, never inferred
                // from a place name. Zero longitude and latitude are legitimate values.
                if (!person.HasBirthCoordinates || !double.IsFinite(person.Latitude) || !double.IsFinite(person.Longitude) ||
                    Math.Abs(person.Latitude) > 90 || Math.Abs(person.Longitude) > 180 ||
                    string.IsNullOrWhiteSpace(person.BirthPlace)) { skipped++; continue; }
                valid.Add((person, date));
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException) { skipped++; }
        }
        valid = valid.OrderBy(p => p.Date).ThenBy(p => p.Person.Id, StringComparer.Ordinal).ToList();
        var neighbors = valid.Select(_ => new Dictionary<int, double>()).ToArray();
        for (int a = 0; a < valid.Count; a++)
        {
            cancel.ThrowIfCancellationRequested();
            DateOnly end = valid[a].Date.AddYears(10);
            for (int b = a + 1; b < valid.Count && valid[b].Date <= end; b++)
            {
                if (valid[a].Person.Category != valid[b].Person.Category) continue;
                // Cheap latitude bound before the great-circle calculation.
                if (Math.Abs(valid[a].Person.Latitude - valid[b].Person.Latitude) * 111.195 > query.DistanceKm) continue;
                double distance = Distance(valid[a].Person, valid[b].Person);
                if (distance <= query.DistanceKm) neighbors[a].Add(b, distance);
            }
        }
        return new WriterEligibility { People = valid.Select(v => v.Person).ToArray(), Neighbors = neighbors, Skipped = skipped };
    }

    public static bool DiedBy(Celebrity person, DateOnly cutoff) => DateOnly.TryParseExact(person.DeathDate,
        "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var death) && death <= cutoff;
}
