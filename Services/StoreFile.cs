namespace Lore.Services;

// The two steps every file of the reader's own data is saved with, shared by the stores
// that keep one (saved charts, inventory answers):
//   • a lock file held, shared with nobody, for the whole of read, change and write
//     back, so two Lore windows (which are two processes) take turns;
//   • the new version written completely to a temporary file and only then put in the
//     place of the old one, which is kept as the backup.
public static class StoreFile
{
    // Holds `lockPath` open until disposed. Null if something else kept it for longer
    // than `wait` (or the folder cannot be written to).
    public static FileStream? TryLock(string lockPath, TimeSpan wait)
    {
        var giveUp = DateTime.UtcNow + wait;
        while (true)
        {
            if (TryOpen(lockPath, out var held, out var problem)) return held;
            if (DateTime.UtcNow >= giveUp)
            {
                Diagnostics.Log($"Could not take {lockPath}: {problem}");
                return null;
            }
            Thread.Sleep(50);
        }
    }

    public static async Task<FileStream?> TryLockAsync(string lockPath, TimeSpan wait)
    {
        var giveUp = DateTime.UtcNow + wait;
        while (true)
        {
            if (TryOpen(lockPath, out var held, out var problem)) return held;
            if (DateTime.UtcNow >= giveUp)
            {
                Diagnostics.Log($"Could not take {lockPath}: {problem}");
                return null;
            }
            await Task.Delay(50);
        }
    }

    private static bool TryOpen(string lockPath, out FileStream? held, out string problem)
    {
        try
        {
            held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.None, 1, FileOptions.DeleteOnClose);
            problem = "";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            held = null;
            problem = ex.Message;
            return false;
        }
    }

    // A name of its own beside `path`, so nothing else can be holding or half-writing it.
    public static string TempPath(string path) => $"{path}.{Guid.NewGuid():N}.tmp";

    // Puts the finished temporary file in the place of `path`, keeping the version it
    // replaces as `backupPath`. Call holding the lock.
    public static void Replace(string temp, string path, string backupPath)
    {
        if (!File.Exists(path))
        {
            File.Move(temp, path);
            return;
        }

        try
        {
            // One step on NTFS: the new file takes the name, the old one becomes the backup.
            File.Replace(temp, path, backupPath);
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            File.Copy(path, backupPath, overwrite: true);
            File.Move(temp, path, overwrite: true);
        }
    }

    // Still there only if the save failed part-way.
    public static void DeleteTemp(string temp)
    {
        try { File.Delete(temp); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
