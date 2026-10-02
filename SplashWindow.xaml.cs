using Lore.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace Lore;

public sealed partial class SplashWindow : Window
{
    // Up long enough to register rather than flicker, and never longer than the cap
    // even if whatever it is waiting on stalls.
    private static readonly TimeSpan MinimumDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaximumDuration = TimeSpan.FromSeconds(10);

    // How long ShowAsync waits for the first painted frame before carrying on regardless.
    private static readonly TimeSpan PaintTimeout = TimeSpan.FromSeconds(1);

    // Share of the work area's shorter side the (square) splash takes up.
    private const double ScreenFraction = 0.55;

    private readonly Task _minimum = Task.Delay(MinimumDuration);
    private bool _closed;

    private SplashWindow()
    {
        InitializeComponent();
        Closed += (_, _) => _closed = true;

        // En space: the gap between the name and the smaller version figure.
        VersionRun.Text = " " + VersionLabel();
    }

    /// <summary>
    /// Shows the splash and returns once it has actually been painted, so the caller's
    /// (UI-thread) startup work happens behind it rather than before it. Returns null if
    /// the splash could not be shown — it is decoration, and must never block a launch.
    /// </summary>
    public static async Task<SplashWindow?> ShowAsync()
    {
        SplashWindow? splash = null;
        try
        {
            splash = new SplashWindow();
            splash.ApplyChrome();
            Task painted = splash.LoadArt();
            splash.Activate();
            await Task.WhenAny(painted, Task.Delay(PaintTimeout));
            return splash;
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"Splash failed: {ex}");
            try { splash?.Close(); } catch { }
            return null;
        }
    }

    /// <summary>
    /// Closes the splash once <paramref name="ready"/> completes — but no sooner than
    /// MinimumDuration after it appeared, and no later than MaximumDuration.
    /// </summary>
    public async Task CloseWhenAsync(Task ready)
    {
        await Task.WhenAny(Task.WhenAll(ready, _minimum), Task.Delay(MaximumDuration));
        if (!_closed) Close();
    }

    // Bare artwork: no caption or border, no taskbar/Alt+Tab entry of its own, and kept
    // above the main window, which opens (maximised) underneath while its data loads.
    private void ApplyChrome()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }
        AppWindow.IsShownInSwitchers = false;

        // Square and centred, sized off the work area (physical pixels) so it looks the
        // same at any DPI without a scale lookup.
        RectInt32 area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int side = (int)(Math.Min(area.Width, area.Height) * ScreenFraction);
        AppWindow.MoveAndResize(new RectInt32(
            area.X + (area.Width - side) / 2,
            area.Y + (area.Height - side) / 2,
            side, side));
    }

    // Starts the artwork loading; the returned task completes once a frame containing it
    // has been drawn (or it failed to load, leaving just the lettering on the backdrop).
    private Task LoadArt()
    {
        var painted = new TaskCompletionSource();

        void OnRendered(object? sender, RenderedEventArgs e)
        {
            CompositionTarget.Rendered -= OnRendered;
            painted.TrySetResult();
        }

        Art.ImageOpened += (_, _) => CompositionTarget.Rendered += OnRendered;
        Art.ImageFailed += (_, e) =>
        {
            Diagnostics.Log($"Splash artwork failed to load: {e.ErrorMessage}");
            painted.TrySetResult();
        };
        Art.Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "splash.png")));

        return painted.Task;
    }

    // "1.2" from the assembly version (the csproj <Version>); the third part only shows
    // when it is non-zero, e.g. "1.2.1".
    private static string VersionLabel()
    {
        Version v = typeof(SplashWindow).Assembly.GetName().Version ?? new Version(0, 0);
        return v.Build > 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : $"{v.Major}.{v.Minor}";
    }
}
