using Lore.Models;
using Lore.Views;
using Microsoft.Graphics.Canvas;
using Windows.Storage.Streams;

namespace Lore.Services;

// Draws the wheels the exports carry, as PNG bytes: the one part of exporting that needs
// Win2D, and so the one part that lives in the app and not in Lore.Core. What an export
// says (the JSON and XML data, the PDF reading) is in ChartDataExport and ChartPdf there,
// where the tests can read it.
public static class ExportService
{
    // ── PNG (Win2D offscreen render, no on-screen control needed) ─────────────
    // `showVerdict`: the coloured rim for the dignity verdict, which belongs to a birth
    // chart and is left off a solar return's wheel, as it is on screen.
    public static Task<byte[]> RenderChartPngAsync(NatalChart chart, int size = 1600, bool showVerdict = true) =>
        RenderPngAsync(size, ds => ChartRenderer.Draw(ds, chart, size, size, showVerdict: showVerdict));

    // The synastry bi-wheel: the first chart inside, the second around it.
    public static Task<byte[]> RenderBiWheelPngAsync(Synastry synastry, int size = 1600) =>
        RenderPngAsync(size, ds => ChartRenderer.DrawBiWheel(ds, synastry, size, size));

    // The Daily view's wheel: the birth chart inside, the day's sky around it.
    public static Task<byte[]> RenderTransitWheelPngAsync(NatalChart natal, DailyReading reading, int size = 1600) =>
        RenderPngAsync(size, ds => ChartRenderer.DrawTransitWheel(ds, natal, reading, size, size));

    // The wheel with the Worksheet's tables around and under it. The page is drawn on a
    // surface taller than it can need, then cut to the height it used.
    public static async Task<byte[]> RenderChartSheetPngAsync(NatalChart chart, bool showVerdict = true)
    {
        var device = CanvasDevice.GetSharedDevice();
        using var scratch = new CanvasRenderTarget(device, ChartSheetRenderer.Width, ChartSheetRenderer.MaxHeight, 96);
        float used;
        using (var ds = scratch.CreateDrawingSession())
        {
            used = ChartSheetRenderer.Draw(ds, chart, showVerdict);
        }

        int height = (int)Math.Ceiling(used);
        using var target = new CanvasRenderTarget(device, ChartSheetRenderer.Width, height, 96);
        using (var ds = target.CreateDrawingSession())
        {
            ds.DrawImage(scratch, 0, 0, new Windows.Foundation.Rect(0, 0, ChartSheetRenderer.Width, height));
        }
        return await SavePngAsync(target);
    }

    private static async Task<byte[]> RenderPngAsync(int size, Action<CanvasDrawingSession> draw)
    {
        var device = CanvasDevice.GetSharedDevice();
        using var target = new CanvasRenderTarget(device, size, size, 96);
        using (var ds = target.CreateDrawingSession())
        {
            draw(ds);
        }
        return await SavePngAsync(target);
    }

    private static async Task<byte[]> SavePngAsync(CanvasRenderTarget target)
    {
        using var stream = new InMemoryRandomAccessStream();
        await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);

        var bytes = new byte[stream.Size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }
}
