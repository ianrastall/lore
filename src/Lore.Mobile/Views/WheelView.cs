using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Lore.Mobile.Views;

// A square canvas for one of WheelRenderer's wheels, as wide as it is given. The page sets
// Draw to the wheel it wants, and calls Redraw when what it draws has changed; Draw
// returning false (nothing to draw) leaves the background.
public sealed class WheelView : SKCanvasView
{
    public Func<SKCanvas, float, float, float, bool>? Draw { get; set; }

    public void Redraw() => InvalidateSurface();

    // Square, whatever the layout: a page's stack gives a width and no height.
    protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
    {
        double side = double.IsFinite(widthConstraint) ? widthConstraint : 360;
        if (double.IsFinite(heightConstraint)) side = Math.Min(side, heightConstraint);
        return new Size(side, side);
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var info = e.Info;
        float px = Width > 0 ? (float)(info.Width / Width) : 1f;
        try
        {
            if (Draw?.Invoke(e.Surface.Canvas, info.Width, info.Height, px) != true)
                e.Surface.Canvas.Clear(WheelRenderer.Background);
        }
        catch (Exception ex)
        {
            // A wheel that cannot be drawn should not take the app down with it.
            Lore.Services.Diagnostics.Log($"Wheel could not be drawn: {ex}");
            e.Surface.Canvas.Clear(WheelRenderer.Background);
        }
    }
}
