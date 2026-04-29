using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Lyricify.Lyrics.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Threading;

namespace Lyricify.Lyrics.App.Platforms.Android;

/// <summary>
/// A lightweight foreground <see cref="Service"/> that publishes real-time lyrics
/// to the SuperLyric Xposed module independently of the floating overlay window.
/// <para>
/// When <see cref="LyricsOverlayService"/> is running it already handles SuperLyric
/// publishing; this service is intended for use when the overlay is disabled but the
/// user still wants SuperLyric integration.
/// </para>
/// Start via <see cref="Context.StartForegroundService"/>.
/// Stop via <see cref="Context.StopService"/>.
/// </summary>
[Service(
    Exported = false,
    ForegroundServiceType = (global::Android.Content.PM.ForegroundService)ForegroundServiceTypeSpecialUseValue)]
public class SuperLyricService : Service
{
    public const string PrefSuperLyricEnabled = "superlyric_enabled";

    // Shared with LyricsOverlayService — both services use the same notification channel.
    private const string ChannelId = "lyricify_overlay";
    private const int NotificationId = 1003;
    private const int ForegroundServiceTypeSpecialUseValue = 0x40000000;
    private const global::Android.Content.PM.ForegroundService ForegroundServiceTypeSpecialUse =
        (global::Android.Content.PM.ForegroundService)ForegroundServiceTypeSpecialUseValue;

    private static int _isRunning;

    /// <summary>True when this service is currently running.</summary>
    public static bool IsRunning => Interlocked.CompareExchange(ref _isRunning, 0, 0) == 1;

    private LyricsViewModel? _viewModel;
    private SuperLyricPublisher? _superLyricPublisher;

    // ── Service lifecycle ─────────────────────────────────────────────────────

    public override void OnCreate()
    {
        base.OnCreate();
        Interlocked.Exchange(ref _isRunning, 1);

        // Start in foreground immediately to satisfy StartForegroundService contract.
        CreateNotificationChannelIfNeeded();
        var notification = BuildNotification();
        if (OperatingSystem.IsAndroidVersionAtLeast(34))
            StartForeground(NotificationId, notification, ForegroundServiceTypeSpecialUse);
        else
            StartForeground(NotificationId, notification);

        // Resolve the shared ViewModel from the MAUI DI container.
        var services = IPlatformApplication.Current?.Services
            ?? global::Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services;
        _viewModel = services?.GetService<LyricsViewModel>();
        if (_viewModel is null)
        {
            StopSelf();
            return;
        }

        _superLyricPublisher = new SuperLyricPublisher(_viewModel);
        _superLyricPublisher.Connect();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
        => StartCommandResult.Sticky;

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnDestroy()
    {
        Interlocked.Exchange(ref _isRunning, 0);

        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _superLyricPublisher?.Dispose();
        _superLyricPublisher = null;

        base.OnDestroy();
    }

    // ── ViewModel events ──────────────────────────────────────────────────────

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_superLyricPublisher is null || _viewModel is null) return;

        switch (e.PropertyName)
        {
            case nameof(LyricsViewModel.CurrentLineIndex):
                _superLyricPublisher.OnLineIndexChanged(_viewModel.CurrentLineIndex);
                break;

            case nameof(LyricsViewModel.IsTrackLoaded) when !_viewModel.IsTrackLoaded:
                _superLyricPublisher.OnPlaybackStopped();
                break;
        }
    }

    // ── Notification helpers ──────────────────────────────────────────────────

    private void CreateNotificationChannelIfNeeded()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;

        var manager = GetSystemService(NotificationService) as NotificationManager;
        if (manager is null) return;

        // Channel creation is idempotent — safe to call even if LyricsOverlayService
        // already created this channel.
        var channel = new NotificationChannel(
            ChannelId,
            "Lyricify overlay",
            NotificationImportance.Low)
        {
            Description = "Shown while Lyricify is running",
        };
        manager.CreateNotificationChannel(channel);
    }

#pragma warning disable CA1416 // Validate platform compatibility
    private Notification BuildNotification()
    {
        var pendingIntent = PendingIntent.GetActivity(
            this,
            0,
            new Intent(this, typeof(MainActivity)),
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        return new Notification.Builder(this, ChannelId)
            .SetContentTitle("Lyricify is running")
            .SetContentText("SuperLyric active")
            .SetSmallIcon(global::Android.Resource.Drawable.IcMediaPlay)
            .SetContentIntent(pendingIntent)
            .SetOngoing(true)
            .Build()!;
    }
#pragma warning restore CA1416
}
