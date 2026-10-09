namespace Lore.Models;

// "North Node (true)" and where it stands.
public sealed record AlternatePoint(string Name, PlanetPosition Position);

public sealed class NatalChart
{
    public required Celebrity Celebrity { get; init; }
    public required IReadOnlyList<PlanetPosition> Planets { get; init; }
    public required IReadOnlyList<HouseCusp> Houses { get; init; }    // 12 entries, index 0 = house 1
    public required IReadOnlyList<Aspect> Aspects { get; init; }

    // The bodies' aspects to the Ascendant and Midheaven; empty without a birth time.
    public IReadOnlyList<AngleAspect> AngleAspects { get; init; } = [];

    // The North Node and Lilith of the kind the settings did not choose (the true node
    // beside a mean one, and so on), for the Worksheet to set beside the ones in use.
    // Nothing else reads them.
    public IReadOnlyList<AlternatePoint> Alternates { get; init; } = [];
    public double Ascendant { get; init; }   // ecliptic longitude
    public double Midheaven { get; init; }   // ecliptic longitude
    public double Vertex { get; init; }      // where the prime vertical meets the ecliptic in the west
    public double Armc { get; init; }        // sidereal time at the birthplace, in degrees

    // The true obliquity of the ecliptic at that instant; null if it could not be had.
    public double? Obliquity { get; init; }

    // The house system the cusps were actually calculated in, as shown to the user,
    // e.g. "Placidus houses" — or a note that another system had to stand in for it.
    public string HouseSystemLabel { get; init; } = "Placidus houses";

    // The instant the chart was calculated for, in Universal Time.
    public DateTime CalculatedForUtc { get; init; }

    // Empty normally. Otherwise what went wrong with the ephemeris: the data files were
    // missing and a rougher model stood in, or a body could not be calculated at all.
    public string EphemerisNote { get; init; } = "";

    // The settings this chart was calculated with.
    public ChartSettings Settings { get; init; } = ChartSettings.Default;

    // The place the angles and houses were taken for: the birthplace, unless the chart
    // was cast for somewhere else (a relocated chart, a return cast for another town).
    public double? GeoLatitude { get; init; }
    public double? GeoLongitude { get; init; }

    // The lunations and stations around the birth and its planetary hour. Looked for in
    // the ephemeris after the chart is cast, and only for a chart that is to be shown;
    // null on any other, and the Worksheet then leaves those lines out.
    public NatalEvents? Events { get; set; }

    // Whether the Sun was above the horizon of the place the chart was cast for, worked
    // out from its altitude there (see ChartService). Null on a chart built without it.
    public bool? SunAboveHorizon { get; init; }

    // True when the Sun is above the horizon, whatever the house system. Meaningless if
    // not Timed. Taken from the Sun's altitude; only a chart without one falls back on
    // the Sun's longitude lying in the half from the Descendant over the Midheaven to
    // the Ascendant, which is the same thing except inside the polar circles, where the
    // Ascendant is by convention kept east and that half can be the one below ground.
    public bool IsDayChart => SunAboveHorizon ??
        (GetPlanet(Planet.Sun) is { } sun && (((sun.Longitude - Ascendant) % 360) + 360) % 360 >= 180);

    // Without a birth time the planets are placed for noon and the Ascendant, Midheaven
    // and houses are unknown; nothing should be read from them.
    public bool Timed => Celebrity.BirthTimeKnown;

    // The technical angles in one line, for under the chart's heading and in the PDF.
    // The Midheaven is not anyone's "sign"; it lives here so it cannot be taken for one.
    public string AnglesLine => !Timed
        ? $"Birth time unknown — planets are placed for noon; no Ascendant, Midheaven or houses   ·   {Settings.Node.Name()}"
        : $"Ascendant {ZodiacSignExtensions.FromLongitude(Ascendant).Name()} {ZodiacSignExtensions.FormatDegreeInSign(Ascendant)}" +
          $"   ·   Midheaven {ZodiacSignExtensions.FromLongitude(Midheaven).Name()} {ZodiacSignExtensions.FormatDegreeInSign(Midheaven)}" +
          $"   ·   {HouseSystemLabel}   ·   {Settings.Node.Name()}";

    public PlanetPosition? GetPlanet(Planet p) =>
        Planets.FirstOrDefault(x => x.Planet == p);

    // Ecliptic longitude of a body or angle; null for a body the chart does not have.
    public double? LongitudeOf(NatalPoint point) => point.Kind switch
    {
        NatalPointKind.Ascendant => Ascendant,
        NatalPointKind.Midheaven => Midheaven,
        NatalPointKind.Vertex => Timed ? Vertex : null,
        NatalPointKind.Fortune => Services.NatalMetricsService.Lot(this, spirit: false),
        NatalPointKind.Spirit => Services.NatalMetricsService.Lot(this, spirit: true),
        _ => GetPlanet(point.Body)?.Longitude
    };

    public HouseCusp GetHouse(int house) =>
        Houses[house - 1];

    public int GetHouseForLongitude(double longitude)
    {
        double lon = ((longitude % 360) + 360) % 360;
        for (int i = 0; i < 12; i++)
        {
            double start = ((Houses[i].Longitude % 360) + 360) % 360;
            double end = ((Houses[(i + 1) % 12].Longitude % 360) + 360) % 360;
            if (end < start) end += 360;
            double test = lon < start ? lon + 360 : lon;
            if (test >= start && test < end)
                return i + 1;
        }
        return 1;
    }
}
