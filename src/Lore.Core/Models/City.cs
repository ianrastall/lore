using System.Text.Json.Serialization;

namespace Lore.Models;

public sealed class City
{
    [JsonPropertyName("name")]    public string Name { get; init; } = "";
    [JsonPropertyName("admin")]   public string Admin { get; init; } = "";
    [JsonPropertyName("country")] public string Country { get; init; } = "";
    [JsonPropertyName("lat")]     public double Latitude { get; init; }
    [JsonPropertyName("lon")]     public double Longitude { get; init; }
    [JsonPropertyName("utc")]     public double UtcOffsetHours { get; init; }
    [JsonPropertyName("tz")]      public string TimeZoneId { get; init; } = "";
    [JsonPropertyName("pop")]     public long Population { get; init; }

    // "Paris, Île-de-France, France" -- the admin region disambiguates the many
    // duplicate city names (Springfield, Portland, ...). Omitted when it adds nothing.
    public string Display =>
        !string.IsNullOrEmpty(Admin) && !Admin.Equals(Name, System.StringComparison.OrdinalIgnoreCase)
            ? $"{Name}, {Admin}, {Country}"
            : $"{Name}, {Country}";
}
