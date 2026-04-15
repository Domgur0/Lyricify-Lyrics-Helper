using Lyricify.Lyrics.App.Services;
using Lyricify.Lyrics.App.ViewModels;
using Lyricify.Lyrics.App.Views;
using Microsoft.Extensions.Logging;

namespace Lyricify.Lyrics.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // ── Services ──────────────────────────────────────────────────────────
        builder.Services.AddSingleton<SpotifyOAuthService>();
        builder.Services.AddSingleton<SpotifyNowPlayingService>();
        builder.Services.AddSingleton<LyricsService>();
        builder.Services.AddSingleton<LyricsSyncService>();

        // ── ViewModels ────────────────────────────────────────────────────────
        builder.Services.AddTransient<LyricsViewModel>();

        // ── Views ─────────────────────────────────────────────────────────────
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<LyricsPage>();
        builder.Services.AddTransient<SettingsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
