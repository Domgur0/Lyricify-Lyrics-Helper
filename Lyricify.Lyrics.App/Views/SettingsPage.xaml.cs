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
    private const string PrefOverlayLyricColor = "overlay_lyric_color";
#if ANDROID
    private const string PrefFlymeStatusBarEnabled = Lyricify.Lyrics.App.Platforms.Android.FlymeStatusBarService.PrefFlymeStatusBarEnabled;
    private const string PrefSuperLyricEnabled = Lyricify.Lyrics.App.Platforms.Android.SuperLyricService.PrefSuperLyricEnabled;
#endif

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
        if (e.Value)
            return;

        Preferences.Set("overlay_should_run", false);
#if ANDROID
        var context = Platform.CurrentActivity ?? Android.App.Application.Context;
        var intent = new Android.Content.Intent(context, typeof(Lyricify.Lyrics.App.Platforms.Android.LyricsOverlayService));
        context.StopService(intent);
#endif
    }

    private void OnFlymeStatusBarToggled(object sender, ToggledEventArgs e)
    {
#if ANDROID
        Preferences.Set(PrefFlymeStatusBarEnabled, e.Value);
        var context = Platform.CurrentActivity ?? Android.App.Application.Context;
        if (e.Value)
        {
            // Start the standalone service only when the overlay isn't already handling Flyme.
            if (!Lyricify.Lyrics.App.Platforms.Android.LyricsOverlayService.IsRunning
                && !Lyricify.Lyrics.App.Platforms.Android.FlymeStatusBarService.IsRunning)
            {
#pragma warning disable CA1416
                context.StartForegroundService(new Android.Content.Intent(
                    context, typeof(Lyricify.Lyrics.App.Platforms.Android.FlymeStatusBarService)));
#pragma warning restore CA1416
            }
        }
        else
        {
            context.StopService(new Android.Content.Intent(
                context, typeof(Lyricify.Lyrics.App.Platforms.Android.FlymeStatusBarService)));
        }
#endif
    }

    private void OnSuperLyricEnabledToggled(object sender, ToggledEventArgs e)
    {
#if ANDROID
        Preferences.Set(PrefSuperLyricEnabled, e.Value);
        var context = Platform.CurrentActivity ?? Android.App.Application.Context;
        if (e.Value)
        {
            // Start the service only when the overlay isn't already handling SuperLyric.
            if (!Lyricify.Lyrics.App.Platforms.Android.LyricsOverlayService.IsRunning
                && !Lyricify.Lyrics.App.Platforms.Android.SuperLyricService.IsRunning)
            {
#pragma warning disable CA1416
                context.StartForegroundService(new Android.Content.Intent(
                    context, typeof(Lyricify.Lyrics.App.Platforms.Android.SuperLyricService)));
#pragma warning restore CA1416
            }
        }
        else
        {
            context.StopService(new Android.Content.Intent(
                context, typeof(Lyricify.Lyrics.App.Platforms.Android.SuperLyricService)));
        }
#endif
    }

    private void OnUnlockOverlayClicked(object sender, EventArgs e)
    {
#if ANDROID
        var context = Platform.CurrentActivity ?? Android.App.Application.Context;
        var intent = new Android.Content.Intent(context, typeof(Lyricify.Lyrics.App.Platforms.Android.LyricsOverlayService));
        intent.SetAction(Lyricify.Lyrics.App.Platforms.Android.LyricsOverlayService.ActionUnlockOverlay);
        context.StartService(intent);
#endif
    }

    // ── Floating lyrics color ─────────────────────────────────────────────────

    private void OnOverlayColorSelected(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not string hexColor)
            return;

        Preferences.Set(PrefOverlayLyricColor, hexColor);
        UpdateColorSwatchSelection(hexColor);

#if ANDROID
        if (Lyricify.Lyrics.App.Platforms.Android.LyricsOverlayService.IsRunning)
        {
            var context = Platform.CurrentActivity ?? Android.App.Application.Context;
            var intent = new Android.Content.Intent(context, typeof(Lyricify.Lyrics.App.Platforms.Android.LyricsOverlayService));
            intent.SetAction(Lyricify.Lyrics.App.Platforms.Android.LyricsOverlayService.ActionSetColor);
            intent.PutExtra(Lyricify.Lyrics.App.Platforms.Android.LyricsOverlayService.ExtraColorHex, hexColor);
            context.StartService(intent);
        }
#endif
    }

    private void UpdateColorSwatchSelection(string selectedHex)
    {
        // The swatch borders are declared in XAML in the same order as LyricsOverlaySettings.PaletteHexColors.
        var swatches = new Border[] { ColorSwatchRed, ColorSwatchBlue, ColorSwatchGreen, ColorSwatchGold, ColorSwatchPurple };
        var palette = LyricsOverlaySettings.PaletteHexColors;
        for (var i = 0; i < swatches.Length; i++)
        {
            var hex = i < palette.Length ? palette[i] : string.Empty;
            swatches[i].Stroke = string.Equals(hex, selectedHex, StringComparison.OrdinalIgnoreCase)
                ? Color.FromArgb("#FFFFFF")
                : Color.FromArgb("#00000000");
        }
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
#if ANDROID
        FlymeStatusBarSwitch.IsToggled = Preferences.Get(PrefFlymeStatusBarEnabled, false);
        SuperLyricEnabledSwitch.IsToggled = Preferences.Get(PrefSuperLyricEnabled, false);
#endif
        UpdateColorSwatchSelection(Preferences.Get(PrefOverlayLyricColor, LyricsOverlaySettings.DefaultLyricColorHex));

        // Re-apply sp_dc to the provider in case the app was restarted.
        var spDc = Preferences.Get(PrefSpDc, string.Empty);
        if (!string.IsNullOrWhiteSpace(spDc))
            ProviderHelper.SpotifyApi.SetSpDc(spDc);

        RefreshLoginStatus();

        // If a JVM crash was detected on startup, prompt the user to export the log.
        // Delay slightly so the page is fully rendered before showing a dialog.
        if (App.HasPendingCrashReport)
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400), () => _ = PromptCrashExportAsync());
    }

    private async Task PromptCrashExportAsync()
    {
        bool export = await DisplayAlertAsync(
            "检测到崩溃记录",
            "上次运行时应用崩溃，已捕获崩溃日志。是否立即导出以便反馈？",
            "导出",
            "稍后");
        if (export)
            await ExportLogAsync();
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

    // ── Diagnostics ───────────────────────────────────────────────────────────

    private async void OnExportLogClicked(object sender, EventArgs e) =>
        await ExportLogAsync();

    private async Task ExportLogAsync()
    {
        var logService = AppLogService.Current;

        // Prefer the persisted (disk) log because it spans multiple sessions
        // and survives hard crashes.  Fall back to the in-memory snapshot.
        var persistedText = logService is not null
            ? await logService.ReadPersistedLogAsync()
            : string.Empty;

        var logText = !string.IsNullOrWhiteSpace(persistedText)
            ? persistedText
            : (logService?.ExportText() ?? "(Log service unavailable – restart the app and try again.)");

        var fileName = $"lyricify-log-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt";
        var filePath = Path.Combine(FileSystem.CacheDirectory, fileName);

        try
        {
            await File.WriteAllTextAsync(filePath, logText);
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Lyricify Log",
                File = new ShareFile(filePath, "text/plain"),
            });
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Export Failed", $"Failed to save log file: {ex.Message}", "OK");
        }
    }
}
