using System.Text.Json.Nodes;
using Lore.Models;
using Lore.Services;

namespace Lore.Tests;

// The Legend as data (Data\legend.json), checked as the other corpora are; and the
// check a copy of Lore makes of itself when started with --self-check.
public sealed class LegendAndSelfCheckTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lore-tests-" + Guid.NewGuid().ToString("N"));

    public LegendAndSelfCheckTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    // ── The Legend ────────────────────────────────────────────────────────────

    [Fact]
    public void The_Legend_is_whole_every_entry_with_its_texts_and_the_aspects_with_their_colours()
    {
        var legend = new LegendService(Repo.Data("legend.json"));
        Assert.True(legend.IsAvailable);
        Assert.Equal(108, legend.Items.Count);
        Assert.Equal("Reading a chart", legend.Items[0].Name);   // what the Legend opens on

        // Everything a reader meets on a chart has an entry.
        var names = legend.Items.Select(i => i.Name).ToHashSet();
        Assert.All(Enum.GetValues<Planet>(), p => Assert.Contains(names, n => n.Contains(p.Name())));   // "Black Moon Lilith"
        Assert.All(Enum.GetValues<ZodiacSign>(), s => Assert.Contains(s.Name(), names));
        Assert.Equal(16, legend.Items.Select(i => i.Group).Distinct().Count());

        // Only the aspects carry a colour, and each of them does.
        var coloured = legend.Items.Where(i => i.ColorHex is not null).ToList();
        Assert.Equal(6, coloured.Count);
        Assert.All(coloured, i => Assert.Equal("Aspects (line colours)", i.Group));

        // A paragraph's worth each, at least.
        Assert.All(legend.Items, i => Assert.True(i.Detail.Length > 80, i.Name));
    }

    public static TheoryData<string, string> Damage => new()
    {
        { "not JSON", "{ not json" },
        { "the whole file null", "null" },
        { "no entries", """{"entries":[]}""" },
        { "entries null", """{"entries":null}""" },
        { "an entry that is null", "entries[3] = null" },
        { "an entry with no name", "entries[3].name = null" },
        { "an entry with no detail", "entries[3].detail = \"  \"" },
        { "an entry with no group", "entries[3].group = \"\"" },
        { "a colour that is not one", "entries[3].color = \"red\"" },
        { "a name given twice in a group", "entries[2].name = \"Sun\"" },
        { "a bold mark with no partner", "entries[3].detail = \"This is **bold and never stops.\"" },
    };

    [Theory]
    [MemberData(nameof(Damage))]
    public void A_damaged_Legend_shows_one_entry_saying_so(string what, string change)
    {
        string path = Path.Combine(_dir, "legend.json");
        if (change.StartsWith("entries["))
        {
            var root = JsonNode.Parse(File.ReadAllText(Repo.Data("legend.json")))!;
            var parts = change.Split(" = ", 2);
            int index = int.Parse(parts[0][8..parts[0].IndexOf(']')]);
            string? field = parts[0].Contains('.') ? parts[0][(parts[0].IndexOf('.') + 1)..] : null;
            if (field is null) root["entries"]![index] = JsonNode.Parse(parts[1]);
            else root["entries"]![index]![field] = JsonNode.Parse(parts[1]);
            File.WriteAllText(path, root.ToJsonString());
        }
        else
        {
            File.WriteAllText(path, change);
        }

        var legend = new LegendService(path);   // must not throw

        Assert.False(legend.IsAvailable, what);
        var only = Assert.Single(legend.Items);
        Assert.Equal("The Legend is not available", only.Name);
        Assert.Contains(@"Data\legend.json", only.Detail);
    }

    [Fact]
    public void A_missing_Legend_is_the_same()
    {
        var legend = new LegendService(Path.Combine(_dir, "nothing.json"));
        Assert.False(legend.IsAvailable);
        Assert.Single(legend.Items);
    }

    // ── The self-check ────────────────────────────────────────────────────────

    // A folder laid out as an installed copy is: the data and the ephemeris from the
    // repository, and a stand-in for each other file on the list.
    private string Stage()
    {
        string stage = Path.Combine(_dir, "stage");
        foreach (string file in InstalledFiles.Read(Path.Combine(Repo.Root, InstalledFiles.ManifestName), published: false)
                     .Append(InstalledFiles.ManifestName))
        {
            string to = Path.Combine(stage, file);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            string from =
                file == InstalledFiles.ManifestName ? Path.Combine(Repo.Root, file) :
                file.StartsWith(@"Data\") ? Path.Combine(Repo.Root, file) :
                file.StartsWith(@"Assets\Ephemeris\") ? Path.Combine(Repo.Ephemeris, Path.GetFileName(file) == "seleapsec.txt" ? @"..\seleapsec.txt" : Path.GetFileName(file)) :
                "";
            if (from.Length > 0) File.Copy(from, to);
            else File.WriteAllText(to, "stand-in");
        }
        return stage;
    }

    [Fact]
    public void A_sound_copy_of_Lore_passes_its_self_check_and_a_damaged_one_says_what_is_wrong()
    {
        string stage = Stage();
        Assert.Empty(SelfCheck.Run(stage, published: false));

        string report = Path.Combine(_dir, "report.txt");
        Assert.Equal(0, SelfCheck.RunAndReport(stage, report));
        Assert.Equal(["OK"], File.ReadAllLines(report));

        // A corpus cut short: there, not empty, and no use.
        string mirror = Path.Combine(stage, "Data", "mirror.json");
        string whole = File.ReadAllText(mirror);
        File.WriteAllText(mirror, whole[..(whole.Length / 2)]);
        Assert.Contains(SelfCheck.Run(stage, published: false), p => p.StartsWith("mirror.json"));
        File.WriteAllText(mirror, whole);

        // The Report's text emptied out, the Legend gone, and an ephemeris file missing.
        File.WriteAllText(Path.Combine(stage, "Data", "interpretations.json"), "{}");
        File.Delete(Path.Combine(stage, "Data", "legend.json"));
        _ = new ChartService(Repo.Ephemeris);   // the library lets go of the staged files it has open
        File.Delete(Path.Combine(stage, "Assets", "Ephemeris", "sepl_18.se1"));
        var problems = SelfCheck.Run(stage, published: false);
        Assert.Contains("interpretations.json: the Report's text is missing or empty", problems);
        Assert.Contains(@"missing or empty: Data\legend.json", problems);
        Assert.Contains("legend.json: the Legend is missing or not whole", problems);
        Assert.Contains(@"missing or empty: Assets\Ephemeris\sepl_18.se1", problems);
        Assert.Contains(problems, p => p.StartsWith("the chart for "));   // calculated from the fallback, and said to be

        Assert.Equal(1, SelfCheck.RunAndReport(stage, report));
        Assert.Equal(problems, File.ReadAllLines(report));

        // The ephemeris path is the test run's again for whatever runs next.
        _ = new ChartService(Repo.Ephemeris);
    }

    [Fact]
    public void The_switch_is_read_from_the_command_line_with_or_without_a_report_file()
    {
        Assert.Null(SelfCheck.ReportPath(["Lore.exe"]));
        Assert.Equal("", SelfCheck.ReportPath(["Lore.exe", "--self-check"]));
        Assert.Equal(@"C:\out.txt", SelfCheck.ReportPath(["Lore.exe", "--self-check", @"C:\out.txt"]));
    }
}
