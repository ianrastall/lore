using Lore.Models;
using NodaTime;

namespace Lore.Services;

// Sunrise, sunset and the planetary hours of a day at the reader's place.
public sealed partial class TransitService
{
    // ── Planetary hours ───────────────────────────────────────────────────────

    // The order the hours run in: the seven planets from slowest to fastest.
    internal static readonly Planet[] Chaldean =
        [Planet.Saturn, Planet.Jupiter, Planet.Mars, Planet.Sun, Planet.Venus, Planet.Mercury, Planet.Moon];

    // The planet each weekday is named for, Sunday first.
    internal static readonly Planet[] DayRulers =
        [Planet.Sun, Planet.Moon, Planet.Mars, Planet.Mercury, Planet.Jupiter, Planet.Venus, Planet.Saturn];

    // The planetary hours of the day that begins with the sunrise falling on a calendar
    // day (given as its two midnights). Null if the Sun does not rise in that day at
    // that place and set before rising again: in the polar summer and winter, or if the
    // place is so far from the clock's time zone that its sunrise falls on another date.
    public PlanetaryHours? PlanetaryHours(DateOnly date, double fromJd, double toJd, HomePlace home)
    {
        if (_charts.NextSunriseOrSet(fromJd, home.Latitude, home.Longitude, sunset: false) is not { } rise || rise >= toJd ||
            _charts.NextSunriseOrSet(rise, home.Latitude, home.Longitude, sunset: true) is not { } set || set - rise >= 1 ||
            _charts.NextSunriseOrSet(set, home.Latitude, home.Longitude, sunset: false) is not { } next || next - set >= 1)
            return null;

        var ruler = DayRulers[(int)date.DayOfWeek];
        int first = Array.IndexOf(Chaldean, ruler);
        List<PlanetaryHour> Twelve(double start, double end, int offset) => Enumerable.Range(0, 12)
            .Select(i => new PlanetaryHour(
                SwissEphemeris.JulianDayToDateTime(start + (end - start) * i / 12),
                SwissEphemeris.JulianDayToDateTime(start + (end - start) * (i + 1) / 12),
                Chaldean[(first + offset + i) % 7]))
            .ToList();

        return new PlanetaryHours
        {
            Place = home,
            SunriseUtc = SwissEphemeris.JulianDayToDateTime(rise),
            SunsetUtc = SwissEphemeris.JulianDayToDateTime(set),
            NextSunriseUtc = SwissEphemeris.JulianDayToDateTime(next),
            DayRuler = ruler,
            Day = Twelve(rise, set, 0),
            Night = Twelve(set, next, 12),
        };
    }
}
