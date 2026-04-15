using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Models;

namespace Lyricify.Lyrics.App.Services;

/// <summary>
/// Drives real-time lyric synchronization.
/// <list type="bullet">
///   <item>Runs a 100 ms timer and calls <see cref="SyncHelper.GetSyncResult(LyricsData, int)"/> on each tick.</item>
///   <item>Estimates the current playback position locally to avoid hammering the Spotify API.</item>
///   <item>Raises <see cref="SyncResultUpdated"/> for UI consumers (ViewModels, overlay views).</item>
/// </list>
/// </summary>
public class LyricsSyncService : IDisposable
{
    private LyricsData? _lyricsData;
    private int _lastKnownPositionMs;
    private DateTime _lastPositionTimestamp = DateTime.MinValue;
    private bool _isPlaying;

    private Timer? _timer;
    private readonly object _stateLock = new();

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Raised on every 100 ms tick while a lyrics dataset is loaded.</summary>
    public event EventHandler<SyncResult>? SyncResultUpdated;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads a new lyrics dataset and starts (or resets) the sync timer.
    /// </summary>
    public void SetLyrics(LyricsData? lyricsData)
    {
        lock (_stateLock)
        {
            _lyricsData = lyricsData;
            _lastKnownPositionMs = 0;
            _lastPositionTimestamp = DateTime.MinValue;
        }

        EnsureTimerRunning();
    }

    /// <summary>
    /// Updates the reference playback position.  Call this whenever a new position
    /// is received from the Spotify polling service so local estimation stays accurate.
    /// </summary>
    public void UpdatePosition(int positionMs, bool isPlaying)
    {
        lock (_stateLock)
        {
            _lastKnownPositionMs = positionMs;
            _lastPositionTimestamp = DateTime.UtcNow;
            _isPlaying = isPlaying;
        }
    }

    /// <summary>Stops the sync timer and releases resources.</summary>
    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>Explicitly starts the timer (called by the ViewModel).</summary>
    public void Start() => EnsureTimerRunning();

    public void Dispose() => Stop();

    // ── Timer ─────────────────────────────────────────────────────────────────

    private void EnsureTimerRunning()
    {
        if (_timer is not null) return;
        _timer = new Timer(OnTick, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
    }

    private void OnTick(object? _)
    {
        LyricsData? lyricsData;
        int estimatedPositionMs;

        lock (_stateLock)
        {
            lyricsData = _lyricsData;
            estimatedPositionMs = EstimatePosition();
        }

        if (lyricsData is null) return;

        var syncResult = SyncHelper.GetSyncResult(lyricsData, estimatedPositionMs);
        SyncResultUpdated?.Invoke(this, syncResult);
    }

    /// <summary>
    /// Extrapolates the current position from the last known API value and elapsed wall-clock time.
    /// When paused, the position stays constant.
    /// </summary>
    private int EstimatePosition()
    {
        if (!_isPlaying || _lastPositionTimestamp == DateTime.MinValue)
            return _lastKnownPositionMs;

        var elapsed = (int)(DateTime.UtcNow - _lastPositionTimestamp).TotalMilliseconds;
        return _lastKnownPositionMs + elapsed;
    }
}
