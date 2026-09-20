namespace Lore.Services;

// Lightweight append-only logger to %TEMP%\lore-crash.txt, shared with the
// App's unhandled-exception handler. Never throws.
public static class Diagnostics
{
    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "lore-crash.txt");

    public static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:s}] {message}\n");
        }
        catch { /* diagnostics must never break the app */ }
    }
}
