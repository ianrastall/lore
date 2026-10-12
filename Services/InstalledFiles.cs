namespace Lore.Services;

// The files Lore is installed with and cannot do without, from the one list of them
// (required-files.txt, which the release script checks a build against too). Lore looks
// for them when it starts: a file that has gone missing is then named in the status
// bar, instead of showing only as a reading that is quietly empty or switched off.
public static class InstalledFiles
{
    public const string ManifestName = "required-files.txt";

    // Marks a file only a published build has (the bundled runtime, say).
    private const string PublishOnly = "publish:";

    // The paths listed, relative to the folder Lore.exe is in. `published`: include
    // those only a published build has.
    public static IReadOnlyList<string> Read(string manifestPath, bool published)
    {
        var files = new List<string>();
        foreach (string raw in File.ReadAllLines(manifestPath))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith(PublishOnly, StringComparison.OrdinalIgnoreCase))
            {
                if (!published) continue;
                line = line[PublishOnly.Length..].Trim();
            }
            files.Add(line);
        }
        return files;
    }

    // The listed files that are not in `baseDirectory` (or are there and empty). The
    // list itself, if that is what is missing.
    public static IReadOnlyList<string> Missing(string baseDirectory, bool published)
    {
        string manifest = Path.Combine(baseDirectory, ManifestName);
        if (!File.Exists(manifest)) return [ManifestName];
        try
        {
            return Read(manifest, published)
                .Where(f => new FileInfo(Path.Combine(baseDirectory, f)) is not { Exists: true, Length: > 0 })
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diagnostics.Log($"The list of installed files could not be read: {ex.Message}");
            return [ManifestName];
        }
    }

    // For the status bar: which files are missing, or null if none is.
    public static string? Problem(string baseDirectory, bool published)
    {
        var missing = Missing(baseDirectory, published);
        if (missing.Count == 0) return null;
        Diagnostics.Log($"Installed files missing from {baseDirectory}: {string.Join(", ", missing)}");
        string named = string.Join(", ", missing.Take(4)) + (missing.Count > 4 ? $" and {missing.Count - 4} more" : "");
        return $"This copy of Lore is incomplete: {named} {(missing.Count == 1 ? "is" : "are")} missing, so some of it will not work. " +
               "Installing Lore again, or unzipping the portable copy afresh, puts it right; your own charts are kept elsewhere.";
    }
}
