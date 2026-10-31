using System.Text.Json;
using System.Text.RegularExpressions;
using Lore.Models;
using Lore.Services;

namespace Lore.Tests;

// The fuller record of each figure (content\records), gathered from Wikidata by
// scripts\wikidata_records.py. Nothing in the app reads it yet, so these tests are what
// keeps it sound until something does.
public class FigureRecordsTests
{
    private static readonly Dictionary<string, Celebrity> ById = Repo.Figures.ToDictionary(f => f.Id);

    private static readonly Dictionary<string, JsonElement> Records =
        JsonDocument.Parse(File.ReadAllText(Repo.Data(@"records\figures.json"))).RootElement
            .EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

    private static readonly List<JsonElement> Connections =
        JsonDocument.Parse(File.ReadAllText(Repo.Data(@"records\connections.json"))).RootElement
            .EnumerateArray().ToList();

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() : null;

    private static string Key(JsonElement link) =>
        string.Join("|", new[] { Text(link, "first")!, Text(link, "second")! }.Order(StringComparer.Ordinal)) +
        $"|{Text(link, "relation")}|{Text(link, "group")}";

    private static IEnumerable<(string Id, JsonElement Event)> Events =>
        Records.SelectMany(r => r.Value.TryGetProperty("events", out var events)
            ? events.EnumerateArray().Select(e => (r.Key, e))
            : []);

    private static bool IsDate(string? date) => date is not null && Regex.IsMatch(date, @"^\d{4}(-\d{2}(-\d{2})?)?$") &&
        (date.Length != 10 || Celebrity.TryParseDate(date, julian: false, out _, out _, out _));

    [Fact]
    public void Every_figure_has_one_record_under_its_own_item()
    {
        Assert.Equal(Repo.Figures.Select(f => f.Id).Order(StringComparer.Ordinal), Records.Keys.Order(StringComparer.Ordinal));
        Assert.All(Repo.Figures, f => Assert.Equal(f.WikidataId, Text(Records[f.Id], "wikidataId")));
    }

    [Fact]
    public void A_record_holds_what_the_plan_gathers_and_nothing_else()
    {
        string[] fields = ["wikidataId", "birthName", "otherNames", "occupations", "portrait", "events"];
        Assert.All(Records.Values, r => Assert.All(r.EnumerateObject(), p => Assert.Contains(p.Name, fields)));
        Assert.All(Events, e => Assert.Contains(Text(e.Event, "kind"), new[] { "marriage", "divorce", "marriageEnded", "child", "award" }));
    }

    [Fact]
    public void Each_connection_joins_two_figures_once()
    {
        Assert.All(Connections, link =>
        {
            Assert.Contains(Text(link, "first")!, ById.Keys);
            Assert.Contains(Text(link, "second")!, ById.Keys);
            Assert.NotEqual(Text(link, "first"), Text(link, "second"));
            Assert.Contains(Text(link, "relation"), new[] { "spouse", "partner", "parent", "sibling", "groupmate", "relative" });
        });
        Assert.Equal(Connections.Count, Connections.Select(Key).Distinct().Count());
    }

    // Anyone who married or grew up with or played beside someone was alive when they were.
    // (A relative need not have been: some are a great-grandfather.)
    [Fact]
    public void People_joined_in_life_have_overlapping_lifetimes()
    {
        var together = Connections.Where(l => Text(l, "relation") is not "relative");
        Assert.All(together, l => Assert.True(Lifetimes.Overlap(ById[Text(l, "first")!], ById[Text(l, "second")!]), Key(l)));
    }

    [Fact]
    public void A_parent_is_old_enough_to_be_one()
    {
        Assert.All(Connections.Where(l => Text(l, "relation") == "parent"), l =>
            Assert.True(ById[Text(l, "second")!].GetRecordedDate().Year - ById[Text(l, "first")!].GetRecordedDate().Year >= 13, Key(l)));
    }

    [Fact]
    public void Every_date_says_as_much_as_is_known_and_no_more()
    {
        Assert.All(Events, e => Assert.True(IsDate(Text(e.Event, "date")), $"{e.Id}: {e.Event}"));
        Assert.All(Connections.SelectMany(l => new[] { Text(l, "from"), Text(l, "to") }).OfType<string>(), date => Assert.True(IsDate(date), date));
    }

    // A father can die before a birth and an award can come after a death; nothing else can.
    [Fact]
    public void Events_fall_within_the_lifetime()
    {
        Assert.All(Events, e =>
        {
            var figure = ById[e.Id];
            string date = Text(e.Event, "date")!;
            Assert.True(string.CompareOrdinal(date, figure.BirthDate[..date.Length]) >= 0, $"{e.Id}: {e.Event}");
            if (figure.DeathDate is not { } died || Text(e.Event, "kind") == "award") return;
            int lastYear = int.Parse(died[..4]) + (Text(e.Event, "kind") == "child" ? 1 : 0);
            Assert.True(int.Parse(date[..4]) <= lastYear, $"{e.Id}: {e.Event}");
        });
    }

    [Fact]
    public void Portraits_are_file_names_and_no_picture_is_kept()
    {
        var portraits = Records.Values.Select(r => Text(r, "portrait")).OfType<string>().ToList();
        Assert.InRange(portraits.Count, 1200, Records.Count);
        Assert.All(portraits, name =>
        {
            Assert.DoesNotContain("/", name);
            Assert.Matches(@"\.(jpe?g|png|gif|svg|tiff?|webp)$", name.ToLowerInvariant());
        });
        Assert.Equal(["connections.json", "figures.json"],
            Directory.GetFiles(Repo.Data("records")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    // A search for what someone was born as has to find them, so the name has to be readable.
    [Fact]
    public void Birth_names_are_in_the_Latin_alphabet_and_the_rest_are_other_names()
    {
        Assert.All(Records.Values.Select(r => Text(r, "birthName")).OfType<string>(), name =>
            Assert.DoesNotMatch(@"[\p{IsCyrillic}\p{IsGreek}\p{IsHebrew}\p{IsArabic}\p{IsCJKUnifiedIdeographs}]", name));
        Assert.Equal("Elvis Aaron Presley", Text(Records["elvis-presley"], "birthName"));
        Assert.Contains("Гарри Кимович Вайнштейн", Records["garry-kasparov"].GetProperty("otherNames").EnumerateArray().Select(n => n.GetString()));
    }

    [Theory]
    [InlineData("ava-gardner", "frank-sinatra", "spouse", null)]
    [InlineData("queen-elizabeth-ii", "charles-iii", "parent", null)]
    [InlineData("edward-vii", "george-v", "parent", null)]
    [InlineData("paul-mccartney", "ringo-starr", "groupmate", "The Beatles")]
    public void Well_known_connections_are_there(string first, string second, string relation, string? group) =>
        Assert.Contains(Connections, l => Text(l, "first") == first && Text(l, "second") == second &&
                                          Text(l, "relation") == relation && Text(l, "group") == group);

    [Fact]
    public void A_one_off_charity_record_makes_nobody_a_groupmate() =>
        Assert.DoesNotContain(Connections, l => Text(l, "group") == "USA for Africa");

    [Fact]
    public void A_figures_life_is_dated_where_it_is_known()
    {
        var elvis = Records["elvis-presley"].GetProperty("events").EnumerateArray().ToList();
        Assert.Contains(elvis, e => Text(e, "kind") == "marriage" && Text(e, "date") == "1967-05-01" && Text(e, "person") == "Priscilla Presley");
        Assert.Contains(elvis, e => Text(e, "kind") == "child" && Text(e, "date") == "1968-02-01" && Text(e, "person") == "Lisa Marie Presley");
        Assert.Contains(elvis, e => Text(e, "kind") == "divorce" && Text(e, "date") == "1973-10-09");
    }
}
