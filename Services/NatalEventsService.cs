using Lore.Models;

namespace Lore.Services;

// Finds what happened in the sky around a birth (see NatalEvents): some hundreds of
// ephemeris calculations, so it is run once for a chart that is to be shown, off the UI
// thread, and not for the charts that are only scored.
public static class NatalEventsService
{
    // The planets that stand still and turn.
    private static readonly Planet[] Stationing =
    [
        Planet.Mercury, Planet.Venus, Planet.Mars, Planet.Jupiter,
        Planet.Saturn, Planet.Uranus, Planet.Neptune, Planet.Pluto,
    ];

    // The longest any of them goes without a station is Mars, some 700 days.
    private const double StationSearchDays = 800;

    public static NatalEvents Compute(ChartService charts, NatalChart chart)
    {
        charts = charts.With(chart.Settings);
        double jd = SwissEphemeris.DateTimeToJulianDay(chart.CalculatedForUtc);
        return new NatalEvents
        {
            NewMoonBefore = LunationBefore(charts, jd, full: false),
            FullMoonBefore = LunationBefore(charts, jd, full: true),
            Stations = Stationing.Select(p => new NearStations(p, Station(charts, p, jd, -1), Station(charts, p, jd, +1))).ToList(),
            Hour = chart.Timed && chart.GeoLatitude is { } latitude && chart.GeoLongitude is { } longitude
                ? HourOf(charts, jd, latitude, longitude) : null,
        };
    }

    // The last New (or Full) Moon at or before an instant. The Moon gains on the Sun by
    // 11° to 14° a day, so stepping back by the lead it has built up since, at its
    // average rate, lands within a few days, and repeating that closes in on the moment.
    private static Lunation? LunationBefore(ChartService charts, double jd, bool full)
    {
        const double GainPerDay = 12.1908;
        double target = full ? 180 : 0;
        double? Lead(double t) =>
            charts.CalculateBody(t, Planet.Sun) is { } sun && charts.CalculateBody(t, Planet.Moon) is { } moon
                ? Normalize(moon.Longitude - sun.Longitude - target) : null;

        if (Lead(jd) is not { } since) return null;
        double at = jd - since / GainPerDay;
        for (int i = 0; i < 12; i++)
        {
            if (Lead(at) is not { } lead) return null;
            at -= (Normalize(lead + 180) - 180) / GainPerDay;
        }
        if (charts.CalculateBody(at, Planet.Moon) is not { } then) return null;

        // An eclipse is greatest within an hour or so of the exact New or Full Moon.
        string? eclipse = charts.NextEclipse(at - 1, lunar: full) is { } e && Math.Abs(e.Jd - at) < 0.5 ? e.Kind : null;
        return new Lunation(full, SwissEphemeris.JulianDayToDateTime(at), then.Longitude, Math.Max(0, jd - at), eclipse);
    }

    // The nearest station before (direction −1) or after (+1) an instant: the first
    // change of direction met stepping away from it, then narrowed to the moment.
    // Mercury is retrograde for three weeks at a time and the others for six or more,
    // so steps of five and ten days cannot step over a whole retrograde.
    private static StationPoint? Station(ChartService charts, Planet planet, double jd, int direction)
    {
        double Speed(double t) => charts.CalculateBody(t, planet)?.SpeedLongitude ?? double.NaN;
        double step = planet == Planet.Mercury ? 5 : 10;

        double from = jd, before = Speed(jd);
        for (double away = step; away <= StationSearchDays && !double.IsNaN(before); away += step)
        {
            double to = jd + direction * away, now = Speed(to);
            if (double.IsNaN(now)) return null;
            if ((before < 0) != (now < 0))
            {
                double lo = Math.Min(from, to), hi = Math.Max(from, to);
                bool retrogradeAfter = Speed(hi) < 0;
                for (int i = 0; i < 40; i++)
                {
                    double mid = (lo + hi) / 2;
                    if ((Speed(mid) < 0) == retrogradeAfter) hi = mid; else lo = mid;
                }
                double at = (lo + hi) / 2;
                return new StationPoint(SwissEphemeris.JulianDayToDateTime(at), retrogradeAfter, Math.Abs(at - jd));
            }
            from = to;
            before = now;
        }
        return null;
    }

    // The planetary day and hour at an instant and place. Null where the Sun did not
    // rise before it within a day and set and rise again within a day each: inside the
    // polar circles in summer and winter.
    private static BirthHour? HourOf(ChartService charts, double jd, double latitude, double longitude)
    {
        // The last sunrise at or before the instant.
        double? rise = null;
        double after = jd - 1.5;
        for (int i = 0; i < 4 && charts.NextSunriseOrSet(after, latitude, longitude, sunset: false) is { } r && r <= jd; i++)
        {
            rise = r;
            after = r + 0.01;
        }
        if (rise is not { } sunrise || jd - sunrise >= 1.1 ||
            charts.NextSunriseOrSet(sunrise, latitude, longitude, sunset: true) is not { } sunset || sunset - sunrise >= 1 ||
            charts.NextSunriseOrSet(sunset, latitude, longitude, sunset: false) is not { } next || next - sunset >= 1 || jd >= next)
            return null;

        // The weekday the sunrise fell on by the place's own (mean solar) clock.
        int weekday = (int)(Math.Floor(sunrise + longitude / 360 + 1.5) % 7);
        var dayRuler = TransitService.DayRulers[weekday];
        bool byDay = jd < sunset;
        double through = byDay ? (jd - sunrise) / (sunset - sunrise) : (jd - sunset) / (next - sunset);
        int hour = Math.Min(11, (int)(through * 12)) + (byDay ? 0 : 12);
        var hourRuler = TransitService.Chaldean[(Array.IndexOf(TransitService.Chaldean, dayRuler) + hour) % 7];
        return new BirthHour(dayRuler, hourRuler, hour + 1, byDay,
            SwissEphemeris.JulianDayToDateTime(sunrise), SwissEphemeris.JulianDayToDateTime(sunset));
    }

    private static double Normalize(double degrees) => ((degrees % 360) + 360) % 360;
}
