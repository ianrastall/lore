using Lore.Mobile.Services;
using Lore.Mobile.Views;
using Lore.ViewModels;

namespace Lore.Mobile;

public partial class App : Application
{
    public App() => InitializeComponent();

    // The engine and the library, once loaded. Every page reads from it; none is shown
    // before it is set.
    public static MainViewModel Main { get; private set; } = null!;

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var loading = new LoadingPage();
        var window = new Window(loading);
        _ = StartAsync(window, loading);
        return window;
    }

    private static async Task StartAsync(Window window, LoadingPage loading)
    {
        try
        {
            await WheelFonts.LoadAsync();
            Main = await LoreHost.BuildAsync();
            window.Page = new AppShell();
            await LoreHost.LoadLibraryAsync(Main);
        }
        catch (Exception ex)
        {
            Lore.Services.Diagnostics.Log($"Lore could not start: {ex}");
            loading.ShowProblem($"Lore could not start: {ex.Message}");
        }
    }

    // Android may end the app at any time once it is in the background, so what is
    // waiting to be written is written now.
    protected override void OnSleep()
    {
        LoreHost.Settings?.Flush();
        base.OnSleep();
    }
}
