using System.Diagnostics;
using Lore.Models;
using Lore.Services;
using Lore.ViewModels;

namespace Lore.Tests;

// The file of inventory answers under pressure: several Lore windows saving at the same
// moment, a save that cannot be made, and a file found damaged. Each store here stands
// for one window; the lock file that makes them take turns is an ordinary file handle,
// which Windows keeps exclusive between processes exactly as it does between two
// handles in one, and one test holds it from a second process to show as much.
public sealed class InventoryStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lore-tests-" + Guid.NewGuid().ToString("N"));

    public InventoryStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string FilePath => Path.Combine(_dir, "inventories.json");
    private string LockPath => FilePath + ".lock";

    private static InventorySitting Sitting(int answer, bool done = false) => new()
    {
        Started = "2026-10-10", Completed = done ? "2026-10-11" : null, Answers = Enumerable.Repeat(answer, 120).ToArray(),
    };

    private InventoryStore Window(int lockWaitMs = 5000) => new(_dir) { LockWait = TimeSpan.FromMilliseconds(lockWaitMs) };

    // Another window in the middle of a save: the lock file, held and shared with nobody.
    private FileStream HoldLock() =>
        new(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);

    // ── Several windows at once ───────────────────────────────────────────────

    [Fact]
    public async Task Eight_windows_saving_for_eight_people_at_the_same_moment_lose_nobodys_answers()
    {
        for (int trial = 0; trial < 8; trial++)
        {
            File.Delete(FilePath);
            File.Delete(FilePath + ".bak");
            var windows = Enumerable.Range(0, 8).Select(_ => Window()).ToList();   // all opened on the same, empty, file
            using var go = new ManualResetEventSlim();

            // Each answers five times running, as a person clicking through a page does.
            var saving = windows.Select((window, n) => Task.Run(async () =>
            {
                go.Wait();
                Task<bool> last = Task.FromResult(true);
                for (int answer = 1; answer <= 5; answer++) last = window.SaveAsync($"user-{n}", [Sitting(answer)]);
                return await last;
            })).ToList();
            go.Set();

            Assert.All(await Task.WhenAll(saving), Assert.True);
            Assert.All(windows, w => Assert.True(w.Flush()));
            Assert.All(windows, w => Assert.Equal(InventorySaveState.Saved, w.State));

            var after = new InventoryStore(_dir);
            for (int n = 0; n < 8; n++)
                Assert.Equal(5, Assert.Single(after.Get($"user-{n}")).Answers[0]);
            Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
            Assert.Empty(Directory.GetFiles(_dir, "inventories.unreadable-*"));
        }
    }

    [Fact]
    public async Task One_window_deleting_a_persons_answers_while_another_saves_someone_elses_does_both()
    {
        var first = Window();
        await first.SaveAsync("user-1", [Sitting(1, done: true)]);
        await first.SaveAsync("user-2", [Sitting(2)]);
        var second = Window();

        for (int trial = 0; trial < 10; trial++)
        {
            await first.SaveAsync("user-1", [Sitting(1, done: true)]);
            var deleting = Task.Run(() => second.RemoveAsync("user-1"));
            var editing = Task.Run(() => first.SaveAsync("user-2", [Sitting(3 + trial % 3)]));
            Assert.True(await deleting);
            Assert.True(await editing);

            var after = new InventoryStore(_dir);
            Assert.Empty(after.Get("user-1"));
            Assert.Equal(3 + trial % 3, Assert.Single(after.Get("user-2")).Answers[0]);
        }
    }

    [Fact]
    public async Task Two_windows_at_odds_over_one_person_leave_one_of_the_two_and_everyone_else_untouched()
    {
        var first = Window();
        await first.SaveAsync("user-9", [Sitting(4, done: true)]);   // a bystander
        var second = Window();

        for (int trial = 0; trial < 10; trial++)
        {
            await first.SaveAsync("user-1", [Sitting(1)]);
            var deleting = Task.Run(() => first.RemoveAsync("user-1"));
            var editing = Task.Run(() => second.SaveAsync("user-1", [Sitting(5)]));
            Assert.True(await deleting);
            Assert.True(await editing);

            // Whichever came second stands; the file is whole either way.
            var after = new InventoryStore(_dir);
            Assert.True(after.IsReadable);
            var left = after.Get("user-1");
            Assert.True(left.Count == 0 || left.Single().Answers[0] == 5);
            Assert.Equal(4, Assert.Single(after.Get("user-9")).Answers[0]);
        }
    }

    [Fact]
    public async Task A_save_waits_for_a_second_process_holding_the_lock_and_then_goes_through()
    {
        // A real second process (as a second Lore window is), holding the lock file the
        // way a save does until it is told to let go.
        string held = Path.Combine(_dir, "held.flag"), release = Path.Combine(_dir, "release.flag");
        string script =
            $"$f = [System.IO.File]::Open('{LockPath}', 'OpenOrCreate', 'ReadWrite', 'None'); " +
            $"Set-Content -LiteralPath '{held}' -Value 1; " +
            $"for ($i = 0; $i -lt 300 -and -not (Test-Path -LiteralPath '{release}'); $i++) {{ Start-Sleep -Milliseconds 100 }}; " +
            "$f.Close()";
        using var other = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -Command \"{script}\"")
        {
            UseShellExecute = false, CreateNoWindow = true,
        })!;
        try
        {
            for (int i = 0; !File.Exists(held); i++)
            {
                Assert.True(i < 300 && !other.HasExited, "The other process did not take the lock.");
                await Task.Delay(100);
            }

            var store = Window(lockWaitMs: 20_000);
            var saving = store.SaveAsync("user-1", [Sitting(3)]);
            await Task.Delay(700);
            Assert.False(saving.IsCompleted);                        // waiting its turn
            Assert.Equal(InventorySaveState.Pending, store.State);
            Assert.False(File.Exists(FilePath));

            File.WriteAllText(release, "1");
            Assert.True(await saving);
            Assert.Equal(InventorySaveState.Saved, store.State);
            Assert.Single(new InventoryStore(_dir).Get("user-1"));
        }
        finally
        {
            File.WriteAllText(release, "1");
            if (!other.WaitForExit(5000)) other.Kill();
        }
    }

    // ── A save that cannot be made ────────────────────────────────────────────

    [Fact]
    public async Task A_save_that_cannot_get_its_turn_says_so_keeps_the_answers_and_goes_through_when_tried_again()
    {
        var store = Window(lockWaitMs: 150);
        int reports = 0;
        store.StatusChanged += () => Interlocked.Increment(ref reports);

        using (HoldLock())
        {
            Assert.False(await store.SaveAsync("user-1", [Sitting(2)]));
            Assert.Equal(InventorySaveState.Failed, store.State);
            Assert.Contains("Another Lore window", store.Problem);
            Assert.Equal(["user-1"], store.Unsaved);
            Assert.False(store.Flush());
            Assert.False(File.Exists(FilePath));

            // Still what this window shows, and still there to be saved.
            Assert.Equal(2, Assert.Single(store.Get("user-1")).Answers[0]);
        }

        // The next answer takes the unsaved ones with it.
        Assert.True(await store.SaveAsync("user-2", [Sitting(4)]));
        Assert.Equal(InventorySaveState.Saved, store.State);
        Assert.Null(store.Problem);
        Assert.Empty(store.Unsaved);
        Assert.True(reports >= 2);

        var after = new InventoryStore(_dir);
        Assert.Single(after.Get("user-1"));
        Assert.Single(after.Get("user-2"));
    }

    [Fact]
    public async Task Answers_that_could_not_be_written_because_the_file_is_in_use_are_not_reported_as_saved()
    {
        var store = Window();
        Assert.True(await store.SaveAsync("user-1", [Sitting(1)]));

        // Some other program (a backup tool, a virus scanner) has the file to itself.
        using (new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.False(await store.SaveAsync("user-2", [Sitting(2)]));
            Assert.Equal(InventorySaveState.Failed, store.State);
            Assert.Contains("in use by another program", store.Problem);
        }
        Assert.Empty(new InventoryStore(_dir).Get("user-2"));   // as the file has it: not there

        Assert.True(await store.RetryAsync());
        Assert.Equal(InventorySaveState.Saved, store.State);
        Assert.Single(new InventoryStore(_dir).Get("user-2"));
        Assert.Empty(Directory.GetFiles(_dir, "inventories.unreadable-*"));   // in use is not damaged
    }

    [Fact]
    public async Task A_deletion_that_could_not_reach_the_file_is_not_taken_for_done()
    {
        var store = Window(lockWaitMs: 150);
        Assert.True(await store.SaveAsync("user-1", [Sitting(3, done: true)]));

        using (HoldLock())
        {
            Assert.False(await store.RemoveAsync("user-1"));
            Assert.Empty(store.Get("user-1"));                    // gone from this window
            Assert.Equal(InventorySaveState.Failed, store.State);  // and said not to be gone from the file
        }
        Assert.Single(new InventoryStore(_dir).Get("user-1"));

        Assert.True(await store.RetryAsync());
        Assert.Empty(new InventoryStore(_dir).Get("user-1"));
    }

    // ── A damaged file ────────────────────────────────────────────────────────

    [Fact]
    public async Task Damage_found_after_start_up_is_set_aside_and_the_backup_restored_before_anything_is_saved()
    {
        var store = Window();
        await store.SaveAsync("user-1", [Sitting(1, done: true)]);
        await store.SaveAsync("user-2", [Sitting(2)]);          // the file before this save becomes the backup
        var other = Window();
        await other.SaveAsync("user-3", [Sitting(3)]);          // another window's, which `store` has never seen
        Assert.True(File.Exists(FilePath + ".bak"));

        File.WriteAllText(FilePath, "precious but damaged");

        Assert.True(await store.SaveAsync("user-4", [Sitting(4)]));

        // The damaged text is kept, word for word, under a name of its own.
        var kept = Assert.Single(Directory.GetFiles(_dir, "inventories.unreadable-*.json"));
        Assert.Equal("precious but damaged", File.ReadAllText(kept));
        Assert.Contains("backup", store.RecoveryNote);
        Assert.Contains(Path.GetFileName(kept), store.RecoveryNote);

        // The backup (the file as it was one save earlier) is what the save went into.
        var after = new InventoryStore(_dir);
        Assert.Single(after.Get("user-1"));
        Assert.Single(after.Get("user-2"));
        Assert.Single(after.Get("user-4"));
    }

    [Fact]
    public async Task A_damaged_file_that_cannot_be_moved_is_never_written_over()
    {
        var store = Window();
        await store.SaveAsync("user-1", [Sitting(1)]);
        File.WriteAllText(FilePath, "precious but damaged");

        // Something has it open: it can be read but not moved.
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.False(await store.SaveAsync("user-2", [Sitting(2)]));
            Assert.Equal(InventorySaveState.Failed, store.State);
            Assert.Contains("could not be moved", store.Problem);
            Assert.Equal(2, Assert.Single(store.Get("user-2")).Answers[0]);   // kept, to be saved later
        }
        Assert.Equal("precious but damaged", File.ReadAllText(FilePath));
        Assert.Empty(Directory.GetFiles(_dir, "inventories.unreadable-*"));

        // Free again: now it is set aside, and the answers kept meanwhile are saved.
        Assert.True(await store.RetryAsync());
        Assert.Equal("precious but damaged", File.ReadAllText(Assert.Single(Directory.GetFiles(_dir, "inventories.unreadable-*.json"))));
        Assert.Single(new InventoryStore(_dir).Get("user-2"));
    }

    [Fact]
    public async Task A_file_damaged_before_start_up_is_set_aside_and_the_backup_restored()
    {
        var store = Window();
        await store.SaveAsync("user-1", [Sitting(1, done: true)]);
        await store.SaveAsync("user-2", [Sitting(2)]);
        File.WriteAllText(FilePath, "{ \"user-1\": [ null ] }");   // good JSON, not answers

        var opened = new InventoryStore(_dir);
        Assert.True(opened.IsReadable);
        Assert.Single(opened.Get("user-1"));                        // from the backup
        Assert.Contains("backup", opened.RecoveryNote);
        Assert.Single(Directory.GetFiles(_dir, "inventories.unreadable-*.json"));
        Assert.Single(new InventoryStore(_dir).Get("user-1"));      // and put back as the file
    }

    [Fact]
    public async Task A_file_that_can_be_neither_read_nor_moved_at_start_up_stops_the_questionnaire_being_offered()
    {
        File.WriteAllText(FilePath, "precious but damaged");
        var inventory = new InventoryService(Repo.Data("inventory.json"));
        var person = Repo.Charts.Calculate(new Celebrity
        {
            Id = "user-1", Name = "Ann Example", Category = UserChartService.MyChartsCategory,
            BirthDate = "1980-05-17", BirthTime = "14:20", BirthTimeKnown = true,
            BirthPlace = "Leeds, United Kingdom", Latitude = 53.8, Longitude = -1.55, TimeZoneId = "Europe/London",
        });

        InventoryStore store;
        InventoryViewModel vm;
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            store = Window();
            Assert.False(store.IsReadable);
            Assert.Contains("couldn't be read or moved", store.Problem);

            // Nothing is known of what this person has answered, so nothing is begun.
            vm = new InventoryViewModel(inventory, store) { Chart = person };
            Assert.Equal(InventoryStage.Unavailable, vm.Stage);
            Assert.Contains("couldn't be read or moved", vm.UnavailableText);
            vm.BeginCommand.Execute(null);
            Assert.Equal(InventoryStage.Unavailable, vm.Stage);
        }
        Assert.Equal("precious but damaged", File.ReadAllText(FilePath));
        Assert.True(await store.RetryAsync());   // nothing was waiting to be written
        Assert.Equal("precious but damaged", File.ReadAllText(FilePath));
    }

    // ── What the view says ────────────────────────────────────────────────────

    private static NatalChart Own(string name = "Ann Example", string id = "user-1") => Repo.Charts.Calculate(new Celebrity
    {
        Id = id, Name = name, Category = UserChartService.MyChartsCategory,
        BirthDate = "1980-05-17", BirthTime = "14:20", BirthTimeKnown = true,
        BirthPlace = "Leeds, United Kingdom", Latitude = 53.8, Longitude = -1.55, TimeZoneId = "Europe/London",
    });

    [Fact]
    public async Task The_view_says_when_answers_have_not_been_saved_and_stops_saying_so_when_they_have()
    {
        var store = Window(lockWaitMs: 150);
        var vm = new InventoryViewModel(new InventoryService(Repo.Data("inventory.json")), store) { Chart = Own() };
        Assert.False(vm.HasSaveProblem);

        using (HoldLock())
        {
            vm.BeginCommand.Execute(null);
            vm.Questions[0].AnswerIndex = 4;
            Assert.False(store.Flush());
            Assert.True(vm.HasSaveProblem);
            Assert.Contains("Another Lore window", vm.SaveProblem);
            Assert.Contains("not in the file yet", vm.SaveProblem);
            Assert.Equal("1 of 120 answered", vm.ProgressText);   // nothing is taken away from the screen
        }

        await vm.RetrySaveCommand.ExecuteAsync(null);
        Assert.False(vm.HasSaveProblem);
        Assert.Equal(5, Assert.Single(new InventoryStore(_dir).Get("user-1")).Answers[0]);
    }

    [Fact]
    public async Task Deleting_answers_reports_whether_they_have_gone_from_the_file()
    {
        var store = Window(lockWaitMs: 150);
        await store.SaveAsync("user-1", [Sitting(4, done: true)]);
        var vm = new InventoryViewModel(new InventoryService(Repo.Data("inventory.json")), store) { Chart = Own() };
        Assert.Equal(InventoryStage.Profile, vm.Stage);

        using (HoldLock())
        {
            Assert.False(await vm.DeleteAnswersAsync());
            Assert.Equal(InventoryStage.Introduction, vm.Stage);   // gone from the view
            Assert.Contains("anything deleted is still there", vm.SaveProblem);
        }
        Assert.Single(new InventoryStore(_dir).Get("user-1"));

        await vm.RetrySaveCommand.ExecuteAsync(null);
        Assert.False(vm.HasSaveProblem);
        Assert.Empty(new InventoryStore(_dir).Get("user-1"));
    }

    // ── Whose profile it is ───────────────────────────────────────────────────

    [Fact]
    public async Task A_chart_renamed_takes_its_profile_and_its_export_with_it()
    {
        var inventory = new InventoryService(Repo.Data("inventory.json"));
        var store = Window();
        await store.SaveAsync("user-1", [Sitting(4, done: true)]);
        var vm = new InventoryViewModel(inventory, store, new MirrorInterpreter(Repo.Data("mirror.json"))) { Chart = Own("Old Name") };
        Assert.Equal("Old Name", vm.Profile!.Name);

        var renamed = Own("New Name");   // the same chart, saved under another name
        vm.Chart = renamed;

        Assert.Equal("New Name — Personality inventory", vm.Title);
        Assert.Equal("New Name", vm.Profile!.Name);
        Assert.Equal(new DateOnly(2026, 10, 11), vm.Profile.TakenOn);
        Assert.Equal("New Name", vm.Reading!.Name);
        string Text(byte[] bytes) => System.Text.Encoding.UTF8.GetString(bytes).TrimStart('﻿');
        Assert.StartsWith("New Name — Personality profile", Text(vm.ProfileText()));
        Assert.StartsWith("New Name — Personality profile", Text(vm.ExportText(renamed, reading: false)!));
        Assert.StartsWith("New Name — The chart and the answers", Text(vm.ExportText(renamed, reading: true)!));
    }

    [Fact]
    public void A_chart_renamed_half_way_through_a_sitting_keeps_its_place()
    {
        var vm = new InventoryViewModel(new InventoryService(Repo.Data("inventory.json")), Window()) { Chart = Own("Old Name") };
        vm.BeginCommand.Execute(null);
        foreach (var q in vm.Questions) q.AnswerIndex = 2;
        vm.NextCommand.Execute(null);

        vm.Chart = Own("New Name");

        Assert.Equal("New Name — Personality inventory", vm.Title);
        Assert.Equal(InventoryStage.Asking, vm.Stage);
        Assert.Equal("Statements 11 to 20 of 120", vm.PageText);
        Assert.Equal("10 of 120 answered", vm.ProgressText);
    }

    [Fact]
    public async Task An_export_is_of_the_person_it_was_asked_for_whoever_is_selected_afterwards()
    {
        var inventory = new InventoryService(Repo.Data("inventory.json"));
        var store = Window();
        await store.SaveAsync("user-1", [Sitting(4, done: true)]);
        await store.SaveAsync("user-2", [Sitting(2, done: true)]);
        var vm = new InventoryViewModel(inventory, store, new MirrorInterpreter(Repo.Data("mirror.json")));
        var ann = Own("Ann Example");
        var ben = Own("Ben Other", "user-2");
        string Text(byte[] bytes) => System.Text.Encoding.UTF8.GetString(bytes).TrimStart('﻿');

        vm.Chart = ann;
        byte[] profile = vm.ExportText(ann, reading: false)!, reading = vm.ExportText(ann, reading: true)!;

        // The save dialog is open, and meanwhile someone else is picked.
        vm.Chart = ben;
        Assert.StartsWith("Ann Example — Personality profile", Text(profile));
        Assert.StartsWith("Ann Example — The chart and the answers", Text(reading));

        // And nothing can now be exported as Ann's from what is Ben's on screen.
        Assert.Null(vm.ExportText(ann, reading: false));
        Assert.Null(vm.ExportText(ann, reading: true));
        Assert.StartsWith("Ben Other — Personality profile", Text(vm.ExportText(ben, reading: false)!));

        // No profile, no file: never an empty one.
        Assert.True(await vm.DeleteAnswersAsync());
        Assert.Null(vm.ExportText(ben, reading: false));
        Assert.Null(vm.ExportText(ben, reading: true));
    }
}
