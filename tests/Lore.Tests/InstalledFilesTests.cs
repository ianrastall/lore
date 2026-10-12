using Lore.Models;
using Lore.Services;
using Lore.ViewModels;

namespace Lore.Tests;

// The one list of the files Lore needs at run time (required-files.txt), which the
// release script checks a published build against and Lore checks when it starts; and
// what happens when the text the Report is written from is not there.
public sealed class InstalledFilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lore-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string Manifest => Path.Combine(Repo.Root, InstalledFiles.ManifestName);

    [Fact]
    public void The_list_names_every_data_file_and_every_view_and_each_is_there_to_ship()
    {
        var published = InstalledFiles.Read(Manifest, published: true);
        var development = InstalledFiles.Read(Manifest, published: false);
        Assert.Equal(published.Count, published.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("coreclr.dll", published);
        Assert.DoesNotContain("coreclr.dll", development);   // only a published build carries the runtime
        Assert.Contains(@"Data\interpretations.json", development);

        // Everything in Data\ that Lore reads, and nothing listed that is not in the repository.
        // (A name beginning with a dot is a script's scratch file, not part of Lore.)
        var data = Directory.GetFiles(Path.Combine(Repo.Root, "Data"), "*.json").Select(Path.GetFileName)
            .Where(f => !f!.StartsWith('.')).Select(f => @"Data\" + f).ToList();
        Assert.Equal(data.Order(), published.Where(f => f.StartsWith(@"Data\")).Order());

        // A view that is not listed would not be missed from a build until it was opened.
        var views = Directory.GetFiles(Path.Combine(Repo.Root, "Views"), "*.xaml").Select(f => @"Views\" + Path.GetFileNameWithoutExtension(f) + ".xbf")
            .Concat(Directory.GetFiles(Repo.Root, "*.xaml").Select(f => Path.GetFileNameWithoutExtension(f) + ".xbf")).ToList();
        Assert.Equal(views.Order(), published.Where(f => f.EndsWith(".xbf")).Order());

        // The six ephemeris files the project copies.
        string project = File.ReadAllText(Path.Combine(Repo.Root, "Lore.csproj"));
        foreach (string file in published.Where(f => f.StartsWith(@"Assets\Ephemeris\")))
            Assert.Contains($"<TargetPath>{file}</TargetPath>", project);
    }

    [Fact]
    public void The_project_copies_every_listed_data_file_whether_or_not_it_exists()
    {
        // "Include it if it is there" is how a build came to ship without its Report.
        string project = File.ReadAllText(Path.Combine(Repo.Root, "Lore.csproj"));
        foreach (string file in InstalledFiles.Read(Manifest, published: true).Where(f => f.StartsWith(@"Data\")).Append(InstalledFiles.ManifestName))
            Assert.Contains($"<Content Include=\"{file}\">", project);
        Assert.DoesNotContain("Condition=\"Exists('Data\\", project);
    }

    [Fact]
    public void A_copy_of_Lore_with_a_file_missing_says_which()
    {
        var files = InstalledFiles.Read(Manifest, published: false);
        void Stage()
        {
            foreach (string file in files.Append(InstalledFiles.ManifestName))
            {
                string path = Path.Combine(_dir, file);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (file == InstalledFiles.ManifestName) File.Copy(Manifest, path, overwrite: true);
                else File.WriteAllText(path, "x");
            }
        }

        Assert.Equal([InstalledFiles.ManifestName], InstalledFiles.Missing(_dir, published: false));   // nothing there at all

        Stage();
        Assert.Empty(InstalledFiles.Missing(_dir, published: false));
        Assert.Null(InstalledFiles.Problem(_dir, published: false));
        Assert.Contains("coreclr.dll", InstalledFiles.Missing(_dir, published: true));

        File.Delete(Path.Combine(_dir, "Data", "interpretations.json"));
        File.WriteAllText(Path.Combine(_dir, "Data", "mirror.json"), "");   // there, and empty
        Assert.Equal([@"Data\interpretations.json", @"Data\mirror.json"], InstalledFiles.Missing(_dir, published: false));
        string problem = InstalledFiles.Problem(_dir, published: false)!;
        Assert.Contains(@"Data\interpretations.json, Data\mirror.json are missing", problem);
        Assert.Contains("Installing Lore again", problem);
    }

    [Fact]
    public void Without_its_text_the_Report_says_it_is_unavailable_and_writes_no_stand_in_prose()
    {
        Directory.CreateDirectory(_dir);
        string empty = Path.Combine(_dir, "empty.json");
        File.WriteAllText(empty, "{}");
        var chart = Repo.Charts.Calculate(Demo.Person());

        foreach (var report in new[] { new ChartInterpreter(Path.Combine(_dir, "nothing.json")), new ChartInterpreter(empty) })
        {
            Assert.False(report.IsAvailable);
            var sections = report.Interpret(chart);
            var only = Assert.Single(sections);
            Assert.Equal("The Report is not available", only.Heading);
            Assert.Contains(@"Data\interpretations.json", Assert.Single(only.Paragraphs));
            Assert.DoesNotContain(sections.SelectMany(s => s.Paragraphs), p => p.Contains("in their own way") || p.Contains("this energy"));

            // The view shows that, not a Report; and the chart-and-answers reading does
            // not borrow sentences that are not there.
            var vm = new ChartViewModel(report, Repo.Charts) { Chart = chart };
            Assert.Equal("The Report is not available", Assert.Single(vm.ReportSections).Heading);

            var inventory = new InventoryService(Repo.Data("inventory.json"));
            var profile = inventory.Score(Enumerable.Repeat(4, 120).ToArray(), "Demo Person", new DateOnly(2026, 10, 10));
            var reading = new MirrorInterpreter(Repo.Data("mirror.json"), report).Compose(chart, profile);
            Assert.DoesNotContain(reading.Sections.SelectMany(s => s.Items), i => i.Text.Contains("in its own way"));
        }

        Assert.True(new ChartInterpreter(Repo.Data("interpretations.json")).IsAvailable);
    }

    [Fact]
    public void Every_shipped_corpus_loads_and_is_usable()
    {
        Assert.True(new ChartInterpreter(Repo.Data("interpretations.json")).IsAvailable);
        Assert.True(new InventoryService(Repo.Data("inventory.json")).IsAvailable);
        Assert.True(new MirrorInterpreter(Repo.Data("mirror.json")).IsAvailable);
        // These three throw if their file is damaged, and say so at start-up.
        _ = new DailyInterpreter(Repo.Data("daily.json"));
        _ = new SynastryInterpreter(Repo.Data("synastry.json"));
        _ = new DavisonInterpreter(Repo.Data("davison.json"));
    }
}
