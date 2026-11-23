namespace Lore.Mobile.Views;

// Shown while Lore's engine is built. The first start after an install or update also
// unpacks Lore's files, which takes a few seconds more.
public sealed class LoadingPage : ContentPage
{
    private readonly Label _message;
    private readonly ActivityIndicator _spinner;

    public LoadingPage()
    {
        _spinner = new ActivityIndicator { IsRunning = true, HorizontalOptions = LayoutOptions.Center };
        _message = new Label { Text = "Starting Lore…", HorizontalTextAlignment = TextAlignment.Center };
        Content = new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Padding = 24,
            Spacing = 16,
            Children = { _spinner, _message },
        };
    }

    public void ShowProblem(string text) => MainThread.BeginInvokeOnMainThread(() =>
    {
        _spinner.IsRunning = false;
        _spinner.IsVisible = false;
        _message.Text = text;
    });
}
