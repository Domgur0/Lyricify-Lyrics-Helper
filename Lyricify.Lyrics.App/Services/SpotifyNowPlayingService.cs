using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Providers.Web.Spotify;

namespace Lyricify.Lyrics.App.Services;

/// <summary>
/// Polls <c>GET /v1/me/player/currently-playing</c> every 500 ms and raises
/// events when the track changes or the playback position updates.
/// </summary>
public class SpotifyNowPlayingService : IDisposable
{
    private readonly SpotifyOAuthService _oauthService;

    private CancellationTokenSource? _cts;

    // ── State ─────────────────────────────────────────────────────────────────

    /// <summary>Spotify track ID of the currently playing track, or null when idle.</summary>
    public string? CurrentTrackId { get; private set; }

    /// <summary>Most recently reported playback position in milliseconds.</summary>
    public int PositionMs { get; private set; }

    /// <summary>Whether Spotify reports the player as active.</summary>
    public bool IsPlaying { get; private set; }

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Raised when a new track starts (or playback resumes on a different track).</summary>
    public event EventHandler<SpotifyCurrentlyPlayingItem?>? TrackChanged;

    /// <summary>Raised on every successful poll (even if the same track is playing).</summary>
    public event EventHandler<NowPlayingState>? StateUpdated;

    // ── Constructor ───────────────────────────────────────────────────────────

    public SpotifyNowPlayingService(SpotifyOAuthService oauthService)
    {
        _oauthService = oauthService;
    }

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
    }

    public void Dispose() => Stop();

    // ── Poll loop ─────────────────────────────────────────────────────────────

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var token = await _oauthService.EnsureValidAccessTokenAsync();
                var state = await ProviderHelper.SpotifyApi.GetCurrentlyPlayingAsync(token);

                var newTrackId = state?.Item?.Id;
                var newPositionMs = state?.ProgressMs ?? 0;
                var newIsPlaying = state?.IsPlaying ?? false;

                // Notify if the track changed.
                if (newTrackId != CurrentTrackId)
                {
                    CurrentTrackId = newTrackId;
                    TrackChanged?.Invoke(this, state?.Item);
                }

                PositionMs = newPositionMs;
                IsPlaying = newIsPlaying;

                StateUpdated?.Invoke(this, new NowPlayingState(newTrackId, newPositionMs, newIsPlaying, state?.Item));
            }
            catch (UnauthorizedAccessException)
            {
                // Token expired and refresh failed – stop polling and surface the error.
                Stop();
                return;
            }
            catch (Exception)
            {
                // Network error: wait a bit longer before retrying.
                await Task.Delay(2000, ct).ConfigureAwait(false);
                continue;
            }

            await Task.Delay(500, ct).ConfigureAwait(false);
        }
    }
}

/// <summary>Snapshot of the Spotify player state delivered by <see cref="SpotifyNowPlayingService.StateUpdated"/>.</summary>
public sealed record NowPlayingState(
    string? TrackId,
    int PositionMs,
    bool IsPlaying,
    SpotifyCurrentlyPlayingItem? Item);
