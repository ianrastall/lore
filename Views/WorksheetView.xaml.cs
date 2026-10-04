using Lore.Models;
using Lore.ViewModels;
using Microsoft.UI;
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
            _viewModel = value;
            value.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ChartViewModel.Worksheet))
                {
                    Bindings.Update();
                    BuildAspectGrid();
                }
            };
            Bindings.Update();
            BuildAspectGrid();
        }
    }

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
        var line = new SolidColorBrush(Color.FromArgb(60, 180, 180, 220));

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
                FontSize = 16,
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
                        Text = aspect.Type.Symbol(),
                        FontSize = 15,
                        Foreground = new SolidColorBrush(AspectColors[(int)aspect.Type]),
                        HorizontalAlignment = HorizontalAlignment.Center,
                    });
                    stack.Children.Add(new TextBlock
                    {
                        Text = aspect.OrbText,
                        FontSize = 10,
                        Opacity = 0.8,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    });
                    cell.Child = stack;
                    ToolTipService.SetToolTip(cell,
                        $"{points[col].Name} {aspect.Type.ToString().ToLowerInvariant()} {points[row].Name}, " +
                        $"{aspect.OrbText[..^2]} from exact, {(aspect.Applying ? "applying" : "separating")}");
                }
                Place(cell, row, col);
            }
        }
    }

    private void Place(FrameworkElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        AspectGrid.Children.Add(element);
    }

    // x:Bind helper.
    public Visibility VisIf(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
}
