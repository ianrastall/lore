using Lore.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Lore.Services;

// The worksheet as PDF content. Kept apart from ExportService (which needs the chart
// renderer, and so WinUI) so that this layout can be exercised by the tests.
public static class WorksheetPdf
{
    static WorksheetPdf()
    {
        // QuestPDF Community licence (free for individuals / <$1M revenue).
        QuestPDF.Settings.License = LicenseType.Community;
    }

    // The worksheet on its own, as a complete document.
    public static byte[] ToPdf(Worksheet w, TimeSensitivity? sensitivity = null) =>
        Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(x => x.FontSize(11).FontFamily("Segoe UI", "Segoe UI Symbol", "Segoe UI Historic"));
            page.Header().Text(w.Name).FontSize(22).SemiBold();
            page.Content().PaddingVertical(12).Column(col => Compose(col, w, sensitivity, newPage: false));
        })).GeneratePdf();

    // The Worksheet view on paper, starting on a fresh page: how the chart was calculated,
    // what depends on the birth time, then the positions, house cusps and aspects.
    public static void Compose(QuestPDF.Fluent.ColumnDescriptor col, Worksheet w, TimeSensitivity? sensitivity, bool newPage = true)
    {
        static IContainer Head(IContainer c) =>
            c.PaddingVertical(2).BorderBottom(1).BorderColor(Colors.Grey.Lighten1);

        if (newPage) col.Item().PageBreak();
        col.Item().Text("Worksheet").FontSize(18).SemiBold();
        col.Item().Text("The numbers behind the chart, and how they were arrived at. Nothing here is interpreted.")
            .FontSize(9).FontColor(Colors.Grey.Darken1);

        col.Item().PaddingTop(6).Text("How this was calculated").FontSize(13).SemiBold();
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c => { c.ConstantColumn(105); c.RelativeColumn(); });
            foreach (var f in w.Facts)
            {
                table.Cell().PaddingVertical(1).Text(f.Label).FontSize(9).FontColor(Colors.Grey.Darken1);
                table.Cell().PaddingVertical(1).Text(f.Value).FontSize(9);
            }
        });

        if (sensitivity is not null)
        {
            col.Item().PaddingTop(6).Text(sensitivity.WholeDay
                ? "Without a birth time"
                : $"If the birth time is off by up to {sensitivity.Minutes} minutes").FontSize(13).SemiBold();
            col.Item().Text($"{sensitivity.Summary}  ({sensitivity.Window})").FontSize(9);
            foreach (var line in sensitivity.Changes)
                col.Item().Text($"Depends on the time — {line}").FontSize(9).FontColor(Colors.Orange.Darken3);
            foreach (var line in sensitivity.Holds)
                col.Item().Text($"Holds — {line}").FontSize(9);
        }

        col.Item().EnsureSpace(120).PaddingTop(6).Text("Positions").FontSize(13).SemiBold();
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(3.2f); // body
                c.RelativeColumn(3.6f); // position
                c.RelativeColumn(1.8f); // latitude
                c.RelativeColumn(2);    // declination
                c.RelativeColumn(2.2f); // speed
                c.RelativeColumn(1.2f); // house
                c.RelativeColumn(0.8f); // retrograde
            });
            table.Header(h =>
            {
                h.Cell().Element(Head).Text("").FontSize(9);
                h.Cell().Element(Head).Text("Position").SemiBold().FontSize(9);
                h.Cell().Element(Head).AlignRight().Text("Latitude").SemiBold().FontSize(9);
                h.Cell().Element(Head).AlignRight().Text("Declination").SemiBold().FontSize(9);
                h.Cell().Element(Head).AlignRight().Text("Speed / day").SemiBold().FontSize(9);
                h.Cell().Element(Head).AlignRight().Text("House").SemiBold().FontSize(9);
                h.Cell().Element(Head).Text("").FontSize(9);
            });
            foreach (var r in w.Positions)
            {
                table.Cell().PaddingVertical(1).Text($"{r.Symbol} {r.Name}").FontSize(9);
                table.Cell().PaddingVertical(1).Text(r.Position).FontSize(9);
                table.Cell().PaddingVertical(1).AlignRight().Text(r.Latitude).FontSize(9);
                table.Cell().PaddingVertical(1).AlignRight().Text(r.Declination).FontSize(9);
                table.Cell().PaddingVertical(1).AlignRight().Text(r.Speed).FontSize(9);
                table.Cell().PaddingVertical(1).AlignRight().Text(r.House).FontSize(9);
                table.Cell().PaddingVertical(1).AlignCenter().Text(r.Motion).FontSize(9);
            }
        });

        if (w.HasCusps)
        {
            col.Item().EnsureSpace(120).PaddingTop(6).Text("House cusps").FontSize(13).SemiBold();
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(22); c.RelativeColumn();
                    c.ConstantColumn(22); c.RelativeColumn();
                });
                // Two columns of six: 1 beside 7, 2 beside 8, and so on.
                for (int i = 0; i < 6; i++)
                    foreach (var cusp in new[] { w.Cusps[i], w.Cusps[i + 6] })
                    {
                        table.Cell().PaddingVertical(1).Text(cusp.House).FontSize(9).FontColor(Colors.Grey.Darken1);
                        table.Cell().PaddingVertical(1).Text(cusp.Position).FontSize(9);
                    }
            });
        }

        col.Item().EnsureSpace(120).PaddingTop(6).Text("Aspects").FontSize(13).SemiBold();
        col.Item().Text("Closest first, including those to the Ascendant and Midheaven. a = applying, s = separating. * = out of sign: within orb, but the two signs are not in that aspect.")
            .FontSize(9).FontColor(Colors.Grey.Darken1);
        col.Item().Table(table =>
        {
            // Two aspects to a row, to keep a long list to a page.
            table.ColumnsDefinition(c =>
            {
                for (int i = 0; i < 2; i++) { c.RelativeColumn(4); c.RelativeColumn(1.6f); }
            });
            foreach (var a in w.Aspects)
            {
                table.Cell().PaddingVertical(1).Text($"{a.A.Name} {a.Type.Symbol()} {a.B.Name}").FontSize(9);
                table.Cell().PaddingVertical(1).Text(a.OutOfSign ? $"{a.OrbText} *" : a.OrbText).FontSize(9).FontColor(Colors.Grey.Darken1);
            }
            if (w.Aspects.Count % 2 == 1) { table.Cell(); table.Cell(); }
        });

        foreach (var section in w.Sections)
        {
            if (section.Rows.Count == 0) continue;
            col.Item().EnsureSpace(90).PaddingTop(6).Text(section.Title).FontSize(13).SemiBold();
            if (section.Note.Length > 0)
                col.Item().Text(section.Note).FontSize(8).FontColor(Colors.Grey.Darken1);

            bool list = section.Headers.Count == 0;   // label-and-value lines
            int columns = list ? 2 : section.Headers.Count;
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    if (list) { c.ConstantColumn(105); c.RelativeColumn(); return; }
                    // Each column in proportion to its widest cell.
                    for (int i = 0; i < columns; i++)
                        c.RelativeColumn(Math.Max(4, section.Rows.Append(section.Headers).Max(r => i < r.Count ? r[i].Length : 0)));
                });
                if (!list)
                    table.Header(h =>
                    {
                        foreach (var head in section.Headers)
                            h.Cell().Element(Head).Text(head).SemiBold().FontSize(9);
                    });
                foreach (var row in section.Rows)
                    for (int i = 0; i < columns; i++)
                    {
                        var text = table.Cell().PaddingVertical(1).Text(i < row.Count ? row[i] : "").FontSize(9);
                        if (list && i == 0) text.FontColor(Colors.Grey.Darken1);
                    }
            });
        }
    }
}
