using Lore.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Lore;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // The two ways of running with no window: the release script's check of this
        // copy, and another Jupiter app asking for a daily reading.
        if (SelfCheck.ReportPath(args) is { } reportPath)
            return SelfCheck.RunAndReport(AppContext.BaseDirectory, reportPath);
        if (DailyReadingExport.Request.Parse(args) is { } request)
            return DailyReadingExport.RunAndReport(AppContext.BaseDirectory, request);

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(callback =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
        return 0;
    }
}
