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
    public static async Task<byte[]> RenderChartPngAsync(NatalChart chart, int size = 1600)
    {
        var device = CanvasDevice.GetSharedDevice();
        using var target = new CanvasRenderTarget(device, size, size, 96);
        using (var ds = target.CreateDrawingSession())
        {
            ChartRenderer.Draw(ds, chart, size, size);
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
    public static byte[] ToPdf(NatalChart chart, IReadOnlyList<ReportSection> report, byte[] chartPng)
    {
        var vm = chart.Celebrity;
        string subtitle = $"{vm.BirthDate}  ·  {vm.BirthPlace}" +
            (vm.BirthTimeKnown ? $"  ·  {vm.BirthTime}" : "  ·  time unknown");
        // Lead with the "Big Three" (Sun first — that's the everyday "sign"); keep the
        // Ascendant/Midheaven as a small, clearly-labelled technical line so the MC can't
        // be mistaken for someone's sign.
        string rising = ZodiacSignExtensions.FromLongitude(chart.Ascendant).Name();
        var bigParts = new List<string>();
        if (chart.GetPlanet(Planet.Sun) is { } sunP)  bigParts.Add($"☉ Sun {sunP.Sign.Name()}");
        if (chart.GetPlanet(Planet.Moon) is { } moonP) bigParts.Add($"☽ Moon {moonP.Sign.Name()}");
        bigParts.Add($"↑ Rising {rising}");
        string bigThree = string.Join("     ·     ", bigParts);
        string angles = $"Ascendant {rising}   ·   Midheaven " +
                        ZodiacSignExtensions.FromLongitude(chart.Midheaven).Name();

        var score = DignityService.Compute(chart);
        string verdictHex = score.Verdict.ColorHex();
        string verdictLine = $"Chart assessment:  {score.Total:+#;-#;0}  —  {score.Verdict.Label()}";

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

                    ChartAssessment(col, score);
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
            BirthTime = c.BirthTime,
            BirthTimeKnown = c.BirthTimeKnown,
            BirthPlace = c.BirthPlace,
            Latitude = c.Latitude,
            Longitude = c.Longitude,
            UtcOffsetHours = c.UtcOffsetHours,
            Ascendant = Round(chart.Ascendant),
            AscendantSign = ZodiacSignExtensions.FromLongitude(chart.Ascendant).Name(),
            Midheaven = Round(chart.Midheaven),
            MidheavenSign = ZodiacSignExtensions.FromLongitude(chart.Midheaven).Name(),
            Planets = chart.Planets.Select(p => new PlanetExport
            {
                Name = p.PlanetName,
                Sign = p.Sign.Name(),
                DegreeInSign = Round(p.DegreeInSign),
                Longitude = Round(p.Longitude),
                House = chart.GetHouseForLongitude(p.Longitude),
                Retrograde = p.IsRetrograde
            }).ToList(),
            Houses = chart.Houses.Select(h => new HouseExport
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
    public string? BirthTime { get; set; }
    public bool BirthTimeKnown { get; set; }
    public string BirthPlace { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double UtcOffsetHours { get; set; }
    public double Ascendant { get; set; }
    public string AscendantSign { get; set; } = "";
    public double Midheaven { get; set; }
    public string MidheavenSign { get; set; } = "";
    public List<PlanetExport> Planets { get; set; } = [];
    public List<HouseExport> Houses { get; set; } = [];
    public List<AspectExport> Aspects { get; set; } = [];
}

public sealed class PlanetExport
{
    public string Name { get; set; } = "";
    public string Sign { get; set; } = "";
    public double DegreeInSign { get; set; }
    public double Longitude { get; set; }
    public int House { get; set; }
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
