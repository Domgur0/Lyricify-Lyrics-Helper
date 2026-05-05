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
/// to the Meizu Flyme status bar independently of the floating overlay window.
/// <para>
/// When <see cref="LyricsOverlayService"/> is running it already handles Flyme
/// publishing; this service is intended for use when the overlay is disabled but
/// the user still wants Flyme status-bar lyrics.
/// </para>
/// <para>
/// Implementation follows the Flyme notification contract described at
/// https://github.com/Moriafly/HiMoriafly/blob/main/docs/android-dev/flyme-lyrics-noti.md.
/// </para>
/// Start via <see cref="Context.StartForegroundService"/>.
/// Stop via <see cref="Context.StopService"/>.
/// </summary>
[Service(
    Exported = false,
    ForegroundServiceType = (global::Android.Content.PM.ForegroundService)ForegroundServiceTypeSpecialUseValue)]
public class FlymeStatusBarService : Service
{
    /// <summary>Preference key that enables/disables Flyme status-bar lyrics.</summary>
    public const string PrefFlymeStatusBarEnabled = "flyme_status_bar_enabled";

    // Shared with LyricsOverlayService — both services use the same notification channel.
    private const string ChannelId = "lyricify_overlay";
    // 1001 = LyricsOverlayService foreground, 1002 = LyricsOverlayService error,
    // 1003 = SuperLyricService foreground, 1004 = LyricsOverlayService Flyme ticker,
    // 1005 = this service's foreground notification, 1006 = Flyme ticker notification.
    private const int NotificationId = 1005;
    private const int FlymeNotificationId = 1006;
    private const int ForegroundServiceTypeSpecialUseValue = 0x40000000;
    private const global::Android.Content.PM.ForegroundService ForegroundServiceTypeSpecialUse =
        (global::Android.Content.PM.ForegroundService)ForegroundServiceTypeSpecialUseValue;

    private static int _isRunning;

    /// <summary>True when this service is currently running.</summary>
    public static bool IsRunning => Interlocked.CompareExchange(ref _isRunning, 0, 0) == 1;

    private LyricsViewModel? _viewModel;
    private FlymeStatusBarPublisher? _flymePublisher;

    /// <summary>
    /// Cancellation source for the 1-second post-track-change delay.
    /// Cancelled when a lyric-line update arrives before the delay expires, or when the
    /// service is destroyed.
    /// </summary>
    private CancellationTokenSource? _trackChangeCts;

    // ── Service lifecycle ─────────────────────────────────────────────────────

    public override void OnCreate()
    {
        base.OnCreate();
        Interlocked.Exchange(ref _isRunning, 1);

        // Start in foreground immediately to satisfy the StartForegroundService contract.
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

        _flymePublisher = new FlymeStatusBarPublisher(this, ChannelId, FlymeNotificationId);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Publish the current lyric immediately in case playback is already active.
        PublishCurrentLyric();
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
        => StartCommandResult.Sticky;

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnDestroy()
    {
        Interlocked.Exchange(ref _isRunning, 0);

        CancelAndDisposeCts(ref _trackChangeCts);

        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _flymePublisher?.Dispose();
        _flymePublisher = null;

        base.OnDestroy();
    }

    // ── ViewModel events ──────────────────────────────────────────────────────

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_viewModel is null) return;

        switch (e.PropertyName)
        {
            case nameof(LyricsViewModel.CurrentLineText):
                // Lyric line changed — cancel any pending track-change delay and publish now.
                CancelAndDisposeCts(ref _trackChangeCts);
                PublishCurrentLyric();
                break;

            case nameof(LyricsViewModel.TrackTitle):
            case nameof(LyricsViewModel.ArtistName):
                // Track changed — delay 1 second before publishing the ticker.
                // The Flyme documentation recommends this delay to avoid Android's
                // notification rate limiter dropping the ticker update immediately
                // after the track-metadata notification is sent.
                _ = PublishAfterTrackChangeDelayAsync();
                break;

            case nameof(LyricsViewModel.IsTrackLoaded) when !_viewModel.IsTrackLoaded:
                // Playback stopped — cancel any pending update and clear the ticker.
                CancelAndDisposeCts(ref _trackChangeCts);
                _flymePublisher?.Publish(null, ResolvePlaybackStatusIcon());
                break;
        }
    }

    /// <summary>
    /// Delays for 1 second (cancellable) and then publishes the current lyric to
    /// the Flyme status bar.  The delay prevents Android's notification rate limiter
    /// from dropping the ticker update when it arrives immediately after a track-change
    /// metadata notification.
    /// </summary>
    private async Task PublishAfterTrackChangeDelayAsync()
    {
        CancelAndDisposeCts(ref _trackChangeCts);
        var cts = new CancellationTokenSource();
        _trackChangeCts = cts;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cts.Token).ConfigureAwait(false);
            PublishCurrentLyric();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("LyricifyFlyme", $"Error in Flyme ticker delay: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_trackChangeCts, cts))
            {
                _trackChangeCts = null;
                cts.Dispose();
            }
        }
    }

    /// <summary>
    /// Thread-safe cancel + dispose of a <see cref="CancellationTokenSource"/> field.
    /// Sets the field to <c>null</c> after disposal.
    /// </summary>
    private static void CancelAndDisposeCts(ref CancellationTokenSource? cts)
    {
        var old = Interlocked.Exchange(ref cts, null);
        if (old is null) return;
        try { old.Cancel(); } catch { }
        old.Dispose();
    }

    private void PublishCurrentLyric()
    {
        var lyric = _viewModel?.CurrentLineText;
        _flymePublisher?.Publish(lyric, ResolvePlaybackStatusIcon());
    }

    private int ResolvePlaybackStatusIcon()
    {
        if (_viewModel?.IsPlaying == true)
            return global::Android.Resource.Drawable.IcMediaPause;
        return global::Android.Resource.Drawable.IcMediaPlay;
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
            .SetContentText("Flyme status-bar lyrics active")
            .SetSmallIcon(global::Android.Resource.Drawable.IcMediaPlay)
            .SetContentIntent(pendingIntent)
            .SetOngoing(true)
            .Build()!;
    }
#pragma warning restore CA1416
}
