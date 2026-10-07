namespace Lore.Services;

// Lightweight logger to %TEMP%\lore-crash.txt, shared with the App's
// unhandled-exception handler. Never throws.
//
// The file is kept small: once it passes MaxBytes it becomes lore-crash.old.txt (the
// one before that is dropped) and a new one is begun, so at most two of them exist.
// Entries name a chart by its Id rather than by the person's name where they can.
public static class Diagnostics
{
    private const long MaxBytes = 512 * 1024;

    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "lore-crash.txt");

    private static readonly string OldLogPath =
        Path.Combine(Path.GetTempPath(), "lore-crash.old.txt");

    // One writer at a time: calculations log from several threads, and two appends
    // meeting at the file would lose one of them.
    private static readonly object Gate = new();

    public static string LogFile => LogPath;

    public static void Log(string message)
    {
        try
        {
            lock (Gate)
            {
                var file = new FileInfo(LogPath);
                if (file.Exists && file.Length > MaxBytes)
                    File.Move(LogPath, OldLogPath, overwrite: true);
                File.AppendAllText(LogPath, $"[{DateTime.Now:s}] {message}\n");
            }
        }
        catch { /* diagnostics must never break the app */ }
    }
}
