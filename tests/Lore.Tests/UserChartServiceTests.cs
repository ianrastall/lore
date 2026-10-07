using Lore.Models;
using Lore.Services;

namespace Lore.Tests;

// The store for the user's own charts: the one file that must never be lost.
public sealed class UserChartServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lore-tests-" + Guid.NewGuid().ToString("N"));
    private string MainFile => Path.Combine(_dir, "mycharts.json");
    private string BackupFile => MainFile + ".bak";

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static Celebrity Chart(string id, string name) => new()
    {
        Id = id, Name = name, Category = UserChartService.MyChartsCategory,
        BirthDate = "1980-05-17", BirthTime = "14:20", BirthTimeKnown = true,
        BirthPlace = "Leeds, United Kingdom", Latitude = 53.8, Longitude = -1.55,
        TimeZoneId = "Europe/London",
    };

    private async Task<UserChartService> Fresh()
    {
        var store = new UserChartService(_dir);
        await store.LoadAsync();
        return store;
    }

    [Fact]
    public async Task Add_edit_and_delete_round_trip_through_the_file()
    {
        var store = await Fresh();
        await store.AddAsync(Chart("user-1", "Ann"));
        await store.AddAsync(Chart("user-2", "Ben"));
        await store.UpdateAsync(Chart("user-1", "Annie"));
        await store.RemoveAsync(Chart("user-2", "Ben"));

        var reloaded = await Fresh();
        var only = Assert.Single(reloaded.Charts);
        Assert.Equal("Annie", only.Name);
        Assert.Null(reloaded.LoadProblem);
    }

    [Fact]
    public async Task Two_windows_open_at_once_do_not_lose_each_others_charts()
    {
        // Both load the same (empty) list, then each saves a chart of its own.
        var first = await Fresh();
        var second = await Fresh();
        await first.AddAsync(Chart("user-1", "Ann"));
        await second.AddAsync(Chart("user-2", "Ben"));
        await first.RemoveAsync(Chart("user-1", "Ann"));

        Assert.Equal(new[] { "Ben" }, first.Charts.Select(c => c.Name));
        Assert.Equal(new[] { "Ben" }, (await Fresh()).Charts.Select(c => c.Name));
    }

    [Fact]
    public async Task Each_save_keeps_the_previous_version_as_a_backup()
    {
        var store = await Fresh();
        await store.AddAsync(Chart("user-1", "Ann"));
        await store.AddAsync(Chart("user-2", "Ben"));

        Assert.True(File.Exists(BackupFile));
        Assert.Contains("Ann", File.ReadAllText(BackupFile));
        Assert.DoesNotContain("Ben", File.ReadAllText(BackupFile));
        Assert.False(File.Exists(MainFile + ".tmp"));
    }

    [Fact]
    public async Task An_unreadable_file_is_kept_aside_and_the_backup_restored()
    {
        var store = await Fresh();
        await store.AddAsync(Chart("user-1", "Ann"));
        await store.AddAsync(Chart("user-2", "Ben"));    // backup now holds Ann only
        File.WriteAllText(MainFile, "{ this is not json");

        var reloaded = await Fresh();
        Assert.Equal("Ann", Assert.Single(reloaded.Charts).Name);
        Assert.NotNull(reloaded.LoadProblem);
        var kept = Assert.Single(Directory.GetFiles(_dir, "mycharts.unreadable-*.json"));
        Assert.Equal("{ this is not json", File.ReadAllText(kept));
    }

    [Fact]
    public async Task An_unreadable_file_with_no_backup_is_never_overwritten()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(MainFile, "damaged but precious");

        var store = await Fresh();
        Assert.Empty(store.Charts);
        Assert.NotNull(store.LoadProblem);

        await store.AddAsync(Chart("user-1", "Ann"));     // a new chart after the problem
        var kept = Assert.Single(Directory.GetFiles(_dir, "mycharts.unreadable-*.json"));
        Assert.Equal("damaged but precious", File.ReadAllText(kept));
    }

    [Fact]
    public async Task A_save_interrupted_between_its_steps_recovers_from_the_backup()
    {
        var store = await Fresh();
        await store.AddAsync(Chart("user-1", "Ann"));
        await store.AddAsync(Chart("user-2", "Ben"));
        File.Delete(MainFile);                            // as if the replace never landed

        var reloaded = await Fresh();
        Assert.Equal("Ann", Assert.Single(reloaded.Charts).Name);
    }

    [Fact]
    public async Task A_failed_save_changes_nothing_in_memory_or_on_disk()
    {
        var store = await Fresh();
        await store.AddAsync(Chart("user-1", "Ann"));
        await store.AddAsync(Chart("user-2", "Ben"));

        // Hold the temporary file open so the next save cannot write it.
        using (new FileStream(MainFile + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => store.RemoveAsync(Chart("user-2", "Ben")));
            await Assert.ThrowsAnyAsync<IOException>(() => store.AddAsync(Chart("user-9", "Zed")));
            await Assert.ThrowsAnyAsync<IOException>(() => store.UpdateAsync(Chart("user-1", "Changed")));
        }
        Assert.Equal(new[] { "Ann", "Ben" }, store.Charts.Select(c => c.Name));

        // A later, successful save must not carry the failed changes with it.
        await store.AddAsync(Chart("user-3", "Cy"));
        Assert.Equal(new[] { "Ann", "Ben", "Cy" }, (await Fresh()).Charts.Select(c => c.Name));
    }

    [Fact]
    public async Task An_exported_copy_can_be_merged_into_another_PCs_charts()
    {
        var home = await Fresh();
        await home.AddAsync(Chart("user-1", "Ann"));
        await home.AddAsync(Chart("user-2", "Ben"));
        string copy = Path.Combine(_dir, "copy.json");
        File.WriteAllBytes(copy, home.ExportBytes());

        // The other PC already has an older Ann and a chart of its own.
        var away = new UserChartService(Path.Combine(_dir, "away"));
        await away.LoadAsync();
        await away.AddAsync(Chart("user-1", "Ann (old)"));
        await away.AddAsync(Chart("user-7", "Gus"));

        var result = await away.ImportAsync(copy);

        Assert.Equal(new UserChartService.ImportResult(Added: 1, Replaced: 1, Skipped: 0), result);
        Assert.Equal(new[] { "Ann", "Gus", "Ben" }, away.Charts.Select(c => c.Name));
        Assert.True(File.Exists(Path.Combine(_dir, "away", "mycharts.json.bak")));
    }

    [Fact]
    public async Task Importing_leaves_out_entries_that_are_not_someones_own_chart()
    {
        var store = await Fresh();
        string file = Path.Combine(_dir, "mixed.json");
        File.WriteAllText(file, """
            [{"id":"user-1","name":"Ann","category":"My Charts","birthDate":"1980-05-17","latitude":53.8,"longitude":-1.55},
             {"id":"elvis-presley","name":"Elvis Presley","category":"Musicians","birthDate":"1935-01-08","latitude":34.26,"longitude":-88.7}]
            """);

        var result = await store.ImportAsync(file);

        Assert.Equal(new UserChartService.ImportResult(Added: 1, Replaced: 0, Skipped: 1), result);
        Assert.Equal("Ann", Assert.Single(store.Charts).Name);
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("[{}]")]
    public async Task Importing_a_file_that_is_not_a_chart_list_changes_nothing(string contents)
    {
        var store = await Fresh();
        await store.AddAsync(Chart("user-1", "Ann"));
        string before = File.ReadAllText(MainFile);
        string file = Path.Combine(_dir, "bad.json");
        File.WriteAllText(file, contents);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync(file));

        Assert.Equal("Ann", Assert.Single(store.Charts).Name);
        Assert.Equal(before, File.ReadAllText(MainFile));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[{}]")]
    [InlineData("""[{"id":"user-1","name":"Ann","birthDate":"17 May 1980","latitude":53.8,"longitude":-1.55}]""")]
    [InlineData("""[{"id":"user-1","name":"Ann","birthDate":"1980-05-17","latitude":953.8,"longitude":-1.55}]""")]
    public async Task A_file_that_is_valid_JSON_but_not_a_chart_list_is_treated_as_unreadable(string contents)
    {
        var store = await Fresh();
        await store.AddAsync(Chart("user-1", "Ann"));
        await store.AddAsync(Chart("user-2", "Ben")); // the backup now holds Ann alone

        File.WriteAllText(MainFile, contents);
        var reloaded = await Fresh();

        Assert.NotNull(reloaded.LoadProblem);
        Assert.Equal(new[] { "Ann" }, reloaded.Charts.Select(c => c.Name)); // restored from the backup
        Assert.Single(Directory.GetFiles(_dir, "mycharts.unreadable-*.json"));
    }
}
