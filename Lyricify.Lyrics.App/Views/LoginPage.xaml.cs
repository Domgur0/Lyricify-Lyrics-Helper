using Lyricify.Lyrics.App.Services;
using Lyricify.Lyrics.Helpers;

namespace Lyricify.Lyrics.App.Views;

public partial class LoginPage : ContentPage
{
    private readonly SpotifyOAuthService _oauthService;

    public LoginPage()
    {
        InitializeComponent();
        _oauthService = IPlatformApplication.Current!.Services.GetRequiredService<SpotifyOAuthService>();
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        LoginButton.IsEnabled = false;
        Spinner.IsRunning = true;
        Spinner.IsVisible = true;
        StatusLabel.IsVisible = false;

        // Save sp_dc if provided.
        var spDc = SpDcEntry.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(spDc))
            ProviderHelper.SpotifyApi.SetSpDc(spDc);

        try
        {
            await _oauthService.AuthorizeAsync();

            // Authorization succeeded – switch to the main shell.
            Application.Current!.MainPage = new AppShell();
        }
        catch (TaskCanceledException)
        {
            ShowError("Login was cancelled.");
        }
        catch (Exception ex)
        {
            ShowError($"Login failed: {ex.Message}");
        }
        finally
        {
            LoginButton.IsEnabled = true;
            Spinner.IsRunning = false;
            Spinner.IsVisible = false;
        }
    }

    private void ShowError(string message)
    {
        StatusLabel.Text = message;
        StatusLabel.IsVisible = true;
    }
}
