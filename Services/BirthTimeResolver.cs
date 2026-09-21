using Lore.Models;
using NodaTime;

namespace Lore.Services;

// Converts a chart's local birth date/time into a UTC instant using the historical
// IANA time-zone database (NodaTime). This is what makes DST — and one-off regimes
// like Britain's 1968–1971 year-round-BST experiment — come out right: the offset is
// whatever was actually in force at the birthplace on the birth date, not a single
// fixed number.
//
// Zone resolution order:
//   1. an explicit IANA id stored on the chart (set from the city database), else
//   2. a geographic lookup from the chart's latitude/longitude (GeoTimeZone), which
//      backfills legacy charts saved before zone ids existed, else
//   3. the stored numeric UtcOffsetHours as a last-resort fixed fallback (e.g. mid-
//      ocean coordinates that map to no zone).
public static class BirthTimeResolver
{
    private static readonly IDateTimeZoneProvider Tzdb = DateTimeZoneProviders.Tzdb;

    public static DateTime ToUtc(Celebrity c)
    {
        var d = c.GetBirthDate();
        var t = c.GetBirthTime();
        var local = new LocalDateTime(d.Year, d.Month, d.Day, t.Hour, t.Minute);

        var zone = ResolveZone(c.TimeZoneId, c.Latitude, c.Longitude);
        if (zone is not null)
            // Lenient: a birth time that never existed (spring-forward gap) shifts
            // forward; an ambiguous one (fall-back overlap) takes the earlier instant.
            return zone.AtLeniently(local).ToDateTimeUtc();

        // No zone available — fall back to the stored fixed offset.
        var unspecified = new DateTime(d.Year, d.Month, d.Day, t.Hour, t.Minute, 0, DateTimeKind.Unspecified);
        return unspecified.AddHours(-c.UtcOffsetHours);
    }

    // Resolve the effective IANA zone id: explicit first, then geographic. Returns null
    // when neither yields a zone (caller then uses a fixed offset).
    public static string? ResolveZoneId(string? explicitId, double lat, double lon)
    {
        if (!string.IsNullOrWhiteSpace(explicitId) && Tzdb.GetZoneOrNull(explicitId) is not null)
            return explicitId;

        string geo = GeoTimeZone.TimeZoneLookup.GetTimeZone(lat, lon).Result;
        return string.IsNullOrEmpty(geo) ? null : geo;
    }

    // For the Add-Chart UI: describe the offset that will actually be applied for a
    // given place and date, e.g. (1.0, "UTC+1 (BST)", "Europe/London").
    public static (double offsetHours, string label, string? zoneId) Describe(
        string? explicitId, double lat, double lon,
        int year, int month, int day, int hour, int minute)
    {
        string? zoneId = ResolveZoneId(explicitId, lat, lon);
        var zone = zoneId is null ? null : Tzdb.GetZoneOrNull(zoneId);
        if (zone is null)
            return (0, "No time zone found — using the manual offset below.", null);

        var local = new LocalDateTime(year, month, day, hour, minute);
        var zoned = zone.AtLeniently(local);
        double hours = zoned.Offset.Milliseconds / 3_600_000.0;
        string abbr = zone.GetZoneInterval(zoned.ToInstant()).Name;
        return (hours, $"Offset for this date: {FormatOffset(hours)} ({abbr})", zoneId);
    }

    private static DateTimeZone? ResolveZone(string? explicitId, double lat, double lon)
    {
        string? id = ResolveZoneId(explicitId, lat, lon);
        return id is null ? null : Tzdb.GetZoneOrNull(id);
    }

    private static string FormatOffset(double hours)
    {
        string sign = hours < 0 ? "-" : "+";
        int totalMin = (int)Math.Round(Math.Abs(hours) * 60);
        return totalMin % 60 == 0
            ? $"UTC{sign}{totalMin / 60}"
            : $"UTC{sign}{totalMin / 60}:{totalMin % 60:D2}";
    }
}
