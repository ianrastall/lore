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
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        Assert.False(File.Exists(MainFile + ".lock"));
    }

    [Fact]
    public async Task Two_windows_saving_at_the_same_moment_both_keep_their_charts()
    {
        var seed = await Fresh();
        await seed.AddAsync(Chart("user-0", "Zero"));

        // Twenty charts from each of two windows, all at once: each save must read what
        // the other has just written, not what it loaded.
        var first = await Fresh();
        var second = await Fresh();
        var saves = Enumerable.Range(1, 20).SelectMany(i => new[]
        {
            Task.Run(() => first.AddAsync(Chart($"a-{i}", $"A{i}"))),
            Task.Run(() => second.AddAsync(Chart($"b-{i}", $"B{i}"))),
        });
        await Task.WhenAll(saves);

        var reloaded = await Fresh();
        Assert.Equal(41, reloaded.Charts.Count);
        Assert.Equal(41, reloaded.Charts.Select(c => c.Id).Distinct().Count());
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public async Task A_file_that_goes_bad_after_loading_is_not_saved_over_and_the_backup_is_kept()
    {
        var store = await Fresh();
        await store.AddAsync(Chart("user-1", "Ann"));
        await store.AddAsync(Chart("user-2", "Ben"));    // backup now holds Ann only
        string goodBackup = File.ReadAllText(BackupFile);
        File.WriteAllText(MainFile, "{ damaged after start-up");

        await Assert.ThrowsAsync<InvalidDataException>(() => store.AddAsync(Chart("user-3", "Cy")));

        Assert.Equal("{ damaged after start-up", File.ReadAllText(MainFile));
        Assert.Equal(goodBackup, File.ReadAllText(BackupFile));
        Assert.Equal(new[] { "Ann", "Ben" }, store.Charts.Select(c => c.Name));

        // The next start sets the damaged file aside and restores the backup.
        var restarted = await Fresh();
        Assert.Equal("Ann", Assert.Single(restarted.Charts).Name);
        Assert.Single(Directory.GetFiles(_dir, "mycharts.unreadable-*.json"));
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
        var store = new UserChartService(_dir) { LockWait = TimeSpan.FromMilliseconds(100) };
        await store.LoadAsync();
        await store.AddAsync(Chart("user-1", "Ann"));
        await store.AddAsync(Chart("user-2", "Ben"));

        // Hold the lock, as another window in the middle of a save would, so the next
        // save cannot go ahead.
        using (new FileStream(MainFile + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
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
    public async Task A_file_another_program_is_holding_is_not_taken_for_a_damaged_one()
    {
        var store = await Fresh();
        await store.AddAsync(Chart("user-1", "Ann"));
        await store.AddAsync(Chart("user-2", "Ben"));    // backup now holds Ann only
        string main = File.ReadAllText(MainFile), backup = File.ReadAllText(BackupFile);

        // Held as a backup tool might hold it: nobody else may read, though it could
        // still be renamed. Starting up now must not roll back to the backup.
        UserChartService during;
        using (new FileStream(MainFile, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete))
        {
            during = await Fresh();
            Assert.Empty(during.Charts);
            Assert.NotNull(during.LoadProblem);
            await Assert.ThrowsAnyAsync<IOException>(() => during.AddAsync(Chart("user-3", "Cy")));
            await Assert.ThrowsAnyAsync<IOException>(() => during.ExportAsync());
        }

        Assert.Empty(Directory.GetFiles(_dir, "mycharts.unreadable-*.json"));
        Assert.Equal(main, File.ReadAllText(MainFile));
        Assert.Equal(backup, File.ReadAllText(BackupFile));

        // Once the file is free again a save works, and loses nothing.
        await during.AddAsync(Chart("user-3", "Cy"));
        Assert.Equal(new[] { "Ann", "Ben", "Cy" }, (await Fresh()).Charts.Select(c => c.Name));
    }

    [Fact]
    public async Task A_damaged_file_is_not_set_aside_while_another_window_holds_the_lock()
    {
        var store = await Fresh();
        await store.AddAsync(Chart("user-1", "Ann"));
        await store.AddAsync(Chart("user-2", "Ben"));    // backup now holds Ann only
        File.WriteAllText(MainFile, "{ this is not json");

        using (new FileStream(MainFile + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var waiting = new UserChartService(_dir) { LockWait = TimeSpan.FromMilliseconds(100) };
            await waiting.LoadAsync();
            Assert.Empty(waiting.Charts);
            Assert.NotNull(waiting.LoadProblem);
            Assert.Empty(Directory.GetFiles(_dir, "mycharts.unreadable-*.json"));
            Assert.Equal("{ this is not json", File.ReadAllText(MainFile));
        }

        // With the lock free, the next start does the recovery.
        Assert.Equal("Ann", Assert.Single((await Fresh()).Charts).Name);
        Assert.Single(Directory.GetFiles(_dir, "mycharts.unreadable-*.json"));
    }

    [Fact]
    public async Task An_export_includes_charts_saved_in_another_window()
    {
        var first = await Fresh();
        await first.AddAsync(Chart("user-1", "Ann"));
        var second = await Fresh();
        await first.AddAsync(Chart("user-2", "Ben"));    // after the second window loaded

        var (bytes, count) = await second.ExportAsync();

        Assert.Equal(2, count);
        string copy = Path.Combine(_dir, "copy.json");
        File.WriteAllBytes(copy, bytes);
        var away = new UserChartService(Path.Combine(_dir, "away"));
        await away.LoadAsync();
        await away.ImportAsync(copy);
        Assert.Equal(new[] { "Ann", "Ben" }, away.Charts.Select(c => c.Name));
    }

    [Fact]
    public async Task An_exported_copy_can_be_merged_into_another_PCs_charts()
    {
        var home = await Fresh();
        await home.AddAsync(Chart("user-1", "Ann"));
        await home.AddAsync(Chart("user-2", "Ben"));
        string copy = Path.Combine(_dir, "copy.json");
        File.WriteAllBytes(copy, (await home.ExportAsync()).Bytes);

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
    // No place at all is read as an empty one.
    [InlineData("""[{"id":"user-1","name":"Ann","category":"My Charts","birthDate":"1980-05-17","birthPlace":null,"bio":null,"latitude":53.8,"longitude":-1.55}]""")]
    public async Task Missing_optional_text_is_read_as_empty(string contents)
    {
        var store = await Fresh();
        string file = Path.Combine(_dir, "sparse.json");
        File.WriteAllText(file, contents);

        await store.ImportAsync(file);

        var ann = Assert.Single(store.Charts);
        Assert.Equal("", ann.BirthPlace);
        Assert.Equal("", ann.Bio);
    }

    [Theory]
    // Said to have a known time, but none given: it would be drawn for noon.
    [InlineData("""[{"id":"user-1","name":"Ann","category":"My Charts","birthDate":"1980-05-17","birthTimeKnown":true,"latitude":53.8,"longitude":-1.55}]""")]
    // A clock no place has ever kept.
    [InlineData("""[{"id":"user-1","name":"Ann","category":"My Charts","birthDate":"1980-05-17","utcOffsetHours":1e300,"utcOffsetFixed":true,"latitude":53.8,"longitude":-1.55}]""")]
    [InlineData("""[{"id":"user-1","name":"Ann","category":"My Charts","birthDate":"1980-05-17","utcOffsetHours":40,"latitude":53.8,"longitude":-1.55}]""")]
    // No date.
    [InlineData("""[{"id":"user-1","name":"Ann","category":"My Charts","birthDate":null,"latitude":53.8,"longitude":-1.55}]""")]
    [InlineData("""[{"id":"user-1","name":"Ann","category":"My Charts","birthDate":"1980-05-17","birthTime":"25:99","latitude":53.8,"longitude":-1.55}]""")]
    public async Task Importing_a_chart_that_could_not_be_calculated_changes_nothing(string contents)
    {
        var store = await Fresh();
        await store.AddAsync(Chart("user-0", "Zero"));
        string before = File.ReadAllText(MainFile);
        string file = Path.Combine(_dir, "bad.json");
        File.WriteAllText(file, contents);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync(file));

        Assert.Equal(before, File.ReadAllText(MainFile));
    }

    [Fact]
    public async Task Importing_a_file_far_too_large_to_be_a_chart_list_is_refused_unread()
    {
        var store = await Fresh();
        string file = Path.Combine(_dir, "huge.json");
        using (var stream = File.Create(file))
            stream.SetLength(UserChartService.MaxImportBytes + 1);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync(file));
        Assert.Contains("too large", ex.Message);
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
