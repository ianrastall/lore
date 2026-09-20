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

    [JsonPropertyName("bio")]
    public string Bio { get; init; } = "";

    public DateOnly GetBirthDate() => DateOnly.ParseExact(BirthDate, "yyyy-MM-dd");

    public TimeOnly GetBirthTime()
    {
        if (BirthTime is { Length: > 0 } t)
            return TimeOnly.ParseExact(t, "HH:mm");
        return new TimeOnly(12, 0); // noon default when unknown
    }

    public DateTime GetUtcBirthDateTime()
    {
        var d = GetBirthDate();
        var t = GetBirthTime();
        var local = new DateTime(d.Year, d.Month, d.Day, t.Hour, t.Minute, 0, DateTimeKind.Unspecified);
        return local.AddHours(-UtcOffsetHours);
    }
}
