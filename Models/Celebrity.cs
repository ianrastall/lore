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

    // The date as recorded, as its year, month and day: Old Style if JulianCalendar is
    // set. It is read by the rules of the calendar it is in, so an Old Style 29 February
    // in 1700, 1800 or 1900, which the Julian calendar had and the Gregorian did not, is
    // a date like any other. BirthTimeResolver turns it into the Gregorian date the
    // calculation uses. Throws FormatException for text that is not a date.
    public (int Year, int Month, int Day) GetRecordedDate() =>
        TryParseDate(BirthDate, JulianCalendar, out int year, out int month, out int day)
            ? (year, month, day)
            : throw new FormatException(
                $"'{BirthDate}' is not a date in the {(JulianCalendar ? "Julian" : "Gregorian")} calendar (yyyy-MM-dd).");

    // The recorded date as a DateOnly, for anything that shows it in a date picker.
    // DateOnly is Gregorian, so this cannot give an Old Style 29 February of a year the
    // Gregorian calendar gave none: ask GetRecordedDate for that.
    public DateOnly GetBirthDate()
    {
        var (year, month, day) = GetRecordedDate();
        return new DateOnly(year, month, day);
    }

    // Reads "yyyy-MM-dd" as a date of the Julian or the Gregorian calendar. (Digits only:
    // the stored text is always in this form, whatever calendar Windows is set to.)
    public static bool TryParseDate(string? text, bool julian, out int year, out int month, out int day)
    {
        year = month = day = 0;
        if (text is not { Length: 10 } || text[4] != '-' || text[7] != '-') return false;
        if (!int.TryParse(text.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out year) ||
            !int.TryParse(text.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out month) ||
            !int.TryParse(text.AsSpan(8, 2), NumberStyles.None, CultureInfo.InvariantCulture, out day))
            return false;
        return year >= 1 && month is >= 1 and <= 12 && day >= 1 && day <= DaysInMonth(year, month, julian);
    }

    // The Julian calendar has a leap day every fourth year without exception; the
    // Gregorian drops it in century years not divisible by 400.
    public static int DaysInMonth(int year, int month, bool julian) =>
        month == 2 && julian ? (year % 4 == 0 ? 29 : 28) : DateTime.DaysInMonth(year, month);

    // True for a day the Julian calendar had and the Gregorian did not.
    public static bool IsJulianOnly(int year, int month, int day) =>
        month == 2 && day == 29 && !DateTime.IsLeapYear(year);

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
