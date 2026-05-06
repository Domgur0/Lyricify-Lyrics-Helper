using Android.Content;
using Android.Content.PM;
using Android.Media;
using Android.Media.Session;
using System.Linq;

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

    /// <summary>
    /// Preference key for the whitelist of app package names considered in
    /// compatibility mode.  The value is a semicolon-separated list of package
    /// names (e.g. <c>com.spotify.music;com.netease.cloudmusic</c>).
    /// When the preference is empty or absent, all media sessions are considered.
    /// </summary>
    public const string PrefCompatibilityModeWhitelist = "compatibility_mode_whitelist";

    /// <summary>
    /// Parses the persisted whitelist preference into a set of package names.
    /// Returns an empty set when no whitelist is configured.
    /// </summary>
    public static HashSet<string> GetWhitelist()
    {
        var raw = Preferences.Get(PrefCompatibilityModeWhitelist, string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return new HashSet<string>(
            raw.Split(';', StringSplitOptions.RemoveEmptyEntries)
               .Select(s => s.Trim())
               .Where(s => s.Length > 0),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Saves <paramref name="packages"/> as the whitelist preference.
    /// </summary>
    public static void SaveWhitelist(IEnumerable<string> packages)
    {
        var value = string.Join(";", packages
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(value))
            Preferences.Remove(PrefCompatibilityModeWhitelist);
        else
            Preferences.Set(PrefCompatibilityModeWhitelist, value);
    }

    /// <summary>
    /// Returns all user-visible installed applications (apps that appear in the
    /// launcher / app drawer), sorted alphabetically by display name.
    /// Each entry is a <c>(PackageName, Label)</c> tuple.
    /// </summary>
    public static List<(string PackageName, string Label)> GetInstalledApps()
    {
        var context = global::Android.App.Application.Context;
        var pm = context.PackageManager;
        if (pm is null) return new List<(string, string)>();

        var selfPackage = context.PackageName ?? string.Empty;

        // Query every app that has a launcher icon (visible in the app drawer).
        var launchIntent = new Intent(Intent.ActionMain);
        launchIntent.AddCategory(Intent.CategoryLauncher);
        var resolveInfos = pm.QueryIntentActivities(launchIntent, PackageInfoFlags.MetaData)
                           ?? new List<ResolveInfo>();

        return resolveInfos
            .Where(r => r.ActivityInfo?.PackageName is not null
                        && r.ActivityInfo.PackageName != selfPackage)
            .Select(r => (
                PackageName: r.ActivityInfo!.PackageName!,
                Label: r.LoadLabel(pm)?.ToString() ?? r.ActivityInfo.PackageName!
            ))
            .DistinctBy(x => x.PackageName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Returns the display label for <paramref name="packageName"/>, or the package
    /// name itself when the app is not found.
    /// </summary>
    public static string GetAppLabel(string packageName)
    {
        try
        {
            var pm = global::Android.App.Application.Context.PackageManager;
            if (pm is null) return packageName;
            var appInfo = pm.GetApplicationInfo(packageName, PackageInfoFlags.MetaData);
            return appInfo?.LoadLabel(pm)?.ToString() ?? packageName;
        }
        catch (Exception ex)
        {
            global::System.Diagnostics.Debug.WriteLine(
                $"[MediaControllerNowPlayingService] GetAppLabel({packageName}) failed: {ex.GetType().Name}: {ex.Message}");
            return packageName;
        }
    }

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

        // Apply whitelist: if one is configured, only consider sessions from those packages.
        var whitelist = GetWhitelist();
        IList<MediaController> candidates = whitelist.Count > 0
            ? controllers.Where(c => whitelist.Contains(c.PackageName ?? string.Empty)).ToList()
            : controllers;

        if (candidates.Count == 0)
        {
            // No whitelisted session active → treat as stopped.
            if (_currentTrackKey is not null)
            {
                _currentTrackKey = null;
                TrackChanged?.Invoke(this, null);
            }
            return;
        }

        // Prefer a controller that is actively playing; otherwise take the first.
        var controller = candidates
            .FirstOrDefault(c => c.PlaybackState?.State == PlaybackStateCode.Playing)
            ?? candidates[0];

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
