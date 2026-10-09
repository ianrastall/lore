using System.Globalization;
using System.Text.Json.Serialization;

namespace Lore.Models;

public sealed class Celebrity
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    // (The optional text below reads an explicit null in a file as empty, so nothing
    // that searches or prints it has to allow for one.)
    [JsonPropertyName("category")]
    public string Category { get; init => field = value ?? ""; } = "";

    [JsonPropertyName("birthDate")]
    public string BirthDate { get; init; } = "";

    // True when BirthDate is an Old Style date, in the Julian calendar that was in use
    // before the Gregorian reform (1582 in Catholic Europe, 1752 in Britain and its
    // colonies, 1918 in Russia). It is converted before anything is calculated.
    [JsonPropertyName("julianCalendar")]
    public bool JulianCalendar { get; init; }

    [JsonPropertyName("birthTime")]
    public string? BirthTime { get; init; }

    [JsonPropertyName("birthTimeKnown")]
    public bool BirthTimeKnown { get; init; }

    // How far out the birth time might be, in minutes either way; 0 when not stated.
    [JsonPropertyName("birthTimeUncertaintyMinutes")]
    public int BirthTimeUncertaintyMinutes { get; init; }

    [JsonPropertyName("birthPlace")]
    public string BirthPlace { get; init => field = value ?? ""; } = "";

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

    // True when the user set UtcOffsetHours themselves (the time-zone database being
    // wrong or ambiguous for this birth): the offset is then used as given and the
    // zone is ignored.
    [JsonPropertyName("utcOffsetFixed")]
    public bool UtcOffsetFixed { get; init; }

    // Where the birth time came from: a Rodden rating (see RoddenRating) and a free-text
    // note of the source. Both optional.
    [JsonPropertyName("roddenRating")]
    public string? RoddenRating { get; init; }

    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [JsonPropertyName("bio")]
    public string Bio { get; init => field = value ?? ""; } = "";

    // Transient dignity-verdict tint for the browse list, filled in after the chart is
    // scored (not persisted). Empty/transparent hex = Ordinary (no highlight).
    [JsonIgnore] public string VerdictColorHex { get; set; } = "#00000000";
    [JsonIgnore] public string VerdictLabel { get; set; } = "";

    // The date as recorded: Old Style if JulianCalendar is set. BirthTimeResolver
    // turns it into the Gregorian date the calculation uses.
    // (Invariant culture: the stored text is always a Gregorian-style yyyy-MM-dd,
    // whatever calendar Windows is set to.)
    public DateOnly GetBirthDate() => DateOnly.ParseExact(BirthDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    // The recorded date for display, marked when it is Old Style: "1642-12-25 O.S."
    [JsonIgnore]
    public string BirthDateLabel => JulianCalendar ? $"{BirthDate} O.S." : BirthDate;

    // Noon when the time is unknown — even if a time is still written in the record
    // (a file brought in from elsewhere may carry one beside birthTimeKnown: false):
    // everything that says "placed for noon" must be describing what was calculated.
    public TimeOnly GetBirthTime()
    {
        if (BirthTimeKnown && BirthTime is { Length: > 0 } t)
            return TimeOnly.ParseExact(t, "HH:mm", CultureInfo.InvariantCulture);
        return new TimeOnly(12, 0);
    }

    // UTC conversion lives in Services/BirthTimeResolver (it needs the tz database and a
    // lat/lon → zone lookup), so the model stays free of those dependencies.
}
