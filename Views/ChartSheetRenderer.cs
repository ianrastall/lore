using Lore.Models;
using Lore.Services;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;

namespace Lore.Views;

// Draws the "chart with tables" image: the wheel with the Worksheet set out around and
// under it, so that one picture carries the chart and the numbers behind it. Everything
// written here comes from WorksheetService.Build (and the dignity score), the same
// source as the Worksheet view and its exports, so the image cannot disagree with them.
//
// The page is a fixed width and as tall as its contents turn out to be: Draw reports the
// height it used, and the caller crops to it.
internal static class ChartSheetRenderer
{
    public const int Width = 2400;
    public const int MaxHeight = 8000;

    private const float Margin = 48, Gap = 40;
    private const float WheelSize = 1300;
    private const float Body = 22, Small = 18, Heading = 30;

    private static readonly Color Background = Color.FromArgb(255, 18, 18, 30);
    private static readonly Color Ink        = Color.FromArgb(255, 232, 232, 240);
    private static readonly Color Soft       = Color.FromArgb(255, 160, 160, 185);
    private static readonly Color Accent     = Color.FromArgb(255, 230, 200, 100);
    private static readonly Color Rule       = Color.FromArgb(70, 180, 180, 220);

    // The colours the wheel draws its aspect lines in, at full strength.
    private static readonly Color[] AspectColors =
    [
        Color.FromArgb(255, 220, 220, 220), // Conjunction
        Color.FromArgb(255, 90, 160, 230),  // Sextile
        Color.FromArgb(255, 220, 60, 60),   // Square
        Color.FromArgb(255, 80, 200, 100),  // Trine
        Color.FromArgb(255, 220, 140, 40),  // Opposition
        Color.FromArgb(255, 150, 150, 170), // Semi-sextile
        Color.FromArgb(255, 200, 110, 110), // Semi-square
        Color.FromArgb(255, 200, 110, 110), // Sesquiquadrate
        Color.FromArgb(255, 170, 130, 200), // Quincunx
        Color.FromArgb(255, 210, 190, 90),  // Quintile
        Color.FromArgb(255, 210, 190, 90),  // Biquintile
    ];

    // Returns the height of what was drawn, margins included.
    public static float Draw(CanvasDrawingSession ds, NatalChart chart)
    {
        var w = WorksheetService.Build(chart);
        var pen = new Pen(ds);
        string Fact(string label) => w.Facts.FirstOrDefault(f => f.Label == label)?.Value ?? "";
        WorksheetSection? Section(string title) => w.Sections.FirstOrDefault(s => s.Title == title && s.Rows.Count > 0);

        // The wheel goes down first: it clears the whole surface to the background.
        const float top = 300;
        ds.Transform = Matrix3x2.CreateTranslation(Margin - 24, top);
        ChartRenderer.Draw(ds, chart, WheelSize, WheelSize);
        ds.Transform = Matrix3x2.Identity;

        // ── Header ────────────────────────────────────────────────────────────
        float y = Margin;
        pen.Text("✦ Lore", Width - Margin, y + 10, 30, Accent, right: true);
        y += pen.Text(chart.Celebrity.Name, Margin, y, 60, Ink, bold: true) + 6;
        y += pen.Text($"{Fact("Born")}   ·   {Fact("Place")}", Margin, y, 26, Ink, maxWidth: Width - 2 * Margin) + 6;
        y += pen.Text($"{Fact("Time conversion")}   ·   {Fact("Universal Time")}   ·   {Fact("Zodiac")}",
                      Margin, y, Small + 2, Soft, maxWidth: Width - 2 * Margin) + 4;
        y += pen.Text($"{Fact("Houses")}   ·   {Fact("North Node")}   ·   Lilith: {Fact("Lilith")}" +
                      (chart.Timed ? $"   ·   {Fact("Sect")}" : ""),
                      Margin, y, Small + 2, Soft, maxWidth: Width - 2 * Margin) + 4;
        y += pen.Text($"Aspect orbs: {Fact("Aspect orbs")}", Margin, y, Small + 2, Soft, maxWidth: Width - 2 * Margin) + 4;
        if (DignityService.ComputeIfTimed(chart) is { } verdict)
            pen.Text($"Dignity score {verdict.Total:+0;−0;0}: {verdict.Verdict.Label()}", Margin, y, Small + 2, ParseHex(verdict.Verdict.ColorHex()));

        // ── Beside the wheel: positions, cusps, elements by mode ──────────────
        float x = Margin + WheelSize + Gap - 24;
        float side = Width - Margin - x;
        y = top + 10;

        y = Title(pen, "Positions", x, y);
        y = Table(pen, ds, x, y, side,
            ["", "", "Position", "Latitude", "Declin.", "Speed/day", "House", ""],
            w.Positions.Select(r => (IReadOnlyList<string>)
                [r.Symbol, r.Name, r.Position, r.Latitude, r.Declination, r.Speed, r.House, r.Motion]).ToList(),
            gap: 20) + 6;
        y += pen.Text("℞ retrograde   ·   S stationary   ·   + north, − south", x, y, Small - 2, Soft) + 22;

        if (w.HasCusps)
        {
            y = Title(pen, "House cusps", x, y);
            y = Table(pen, ds, x, y, side, [],
                Enumerable.Range(0, 6).Select(i => (IReadOnlyList<string>)
                    [w.Cusps[i].House, w.Cusps[i].Position, "", w.Cusps[i + 6].House, w.Cusps[i + 6].Position]).ToList(),
                gap: 20) + 22;
        }

        if (Section("Elements and modes") is { } grid)
        {
            y = Title(pen, grid.Title, x, y);
            y = Table(pen, ds, x, y, side, grid.Headers, grid.Rows, size: Body + 2, gap: 44, soften: 0) + 22;
        }
        y = Math.Max(y, top + WheelSize) + Gap;

        // ── The aspect grid, and beside it the summaries ──────────────────────
        float rowTop = y;
        const float cell = 66;
        float gridBottom = AspectGrid(pen, ds, w, chart, Margin, Title(pen, "Aspects", Margin, rowTop), cell);

        x = Margin + w.Points.Count * cell + Gap + 8;
        side = Width - Margin - x;
        y = rowTop;
        foreach (string title in new[] { "The Moon's phase", "Balance", "Aspects in sum", "Rulers", "Declination" })
            if (Section(title) is { } s)
            {
                y = Title(pen, s.Title, x, y);
                y = Table(pen, ds, x, y, side, s.Headers, s.Rows, soften: 0, firstColumn: 240) + 22;
            }
        y = Math.Max(y, gridBottom) + Gap;

        // ── The tables, three abreast: each goes in whichever column is shortest ──
        var tables = new List<WorksheetSection>();
        foreach (string title in new[] { "Distance from the Sun", "Angles and houses", "House rulers", "Dispositors" })
            if (Section(title) is { } s) tables.Add(s);
        if (DignityService.ComputeIfTimed(chart) is { } score)
            tables.Add(new("Dignity", ["", "Essential", "Accidental", "Total", "From"],
                score.Planets.Select(p => (IReadOnlyList<string>)
                [
                    $"{p.Planet.Symbol()} {p.Planet.Name()}", Plus(p.Essential), Plus(p.Accidental), Plus(p.Total), p.Notes,
                ]).ToList()));

        const int columns = 3;
        float columnWidth = (Width - 2 * Margin - (columns - 1) * Gap) / columns;
        var bottoms = Enumerable.Repeat(y, columns).ToArray();
        foreach (var s in tables)
        {
            int c = Array.IndexOf(bottoms, bottoms.Min());
            float cx = Margin + c * (columnWidth + Gap);
            float cy = Title(pen, s.Title, cx, bottoms[c]);
            bottoms[c] = Table(pen, ds, cx, cy, columnWidth, s.Headers, s.Rows, size: Body - 2, gap: 18) + 30;
        }
        y = bottoms.Max() + Gap - 30;

        // ── Everything by degree ──────────────────────────────────────────────
        y = Title(pen, "By degree within the sign", Margin, y);
        y = DegreeStrip(pen, ds, w, chart, y) + 26;

        ds.DrawLine(Margin, y, Width - Margin, y, Rule, 1.5f);
        y += 14;
        y += pen.Text($"Generated by Lore on {DateTime.Now:yyyy-MM-dd}   ·   {Fact("Engine")}", Margin, y, Small - 2, Soft);
        return Math.Min(MaxHeight, y + Margin);
    }

    private static string Plus(int n) => n.ToString("+0;−0;0");

    // ── Pieces ────────────────────────────────────────────────────────────────

    private static float Title(Pen pen, string text, float x, float y) =>
        y + pen.Text(text, x, y, Heading, Accent, bold: true) + 10;

    // A table of already-formatted cells: each column as wide as its widest cell, the
    // last taking what is left and wrapping. With no headers it is a list of
    // label-and-value lines, the labels in the softer colour. Returns the y below it.
    // `soften`: the column drawn in the softer colour, or -1 for none.
    private static float Table(Pen pen, CanvasDrawingSession ds, float x, float y, float width,
        IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows,
        float size = Body, float gap = 28, int soften = -1, float firstColumn = 0)
    {
        if (rows.Count == 0) return y;
        int columns = Math.Max(headers.Count, rows.Max(r => r.Count));
        var widths = new float[columns];
        if (firstColumn > 0 && headers.Count == 0) widths[0] = firstColumn;
        for (int i = 0; i < columns; i++)
        {
            if (i < headers.Count) widths[i] = Math.Max(widths[i], pen.Measure(headers[i], size - 4));
            foreach (var row in rows)
                if (i < row.Count) widths[i] = Math.Max(widths[i], pen.Measure(row[i], size));
        }
        float before = widths.Take(columns - 1).Sum() + gap * (columns - 1);
        widths[^1] = Math.Min(widths[^1] + 2, Math.Max(140, width - before));

        if (headers.Count > 0)
        {
            float hx = x;
            float height = 0;
            for (int i = 0; i < headers.Count; i++)
            {
                height = Math.Max(height, pen.Text(headers[i], hx, y, size - 4, Soft));
                hx += widths[i] + gap;
            }
            y += height + 4;
            ds.DrawLine(x, y, x + Math.Min(width, before + widths[^1]), y, Rule, 1.5f);
            y += 6;
        }

        foreach (var row in rows)
        {
            float cx = x, height = 0;
            for (int i = 0; i < row.Count; i++)
            {
                bool last = i == columns - 1;
                height = Math.Max(height, pen.Text(row[i], cx, y, size, i == soften ? Soft : Ink, maxWidth: last ? widths[i] : 0));
                cx += widths[i] + gap;
            }
            y += Math.Max(height, size * 1.3f) + 6;
        }
        return y;
    }

    // The triangular grid of the Worksheet view: each point names a cell on the diagonal,
    // and the cell where two meet holds their aspect and its orb. Returns the y below it.
    private static float AspectGrid(Pen pen, CanvasDrawingSession ds, Worksheet w, NatalChart chart, float x, float y, float cell)
    {
        var points = w.Points;
        for (int row = 0; row < points.Count; row++)
        {
            float cy = y + row * cell;
            var box = new Rect(x + row * cell, cy, cell, cell);
            ds.FillRectangle(box, Color.FromArgb(40, 180, 180, 220));
            ds.DrawRectangle(box, Rule, 1.5f);
            Centred(pen, points[row].Symbol, x + row * cell, cy + cell * 0.2f, cell, cell * 0.4f, Ink);

            for (int col = 0; col < row; col++)
            {
                float cx = x + col * cell;
                ds.DrawRectangle(new Rect(cx, cy, cell, cell), Rule, 1f);
                if (w.AspectBetween(points[row], points[col]) is not { } aspect) continue;

                // An out-of-sign aspect carries a star, as on the Worksheet.
                Centred(pen, aspect.OutOfSign ? aspect.Type.Symbol() + "*" : aspect.Type.Symbol(),
                    cx, cy + cell * 0.08f, cell, cell * 0.36f, AspectColors[(int)aspect.Type]);
                Centred(pen, aspect.OrbText, cx, cy + cell * 0.62f, cell, cell * 0.22f, Soft);
            }
        }
        y += points.Count * cell + 14;

        // The key: the aspects in force, each in its colour.
        float kx = x;
        foreach (var type in chart.Settings.Orbs.Types().OrderBy(t => t.Angle()))
        {
            string label = $"{type.Symbol()} {type.Name()} {type.Angle():0}°";
            float width = pen.Measure(label, Small - 2);
            if (kx + width > x + Math.Max(points.Count * cell, 900)) { kx = x; y += Small + 8; }
            pen.Text(label, kx, y, Small - 2, AspectColors[(int)type]);
            kx += width + 26;
        }
        y += Small + 10;
        y += pen.Text("Under each aspect, how far it is from exact: a applying, s separating.   * out of sign." +
                      (chart.Timed ? "   The Vertex (Vx) and Part of Fortune (⊗) are aspected here and nowhere else in Lore." : ""),
                      x, y, Small - 2, Soft, maxWidth: Math.Max(points.Count * cell, 900));
        return y;
    }

    // A ruler of one sign, 0° to 30°, with every point of the chart set above its degree
    // whatever sign it is in: points that stand together here are at the same degree of
    // their signs, which is where the aspects fall. Returns the y below it.
    private static float DegreeStrip(Pen pen, CanvasDrawingSession ds, Worksheet w, NatalChart chart, float y)
    {
        float x0 = Margin + 30, x1 = Width - Margin - 30;
        float X(double degree) => x0 + (float)(degree / 30 * (x1 - x0));
        const float glyph = 30, level = 62;

        // Lowest free row for each glyph, working left to right.
        var placed = new List<(string Symbol, string Label, float X, int Level)>();
        var edges = new List<float>();
        foreach (var (point, longitude) in w.Points
                     .Select(p => (Point: p, Longitude: chart.LongitudeOf(p)))
                     .Where(p => p.Longitude is not null)
                     .Select(p => (p.Point, ZodiacSignExtensions.DegreeInSign(p.Longitude!.Value)))
                     .OrderBy(p => p.Item2))
        {
            float px = X(longitude);
            float half = Math.Max(pen.Measure(point.Symbol, glyph), pen.Measure($"{(int)longitude}°", Small - 4)) / 2 + 5;
            int at = edges.FindIndex(edge => px - half >= edge);
            if (at < 0) { at = edges.Count; edges.Add(0); }
            edges[at] = px + half;
            placed.Add((point.Symbol, $"{(int)longitude}°", px, at));
        }

        float baseline = y + Math.Max(1, edges.Count) * level + 6;
        foreach (var p in placed)
        {
            float py = baseline - (p.Level + 1) * level;
            Centred(pen, p.Symbol, p.X - 50, py, 100, glyph, Ink);
            Centred(pen, p.Label, p.X - 50, py + glyph * 1.25f, 100, Small - 4, Soft);
            ds.DrawLine(p.X, py + level - 6, p.X, baseline, Rule, 1f);
        }

        ds.DrawLine(x0, baseline, x1, baseline, Accent, 2f);
        for (int degree = 0; degree <= 30; degree++)
        {
            bool five = degree % 5 == 0;
            ds.DrawLine(X(degree), baseline, X(degree), baseline + (five ? 16 : 8), five ? Accent : Rule, five ? 2f : 1.5f);
            if (five) Centred(pen, $"{degree}°", X(degree) - 50, baseline + 20, 100, Small, Accent);
        }
        return baseline + 20 + Small * 1.4f;
    }

    private static void Centred(Pen pen, string text, float x, float y, float width, float size, Color color) =>
        pen.Text(text, x + (width - pen.Measure(text, size)) / 2, y, size, color);

    private static Color ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        return Color.FromArgb(255,
            Convert.ToByte(hex[..2], 16), Convert.ToByte(hex.Substring(2, 2), 16), Convert.ToByte(hex.Substring(4, 2), 16));
    }

    // Text measured and drawn through one drawing session.
    private sealed class Pen(CanvasDrawingSession ds)
    {
        private CanvasTextLayout Layout(string text, float size, float maxWidth, bool bold)
        {
            using var format = new CanvasTextFormat
            {
                FontSize = size,
                WordWrapping = maxWidth > 0 ? CanvasWordWrapping.Wrap : CanvasWordWrapping.NoWrap,
                FontWeight = new Windows.UI.Text.FontWeight { Weight = (ushort)(bold ? 600 : 400) },
            };
            var layout = new CanvasTextLayout(ds, text, format, maxWidth > 0 ? maxWidth : 0, 0);
            // Left to itself the text engine draws Venus and Mars as boxed emoji; the
            // symbol font has them as plain glyphs, like the other planets.
            for (int i = 0; i < text.Length; i++)
                if (text[i] is '♀' or '♂')
                    layout.SetFontFamily(i, 1, "Segoe UI Symbol");
            return layout;
        }

        public float Measure(string text, float size)
        {
            if (text.Length == 0) return 0;
            using var layout = Layout(text, size, 0, false);
            return (float)layout.LayoutBounds.Width;
        }

        // Draws the text with its top-left corner at (x, y), or its top-right if `right`.
        // Returns its height.
        public float Text(string text, float x, float y, float size, Color color,
            float maxWidth = 0, bool bold = false, bool right = false)
        {
            if (text.Length == 0) return 0;
            using var layout = Layout(text, size, maxWidth, bold);
            ds.DrawTextLayout(layout, right ? x - (float)layout.LayoutBounds.Width : x, y, color);
            return (float)layout.LayoutBounds.Height;
        }
    }
}
