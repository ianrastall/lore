using System.Reflection;
using System.Runtime.InteropServices;
using Lore.Models;
using Lore.Services;
using Lore.ViewModels;
using Lore.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace Lore;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    public bool IsClosed { get; private set; }

    private readonly ShellPreferences _preferences = App.Current.Preferences;

    private SettingsWindow? _settingsWindow;
    private readonly List<Window> _writerWindows = [];

    public MainWindow(MainViewModel vm)
    {
        ViewModel = vm;
        InitializeComponent();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        App.Current.Accent.Attach(Root);
        WritersViewControl.ExportRequested += ExportWriterBrief;
        WritersViewControl.ChartRequested += OpenWriterChart;

        RestorePlacement();
        ReadingText.Current.Step = _preferences.ReadingSize;
        ChartsColumn.Width = new GridLength(_preferences.ChartsPaneWidth);
        ShowChartsPane(_preferences.ChartsPaneVisible);

        // Shortcuts that have no key of their own in the menus: a menu item can show
        // "Ctrl+," but XAML has no name for the comma, plus or minus keys.
        Accelerator(CommaKey, VirtualKeyModifiers.Control, () => OpenSettings());
        Accelerator(PlusKey, VirtualKeyModifiers.Control, () => StepReading(+1));
        Accelerator(VirtualKey.Add, VirtualKeyModifiers.Control, () => StepReading(+1));
        Accelerator(MinusKey, VirtualKeyModifiers.Control, () => StepReading(-1));
        Accelerator(VirtualKey.Subtract, VirtualKeyModifiers.Control, () => StepReading(-1));
        Accelerator(VirtualKey.Number0, VirtualKeyModifiers.Control, () => SetReading(ReadingText.DefaultStep));

        // The window (and so the taskbar) names whoever's chart is open.
        vm.ChartVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChartViewModel.Chart))
                Title = vm.ChartVM.Chart is { } c ? $"{c.Celebrity.Name} - Lore" : "Lore";
        };
        // Picking a person returns from the Legend to their chart (MainViewModel).
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ShowLegend) && !vm.ShowLegend && _view == "Legend")
                ShowView(_lastChartView);
            // Adding a chart clears the search so that the new chart is in the list.
            if (e.PropertyName == nameof(MainViewModel.SearchText) && SearchBox.Text != vm.SearchText)
                SearchBox.Text = vm.SearchText;
        };
        AppWindow.Closing += OnClosing;
        Closed += (_, _) =>
        {
            IsClosed = true;
            SavePlacement();
            _settingsWindow?.Close();
            ViewModel.WritersVM.Cancel();
            foreach (var window in _writerWindows.ToArray()) window.Close();
        };

        // The Charts list's own rows say whether the Synastry view is open by {Binding}.
        Root.DataContext = ViewModel;

        // One-time data load. Window.Activated proved unreliable here (it never fired,
        // so the app started with empty lists), so drive it from the root element's
        // Loaded event, with Activated kept as a fallback. The guard runs it once.
        bool initialized = false;
        async void InitOnce(string via)
        {
            if (initialized || IsClosed) return;
            initialized = true;
            Diagnostics.Log($"InitOnce via {via}");
            string dataPath      = Path.Combine(AppContext.BaseDirectory, "Data", "celebrities.json");
            string citiesPath    = Path.Combine(AppContext.BaseDirectory, "Data", "cities.json");
            string hospitalsPath = Path.Combine(AppContext.BaseDirectory, "Data", "hospitals.json");
            await ViewModel.InitializeAsync(dataPath, citiesPath, hospitalsPath);
            if (IsClosed) return;
            ShowView(ViewModel.LastView); // back to the view the last session ended in
            if (ViewModel.SelectedCelebrity is { } last) CelebrityList.ScrollIntoView(last);
            _initialized.TrySetResult();
        }

        Root.Loaded += (_, _) => InitOnce("Loaded");
        Activated += (_, _) => InitOnce("Activated");
    }

    // Done when the first data load has finished and the list is on show: what the splash
    // stays up for.
    private readonly TaskCompletionSource _initialized = new();
    public Task Initialized => _initialized.Task;

    private const VirtualKey CommaKey = (VirtualKey)188, PlusKey = (VirtualKey)187, MinusKey = (VirtualKey)189;

    private void Accelerator(VirtualKey key, VirtualKeyModifiers modifiers, Action run)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += (_, args) =>
        {
            args.Handled = true;
            run();
        };
        Root.KeyboardAccelerators.Add(accelerator);
    }

    // ── Window placement ──────────────────────────────────────────────────────

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint window);

    // Standard 6.1: a workspace opens at 1200 × 800, centered, and after that where it
    // was left, kept inside the work area of the display.
    private void RestorePlacement()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        double scale = GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0;
        int width = Math.Min(_preferences.WindowWidth > 0 ? _preferences.WindowWidth : (int)(1200 * scale), area.Width);
        int height = Math.Min(_preferences.WindowHeight > 0 ? _preferences.WindowHeight : (int)(800 * scale), area.Height);
        var saved = new RectInt32(_preferences.WindowX, _preferences.WindowY, width, height);
        bool visible = _preferences.WindowX != int.MinValue
            && saved.X >= area.X - 32 && saved.Y >= area.Y - 8
            && saved.X + 64 <= area.X + area.Width && saved.Y + 64 <= area.Y + area.Height;
        AppWindow.MoveAndResize(visible ? saved : new RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(800 * scale);
            presenter.PreferredMinimumHeight = (int)(560 * scale);
            if (_preferences.WindowMaximized) presenter.Maximize();
        }
    }

    private void SavePlacement()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            _preferences.WindowMaximized = presenter.State == OverlappedPresenterState.Maximized;
            if (presenter.State == OverlappedPresenterState.Restored)
            {
                _preferences.WindowX = AppWindow.Position.X;
                _preferences.WindowY = AppWindow.Position.Y;
                _preferences.WindowWidth = AppWindow.Size.Width;
                _preferences.WindowHeight = AppWindow.Size.Height;
            }
        }
        if (_preferences.ChartsPaneVisible && ChartsColumn.ActualWidth > 0)
            _preferences.ChartsPaneWidth = ChartsColumn.ActualWidth;
        _preferences.Save();
    }

    // Only one dialog can be open at a time; a second Ctrl+N while one is up does nothing.
    private bool _dialogOpen;

    // Set once it is settled that the window may go: everything is saved, or the reader
    // has said to close without what could not be.
    private bool _closeNow;

    // Inventory answers are saved in the background as they are given. Closing waits for
    // the ones still on their way, and if they cannot be saved it says so and asks,
    // rather than letting them go without a word.
    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeNow || (!ViewModel.HasUnsavedAnswers && !ViewModel.WritersVM.IsSaving && ViewModel.WritersVM.SaveProblem.Length == 0)) return;
        args.Cancel = true;
        try
        {
            while (!await ViewModel.WritersVM.FinishSaveAsync())
            {
                if (_dialogOpen) return;
                _dialogOpen = true;
                ContentDialogResult choice;
                try
                {
                    var dialog = NewDialog("Close without saving this Writers brief?", ViewModel.WritersVM.SaveProblem);
                    dialog.PrimaryButtonText = "Try again"; dialog.SecondaryButtonText = "Close without saving"; dialog.CloseButtonText = "Keep Lore open";
                    dialog.DefaultButton = ContentDialogButton.Close;
                    choice = await dialog.ShowAsync();
                }
                finally { _dialogOpen = false; }
                if (choice == ContentDialogResult.None) return;
                if (choice == ContentDialogResult.Secondary) break;
                await ViewModel.WritersVM.RetrySaveAsync();
            }
            while (!await ViewModel.SaveOutstandingAnswersAsync())
            {
                if (_dialogOpen) return; // another dialog is up: stay open, and ask on the next close
                _dialogOpen = true;
                ContentDialogResult answer;
                try
                {
                    var dialog = NewDialog("Close Lore without saving these answers?",
                        (ViewModel.UnsavedAnswersProblem ?? "The inventory answers file could not be written.") +
                        "\n\nIf Lore closes now, what was answered or deleted in the Inventory view since the last " +
                        "successful save is lost. Answers saved earlier are not affected.");
                    dialog.PrimaryButtonText = "Try again";
                    dialog.SecondaryButtonText = "Close without saving";
                    dialog.CloseButtonText = "Keep Lore open";
                    dialog.DefaultButton = ContentDialogButton.Primary;
                    answer = await dialog.ShowAsync();
                }
                finally
                {
                    _dialogOpen = false;
                }
                if (answer == ContentDialogResult.None) return; // stay open
                if (answer == ContentDialogResult.Secondary) break;
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"Saving the inventory answers before closing failed: {ex}");
        }
        _closeNow = true;
        Close();
    }

    private ContentDialog NewDialog(string title, object content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            RequestedTheme = ElementTheme.Dark,
            XamlRoot = Root.XamlRoot,
        };
        App.Current.Accent.Attach(dialog);
        return dialog;
    }

    // ── File and Chart menus: charts ──────────────────────────────────────────

    private async void NewChart_Click(object sender, RoutedEventArgs e)
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        try
        {
            var dialog = new AddChartDialog(ViewModel.Cities, ViewModel.Hospitals, ViewModel.EnsureHospitalsLoadedAsync()) { XamlRoot = Root.XamlRoot };
            App.Current.Accent.Attach(dialog);
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && dialog.Result is { } chart)
                await ViewModel.AddCustomChartAsync(chart);
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    private async void EditChart_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedCelebrity is not { } existing || !ViewModel.SelectedIsCustom || _dialogOpen) return;
        _dialogOpen = true;
        try
        {
            var dialog = new AddChartDialog(ViewModel.Cities, ViewModel.Hospitals, ViewModel.EnsureHospitalsLoadedAsync(), existing) { XamlRoot = Root.XamlRoot };
            App.Current.Accent.Attach(dialog);
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && dialog.Result is { } chart)
                await ViewModel.UpdateCustomChartAsync(chart);
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    // Deleting a chart can't be undone from inside the app, so ask first.
    private async void DeleteChart_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedCelebrity is not { } chart || !ViewModel.SelectedIsCustom || _dialogOpen) return;
        _dialogOpen = true;
        try
        {
            var confirm = NewDialog($"Delete {chart.Name}?", "This removes the chart from My Charts. It can't be undone from inside Lore.");
            confirm.PrimaryButtonText = "Delete";
            confirm.CloseButtonText = "Cancel";
            confirm.DefaultButton = ContentDialogButton.Close;
            if (await confirm.ShowAsync() == ContentDialogResult.Primary)
                await ViewModel.DeleteSelectedCommand.ExecuteAsync(null);
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    // ── File, Edit and Help menus ─────────────────────────────────────────────

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Activate();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Find_Click(object sender, RoutedEventArgs e)
    {
        ShowChartsPane(true);
        SearchBox.Focus(FocusState.Keyboard);
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            ViewModel.SearchText = sender.Text;
    }

    private void Legend_Click(object sender, RoutedEventArgs e) => ShowView(_view == "Legend" ? _lastChartView : "Legend");

    private async void About_Click(object sender, RoutedEventArgs e)
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        try
        {
            string version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
            string build = version.Contains('+') ? version[(version.IndexOf('+') + 1)..] : "";
            var text = new TextBlock
            {
                Style = (Style)Application.Current.Resources["JupiterBodyTextStyle"],
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                Text = $"Lore\nPart of Jupiter\nVersion {version.Split('+')[0]}" +
                       (build.Length > 0 ? $"\nBuild {build[..Math.Min(build.Length, 12)]}" : "") +
                       "\nGNU Affero General Public License 3.0" +
                       "\nCalculations by the Swiss Ephemeris (Astrodienst), under the same license.",
            };
            var dialog = NewDialog("About Lore", text);
            dialog.CloseButtonText = "Close";
            await dialog.ShowAsync();
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    public bool HasNotice(string? notice) => !string.IsNullOrEmpty(notice);

    private void Notice_CloseButtonClick(InfoBar sender, object args) => ViewModel.Notice = null;

    // ── View menu ─────────────────────────────────────────────────────────────

    private void ChartsPane_Click(object sender, RoutedEventArgs e) => ShowChartsPane(ChartsPaneItem.IsChecked);

    private void ShowChartsPane(bool show)
    {
        if (!show && ChartsColumn.ActualWidth > 0)
            _preferences.ChartsPaneWidth = ChartsColumn.ActualWidth;
        _preferences.ChartsPaneVisible = show;
        ChartsPaneItem.IsChecked = show;
        ChartsPane.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ChartsSplitter.Visibility = ChartsPane.Visibility;
        ChartsColumn.MinWidth = show ? 240 : 0;
        ChartsColumn.Width = show ? new GridLength(_preferences.ChartsPaneWidth) : new GridLength(0);
    }

    private void ReadingLarger_Click(object sender, RoutedEventArgs e) => StepReading(+1);
    private void ReadingSmaller_Click(object sender, RoutedEventArgs e) => StepReading(-1);
    private void ReadingDefault_Click(object sender, RoutedEventArgs e) => SetReading(ReadingText.DefaultStep);

    private void StepReading(int by) => SetReading(ReadingText.Current.Step + by);

    private void SetReading(int step)
    {
        ReadingText.Current.Step = step;
        _preferences.ReadingSize = ReadingText.Current.Step;
        _preferences.Save();
    }

    private void FullScreen_Click(object sender, RoutedEventArgs e) => AppWindow.SetPresenter(
        AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen ? AppWindowPresenterKind.Overlapped : AppWindowPresenterKind.FullScreen);

    // ── People from the Charts list into the comparison ─────────────────────

    public static string TakeName(string name) => $"Add {name} to the comparison";

    private void TakePerson_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Celebrity person }) ViewModel.SynastryVM.Take(person);
    }

    // The menu works from any view, and goes to the Synastry view to show what it did.
    private void CompareWith_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: Celebrity person }) return;
        ViewModel.SynastryVM.CompareWith(person);
        ShowView("Synastry");
    }

    private void AddAsThird_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: Celebrity person }) return;
        ViewModel.SynastryVM.AddAsThird(person);
        ShowView("Synastry");
    }

    private void ViewMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: string view }) ShowView(view);
    }

    private void ViewBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem?.Tag is string view && view != _view) ShowView(view);
    }

    // The view on show, and the last one that was not the Legend (the one to go back to).
    private string _view = "Chart";
    private string _lastChartView = "Chart";

    private void ShowView(string view)
    {
        var item = ViewBar.Items.FirstOrDefault(i => i.Tag as string == view) ?? ViewBar.Items[0];
        view = (string)item.Tag;
        _view = view;
        bool legend = view == "Legend";
        if (!legend) _lastChartView = view;

        ViewModel.ShowLegend = legend;
        ViewModel.ShowReport = view == "Report";
        ViewModel.ShowWorksheet = view == "Worksheet";
        ViewModel.ShowDaily = view == "Daily";
        ViewModel.ShowForecast = view == "Forecast";
        ViewModel.ShowTiming = view == "Timing";
        ViewModel.ShowSynastry = view == "Synastry";
        ViewModel.ShowInventory = view == "Inventory";

        ChartViewControl.Visibility = Shown(view == "Chart");
        ReportViewControl.Visibility = Shown(view == "Report");
        WorksheetViewControl.Visibility = Shown(view == "Worksheet");
        DailyViewControl.Visibility = Shown(view == "Daily");
        ForecastViewControl.Visibility = Shown(view == "Forecast");
        TimingViewControl.Visibility = Shown(view == "Timing");
        SynastryViewControl.Visibility = Shown(view == "Synastry");
        InventoryViewControl.Visibility = Shown(view == "Inventory");
        WritersViewControl.Visibility = Shown(view == "Writers");
        LegendViewControl.Visibility = Shown(legend);

        // Writers starts without a natal-chart anchor and uses the whole workspace.
        // Its library search is inside the view; the Charts pane preference is retained.
        bool writers = view == "Writers";
        Grid.SetColumn(ViewWorkspace, writers ? 0 : 2);
        Grid.SetColumnSpan(ViewWorkspace, writers ? 3 : 1);
        ChartsPane.Visibility = ChartsSplitter.Visibility = Shown(!writers && _preferences.ChartsPaneVisible);
        if (!writers) ShowChartsPane(_preferences.ChartsPaneVisible);

        ViewBar.SelectedItem = item;
        if (!legend) ViewModel.RememberView(view);
    }

    private static Visibility Shown(bool show) => show ? Visibility.Visible : Visibility.Collapsed;

    private void OpenWriterChart(NatalChart chart)
    {
        var vm = new ChartViewModel(); vm.Show(chart, null);
        var root = new Grid { RequestedTheme = ElementTheme.Dark,
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["JupiterWindowBrush"] };
        root.Children.Add(new ChartView { ViewModel = vm });
        var window = new Window { Title = $"{chart.Celebrity.Name} — Writers — Lore", Content = root };
        window.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        window.AppWindow.TitleBar.PreferredTheme = TitleBarTheme.Dark;
        double scale = GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0;
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int width = Math.Min(workArea.Width, (int)(1100 * scale)), height = Math.Min(workArea.Height, (int)(800 * scale));
        window.AppWindow.MoveAndResize(new RectInt32(workArea.X + (workArea.Width - width) / 2,
            workArea.Y + (workArea.Height - height) / 2, width, height));
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(800 * scale);
            presenter.PreferredMinimumHeight = (int)(560 * scale);
        }
        App.Current.Accent.Attach(root);
        _writerWindows.Add(window); window.Closed += (_, _) => _writerWindows.Remove(window);
        window.Activate();
    }

    private async void ExportWriterBrief(WriterBrief brief, bool pdf)
    {
        if (_exporting) return;
        _exporting = true;
        try
        {
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = string.Concat(brief.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) };
            picker.FileTypeChoices.Add(pdf ? "PDF document" : "Text document", [pdf ? ".pdf" : ".txt"]);
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            if (await picker.PickSaveFileAsync() is not { } file) return;
            byte[] bytes = await Task.Run(() => pdf ? DailyExportService.WritersToPdf(brief) : System.Text.Encoding.UTF8.GetBytes(WriterInterpreter.ToText(brief)));
            await FileIO.WriteBytesAsync(file, bytes);
            ViewModel.StatusMessage = $"Saved {file.Name}.";
        }
        catch (Exception ex) { ViewModel.StatusMessage = $"The Writers brief could not be exported: {ex.Message}"; }
        finally { _exporting = false; }
    }

    private void WriterExport_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.WritersVM.Brief is not { } brief)
        { ViewModel.StatusMessage = "Open a Writers reading before exporting it."; return; }
        string name = ViewModel.WritersVM.BriefName.Trim();
        ExportWriterBrief(brief with { Title = name.Length > 0 ? name : brief.Title }, (sender as MenuFlyoutItem)?.Tag as string == "pdf");
    }

    // ── Export ────────────────────────────────────────────────────────────────

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: string kind }) await ExportAsync(kind);
    }

    // One export at a time: a second, started while the first is still being written,
    // would be competing with it for the same drawing surface and status line.
    private bool _exporting;

    private async Task ExportAsync(string kind)
    {
        if (_exporting)
        {
            ViewModel.StatusMessage = "An export is still being written. Try again when it has finished.";
            return;
        }
        _exporting = true;
        try
        {
            await ExportCoreAsync(kind);
        }
        finally
        {
            _exporting = false;
        }
    }

    private async Task ExportCoreAsync(string kind)
    {
        try
        {
            // Everything the export will use is taken now, before the save dialog is shown:
            // while that dialog is open another chart may be selected, a calculation may
            // finish or answers be deleted, and the file must not end up with one person's
            // name and another's contents. From here on only `request` is read, never the
            // view models (see ExportRequest).
            if (ExportRequest.Prepare(ViewModel, kind, out string refusal) is not { } request)
            {
                ViewModel.StatusMessage = refusal;
                return;
            }
            var chart = request.Chart;

            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeChoices.Add(request.Label, [request.Extension]);
            picker.SuggestedFileName = request.SuggestedName;

            var file = await picker.PickSaveFileAsync();
            if (file is null) return; // user cancelled

            ViewModel.StatusMessage = $"Exporting {file.Name}…";
            // The wheel first, for the kinds that have one. (Drawn here: the drawing
            // code shares its fonts and device with the wheels on screen.)
            byte[]? wheel = kind switch
            {
                "pdf" or "png" => await ExportService.RenderChartPngAsync(chart),
                "sheetpng" => await ExportService.RenderChartSheetPngAsync(chart),
                "dailypdf" or "dailypng" => await ExportService.RenderTransitWheelPngAsync(chart, request.Daily!),
                "timingpdf" when request.Timing!.Return is { } solar => await ExportService.RenderChartPngAsync(solar.Chart, showVerdict: false),
                "synastrypdf" or "synastrypng" => await ExportService.RenderBiWheelPngAsync(request.Comparison!),
                "davisonpdf" or "davisonpng" => await ExportService.RenderChartPngAsync(request.Davison!.Chart, showVerdict: false),
                "davisonsheetpng" => await ExportService.RenderChartSheetPngAsync(request.Davison!.Chart, showVerdict: false),
                "trinepdf" or "trinepng" => await ExportService.RenderTrioWheelPngAsync(request.Trio!.Trio),
                _ => null,
            };

            // Then the document itself, off the UI thread: laying out a PDF of a dozen
            // pages takes long enough to freeze the window if it is done on it.
            byte[] bytes = await Task.Run(() => kind switch
            {
                "pdf"  => ChartPdf.ToPdf(chart, request.Report, wheel!, request.Worksheet, request.Sensitivity),
                "png" or "sheetpng" or "dailypng" or "synastrypng" or "davisonpng" or "davisonsheetpng" or "trinepng" => wheel!,
                "json" => ChartDataExport.ToJson(chart),
                "xml"  => ChartDataExport.ToXml(chart),
                "worksheettxt" => WorksheetService.ToText(request.Worksheet ?? WorksheetService.Build(chart), request.Sensitivity),
                "dailypdf" => DailyExportService.ToPdf(request.Daily!, wheel!),
                "dailytxt" => DailyExportService.ToText(request.Daily!),
                "forecastpdf" => DailyExportService.ForecastToPdf(request.Forecast!),
                "forecasttxt" => DailyInterpreter.ForecastToText(request.Forecast!),
                "timingpdf" => DailyExportService.TimingToPdf(request.Timing!, wheel),
                "timingtxt" => TimingService.ToText(request.Timing!),
                "synastrypdf" => SynastryExportService.ToPdf(request.Comparison!, request.Synastry!, wheel!),
                "davisonpdf" => SynastryExportService.DavisonToPdf(request.Comparison!, request.Davison!, wheel!,
                    WorksheetService.Build(request.Davison!.Chart)),
                "davisontxt" => WorksheetService.ToText(WorksheetService.Build(request.Davison!.Chart)),
                "trinepdf" => SynastryExportService.TrioToPdf(request.Trio!, wheel!),
                "trinetxt" => SynastryExportService.TrioToText(request.Trio!),
                "inventorytxt" or "charactertxt" or "mirrortxt" => request.Text!,
                _      => throw new ArgumentOutOfRangeException(nameof(kind)),
            });

            await FileIO.WriteBytesAsync(file, bytes);
            ViewModel.StatusMessage = $"Exported {file.Name}";
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = "Export failed.";
            ViewModel.Notice = $"The export could not be written: {ex.Message} Nothing else is affected; try again, or choose another folder.";
            Diagnostics.Log($"Export {kind} failed: {ex}");
        }
    }

    // ── My Charts as a file: for a backup, or to move them to another PC ──────

    private async void ExportMyCharts_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Taken before the dialog opens, like the other exports, and from the file,
            // which may hold charts saved in another Lore window since this one opened.
            var (bytes, count) = await ViewModel.ExportMyChartsAsync();
            if (count == 0)
            {
                ViewModel.StatusMessage = "There are no charts of your own to export yet.";
                return;
            }

            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeChoices.Add("Lore charts", [".json"]);
            picker.SuggestedFileName =
                $"Lore - My Charts {DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}";

            var file = await picker.PickSaveFileAsync();
            if (file is null) return; // user cancelled

            await FileIO.WriteBytesAsync(file, bytes);
            ViewModel.StatusMessage = $"Exported {count} of your charts to {file.Name}";
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = "Export failed.";
            ViewModel.Notice = $"My Charts could not be exported: {ex.Message} Your saved charts are not affected.";
            Diagnostics.Log($"My Charts export failed: {ex}");
        }
    }

    private async void ImportMyCharts_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add(".json");

            var file = await picker.PickSingleFileAsync();
            if (file is null) return; // user cancelled

            await ViewModel.ImportMyChartsAsync(file.Path);
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = "Import failed.";
            ViewModel.Notice = $"The charts could not be imported: {ex.Message} Your saved charts are not affected.";
            Diagnostics.Log($"My Charts import failed: {ex}");
        }
    }
}
