using SkiaSharp.Views.Maui.Controls.Hosting;

namespace Lore.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSkiaSharp()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("NotoSansSymbols-Regular.ttf", "Symbols");
                fonts.AddFont("NotoSansSymbols2-Regular.ttf", "Symbols2");
            });
        return builder.Build();
    }
}
