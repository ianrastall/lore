using System.Text.Json;
using Lore.Models;
using Lore.Services;
using NodaTime;

namespace Lore.Tests;

// The bundled library (Data\celebrities.json), checked for internal consistency and
// against Astro-Databank's records (Reference\adb-reference.json).
public class FigureLibraryTests
{
    private static readonly string[] Categories =
    [
        "Actor", "Musician", "Writer", "Artist", "Political", "Scientist", "Historical", "Philosopher",
        "Director", "Athlete", "Media", "Entrepreneur", "Royalty", "Spiritual",
    ];

    private const int OldestAge = 122;

    private sealed record AdbRecord(string AdbTitle, string Date, string? Time, string Zone, string Rating,
                                    string? ExpectedUtc, string? Note);

    private static readonly Dictionary<string, AdbRecord> Reference = LoadReference();

    private static Dictionary<string, AdbRecord> LoadReference()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Reference", "adb-reference.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("figures").Deserialize<Dictionary<string, AdbRecord>>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    [Fact]
    public void Ids_are_unique_and_well_formed()
    {
        var ids = Repo.Figures.Select(f => f.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", id));
    }

    [Fact]
    public void Every_figure_has_complete_valid_data()
    {
        Assert.All(Repo.Figures, f =>
        {
            Assert.Contains(f.Category, Categories);
            Assert.False(string.IsNullOrWhiteSpace(f.Name));
            Assert.False(string.IsNullOrWhiteSpace(f.Bio));
            f.GetBirthDate();
            Assert.True(f.BirthTimeKnown, $"{f.Name}: the library only holds timed charts");
            f.GetBirthTime();
            Assert.InRange(f.Latitude, -90, 90);
            Assert.InRange(f.Longitude, -180, 180);
            // An explicit zone on every figure: the map lookup is a fallback for user
            // charts, and once put Jung on German rather than Swiss time.
            Assert.NotNull(DateTimeZoneProviders.Tzdb.GetZoneOrNull(f.TimeZoneId ?? ""));
        });
    }

    // Dates of death are Gregorian, as the birth dates are, and fall within a lifetime.
    [Fact]
    public void Every_death_date_is_a_real_day_within_a_lifetime_of_the_birth()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        Assert.All(Repo.Figures.Where(f => f.DeathDate is not null), f =>
        {
            Assert.True(Celebrity.TryParseDate(f.DeathDate, julian: false, out int year, out int month, out int day), $"{f.Name}: {f.DeathDate}");
            var (born, died) = (BirthTimeResolver.GregorianDate(f), new DateOnly(year, month, day));
            Assert.True(born < died && died <= today, $"{f.Name}: {f.BirthDate} to {f.DeathDate}");
            Assert.True(died.Year - born.Year <= OldestAge, $"{f.Name}: {f.BirthDate} to {f.DeathDate}");
        });
    }

    // Nobody now alive was born this long ago, so anyone without a date has been missed.
    [Fact]
    public void Nobody_is_left_living_beyond_the_oldest_age_anyone_has_reached()
    {
        int born = DateTime.Today.Year - OldestAge;
        Assert.All(Repo.Figures.Where(f => f.GetRecordedDate().Year < born), f => Assert.NotNull(f.DeathDate));
    }

    [Theory]
    [InlineData("elvis-presley", "1977-08-16")]
    [InlineData("albert-einstein", "1955-04-18")]
    [InlineData("marie-curie", "1934-07-04")]
    [InlineData("queen-elizabeth-ii", "2022-09-08")]
    [InlineData("johann-wolfgang-von-goethe", "1832-03-22")]
    [InlineData("paul-mccartney", null)]
    [InlineData("william-saliba", null)]
    public void Well_known_lifetimes_are_as_recorded(string id, string? died) =>
        Assert.Equal(died, Repo.Figure(id).DeathDate);

    [Fact]
    public void Every_figure_has_a_Wikidata_item_of_its_own()
    {
        Assert.All(Repo.Figures, f =>
        {
            Assert.Matches("^Q[1-9][0-9]*$", f.WikidataId ?? "");
            Assert.True(f.Sitelinks > 0, f.Name);
        });
        Assert.Equal(Repo.Figures.Count, Repo.Figures.Select(f => f.WikidataId).Distinct().Count());
    }

    [Fact]
    public void Every_figure_has_an_Astro_Databank_record_with_the_same_date()
    {
        Assert.All(Repo.Figures, f =>
        {
            Assert.True(Reference.ContainsKey(f.Id), $"{f.Name} has no Astro-Databank reference entry");
            Assert.Equal(Reference[f.Id].Date, f.BirthDate);
        });
    }

    // The library's rule: only birth times from a birth record (AA) or from the person
    // or their family (A). Anything rated lower (B, C, DD) or untimed (X) stays out.
    [Fact]
    public void Every_figure_has_a_well_documented_birth_time()
    {
        Assert.All(Repo.Figures, f =>
            Assert.True(Reference[f.Id].Rating is "AA" or "A", $"{f.Name} is rated {Reference[f.Id].Rating} on Astro-Databank"));
    }

    // Each figure carries the rating Astro-Databank gives it, and says where it came from.
    [Fact]
    public void Every_figure_shows_its_Astro_Databank_rating_and_source()
    {
        Assert.All(Repo.Figures, f =>
        {
            Assert.True(f.RoddenRating == Reference[f.Id].Rating, $"{f.Name}: {f.RoddenRating} but Astro-Databank says {Reference[f.Id].Rating}");
            Assert.True(Lore.Models.RoddenRating.IsKnown(f.RoddenRating), f.Name);
            Assert.StartsWith("Astro-Databank", f.Source);
        });
    }

    // For every figure whose time Astro-Databank rates AA or A: the recorded clock time
    // matches, and Lore turns it into the same instant Astro-Databank's stated time
    // standard does (within a minute).
    [Fact]
    public void Well_documented_birth_times_match_Astro_Databank()
    {
        var checkedOnes = Repo.Figures.Where(f => Reference[f.Id].Rating is "AA" or "A").ToList();
        Assert.NotEmpty(checkedOnes);
        Assert.All(checkedOnes, f =>
        {
            var r = Reference[f.Id];
            Assert.Equal(r.Time![..5], f.BirthTime);
            var lore = BirthTimeResolver.ToUtc(f);
            var adb = DateTime.Parse(r.ExpectedUtc!);
            Assert.True(Math.Abs((lore - adb).TotalSeconds) < 60,
                $"{f.Name}: Lore {lore:u} vs Astro-Databank {adb:u} ({r.Zone})");
        });
    }
}
