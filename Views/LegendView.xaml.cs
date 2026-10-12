using System;
using System.Collections.Generic;
using System.Linq;
using Lore.Models;
using Lore.Services;
using Microsoft.UI;
using Microsoft.UI.Text;
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
// fuller explanation of the clicked item on the right. Raises CloseRequested when the
// Back button is pressed.
public sealed partial class LegendView : UserControl
{
    public event EventHandler? CloseRequested;

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

    private void Back_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => CloseRequested?.Invoke(this, EventArgs.Empty);

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
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 6, 0, 0),
                };
                if (ThemeBrush("AccentTextFillColorPrimaryBrush") is { } accent) head.Foreground = accent;
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
                var para = new TextBlock { FontSize = 15, LineHeight = 23, TextWrapping = TextWrapping.Wrap };
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
        var grid = new Grid { Margin = new Thickness(2, 0, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var dot = new TextBlock { Text = "•", FontSize = 15, LineHeight = 23 };
        if (ThemeBrush("TextFillColorSecondaryBrush") is { } sec) dot.Foreground = sec;
        Grid.SetColumn(dot, 0);

        var body = new TextBlock { FontSize = 15, LineHeight = 23, TextWrapping = TextWrapping.Wrap };
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

    private static Brush? ThemeBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out var v) && v is Brush b ? b : null;

    private static Color ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        return Color.FromArgb(255,
            Convert.ToByte(hex.Substring(0, 2), 16),
            Convert.ToByte(hex.Substring(2, 2), 16),
            Convert.ToByte(hex.Substring(4, 2), 16));
    }

}
