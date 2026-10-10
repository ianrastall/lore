namespace Lore.Services;

// Where Lore keeps what belongs to the person using it: their saved charts, their
// settings, their answers to the personality inventory.
internal static class AppFolder
{
    // %LOCALAPPDATA%\Lore. A debug build looks somewhere else instead when LORE_HOME is
    // set, so that the app can be tried with made-up charts and answers and never touch
    // (or show) the real ones. A release build ignores it.
    public static string Path
    {
        get
        {
#if DEBUG
            if (Environment.GetEnvironmentVariable("LORE_HOME") is { Length: > 0 } elsewhere)
                return elsewhere;
#endif
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lore");
        }
    }
}
