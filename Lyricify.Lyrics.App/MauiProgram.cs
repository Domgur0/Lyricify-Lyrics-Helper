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

        // ── Logging ───────────────────────────────────────────────────────────
        // Create AppLogService early so the logging provider can reference it
        // and so non-DI paths (App.xaml.cs, LyricsOverlayService) can use
        // AppLogService.Current immediately.
        var appLogService = new AppLogService();
        builder.Services.AddSingleton(appLogService);
        builder.Logging.AddProvider(new AppLogProvider(appLogService));

        // Persist log entries to disk so they survive hard crashes.
        try { appLogService.InitPersistence(FileSystem.AppDataDirectory); }
        catch { /* Non-critical if the path is not yet available. */ }

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // ── Services ──────────────────────────────────────────────────────────
        builder.Services.AddSingleton<SpotifyOAuthService>();
        builder.Services.AddSingleton<SpotifyNowPlayingService>();
        builder.Services.AddSingleton<LyricsService>();
        builder.Services.AddSingleton<LyricsSyncService>();

        // ── ViewModels ────────────────────────────────────────────────────────
        builder.Services.AddSingleton<LyricsViewModel>();

        // ── Views ─────────────────────────────────────────────────────────────
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<LyricsPage>();
        builder.Services.AddTransient<SettingsPage>();

        return builder.Build();
    }
}
