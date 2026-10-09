namespace Lore.Models;

// What the sky did shortly before and after a birth, as against where it stood at the
// moment: the New and Full Moon before it, each planet's nearest stations, and the
// planetary day and hour. Unlike everything in NatalMetrics these are looked for in the
// ephemeris, over days or months either side, so they are worked out once for a chart
// that is to be shown (see NatalEventsService) and not for every chart that is scored.
public sealed class NatalEvents
{
    public Lunation? NewMoonBefore { get; init; }
    public Lunation? FullMoonBefore { get; init; }

    // Mercury to Pluto.
    public required IReadOnlyList<NearStations> Stations { get; init; }

    // Null without a birth time, or where the Sun did not rise and set that day.
    public BirthHour? Hour { get; init; }

    // The later of the two lunations: the "prenatal syzygy" of the traditional texts.
    public Lunation? Syzygy =>
        NewMoonBefore is null ? FullMoonBefore
        : FullMoonBefore is null || NewMoonBefore.Utc > FullMoonBefore.Utc ? NewMoonBefore : FullMoonBefore;
}

// A New or Full Moon: when, where the Moon stood, how many days before the birth, and
// the kind of eclipse it was ("total", "partial"…), if it was one.
public sealed record Lunation(bool Full, DateTime Utc, double Longitude, double DaysBefore, string? Eclipse);

// The last time a planet stood still before the birth and the next time after it; either
// is null if none was found within the search (some 800 days).
public sealed record NearStations(Planet Planet, StationPoint? Before, StationPoint? After)
{
    public StationPoint? Nearest => Before is null ? After : After is null || Before.Days <= After.Days ? Before : After;
}

// One station: when, which way the planet turned, and how many days from the birth.
public sealed record StationPoint(DateTime Utc, bool TurnsRetrograde, double Days);

// The planetary day and hour of a birth. The day runs from sunrise to sunrise and is
// named for the planet of the weekday it begins on; daylight and darkness are each cut
// into twelve hours, given to the planets in the Chaldean order beginning with the
// day's own. Hour counts from 1 at sunrise to 24.
public sealed record BirthHour(Planet DayRuler, Planet HourRuler, int Hour, bool ByDay, DateTime SunriseUtc, DateTime SunsetUtc);
