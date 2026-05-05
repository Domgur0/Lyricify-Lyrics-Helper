using Android.Content;
using Android.Media;
using Android.Media.Session;

namespace Lyricify.Lyrics.App.Platforms.Android;

/// <summary>
/// Polls the system's active <see cref="MediaSession"/>s every 500 ms and raises
/// events when the track or playback state changes.
/// <para>
/// Requires the user to have granted <em>notification-listener</em> access to this
/// app (Settings → Apps → Special app access → Notification access).
/// </para>
/// </summary>
public sealed class MediaControllerNowPlayingService : IDisposable
{
    /// <summary>Preference key for the compatibility-mode enabled flag.</summary>
    public const string PrefCompatibilityModeEnabled = "compatibility_mode_enabled";

    private CancellationTokenSource? _cts;
    private string? _currentTrackKey; // "{title}|{artist}" of the last reported track

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised when the active track changes.
    /// A <c>null</c> value means no media is playing.
    /// </summary>
    public event EventHandler<MediaTrackInfo?>? TrackChanged;

    /// <summary>Raised on every poll tick with the current playback state.</summary>
    public event EventHandler<MediaPlaybackState>? StateUpdated;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>Starts the polling loop.</summary>
    public void Start()
    {
        if (_cts is { IsCancellationRequested: false }) return; // already running

        _cts = new CancellationTokenSource();
        _ = PollLoopAsync(_cts.Token);
    }

    /// <summary>Stops the polling loop.</summary>
    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        _currentTrackKey = null;
    }

    public void Dispose() => Stop();

    // ── Polling ───────────────────────────────────────────────────────────────

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                PollMediaSession();
            }
            catch (Exception ex)
            {
                // Log at debug level so permission/config problems are diagnosable
                // without impacting normal operation.
                global::System.Diagnostics.Debug.WriteLine(
                    $"[MediaControllerNowPlayingService] Poll error: {ex.GetType().Name}: {ex.Message}");
            }

            await Task.Delay(1000, ct).ConfigureAwait(false);
        }
    }

    private void PollMediaSession()
    {
        var context = global::Android.App.Application.Context;

        var sessionManager = context.GetSystemService(Context.MediaSessionService)
            as MediaSessionManager;
        if (sessionManager is null) return;

        // GetActiveSessions requires the calling package to have an active
        // NotificationListenerService.
        var componentName = new ComponentName(
            context.PackageName!,
            Java.Lang.Class.FromType(typeof(MediaNotificationListenerService)).Name!);

        IList<MediaController>? controllers;
        try
        {
            controllers = sessionManager.GetActiveSessions(componentName);
        }
        catch (Exception ex)
        {
            // Catches SecurityException (notification listener not granted) and any
            // other unexpected errors. Both are non-fatal – just skip this poll tick.
            global::System.Diagnostics.Debug.WriteLine(
                $"[MediaControllerNowPlayingService] GetActiveSessions failed: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        if (controllers is null || controllers.Count == 0)
        {
            // No active session → player stopped.
            if (_currentTrackKey is not null)
            {
                _currentTrackKey = null;
                TrackChanged?.Invoke(this, null);
            }
            return;
        }

        // Prefer a controller that is actively playing; otherwise take the first.
        var controller = controllers
            .FirstOrDefault(c => c.PlaybackState?.State == PlaybackStateCode.Playing)
            ?? controllers[0];

        var metadata = controller.Metadata;
        var playbackState = controller.PlaybackState;

        if (metadata is null) return;

        var title = metadata.GetString(MediaMetadata.MetadataKeyTitle) ?? string.Empty;
        var artist = metadata.GetString(MediaMetadata.MetadataKeyArtist)
            ?? metadata.GetString(MediaMetadata.MetadataKeyAlbumArtist)
            ?? string.Empty;
        var durationMs = (int)metadata.GetLong(MediaMetadata.MetadataKeyDuration);

        var trackKey = $"{title}|{artist}";
        if (trackKey != _currentTrackKey)
        {
            _currentTrackKey = trackKey;
            TrackChanged?.Invoke(this, new MediaTrackInfo(title, artist, durationMs));
        }

        var positionMs = (int)(playbackState?.Position ?? 0L);
        var isPlaying = playbackState?.State == PlaybackStateCode.Playing;
        StateUpdated?.Invoke(this, new MediaPlaybackState(positionMs, isPlaying));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when the user has granted notification-listener access
    /// to this application.
    /// </summary>
    public static bool HasNotificationListenerAccess()
    {
        var context = global::Android.App.Application.Context;
        var packageName = context.PackageName ?? string.Empty;
        var enabledListeners = global::Android.Provider.Settings.Secure.GetString(
            context.ContentResolver,
            "enabled_notification_listeners");
        return enabledListeners?.Contains(packageName) ?? false;
    }
}

/// <summary>Describes the media track currently playing in compatibility mode.</summary>
public sealed record MediaTrackInfo(string Title, string Artist, int DurationMs);

/// <summary>Playback state snapshot from the active media session.</summary>
public sealed record MediaPlaybackState(int PositionMs, bool IsPlaying);
