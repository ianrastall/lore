using Lore.Mobile.Views;
using SkiaSharp;

namespace Lore.Mobile.Services;

// The two Noto Sans Symbols fonts packed with the app, for the wheel's glyphs. Android's
// own fonts would draw the zodiac signs as colour emoji.
public static class WheelFonts
{
    public static async Task LoadAsync()
    {
        WheelRenderer.Symbols ??= await LoadAsync("NotoSansSymbols-Regular.ttf");
        WheelRenderer.Symbols2 ??= await LoadAsync("NotoSansSymbols2-Regular.ttf");
    }

    private static async Task<SKTypeface?> LoadAsync(string name)
    {
        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync(name);
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy);
            copy.Position = 0;
            return SKTypeface.FromStream(copy);
        }
        catch (Exception ex) when (ex is IOException or FileNotFoundException)
        {
            Lore.Services.Diagnostics.Log($"Wheel font {name} could not be read: {ex.Message}");
            return null;
        }
    }
}
