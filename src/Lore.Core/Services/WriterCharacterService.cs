using System.Globalization;
using Lore.Models;

namespace Lore.Services;

public sealed class WriterCharacterService(ChartService charts)
{
    public IReadOnlyList<WriterCandidate> Find(WriterCharacterQuery query, CancellationToken cancel = default,
        IProgress<string>? progress = null)
    {
        if (query.Days is < 1 or > 366 || query.Start.Year < 1200 || query.Start.AddDays(query.Days).Year > 2400 ||
            !Enum.IsDefined(query.Anchor) || query.Planet > Planet.Saturn || query.Planet < Planet.Sun ||
            string.IsNullOrWhiteSpace(query.Place.Name) || !double.IsFinite(query.Place.Latitude) ||
            !double.IsFinite(query.Place.Longitude) || Math.Abs(query.Place.Latitude) > 90 || Math.Abs(query.Place.Longitude) > 180)
            throw new ArgumentException("Choose a city and 1–366 days within 1200–2400; angular anchors use the Sun through Saturn.");
        var pinned = charts.With(charts.Settings);
        var template = Person(query.Place, query.Start.ToDateTime(TimeOnly.MinValue));
        DateTime from = BirthTimeResolver.ToUtc(template);
        DateTime to = BirthTimeResolver.ToUtc(Person(query.Place, query.Start.AddDays(query.Days).ToDateTime(TimeOnly.MinValue)));
        var found = new List<WriterCandidate>();
        WriterCandidate At(DateTime utc, string anchor, string evidence)
        {
            var local = BirthTimeResolver.LocalTimeAt(template, utc);
            var person = Person(query.Place, local);
            return new WriterCandidate(new WriterBirth(person, DateTime.SpecifyKind(utc, DateTimeKind.Utc)), anchor, evidence);
        }

        if (query.Anchor is WriterAnchor.SkyEvents or WriterAnchor.SkyAndAngle)
        {
            progress?.Report("Finding New and Full Moons, eclipses, stations and sign changes…");
            foreach (var sky in new TransitService(pinned).SkyCalendar(from, to, cancel))
            {
                cancel.ThrowIfCancellationRequested();
                if (sky.Utc < from || sky.Utc >= to) continue;
                string label = sky.Kind switch
                {
                    SkyEventKind.NewMoon => "New Moon", SkyEventKind.FullMoon => "Full Moon",
                    SkyEventKind.SolarEclipse => $"{sky.EclipseType} solar eclipse lunation",
                    SkyEventKind.LunarEclipse => $"{sky.EclipseType} lunar eclipse lunation",
                    SkyEventKind.Station => $"{sky.Body.Name()} turns {(sky.Retrograde ? "retrograde" : "direct")}",
                    _ => $"{sky.Body.Name()} enters {sky.Sign.Name()}",
                };
                string evidence = $"{sky.Utc:yyyy-MM-dd HH:mm:ss} UTC · {sky.Body.Name()} at " +
                    $"{ZodiacSignExtensions.FormatDegreeInSign(sky.Longitude)} {sky.Sign.Name()}.";
                if (sky.IsEclipse) evidence += " The moment shown is the exact lunation, not greatest eclipse; local visibility is not assumed.";
                if (query.Anchor == WriterAnchor.SkyAndAngle)
                {
                    double jd = SwissEphemeris.DateTimeToJulianDay(sky.Utc);
                    var angles = pinned.AnglesAt(jd, query.Place.Latitude, query.Place.Longitude);
                    var planet = pinned.CalculateBody(jd, query.Planet);
                    if (planet is null) continue;
                    double asc = Separation(planet.Longitude, angles.Ascendant), mc = Separation(planet.Longitude, angles.Midheaven);
                    if (Math.Min(asc, mc) > 3) continue;
                    label += $" · {query.Planet.Name()} near {(asc <= mc ? "Ascendant" : "Midheaven")}";
                    evidence += $" {query.Planet.Name()} is {Math.Min(asc, mc):0.000}° from that angle at this place (limit 3°).";
                }
                found.Add(At(sky.Utc, label, evidence) with
                {
                    Key = sky.Kind == SkyEventKind.Station ? (sky.Retrograde ? "RetrogradeStation" : "DirectStation") : sky.Kind.ToString(),
                    Body = sky.Body,
                });
            }
        }
        else if (query.Anchor is WriterAnchor.Ascendant or WriterAnchor.Midheaven)
        {
            double jd0 = SwissEphemeris.DateTimeToJulianDay(from), jd1 = SwissEphemeris.DateTimeToJulianDay(to);
            bool ascendant = query.Anchor == WriterAnchor.Ascendant;
            double Difference(double jd)
            {
                cancel.ThrowIfCancellationRequested();
                if (pinned.CalculateBody(jd, query.Planet) is not { } body) return double.NaN;
                var angles = pinned.AnglesAt(jd, query.Place.Latitude, query.Place.Longitude);
                return Signed(body.Longitude - (ascendant ? angles.Ascendant : angles.Midheaven));
            }
            // Five-minute brackets avoid missing the fast Ascendant near high latitudes.
            // At a discontinuity the final residual check rejects a false root.
            double a = jd0, fa = Difference(a), last = double.NegativeInfinity;
            for (double b = Math.Min(jd0 + 1.0 / 288, jd1); a < jd1; b = Math.Min(b + 1.0 / 288, jd1))
            {
                double fb = Difference(b);
                if (double.IsFinite(fa) && double.IsFinite(fb) && fa * fb <= 0 && Math.Abs(fa - fb) < 180)
                {
                    double lo = a, hi = b, flo = fa;
                    for (int i = 0; i < 28; i++)
                    {
                        double mid = (lo + hi) / 2, fm = Difference(mid);
                        if ((fm >= 0) == (flo >= 0)) { lo = mid; flo = fm; } else hi = mid;
                    }
                    double at = (lo + hi) / 2;
                    if (at < jd1 && at - last > 1.0 / 1440 && Math.Abs(Difference(at)) < 0.001)
                    {
                        last = at;
                        found.Add(At(SwissEphemeris.JulianDayToDateTime(at),
                            $"{query.Planet.Name()} on the {(ascendant ? "Ascendant" : "Midheaven")}",
                            "Ecliptic longitudes coincide to within 0.001°. This is an astrological angle contact; physical horizon visibility is not inferred.") with
                            { Key = ascendant ? "Ascendant" : "Midheaven", Body = query.Planet });
                    }
                }
                a = b; fa = fb;
                if (((int)((a - jd0) * 288) & 511) == 0) progress?.Report($"Searching local angles: {Math.Min(query.Days, a - jd0):0} of {query.Days} days…");
            }
        }
        else
        {
            // A pattern is an interval, not an exact event. Show one representative
            // per pattern per local day, explicitly labelled as an hourly sample.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (DateTime utc = from; utc < to; utc = utc.AddHours(1))
            {
                cancel.ThrowIfCancellationRequested();
                var candidate = At(utc, "", "");
                var chart = pinned.CalculateAt(candidate.Birth.Person, utc);
                foreach (var pattern in AspectPatternService.Detect(chart))
                {
                    if (pattern.Points.Count(p => !SynastryScoring.IsGenerational(p)) < 2) continue;
                    string signature = $"{candidate.Birth.Person.BirthDate}|{pattern.Type}|" + string.Join(',', pattern.Points.OrderBy(p => p.Name).Select(p => p.Name));
                    if (!seen.Add(signature)) continue;
                    string points = string.Join(", ", pattern.Points.Select(p => p.Name));
                    found.Add(candidate with { Anchor = PatternName(pattern.Type), Key = pattern.Type.ToString(),
                        Evidence = $"{points}. Detected at an hourly sample, not an exact formation time; one example per pattern per local day. {chart.HouseSystemLabel}." });
                }
                if ((utc - from).TotalHours % 24 == 0) progress?.Report($"Sampling chart patterns: {(utc - from).TotalDays:0} of {query.Days} days…");
            }
        }
        cancel.ThrowIfCancellationRequested();
        return found.OrderBy(c => c.Birth.Utc).ThenBy(c => c.Anchor, StringComparer.Ordinal).ToArray();
    }

    public static string PatternName(PatternType kind) => kind switch
    {
        PatternType.GrandTrine => "Grand trine", PatternType.TSquare => "T-square",
        PatternType.GrandCross => "Grand cross", PatternType.MysticRectangle => "Mystic rectangle",
        _ => kind.ToString(),
    };

    private static Celebrity Person(City place, DateTime local) => new()
    {
        Id = $"fiction:{place.Latitude.ToString("R", CultureInfo.InvariantCulture)}:{place.Longitude.ToString("R", CultureInfo.InvariantCulture)}:{local:yyyyMMddHHmmss}",
        Name = "Fictional character", Category = "Fictional", BirthDate = local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        BirthTime = local.ToString("HH:mm", CultureInfo.InvariantCulture), BirthTimeKnown = true,
        BirthPlace = place.Display, Latitude = place.Latitude, Longitude = place.Longitude,
        TimeZoneId = place.TimeZoneId, UtcOffsetHours = place.UtcOffsetHours,
        Source = "Fictional birth generated by Lore; the exact UTC instant is retained in the Writers brief.",
    };
    private static double Signed(double x) => ((x % 360) + 540) % 360 - 180;
    private static double Separation(double a, double b) => Math.Abs(Signed(a - b));
}
