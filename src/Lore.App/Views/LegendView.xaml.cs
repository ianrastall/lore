using System;
using System.Collections.Generic;
using System.Linq;
using Lore.Models;
using Lore.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Lore.Views;

// A group of legend items, shown under a header in the list.
public sealed class LegendGroup : List<LegendItem>
{
    public string Key { get; init; } = "";
    public LegendGroup(string key, IEnumerable<LegendItem> items) : base(items) => Key = key;
}

// Full-page reference: a grouped list of every glyph/colour/term on the left, and a
// fuller explanation of the clicked item on the right.
public sealed partial class LegendView : UserControl
{
    // The entries, read once from Data\legend.json (they were once written out in this
    // file; they are content, and are now kept and checked as the other corpora are).
    private static readonly Lazy<LegendService> Legend =
        new(() => new LegendService(System.IO.Path.Combine(AppContext.BaseDirectory, "Data", "legend.json")));

    private static IReadOnlyList<LegendItem> Items => Legend.Value.Items;

    public LegendView()
    {
        InitializeComponent();

        var groups = Items
            .GroupBy(i => i.Group)
            .Select(g => new LegendGroup(g.Key, g))
            .ToList();
        GroupedItems.Source = groups;

        if (Items.Count > 0)
        {
            ShowDetail(Items[0]);
            ItemsList.SelectedItem = Items[0];
        }
    }

    private void ItemsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ItemsList.SelectedItem is LegendItem item)
            ShowDetail(item);
    }

    private void ShowDetail(LegendItem item)
    {
        DetailSymbol.Text = item.Symbol;
        DetailName.Text = item.Name;
        DetailGroup.Text = item.Group;
        BuildDetail(item.Detail);

        if (item.ColorHex is { } hex)
        {
            DetailSwatch.Background = new SolidColorBrush(ParseHex(hex));
            DetailSwatch.Visibility = Visibility.Visible;
        }
        else
        {
            DetailSwatch.Visibility = Visibility.Collapsed;
        }
    }

    // Renders a Detail string into styled blocks. Lightweight markup:
    //   • blocks are separated by a blank line ("\n\n")
    //   • a block beginning "## " is a sub-heading
    //   • a block whose lines all begin "• " (or "- ") is a bullet list
    //   • "**bold**" marks bold text inline, anywhere
    private void BuildDetail(string detail)
    {
        DetailContent.Children.Clear();
        foreach (var raw in detail.Split(new[] { "\n\n" }, StringSplitOptions.None))
        {
            var block = raw.Trim('\n', ' ');
            if (block.Length == 0) continue;

            if (block.StartsWith("## ", StringComparison.Ordinal))
            {
                var head = new TextBlock
                {
                    Style = TextStyle("JupiterSectionHeadingTextStyle"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 8, 0, 0),
                };
                AddInlines(head, block.Substring(3));
                DetailContent.Children.Add(head);
            }
            else if (IsBulletBlock(block))
            {
                foreach (var line in block.Split('\n'))
                {
                    var t = line.TrimStart();
                    if (t.Length == 0) continue;
                    DetailContent.Children.Add(BulletRow(t.Substring(2))); // drop "• " / "- "
                }
            }
            else
            {
                var para = ReadingBlock();
                AddInlines(para, block.Replace("\n", " "));
                DetailContent.Children.Add(para);
            }
        }
    }

    private static bool IsBulletBlock(string block)
    {
        bool any = false;
        foreach (var line in block.Split('\n'))
        {
            var t = line.TrimStart();
            if (t.Length == 0) continue;
            if (!t.StartsWith("• ", StringComparison.Ordinal) && !t.StartsWith("- ", StringComparison.Ordinal))
                return false;
            any = true;
        }
        return any;
    }

    private FrameworkElement BulletRow(string text)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var dot = ReadingBlock();
        dot.Text = "•";
        dot.Foreground = (Brush)Application.Current.Resources["JupiterSecondaryTextBrush"];
        Grid.SetColumn(dot, 0);

        var body = ReadingBlock();
        AddInlines(body, text);
        Grid.SetColumn(body, 1);

        grid.Children.Add(dot);
        grid.Children.Add(body);
        return grid;
    }

    // Adds text to a TextBlock, turning "**...**" spans into bold runs.
    private static void AddInlines(TextBlock tb, string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            int open = text.IndexOf("**", i, StringComparison.Ordinal);
            if (open < 0) { tb.Inlines.Add(new Run { Text = text.Substring(i) }); break; }
            if (open > i) tb.Inlines.Add(new Run { Text = text.Substring(i, open - i) });

            int close = text.IndexOf("**", open + 2, StringComparison.Ordinal);
            if (close < 0) { tb.Inlines.Add(new Run { Text = text.Substring(open) }); break; }

            var bold = new Bold();
            bold.Inlines.Add(new Run { Text = text.Substring(open + 2, close - open - 2) });
            tb.Inlines.Add(bold);
            i = close + 2;
        }
    }

    private static Style TextStyle(string key) => (Style)Application.Current.Resources[key];

    // Long-form text, at the size the reader has chosen for the readings.
    private static TextBlock ReadingBlock() => new()
    {
        Style = TextStyle("JupiterBodyTextStyle"),
        FontSize = ReadingText.Current.FontSize,
        LineHeight = ReadingText.Current.LineHeight,
        TextWrapping = TextWrapping.Wrap,
        IsTextSelectionEnabled = true,
    };

    private static Color ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        return Color.FromArgb(255,
            Convert.ToByte(hex.Substring(0, 2), 16),
            Convert.ToByte(hex.Substring(2, 2), 16),
            Convert.ToByte(hex.Substring(4, 2), 16));
    }

}
