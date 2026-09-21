using System.Text.Json.Serialization;

namespace Lore.Models;

public sealed class Celebrity
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("category")]
    public string Category { get; init; } = "";

    [JsonPropertyName("birthDate")]
    public string BirthDate { get; init; } = "";

    [JsonPropertyName("birthTime")]
    public string? BirthTime { get; init; }

    [JsonPropertyName("birthTimeKnown")]
    public bool BirthTimeKnown { get; init; }

    [JsonPropertyName("birthPlace")]
    public string BirthPlace { get; init; } = "";

    [JsonPropertyName("latitude")]
    public double Latitude { get; init; }

    [JsonPropertyName("longitude")]
    public double Longitude { get; init; }

    [JsonPropertyName("utcOffsetHours")]
    public double UtcOffsetHours { get; init; }

    // IANA time-zone id (e.g. "Europe/London"). When present, the birth instant is
    // resolved against the historical tz database rather than the fixed offset above.
    // Null on legacy charts — BirthTimeResolver backfills it from latitude/longitude.
    [JsonPropertyName("timeZoneId")]
    public string? TimeZoneId { get; init; }

    [JsonPropertyName("bio")]
    public string Bio { get; init; } = "";

    // Transient dignity-verdict tint for the browse list, filled in after the chart is
    // scored (not persisted). Empty/transparent hex = Ordinary (no highlight).
    [JsonIgnore] public string VerdictColorHex { get; set; } = "#00000000";
    [JsonIgnore] public string VerdictLabel { get; set; } = "";

    public DateOnly GetBirthDate() => DateOnly.ParseExact(BirthDate, "yyyy-MM-dd");

    public TimeOnly GetBirthTime()
    {
        if (BirthTime is { Length: > 0 } t)
            return TimeOnly.ParseExact(t, "HH:mm");
        return new TimeOnly(12, 0); // noon default when unknown
    }

    // UTC conversion lives in Services/BirthTimeResolver (it needs the tz database and a
    // lat/lon → zone lookup), so the model stays free of those dependencies.
}
