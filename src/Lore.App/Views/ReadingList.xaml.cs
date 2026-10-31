using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lore.Views;

public sealed partial class ReadingList : UserControl
{
    public static readonly DependencyProperty SectionsProperty = DependencyProperty.Register(
        nameof(Sections), typeof(object), typeof(ReadingList), new PropertyMetadata(null));

    public object? Sections
    {
        get => GetValue(SectionsProperty);
        set => SetValue(SectionsProperty, value);
    }

    public ReadingList()
    {
        InitializeComponent();
    }
}
