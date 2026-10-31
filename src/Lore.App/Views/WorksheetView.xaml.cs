using Lore.Models;
using Lore.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Lore.Views;

public sealed partial class WorksheetView : UserControl
{
    private ChartViewModel _viewModel = new();

    public ChartViewModel ViewModel
    {
        get => _viewModel;
        set
        {
            if (ReferenceEquals(_viewModel, value)) return;
            _viewModel.PropertyChanged -= OnViewModelChanged;
            _viewModel = value;
            value.PropertyChanged += OnViewModelChanged;
            Bindings.Update();
            MarginBox.Value = value.UncertaintyMinutes;
            BuildAspectGrid();
            BuildSections();
        }
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChartViewModel.Worksheet))
        {
            Bindings.Update();
            BuildAspectGrid();
            BuildSections();
        }
        else if (e.PropertyName is nameof(ChartViewModel.Sensitivity) or nameof(ChartViewModel.CanTestTime)
                 or nameof(ChartViewModel.SensitivityHeading))
        {
            Bindings.Update();
        }
        else if (e.PropertyName == nameof(ChartViewModel.UncertaintyMinutes))
        {
            MarginBox.Value = ViewModel.UncertaintyMinutes;
        }
    }

    private void MarginBox_ValueChanged(object? sender, EventArgs e) => ViewModel.UncertaintyMinutes = MarginBox.Value;

    public WorksheetView()
    {
        InitializeComponent();
    }

    // The same colours the wheel draws its aspect lines in.
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

    private const double CellWidth = 62, CellHeight = 40;

    // A triangular grid: each point heads a column and a row, and the cell where two
    // meet holds their aspect, if they make one.
    private void BuildAspectGrid()
    {
        AspectGrid.Children.Clear();
        AspectGrid.RowDefinitions.Clear();
        AspectGrid.ColumnDefinitions.Clear();
        if (ViewModel.Worksheet is not { } sheet) return;

        var points = sheet.Points;
        var line = Brush("JupiterSeparatorBrush");

        for (int i = 0; i < points.Count; i++)
        {
            AspectGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CellHeight) });
            AspectGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CellWidth) });
        }

        for (int row = 0; row < points.Count; row++)
        {
            // The diagonal cell names the point; cells to its left hold its aspects.
            var label = new TextBlock
            {
                Text = points[row].Symbol,
                Style = TextStyle("JupiterPaneHeadingTextStyle"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(label, points[row].Name);
            Place(new Border { Child = label, BorderBrush = line, BorderThickness = new Thickness(1) }, row, row);

            for (int col = 0; col < row; col++)
            {
                var cell = new Border { BorderBrush = line, BorderThickness = new Thickness(1, 0, 0, 1) };
                if (sheet.AspectBetween(points[row], points[col]) is { } aspect)
                {
                    var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    stack.Children.Add(new TextBlock
                    {
                        // An out-of-sign aspect carries a star (explained under the grid).
                        Text = aspect.OutOfSign ? aspect.Type.Symbol() + "*" : aspect.Type.Symbol(),
                        Style = TextStyle("JupiterBodyTextStyle"),
                        Foreground = new SolidColorBrush(AspectColors[(int)aspect.Type]),
                        HorizontalAlignment = HorizontalAlignment.Center,
                    });
                    stack.Children.Add(new TextBlock
                    {
                        Text = aspect.OrbText,
                        Style = TextStyle("JupiterCaptionTextStyle"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                    });
                    cell.Child = stack;
                    ToolTipService.SetToolTip(cell,
                        $"{points[col].Name} {aspect.Type.Name().ToLowerInvariant()} {points[row].Name}, " +
                        $"{aspect.OrbText[..^2]} from exact, {(aspect.Applying ? "applying" : "separating")}" +
                        (aspect.OutOfSign ? " — out of sign" : ""));
                }
                Place(cell, row, col);
            }
        }
    }

    // The further measurements: under each heading a small table, or a list of
    // label-and-value lines where the section has no column headings.
    private void BuildSections()
    {
        SectionsPanel.Children.Clear();
        if (ViewModel.Worksheet is not { } sheet) return;

        foreach (var section in sheet.Sections)
        {
            if (section.Rows.Count == 0) continue;
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = section.Title, Style = TextStyle("JupiterSectionHeadingTextStyle") });

            bool list = section.Headers.Count == 0;
            int columns = list ? 2 : section.Headers.Count;
            var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left, ColumnSpacing = 24 };
            for (int i = 0; i < columns; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = list && i == 0 ? new GridLength(150) : GridLength.Auto,
                    MinWidth = list ? 0 : 70,
                });

            int row = 0;
            void Add(IReadOnlyList<string> cells, bool header)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (int i = 0; i < columns; i++)
                {
                    var cell = new TextBlock
                    {
                        Text = i < cells.Count ? cells[i] : "",
                        Style = TextStyle(header ? "JupiterCaptionStrongTextStyle" : "JupiterBodyTextStyle"),
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 640,
                        Padding = new Thickness(0, 0, 0, 4),
                        IsTextSelectionEnabled = !header,
                    };
                    if (!header && list && i == 0) cell.Foreground = Brush("JupiterSecondaryTextBrush");
                    Grid.SetRow(cell, row);
                    Grid.SetColumn(cell, i);
                    grid.Children.Add(cell);
                }
                row++;
            }

            if (!list)
            {
                Add(section.Headers, header: true);
                var rule = new Border { BorderBrush = Brush("JupiterSeparatorBrush"), BorderThickness = new Thickness(0, 0, 0, 1) };
                Grid.SetColumnSpan(rule, columns);
                grid.Children.Add(rule);
            }
            foreach (var cells in section.Rows) Add(cells, header: false);
            panel.Children.Add(grid);

            if (section.Note.Length > 0)
                panel.Children.Add(new TextBlock
                {
                    Text = section.Note, Style = TextStyle("JupiterCaptionTextStyle"), TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 790, HorizontalAlignment = HorizontalAlignment.Left,
                });
            SectionsPanel.Children.Add(panel);
        }
    }

    private void Place(FrameworkElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        AspectGrid.Children.Add(element);
    }

    private static Style TextStyle(string key) => (Style)Application.Current.Resources[key];
    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    // x:Bind helper.
    public Visibility VisIf(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
}
