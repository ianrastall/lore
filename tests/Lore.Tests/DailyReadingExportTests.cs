using System.Text.Json;
using Lore.Models;
using Lore.Services;

namespace Lore.Tests;

// "Lore.exe --daily-reading": the file Jupiter HUD reads. What is checked here is the
// contract: the kind, the fields, and that the reader's saved charts are only ever read.
public sealed class DailyReadingExportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lore-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static readonly DateOnly Day = new(2026, 10, 31);

    // A copy of Lore holding only what the reading needs, and a reader's folder.
    private (string Base, string User) Stage()
    {
        string stage = Path.Combine(_dir, "stage"), user = Path.Combine(_dir, "user");
        string ephemeris = Path.Combine(stage, "Assets", "Ephemeris");
        Directory.CreateDirectory(ephemeris);
        Directory.CreateDirectory(Path.Combine(stage, "Data"));
        Directory.CreateDirectory(user);
        foreach (string file in Directory.GetFiles(Repo.Ephemeris))
            File.Copy(file, Path.Combine(ephemeris, Path.GetFileName(file)));
        File.Copy(Repo.Data("daily.json"), Path.Combine(stage, "Data", "daily.json"));
        File.WriteAllText(Path.Combine(user, "mycharts.json"), JsonSerializer.Serialize(new List<Celebrity> { Demo.Person() }));
        return (stage, user);
    }

    [Fact]
    public void The_command_line_names_a_chart_and_a_file_or_it_is_not_this_command()
    {
        var request = DailyReadingExport.Request.Parse(["--daily-reading", "Demo Person", @"C:\out.json"]);
        Assert.Equal(new DailyReadingExport.Request("Demo Person", @"C:\out.json"), request);
        Assert.Null(DailyReadingExport.Request.Parse(["--daily-reading", "Demo Person"]));
        Assert.Null(DailyReadingExport.Request.Parse([]));
    }

    [Fact]
    public void The_reading_is_written_in_the_form_the_HUD_reads()
    {
        var (stage, user) = Stage();
        string output = Path.Combine(_dir, "out", "reading.json");

        DailyReadingExport.Run(stage, new("Demo Person", output), user, Day, NodaTime.DateTimeZone.Utc);

        using var json = JsonDocument.Parse(File.ReadAllText(output));
        var root = json.RootElement;
        Assert.Equal("lore.daily.v1", root.GetProperty("kind").GetString());
        Assert.Equal("Demo Person", root.GetProperty("name").GetString());
        Assert.Equal("2026-10-31", root.GetProperty("date").GetString());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("dateText").GetString()));
        Assert.True(Enum.IsDefined(Enum.Parse<DayTone>(root.GetProperty("tone").GetString()!)));
        Assert.StartsWith("#", root.GetProperty("toneColorHex").GetString());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("summary").GetString()));
        foreach (string list in new[] { "highlights", "themes", "skyNotes" })
            Assert.Equal(JsonValueKind.Array, root.GetProperty(list).ValueKind);

        // Nothing left behind beside it.
        Assert.Equal(["reading.json"], Directory.GetFiles(Path.GetDirectoryName(output)!).Select(Path.GetFileName));
    }

    [Fact]
    public void Saved_charts_are_read_and_not_written()
    {
        var (stage, user) = Stage();
        string charts = Path.Combine(user, "mycharts.json");
        byte[] before = File.ReadAllBytes(charts);

        DailyReadingExport.Run(stage, new("Demo Person", Path.Combine(_dir, "reading.json")), user, Day, NodaTime.DateTimeZone.Utc);

        Assert.Equal(before, File.ReadAllBytes(charts));
        Assert.Equal(["mycharts.json"], Directory.GetFiles(user).Select(Path.GetFileName));
    }

    [Fact]
    public void A_name_that_is_not_in_My_Charts_is_refused_and_no_file_is_written()
    {
        var (stage, user) = Stage();
        string output = Path.Combine(_dir, "reading.json");

        var problem = Assert.Throws<InvalidOperationException>(() =>
            DailyReadingExport.Run(stage, new("Nobody", output), user, Day, NodaTime.DateTimeZone.Utc));

        Assert.Contains("Nobody", problem.Message);
        Assert.False(File.Exists(output));
    }

}
