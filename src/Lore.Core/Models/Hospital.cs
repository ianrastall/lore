using System.Text.Json.Serialization;

namespace Lore.Models;

// A hospital birthplace, used by the Add-Chart dialog's autocomplete to autofill
// precise coordinates. Unlike City there is no timezone/population data: the app
// resolves the historical, DST-aware zone from lat/lon (BirthTimeResolver), and
// prefix search ranks by name rather than population.
public sealed class Hospital
{
    [JsonPropertyName("name")]    public string Name { get; init; } = "";
    [JsonPropertyName("city")]    public string City { get; init; } = "";
    [JsonPropertyName("country")] public string Country { get; init; } = "";
    [JsonPropertyName("lat")]     public double Latitude { get; init; }
    [JsonPropertyName("lon")]     public double Longitude { get; init; }

    // "St Thomas' Hospital — London, United Kingdom". City is often absent in the
    // source data, so it is dropped from the label when empty.
    public string Display =>
        !string.IsNullOrEmpty(City)
            ? $"{Name} — {City}, {Country}"
            : string.IsNullOrEmpty(Country) ? Name : $"{Name} — {Country}";
}
