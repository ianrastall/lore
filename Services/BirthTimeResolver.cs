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
//
// One exception to "whatever tzdb says": a birth from before standard time existed
// uses the local mean time of the birthplace's own longitude (see IsLocalMeanTime).
public static class BirthTimeResolver
{
    private static readonly IDateTimeZoneProvider Tzdb = DateTimeZoneProviders.Tzdb;

    // The birth date in the Gregorian calendar, which everything is calculated in. An
    // Old Style (Julian) date is converted: 25 December 1642 becomes 4 January 1643.
    public static DateOnly GregorianDate(Celebrity c) =>
        GregorianDate(c.GetBirthDate(), c.JulianCalendar);

    public static DateOnly GregorianDate(DateOnly recorded, bool julian)
    {
        if (!julian) return recorded;
        var iso = new LocalDate(recorded.Year, recorded.Month, recorded.Day, CalendarSystem.Julian)
            .WithCalendar(CalendarSystem.Iso);
        return new DateOnly(iso.Year, iso.Month, iso.Day);
    }

    public static DateTime ToUtc(Celebrity c)
    {
        var d = GregorianDate(c);
        var t = c.GetBirthTime();
        var local = new LocalDateTime(d.Year, d.Month, d.Day, t.Hour, t.Minute);

        // An offset the user set themselves overrides the time-zone database.
        var zone = c.UtcOffsetFixed ? null : ResolveZone(c.TimeZoneId, c.Latitude, c.Longitude);
        if (zone is not null)
        {
            // Lenient: a birth time that never existed (spring-forward gap) shifts
            // forward; an ambiguous one (fall-back overlap) takes the earlier instant.
            var zoned = zone.AtLeniently(local);
            if (IsLocalMeanTime(zone, zoned.ToInstant()))
                return DateTime.SpecifyKind(
                    local.ToDateTimeUnspecified().AddSeconds(-LocalMeanTimeSeconds(c.Longitude)),
                    DateTimeKind.Utc);
            return zoned.ToDateTimeUtc();
        }

        // No zone available, or overridden — use the stored fixed offset.
        var unspecified = new DateTime(d.Year, d.Month, d.Day, t.Hour, t.Minute, 0, DateTimeKind.Unspecified);
        return unspecified.AddHours(-c.UtcOffsetHours);
    }

    // The calendar day of birth at the birthplace, as two instants — local midnight to the
    // next local midnight — and a way to turn any instant between them back into the
    // clock time there. On a day the clocks changed this is 23 or 25 hours long.
    public static (DateTime startUtc, DateTime endUtc, Func<DateTime, string> clock) LocalDay(Celebrity c)
    {
        var d = GregorianDate(c);
        var date = new LocalDate(d.Year, d.Month, d.Day);

        var zone = c.UtcOffsetFixed ? null : ResolveZone(c.TimeZoneId, c.Latitude, c.Longitude);
        if (zone is not null && !IsLocalMeanTime(zone, StartOfDay(zone, date).ToInstant()))
        {
            return (StartOfDay(zone, date).ToDateTimeUtc(),
                    StartOfDay(zone, date.PlusDays(1)).ToDateTimeUtc(),
                    utc => ClockIn(zone, utc));
        }

        // A fixed offset: set by hand, local mean time, or the fallback when no zone is known.
        double seconds = zone is not null ? LocalMeanTimeSeconds(c.Longitude) : c.UtcOffsetHours * 3600;
        var start = DateTime.SpecifyKind(new DateTime(d.Year, d.Month, d.Day).AddSeconds(-seconds), DateTimeKind.Utc);
        return (start, start.AddDays(1), utc => ClockAt(seconds, utc));
    }

    // The first moment of a calendar day in a zone, or of the next day the zone had if
    // it never had this one: Samoa went from 29 to 31 December 2011 when it crossed the
    // date line, and the day after the 29th there began on the 31st.
    public static ZonedDateTime StartOfDay(DateTimeZone zone, LocalDate date)
    {
        for (int skipped = 0; ; skipped++)
        {
            try { return zone.AtStartOfDay(date.PlusDays(skipped)); }
            catch (SkippedTimeException) when (skipped < 3) { }
        }
    }

    // Turns an instant near the birth back into the clock time at the birthplace, by the
    // rule ToUtc went the other way with: the zone's clock as it stood at that instant
    // (so an hour the clocks skipped is skipped here too), or a fixed offset.
    public static Func<DateTime, string> Clock(Celebrity c)
    {
        var zone = c.UtcOffsetFixed ? null : ResolveZone(c.TimeZoneId, c.Latitude, c.Longitude);
        if (zone is null)
            return utc => ClockAt(c.UtcOffsetHours * 3600, utc);
        if (IsLocalMeanTime(zone, Instant.FromDateTimeUtc(DateTime.SpecifyKind(ToUtc(c), DateTimeKind.Utc))))
            return utc => ClockAt(LocalMeanTimeSeconds(c.Longitude), utc);
        return utc => ClockIn(zone, utc);
    }

    private static string ClockIn(DateTimeZone zone, DateTime utc) =>
        Instant.FromDateTimeUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc))
            .InZone(zone).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    private static string ClockAt(double offsetSeconds, DateTime utc) =>
        utc.AddSeconds(offsetSeconds).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    // The same conversion, spelled out for the worksheet: the instant, and the offset
    // that produced it with where that offset came from — "UTC-6 (CST, America/Chicago)".
    public static (DateTime utc, string offset) Explain(Celebrity c)
    {
        var utc = ToUtc(c);
        if (c.UtcOffsetFixed)
            return (utc, $"{FormatOffset(c.UtcOffsetHours)} (set by hand)");

        string? zoneId = ResolveZoneId(c.TimeZoneId, c.Latitude, c.Longitude);
        var zone = zoneId is null ? null : Tzdb.GetZoneOrNull(zoneId);
        if (zone is null)
            return (utc, $"{FormatOffset(c.UtcOffsetHours)} (no time zone found for this place)");

        var instant = Instant.FromDateTimeUtc(utc);
        if (IsLocalMeanTime(zone, instant))
            return (utc, $"{FormatOffset(LocalMeanTimeSeconds(c.Longitude) / 3600.0)} " +
                         "(local mean time at the birthplace's longitude; standard time zones did not exist yet)");

        var interval = zone.GetZoneInterval(instant);
        return (utc, $"{FormatOffset(interval.WallOffset.Seconds / 3600.0)} ({interval.Name}, {zoneId})");
    }

    // Before standard time, every town kept its own clock by the Sun. The tz database
    // marks that era "LMT" but can only give the mean time of the zone's reference city
    // (Rome for all of Italy, say), which is minutes off anywhere else — and four minutes
    // of clock time is a degree on the Ascendant. So for an LMT-era birth the offset is
    // taken from the birthplace's own longitude instead. Later city-based legal times
    // (Paris, Prague, Bern or Dublin Mean Time) were real nationwide clocks and are left
    // to tzdb. India is the exception: its "Howrah" and "Madras" times before 1906 were
    // railway timetables, and towns went on keeping local time.
    private static bool IsLocalMeanTime(DateTimeZone zone, Instant instant)
    {
        string era = zone.GetZoneInterval(instant).Name;
        return era == "LMT" || (zone.Id == "Asia/Kolkata" && era is "HMT" or "MMT");
    }

    // Local mean time runs four minutes ahead of UTC per degree of east longitude.
    private static double LocalMeanTimeSeconds(double longitude) => Math.Round(longitude * 240);

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
        if (IsLocalMeanTime(zone, zoned.ToInstant()))
        {
            double lmt = LocalMeanTimeSeconds(lon) / 3600.0;
            return (lmt, $"Offset for this date: {FormatOffset(lmt)} (local mean time at this longitude)", zoneId);
        }
        double hours = zoned.Offset.Milliseconds / 3_600_000.0;
        string abbr = zone.GetZoneInterval(zoned.ToInstant()).Name;
        // A clock time on the day the clocks changed may have happened twice, or not at
        // all. ToUtc settles it quietly (see AtLeniently above); say so here.
        string caution = zone.MapLocal(local).Count switch
        {
            0 => ". This clock time was skipped that day (the clocks went forward), so it is read as the time after the change.",
            2 => ". This clock time happened twice that day (the clocks went back); the earlier one is used. " +
                 "If it was the later one, set the UTC offset yourself under Time zone override.",
            _ => "",
        };
        return (hours, $"Offset for this date: {FormatOffset(hours)} ({abbr}){caution}", zoneId);
    }

    private static DateTimeZone? ResolveZone(string? explicitId, double lat, double lon)
    {
        string? id = ResolveZoneId(explicitId, lat, lon);
        return id is null ? null : Tzdb.GetZoneOrNull(id);
    }

    private static string FormatOffset(double hours)
    {
        string sign = hours < 0 ? "-" : "+";
        int totalSec = (int)Math.Round(Math.Abs(hours) * 3600);
        int h = totalSec / 3600, m = totalSec / 60 % 60, s = totalSec % 60;
        return s != 0 ? $"UTC{sign}{h}:{m:D2}:{s:D2}"
             : m != 0 ? $"UTC{sign}{h}:{m:D2}"
             : $"UTC{sign}{h}";
    }
}
