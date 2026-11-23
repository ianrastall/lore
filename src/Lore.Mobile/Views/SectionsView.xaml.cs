using Lore.Models;

namespace Lore.Mobile.Views;

public partial class SectionsView : ContentView
{
    public static readonly BindableProperty SectionsProperty = BindableProperty.Create(
        nameof(Sections), typeof(IReadOnlyList<DailySection>), typeof(SectionsView), Array.Empty<DailySection>());

    public SectionsView() => InitializeComponent();

    public IReadOnlyList<DailySection> Sections
    {
        get => (IReadOnlyList<DailySection>)GetValue(SectionsProperty);
        set => SetValue(SectionsProperty, value);
    }
}
