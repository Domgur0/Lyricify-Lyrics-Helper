using Lyricify.Lyrics.App.Services;

namespace Lyricify.Lyrics.App.Views;

public partial class LoginPage : ContentPage
{
    private SpotifyOAuthService? _oauthService;

    public LoginPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!EnsureOAuthService())
        {
            CredentialsMissingFrame.IsVisible = true;
            LoginButton.IsEnabled = false;
            ShowError("服务初始化中，请稍后重试。");
            return;
        }

        // Show a warning and disable the login button when no Client ID is saved.
        var missingCredentials = !_oauthService!.HasClientId;
        CredentialsMissingFrame.IsVisible = missingCredentials;
        LoginButton.IsEnabled = !missingCredentials;
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        if (!EnsureOAuthService())
        {
            ShowError("服务初始化中，请稍后重试。");
            return;
        }

        if (!_oauthService!.HasClientId)
        {
            ShowError("Please configure your Spotify Client ID in Settings first.");
            return;
        }

        LoginButton.IsEnabled = false;
        Spinner.IsRunning = true;
        Spinner.IsVisible = true;
        StatusLabel.IsVisible = false;

        try
        {
            await _oauthService.AuthorizeAsync();

            // Authorization succeeded – switch to the main shell.
            Application.Current!.Windows[0].Page = new AppShell();
        }
        catch (TaskCanceledException)
        {
            ShowError("Login was cancelled.");
        }
        catch (InvalidOperationException ex)
        {
            // Missing credentials — guide user to Settings.
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError($"Login failed: {ex.Message}");
        }
        finally
        {
            LoginButton.IsEnabled = _oauthService.HasClientId;
            Spinner.IsRunning = false;
            Spinner.IsVisible = false;
        }
    }

    private async void OnOpenSettingsClicked(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new SettingsPage());
    }

    private void ShowError(string message)
    {
        StatusLabel.Text = message;
        StatusLabel.IsVisible = true;
    }

    private bool EnsureOAuthService()
    {
        if (_oauthService is not null) return true;

        var services = Handler?.MauiContext?.Services
            ?? Application.Current?.Handler?.MauiContext?.Services
            ?? IPlatformApplication.Current?.Services;
        _oauthService = services?.GetService(typeof(SpotifyOAuthService)) as SpotifyOAuthService;

        return _oauthService is not null;
    }
}
