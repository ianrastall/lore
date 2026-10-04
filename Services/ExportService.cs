using System.Text;
using System.Text.Json;
using System.Xml.Serialization;
using Lore.Models;
using Lore.Views;
using Microsoft.Graphics.Canvas;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Windows.Storage.Streams;

namespace Lore.Services;

// Produces exportable artifacts from a computed NatalChart: a rendered PNG of the
// chart wheel, structured JSON/XML data, and a formatted PDF "reading" (birthdata
// + wheel image + the written report). Returns bytes; the caller writes the file.
public static class ExportService
{
    static ExportService()
    {
        // QuestPDF Community licence (free for individuals / <$1M revenue).
        QuestPDF.Settings.License = LicenseType.Community;
    }

    // ── PNG (Win2D offscreen render, no on-screen control needed) ─────────────
    public static Task<byte[]> RenderChartPngAsync(NatalChart chart, int size = 1600) =>
        RenderPngAsync(size, ds => ChartRenderer.Draw(ds, chart, size, size));

    // The synastry bi-wheel: the first chart inside, the second around it.
    public static Task<byte[]> RenderBiWheelPngAsync(Synastry synastry, int size = 1600) =>
        RenderPngAsync(size, ds => ChartRenderer.DrawBiWheel(ds, synastry, size, size));

    private static async Task<byte[]> RenderPngAsync(int size, Action<CanvasDrawingSession> draw)
    {
        var device = CanvasDevice.GetSharedDevice();
        using var target = new CanvasRenderTarget(device, size, size, 96);
        using (var ds = target.CreateDrawingSession())
        {
            draw(ds);
        }

        using var stream = new InMemoryRandomAccessStream();
        await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);

        var bytes = new byte[stream.Size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }

    // ── JSON / XML (structured chart data) ────────────────────────────────────
    public static byte[] ToJson(NatalChart chart)
    {
        var dto = BuildDto(chart);
        var opts = new JsonSerializerOptions { WriteIndented = true };
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(dto, opts));
    }

    public static byte[] ToXml(NatalChart chart)
    {
        var dto = BuildDto(chart);
        var serializer = new XmlSerializer(typeof(ChartExport));
        using var ms = new MemoryStream();
        using (var writer = new StreamWriter(ms, new UTF8Encoding(false)))
        {
            serializer.Serialize(writer, dto);
        }
        return ms.ToArray();
    }

    // ── PDF (full reading) ────────────────────────────────────────────────────
    // `worksheet` and `sensitivity`, when given, add the numbers behind the chart after
    // the reading: how it was calculated, the positions, cusps and aspects, and what
    // would change if the birth time were off.
    public static byte[] ToPdf(NatalChart chart, IReadOnlyList<ReportSection> report, byte[] chartPng,
        Worksheet? worksheet = null, TimeSensitivity? sensitivity = null)
    {
        var vm = chart.Celebrity;
        string subtitle = $"{vm.BirthDateLabel}  ·  {vm.BirthPlace}" +
            (vm.BirthTimeKnown ? $"  ·  {vm.BirthTime}" : "  ·  time unknown") +
            (string.IsNullOrWhiteSpace(vm.RoddenRating) ? "" : $"  ·  Rodden {vm.RoddenRating}");
        // Lead with the "Big Three" (Sun first — that's the everyday "sign"); keep the
        // Ascendant/Midheaven as a small, clearly-labelled technical line so the MC can't
        // be mistaken for someone's sign.
        string rising = ZodiacSignExtensions.FromLongitude(chart.Ascendant).Name();
        var bigParts = new List<string>();
        if (chart.GetPlanet(Planet.Sun) is { } sunP)  bigParts.Add($"☉ Sun {sunP.Sign.Name()}");
        // With no birth time the Moon may have changed sign during the day: name both.
        if (!chart.Timed && sensitivity is { MoonSigns.Count: > 1 })
            bigParts.Add($"☽ Moon {string.Join(" or ", sensitivity.MoonSigns.Select(s => s.Name()))}");
        else if (chart.GetPlanet(Planet.Moon) is { } moonP) bigParts.Add($"☽ Moon {moonP.Sign.Name()}");
        if (chart.Timed) bigParts.Add($"↑ Rising {rising}");
        string bigThree = string.Join("     ·     ", bigParts);
        string angles = ViewModels.ChartViewModel.FormatAngles(chart);

        // No dignity score without a birth time (see DignityService.ComputeIfTimed).
        var score = DignityService.ComputeIfTimed(chart);
        string verdictHex = score?.Verdict.ColorHex() ?? ChartVerdict.Ordinary.ColorHex();
        string verdictLine = score is null
            ? "Chart assessment:  not scored — it needs a birth time"
            : $"Chart assessment:  {score.Total:+#;-#;0}  —  {score.Verdict.Label()}";

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                // Segoe UI covers the text; the trailing families are a fallback chain
                // supplying the astrological glyphs (☉ ♈ △ …) the primary font lacks.
                page.DefaultTextStyle(x => x.FontSize(11)
                    .FontFamily("Segoe UI", "Segoe UI Symbol", "Segoe UI Historic"));

                page.Header().Column(col =>
                {
                    col.Item().Text(vm.Name).FontSize(22).SemiBold();
                    col.Item().Text(subtitle).FontSize(11).FontColor(Colors.Grey.Darken2);
                });

                page.Content().PaddingVertical(12).Column(col =>
                {
                    col.Spacing(8);

                    if (chartPng is { Length: > 0 })
                        col.Item().AlignCenter().Width(340).Image(chartPng);

                    col.Item().AlignCenter().Text(bigThree).FontSize(13).SemiBold();
                    col.Item().AlignCenter().Text(angles)
                        .FontSize(9).FontColor(Colors.Grey.Darken1);
                    col.Item().AlignCenter().Text(verdictLine)
                        .FontSize(12).SemiBold().FontColor(verdictHex);

                    foreach (var section in report)
                    {
                        col.Item().PaddingTop(8).Text(section.Heading).FontSize(15).SemiBold();
                        foreach (var para in section.Paragraphs)
                            col.Item().Text(para).FontSize(11).LineHeight(1.35f);
                    }

                    if (score is not null)
                        ChartAssessment(col, score);

                    if (worksheet is not null)
                        WorksheetPdf.Compose(col, worksheet, sensitivity);
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Generated by Lore · ").FontSize(9).FontColor(Colors.Grey.Medium);
                    x.Span(DateTime.Now.ToString("yyyy-MM-dd")).FontSize(9).FontColor(Colors.Grey.Medium);
                });
            });
        });

        return doc.GeneratePdf();
    }

    // Traditional dignity breakdown: the verdict, a plain-language note, and a per-planet
    // table showing where each planet's points came from.
    private static void ChartAssessment(QuestPDF.Fluent.ColumnDescriptor col, ChartScore score)
    {
        col.Item().PaddingTop(10).Text("Chart Assessment").FontSize(15).SemiBold();
        col.Item().Text(
            "A traditional dignity score for the seven classical planets (Sun–Saturn): essential " +
            "dignity by sign and degree, plus accidental dignity by house, motion, and closeness to " +
            "the Sun. Higher means more traditionally dignified. Most charts fall between roughly +15 " +
            "and +35; the bands flag the strongest (Extraordinary) and most afflicted (Alarming).")
            .FontSize(9).FontColor(Colors.Grey.Darken1).LineHeight(1.3f);

        col.Item().PaddingTop(2).Text(t =>
        {
            t.Span($"Total {score.Total:+#;-#;0} — {score.Verdict.Label()}")
                .SemiBold().FontColor(score.Verdict.ColorHex());
            t.Span($"   ({(score.IsDayChart ? "day chart" : "night chart")})")
                .FontSize(9).FontColor(Colors.Grey.Medium);
        });

        col.Item().PaddingTop(4).Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(3);    // planet
                c.RelativeColumn(1.5f); // essential
                c.RelativeColumn(1.6f); // accidental
                c.RelativeColumn(1.2f); // total
                c.RelativeColumn(6);    // notes
            });

            static IContainer Head(IContainer c) =>
                c.PaddingVertical(2).BorderBottom(1).BorderColor(Colors.Grey.Lighten1);

            table.Header(h =>
            {
                h.Cell().Element(Head).Text("Planet").SemiBold().FontSize(9);
                h.Cell().Element(Head).AlignRight().Text("Essential").SemiBold().FontSize(9);
                h.Cell().Element(Head).AlignRight().Text("Accidental").SemiBold().FontSize(9);
                h.Cell().Element(Head).AlignRight().Text("Total").SemiBold().FontSize(9);
                h.Cell().Element(Head).Text("Why").SemiBold().FontSize(9);
            });

            foreach (var pd in score.Planets)
            {
                table.Cell().PaddingVertical(1).Text($"{pd.Planet.Symbol()} {pd.Planet.Name()}").FontSize(9);
                table.Cell().PaddingVertical(1).AlignRight().Text($"{pd.Essential:+#;-#;0}").FontSize(9);
                table.Cell().PaddingVertical(1).AlignRight().Text($"{pd.Accidental:+#;-#;0}").FontSize(9);
                table.Cell().PaddingVertical(1).AlignRight().Text($"{pd.Total:+#;-#;0}").SemiBold().FontSize(9);
                table.Cell().PaddingVertical(1).Text(pd.Notes).FontSize(8).FontColor(Colors.Grey.Darken1);
            }
        });
    }

    // ── DTO ───────────────────────────────────────────────────────────────────
    private static ChartExport BuildDto(NatalChart chart)
    {
        var c = chart.Celebrity;
        return new ChartExport
        {
            GeneratedBy = "Lore",
            GeneratedAt = DateTime.Now.ToString("s"),
            Name = c.Name,
            Category = c.Category,
            BirthDate = c.BirthDate,
            Calendar = c.JulianCalendar ? "Julian (Old Style)" : "Gregorian",
            GregorianBirthDate = BirthTimeResolver.GregorianDate(c).ToString("yyyy-MM-dd"),
            BirthTime = c.BirthTime,
            BirthTimeKnown = c.BirthTimeKnown,
            BirthTimeUncertaintyMinutes = c.BirthTimeUncertaintyMinutes,
            RoddenRating = c.RoddenRating ?? "",
            Source = c.Source ?? "",
            BirthPlace = c.BirthPlace,
            Latitude = c.Latitude,
            Longitude = c.Longitude,
            UtcOffsetHours = c.UtcOffsetHours,
            HouseSystem = chart.HouseSystemLabel,
            NodeType = chart.Settings.Node.Name(),
            // The angles and houses are left out, not guessed, when there is no birth time.
            Ascendant = chart.Timed ? Round(chart.Ascendant) : null,
            AscendantSign = chart.Timed ? ZodiacSignExtensions.FromLongitude(chart.Ascendant).Name() : "",
            Midheaven = chart.Timed ? Round(chart.Midheaven) : null,
            MidheavenSign = chart.Timed ? ZodiacSignExtensions.FromLongitude(chart.Midheaven).Name() : "",
            Planets = chart.Planets.Select(p => new PlanetExport
            {
                Name = p.PlanetName,
                Sign = p.Sign.Name(),
                DegreeInSign = Round(p.DegreeInSign),
                Longitude = Round(p.Longitude),
                Latitude = Round(p.Latitude),
                Declination = Round(p.Declination),
                SpeedPerDay = Round(p.SpeedLongitude),
                House = chart.Timed ? chart.GetHouseForLongitude(p.Longitude) : null,
                Retrograde = p.IsRetrograde
            }).ToList(),
            Houses = (chart.Timed ? chart.Houses : []).Select(h => new HouseExport
            {
                House = h.House,
                Longitude = Round(h.Longitude),
                Sign = h.Sign.Name(),
                DegreeInSign = Round(h.DegreeInSign)
            }).ToList(),
            Aspects = chart.Aspects.Select(a => new AspectExport
            {
                PlanetA = a.PlanetA.Name(),
                PlanetB = a.PlanetB.Name(),
                Type = a.Type.ToString(),
                ExactAngle = a.Type.Angle(),
                Orb = Round(a.Orb),
                Applying = a.IsApplying
            }).ToList(),
            Patterns = AspectPatternService.Detect(chart).Select(p => new PatternExport
            {
                Type = p.Type.ToString(),
                Planets = p.Planets.Select(x => x.Name()).ToList(),
                Sign = p.Sign.HasValue ? p.Sign.Value.Name() : "",
                Element = p.Element.HasValue ? p.Element.Value.ToString() : "",
                Modality = p.Modality.HasValue ? p.Modality.Value.ToString() : "",
                Apex = p.Apex.HasValue ? p.Apex.Value.Name() : ""
            }).ToList()
        };
    }

    private static double Round(double v) => Math.Round(v, 4);
}

// XmlSerializer requires public parameterless types with get/set members.
public sealed class ChartExport
{
    public string GeneratedBy { get; set; } = "";
    public string GeneratedAt { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string BirthDate { get; set; } = "";
    public string Calendar { get; set; } = "";
    public string GregorianBirthDate { get; set; } = "";
    public string? BirthTime { get; set; }
    public bool BirthTimeKnown { get; set; }
    public int BirthTimeUncertaintyMinutes { get; set; }
    public string RoddenRating { get; set; } = "";
    public string Source { get; set; } = "";
    public string BirthPlace { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double UtcOffsetHours { get; set; }
    public string HouseSystem { get; set; } = "";
    public string NodeType { get; set; } = "";
    public double? Ascendant { get; set; }   // null without a birth time
    public string AscendantSign { get; set; } = "";
    public double? Midheaven { get; set; }   // null without a birth time
    public string MidheavenSign { get; set; } = "";
    public List<PlanetExport> Planets { get; set; } = [];
    public List<HouseExport> Houses { get; set; } = [];
    public List<AspectExport> Aspects { get; set; } = [];
    public List<PatternExport> Patterns { get; set; } = [];
}

public sealed class PlanetExport
{
    public string Name { get; set; } = "";
    public string Sign { get; set; } = "";
    public double DegreeInSign { get; set; }
    public double Longitude { get; set; }
    public double Latitude { get; set; }
    public double Declination { get; set; }
    public double SpeedPerDay { get; set; }
    public int? House { get; set; }          // null without a birth time
    public bool Retrograde { get; set; }
}

public sealed class HouseExport
{
    public int House { get; set; }
    public double Longitude { get; set; }
    public string Sign { get; set; } = "";
    public double DegreeInSign { get; set; }
}

public sealed class AspectExport
{
    public string PlanetA { get; set; } = "";
    public string PlanetB { get; set; } = "";
    public string Type { get; set; } = "";
    public double ExactAngle { get; set; }
    public double Orb { get; set; }
    public bool Applying { get; set; }
}

public sealed class PatternExport
{
    public string Type { get; set; } = "";                 // Stellium / GrandTrine / TSquare / GrandCross
    [XmlArrayItem("Planet")]
    public List<string> Planets { get; set; } = [];
    public string Sign { get; set; } = "";                 // Stellium: the shared sign
    public string Element { get; set; } = "";              // Grand Trine: the shared element
    public string Modality { get; set; } = "";             // T-Square / Grand Cross: the shared modality
    public string Apex { get; set; } = "";                 // T-Square: the body squaring both ends
}
