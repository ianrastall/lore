namespace Lore.Models;

// One point in the first person's chart in aspect to one point in the second's. Neither
// side moves, so unlike a natal aspect or a transit there is no applying or separating,
// and no time attached: the contact is simply there, as close as its orb.
public sealed class SynastryAspect
{
    public required NatalPoint First { get; init; }   // the point in the first chart
    public required NatalPoint Second { get; init; }  // the point in the second chart
    public required AspectType Type { get; init; }
    public double Orb { get; init; }                  // distance from exact, degrees

    public TransitTone Tone => Type.Tone();

    // Editorial priority assigned by the interpreter — how much the reading should care
    // about this contact, not a measure of compatibility.
    public double Score { get; set; }
    public bool Shown { get; set; }
}

// One person's planet falling in a house of the other person's chart.
public sealed record HouseOverlay(Planet Planet, int House);

// Two birth charts compared: what SynastryService finds between them, for a reading to
// be written from.
public sealed class Synastry
{
    public required NatalChart First { get; init; }
    public required NatalChart Second { get; init; }

    public required IReadOnlyList<SynastryAspect> Aspects { get; init; }  // closest first

    // Where each person's planets land in the other's houses. Empty when the chart
    // owning the houses has no birth time.
    public required IReadOnlyList<HouseOverlay> FirstInSecondHouses { get; init; }
    public required IReadOnlyList<HouseOverlay> SecondInFirstHouses { get; init; }
}
