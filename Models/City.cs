using System.Text.Json.Serialization;

namespace Lore.Models;

public sealed class City
{
    [JsonPropertyName("name")]   public string Name { get; init; } = "";
    [JsonPropertyName("country")] public string Country { get; init; } = "";
    [JsonPropertyName("lat")]    public double Latitude { get; init; }
    [JsonPropertyName("lon")]    public double Longitude { get; init; }
    [JsonPropertyName("utc")]    public double UtcOffsetHours { get; init; }

    public string Display => $"{Name}, {Country}";
}
