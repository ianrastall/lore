namespace Lore.Mobile.Services;

// What Lore reads (the figure library, the cities and hospitals, the reading texts and
// the Swiss Ephemeris files) is packed inside the app, where only a stream can reach it.
// The core reads files by path, so on first start, and after each update of the app,
// they are copied once into the app's private storage. A copy that stopped halfway is
// never used: the files go to a fresh folder, which is renamed into place only when all
// of them are there.
public static class ContentFiles
{
    // Everything under content\ and ephe\ in the app (see Lore.Mobile.csproj).
    private static readonly string[] Content =
    [
        "celebrities.json", "cities.json", "hospitals.json", "interpretations.json", "daily.json", "sky.json",
        "synastry.json", "davison.json", "inventory.json", "reading-variants.json", "character.json",
        "lifeareas.json", "mirror.json", "legend.json",
    ];

    private static readonly string[] Ephemeris =
    [
        "sepl_12.se1", "sepl_18.se1", "semo_12.se1", "semo_18.se1", "seas_12.se1", "seas_18.se1", "seleapsec.txt",
    ];

    public sealed record Folders(string Content, string Ephemeris);

    public static async Task<Folders> EnsureAsync()
    {
        string root = Path.Combine(FileSystem.AppDataDirectory, "bundle");
        string ready = Path.Combine(root, InstallStamp());
        var folders = new Folders(Path.Combine(ready, "content"), Path.Combine(ready, "ephe"));
        if (Directory.Exists(ready)) return folders;

        // Earlier versions' copies, and any copy left half done, are of no further use.
        if (Directory.Exists(root))
            foreach (string old in Directory.GetDirectories(root))
                TryDelete(old);

        string staging = Path.Combine(root, "incoming-" + Guid.NewGuid().ToString("N"));
        await CopyAllAsync(Content, "content", Path.Combine(staging, "content"));
        await CopyAllAsync(Ephemeris, "ephe", Path.Combine(staging, "ephe"));
        Directory.Move(staging, ready);
        return folders;
    }

    private static async Task CopyAllAsync(string[] names, string packedFolder, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string name in names)
        {
            await using Stream source = await FileSystem.OpenAppPackageFileAsync($"{packedFolder}/{name}");
            await using var file = File.Create(Path.Combine(target, name));
            await source.CopyToAsync(file);
        }
    }

    // When this copy of the app was installed or last updated: a new install of the same
    // version number (as during development) still gets fresh files.
    private static string InstallStamp()
    {
#if ANDROID
        var context = Android.App.Application.Context;
        var info = context.PackageManager!.GetPackageInfo(context.PackageName!, 0)!;
        return $"{AppInfo.BuildString}-{info.LastUpdateTime}";
#else
        return AppInfo.BuildString;
#endif
    }

    private static void TryDelete(string folder)
    {
        try { Directory.Delete(folder, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
