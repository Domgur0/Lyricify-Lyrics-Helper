using Lyricify.Lyrics.App.Services;
using Lyricify.Lyrics.Helpers;

namespace Lyricify.Lyrics.App.Views;

public partial class SettingsPage : ContentPage
{
    private readonly SpotifyOAuthService _oauthService;

    public SettingsPage()
    {
        InitializeComponent();
        _oauthService = IPlatformApplication.Current!.Services.GetRequiredService<SpotifyOAuthService>();
    }

    // ── sp_dc ─────────────────────────────────────────────────────────────────

    private void OnSaveSpDcClicked(object sender, EventArgs e)
    {
        var spDc = SpDcEntry.Text?.Trim();
        ProviderHelper.SpotifyApi.SetSpDc(spDc);

        if (!string.IsNullOrWhiteSpace(spDc))
            Preferences.Set("spotify_sp_dc", spDc);

        DisplayAlert("Saved", "sp_dc cookie saved.", "OK");
    }

    // ── Sign out ──────────────────────────────────────────────────────────────

    private async void OnSignOutClicked(object sender, EventArgs e)
    {
        var confirm = await DisplayAlert("Sign out", "Sign out of Spotify?", "Yes", "No");
        if (!confirm) return;

        _oauthService.SignOut();
        Application.Current!.MainPage = new NavigationPage(new LoginPage());
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
        FontSizeSlider.Value = Preferences.Get("lyrics_font_size", 17);
        OpacitySlider.Value = Preferences.Get("overlay_opacity", 0.9f);

        var spDc = Preferences.Get("spotify_sp_dc", string.Empty);
        if (!string.IsNullOrWhiteSpace(spDc))
        {
            SpDcEntry.Text = spDc;
            ProviderHelper.SpotifyApi.SetSpDc(spDc);
        }
    }
}
