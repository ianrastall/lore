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
}
