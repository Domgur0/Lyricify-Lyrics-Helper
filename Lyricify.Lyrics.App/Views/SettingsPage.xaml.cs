using Lyricify.Lyrics.App.Services;
using Lyricify.Lyrics.Helpers;

namespace Lyricify.Lyrics.App.Views;

public partial class SettingsPage : ContentPage
{
    private SpotifyOAuthService? _oauthService;

    // Preference keys (must match those in SpotifyOAuthService)
    private const string PrefClientId = "spotify_client_id";
    private const string PrefClientSecret = "spotify_client_secret";
    private const string PrefSpDc = "spotify_sp_dc";
    private const string PrefOverlayEnabled = "overlay_enabled";

    public SettingsPage()
    {
        InitializeComponent();
    }

    // ── Save all Spotify credentials ──────────────────────────────────────────

    private async void OnSignInClicked(object sender, EventArgs e)
    {
        if (!EnsureOAuthService()) return;

        try
        {
            await _oauthService!.AuthorizeAsync();
            RefreshLoginStatus();
            await DisplayAlertAsync("Signed in", "Spotify login successful.", "OK");
        }
        catch (TaskCanceledException)
        {
            await DisplayAlertAsync("Cancelled", "Spotify login was cancelled.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Login failed", ex.Message, "OK");
        }
    }

    private async void OnSaveCredentialsClicked(object sender, EventArgs e)
    {
        var clientId = ClientIdEntry.Text?.Trim() ?? string.Empty;
        var clientSecret = ClientSecretEntry.Text?.Trim() ?? string.Empty;
        var spDc = SpDcEntry.Text?.Trim() ?? string.Empty;

        // Validate Client ID (required).
        if (string.IsNullOrWhiteSpace(clientId))
        {
            await DisplayAlertAsync("Missing Client ID", "Please enter your Spotify Client ID.", "OK");
            return;
        }

        // Persist credentials.
        Preferences.Set(PrefClientId, clientId);

        if (!string.IsNullOrWhiteSpace(clientSecret))
            Preferences.Set(PrefClientSecret, clientSecret);
        else
            Preferences.Remove(PrefClientSecret);

        if (!string.IsNullOrWhiteSpace(spDc))
        {
            Preferences.Set(PrefSpDc, spDc);
            ProviderHelper.SpotifyApi.SetSpDc(spDc);
        }
        else
        {
            Preferences.Remove(PrefSpDc);
        }

        await DisplayAlertAsync("Saved", "Spotify credentials saved.", "OK");
    }

    // ── Sign out ──────────────────────────────────────────────────────────────

    private async void OnSignOutClicked(object sender, EventArgs e)
    {
        if (!EnsureOAuthService()) return;

        var confirm = await DisplayAlertAsync("Sign out", "Sign out of Spotify?", "Yes", "No");
        if (!confirm) return;

        _oauthService!.SignOut();
        Application.Current!.Windows[0].Page = new AppShell();
    }

    // ── Font size ─────────────────────────────────────────────────────────────

    private void OnFontSizeChanged(object sender, ValueChangedEventArgs e)
    {
        var size = (int)e.NewValue;
        FontSizeLabel.Text = size.ToString();
        Preferences.Set("lyrics_font_size", size);
    }

    // ── Overlay opacity (Android) ─────────────────────────────────────────────

    private void OnOverlayOpacityChanged(object sender, ValueChangedEventArgs e)
    {
        Preferences.Set("overlay_opacity", (float)e.NewValue);
    }

    private void OnOverlayEnabledToggled(object sender, ToggledEventArgs e)
    {
        Preferences.Set(PrefOverlayEnabled, e.Value);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        EnsureOAuthService();

        // Restore persisted settings.
        ClientIdEntry.Text = Preferences.Get(PrefClientId, string.Empty);
        ClientSecretEntry.Text = Preferences.Get(PrefClientSecret, string.Empty);
        SpDcEntry.Text = Preferences.Get(PrefSpDc, string.Empty);

        FontSizeSlider.Value = Preferences.Get("lyrics_font_size", 17);
        OpacitySlider.Value = Preferences.Get("overlay_opacity", 0.9f);
        OverlayEnabledSwitch.IsToggled = Preferences.Get(PrefOverlayEnabled, false);

        // Re-apply sp_dc to the provider in case the app was restarted.
        var spDc = Preferences.Get(PrefSpDc, string.Empty);
        if (!string.IsNullOrWhiteSpace(spDc))
            ProviderHelper.SpotifyApi.SetSpDc(spDc);

        RefreshLoginStatus();
    }

    // ── Login status ──────────────────────────────────────────────────────────

    private void RefreshLoginStatus()
    {
        if (_oauthService is null || !_oauthService.HasValidToken)
        {
            LoginStatusFrame.IsVisible = false;
            return;
        }

        LoginStatusLabel.Text = "✓ 已登录 Spotify";

        var expiry = _oauthService.TokenExpiresAt;
        if (expiry is { } dt)
        {
            var diff = dt - DateTimeOffset.UtcNow;
            if (diff <= TimeSpan.Zero)
            {
                LoginExpiryLabel.Text = "令牌已过期，将在下次请求时自动刷新";
            }
            else
            {
                var local = dt.LocalDateTime;
                var isSameDay = local.Date == DateTime.Today;
                LoginExpiryLabel.Text = isSameDay
                    ? $"令牌有效期至今天 {local:HH:mm}"
                    : $"令牌有效期至 {local:yyyy-MM-dd HH:mm}";
            }
        }
        else
        {
            LoginExpiryLabel.Text = string.Empty;
        }

        LoginStatusFrame.IsVisible = true;
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
