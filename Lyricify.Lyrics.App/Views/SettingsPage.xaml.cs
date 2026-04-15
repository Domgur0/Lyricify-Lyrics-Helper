using Lyricify.Lyrics.App.Services;
using Lyricify.Lyrics.Helpers;

namespace Lyricify.Lyrics.App.Views;

public partial class SettingsPage : ContentPage
{
    private readonly SpotifyOAuthService _oauthService;

    // Preference keys (must match those in SpotifyOAuthService)
    private const string PrefClientId = "spotify_client_id";
    private const string PrefClientSecret = "spotify_client_secret";
    private const string PrefSpDc = "spotify_sp_dc";

    public SettingsPage()
    {
        InitializeComponent();
        _oauthService = IPlatformApplication.Current!.Services.GetRequiredService<SpotifyOAuthService>();
    }

    // ── Save all Spotify credentials ──────────────────────────────────────────

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
        var confirm = await DisplayAlertAsync("Sign out", "Sign out of Spotify?", "Yes", "No");
        if (!confirm) return;

        _oauthService.SignOut();
        Application.Current!.Windows[0].Page = new NavigationPage(new LoginPage());
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

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Restore persisted settings.
        ClientIdEntry.Text = Preferences.Get(PrefClientId, string.Empty);
        ClientSecretEntry.Text = Preferences.Get(PrefClientSecret, string.Empty);
        SpDcEntry.Text = Preferences.Get(PrefSpDc, string.Empty);

        FontSizeSlider.Value = Preferences.Get("lyrics_font_size", 17);
        OpacitySlider.Value = Preferences.Get("overlay_opacity", 0.9f);

        // Re-apply sp_dc to the provider in case the app was restarted.
        var spDc = Preferences.Get(PrefSpDc, string.Empty);
        if (!string.IsNullOrWhiteSpace(spDc))
            ProviderHelper.SpotifyApi.SetSpDc(spDc);
    }
}
