using Lore.Models;
using NodaTime;

namespace Lore.Services;

// The sky's own calendar over a stretch of days: New and Full Moons, eclipses, stations
// and the slow planets' changes of sign.
public sealed partial class TransitService
{
    // ── Sky calendar ──────────────────────────────────────────────────────────
    // What the sky itself does over the same stretch: New and Full Moons (and which of
    // them are eclipses), the planets' stations, and the slow planets' changes of sign.
    // The events are the same for everyone; the chart only says which house each falls in.

    private static readonly Planet[] Stationing =
    [
        Planet.Mercury, Planet.Venus, Planet.Mars, Planet.Jupiter,
        Planet.Saturn, Planet.Uranus, Planet.Neptune, Planet.Pluto,
    ];

    // The planets slow enough for a change of sign to be worth marking: Jupiter takes a
    // year over one, Pluto up to thirty.
    private static readonly Planet[] SlowIngress =
        [Planet.Jupiter, Planet.Saturn, Planet.Uranus, Planet.Neptune, Planet.Pluto];

    // How far past the end to look for the station that ends a retrograde begun inside
    // the forecast. Pluto, the slowest, is retrograde for about 160 days.
    private const int StationLookahead = 200;

    public IReadOnlyList<SkyEvent> SkyCalendar(
        NatalChart natal, DateOnly start, int days, DateTimeZone zone, CancellationToken cancel = default)
    {
        if (!_pinned) return For(natal).SkyCalendar(natal, start, days, zone, cancel);

        var first = new LocalDate(start.Year, start.Month, start.Day);
        double jd0 = SwissEphemeris.DateTimeToJulianDay(BirthTimeResolver.StartOfDay(zone, first).ToDateTimeUtc());
        double jd1 = SwissEphemeris.DateTimeToJulianDay(BirthTimeResolver.StartOfDay(zone, first.PlusDays(days)).ToDateTimeUtc());
        int n = Math.Max(1, (int)Math.Ceiling(jd1 - jd0));
        double At(int i) => Math.Min(jd0 + i, jd1);

        bool timed = natal.Celebrity.BirthTimeKnown;
        int? House(double lon) => timed ? natal.GetHouseForLongitude(lon) : null;
        var events = new List<SkyEvent>();

        // New and Full Moons: the Moon's lead over the Sun passing 0° and 180°. It gains
        // about 12° a day, so daily samples cannot step over one.
        double Lead(double jd) =>
            _charts.CalculateBody(jd, Planet.Sun) is { } s && _charts.CalculateBody(jd, Planet.Moon) is { } m
                ? Normalize(m.Longitude - s.Longitude) : double.NaN;
        var eclipses = new[] { Eclipses(jd0, jd1, lunar: false), Eclipses(jd0, jd1, lunar: true) };
        for (int i = 0; i < n; i++)
        {
            cancel.ThrowIfCancellationRequested();
            double from = Lead(At(i)), to = Lead(At(i + 1));
            if (double.IsNaN(from) || double.IsNaN(to)) break;
            for (int full = 0; full <= 1; full++)
            {
                double ahead = Normalize(full * 180 - from);
                if (ahead <= 0 || ahead > Normalize(to - from)) continue;

                double target = full * 180;
                double jd = Bisect(t => Signed(Lead(t) - target), At(i), At(i + 1));
                if (_charts.CalculateBody(jd, Planet.Moon) is not { } moon) continue;
                // An eclipse is greatest within an hour or so of the exact New or Full Moon.
                var eclipse = eclipses[full].FirstOrDefault(e => Math.Abs(e.Jd - jd) < 0.5);
                events.Add(new SkyEvent(
                    (full, eclipse.Kind) switch
                    {
                        (0, null) => SkyEventKind.NewMoon,
                        (0, _)    => SkyEventKind.SolarEclipse,
                        (_, null) => SkyEventKind.FullMoon,
                        _         => SkyEventKind.LunarEclipse,
                    },
                    SwissEphemeris.JulianDayToDateTime(jd), Planet.Moon, moon.Longitude, moon.Sign)
                {
                    EclipseType = eclipse.Kind,
                    NatalHouse = House(moon.Longitude),
                });
            }
        }

        // Stations: where a planet's speed changes sign. Looked for some way past the
        // end as well, so a retrograde that begins inside the forecast can say when it ends.
        foreach (var planet in Stationing)
        {
            cancel.ThrowIfCancellationRequested();
            int m = n + StationLookahead;
            var speed = new double[m + 1];
            bool available = true;
            for (int i = 0; i <= m && available; i++)
            {
                speed[i] = SpeedAt(planet, jd0 + i);
                available = !double.IsNaN(speed[i]);
            }
            if (!available) continue;

            var stations = new List<(double Jd, bool Retrograde)>();
            for (int i = 0; i < m; i++)
                if ((speed[i] < 0) != (speed[i + 1] < 0))
                    stations.Add((Bisect(t => SpeedAt(planet, t), jd0 + i, jd0 + i + 1), speed[i + 1] < 0));

            for (int k = 0; k < stations.Count; k++)
            {
                var (jd, retrograde) = stations[k];
                if (jd >= jd1 || _charts.CalculateBody(jd, planet) is not { } p) continue;
                events.Add(new SkyEvent(SkyEventKind.Station, SwissEphemeris.JulianDayToDateTime(jd), planet, p.Longitude, p.Sign)
                {
                    Retrograde = retrograde,
                    DirectUtc = retrograde && k + 1 < stations.Count ? SwissEphemeris.JulianDayToDateTime(stations[k + 1].Jd) : null,
                    NatalHouse = House(p.Longitude),
                });
            }
        }

        // Changes of sign. A planet going backwards over a boundary re-enters the sign
        // before; the boundary it crosses is then the start of the sign it is leaving.
        foreach (var planet in SlowIngress)
        {
            cancel.ThrowIfCancellationRequested();
            var at = new PlanetPosition?[n + 1];
            for (int i = 0; i <= n; i++) at[i] = _charts.CalculateBody(At(i), planet);
            for (int i = 0; i < n; i++)
            {
                if (at[i] is not { } a || at[i + 1] is not { } b || a.Sign == b.Sign) continue;
                bool backwards = (int)b.Sign == ((int)a.Sign + 11) % 12;
                double boundary = (int)(backwards ? a.Sign : b.Sign) * 30;
                double jd = Bisect(t => SignedAt(planet, boundary, t), At(i), At(i + 1));
                events.Add(new SkyEvent(SkyEventKind.Ingress, SwissEphemeris.JulianDayToDateTime(jd), planet, boundary, b.Sign)
                {
                    Retrograde = backwards,
                    // Just inside the sign entered, so the house is the one it arrives in.
                    NatalHouse = House(boundary + (backwards ? -1e-6 : 1e-6)),
                });
            }
        }

        events.Sort((x, y) => x.Utc.CompareTo(y.Utc));
        return events;
    }

    // Every eclipse of one kind between two instants, with a day to spare at each end.
    private List<(double Jd, string? Kind)> Eclipses(double fromJd, double toJd, bool lunar)
    {
        var found = new List<(double, string?)>();
        double after = fromJd - 1;
        // There are at most five of either kind in a year; the cap only guards the loop.
        for (int i = 0; i < 64 && _charts.NextEclipse(after, lunar) is { } e && e.Jd < toJd + 1; i++)
        {
            found.Add((e.Jd, e.Kind));
            after = e.Jd + 1;
        }
        return found;
    }
}
