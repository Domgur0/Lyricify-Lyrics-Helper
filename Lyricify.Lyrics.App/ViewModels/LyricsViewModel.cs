using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lyricify.Lyrics.App.Services;
using Lyricify.Lyrics.Models;
using Lyricify.Lyrics.Providers.Web.Spotify;

namespace Lyricify.Lyrics.App.ViewModels;

/// <summary>
/// Shared view-model consumed by <c>LyricsPage</c> (iOS) and the
/// Android overlay service.  Orchestrates now-playing polling,
/// lyrics fetching and sync-result delivery.
/// </summary>
public partial class LyricsViewModel : ObservableObject, IDisposable
{
    private readonly SpotifyOAuthService _oauthService;
    private readonly SpotifyNowPlayingService _nowPlayingService;
    private readonly LyricsService _lyricsService;
    private readonly LyricsSyncService _syncService;

    // ── Observable properties ─────────────────────────────────────────────────

    [ObservableProperty]
    private string _trackTitle = string.Empty;

    [ObservableProperty]
    private string _artistName = string.Empty;

    [ObservableProperty]
    private string _albumArtUrl = string.Empty;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private int _currentLineIndex = -1;

    [ObservableProperty]
    private int _currentSyllableIndex = -1;

    [ObservableProperty]
    private double _lineProgress;

    [ObservableProperty]
    private double _syllableProgress;

    [ObservableProperty]
    private string _currentLineText = string.Empty;

    [ObservableProperty]
    private string _nextLineText = string.Empty;

    [ObservableProperty]
    private List<ILineInfo> _lyricLines = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoLyricsHint))]
    [NotifyPropertyChangedFor(nameof(ShowNotPlayingHint))]
    private bool _isLoadingLyrics;

    /// <summary>
    /// True when lyrics were loaded for the current track.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoLyricsHint))]
    private bool _hasLyrics;

    /// <summary>
    /// True when a track has been loaded (even if lyrics haven't been found yet).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoLyricsHint))]
    [NotifyPropertyChangedFor(nameof(ShowNotPlayingHint))]
    private bool _isTrackLoaded;

    /// <summary>
    /// True when no track is playing – shows the "waiting for playback" hint.
    /// </summary>
    public bool ShowNotPlayingHint => !IsTrackLoaded && !IsLoadingLyrics;

    /// <summary>
    /// True when a track is playing but no lyrics are available – shows the
    /// "No lyrics available" hint in the UI.
    /// </summary>
    public bool ShowNoLyricsHint => IsTrackLoaded && !HasLyrics && !IsLoadingLyrics;

    // ── Constructor ───────────────────────────────────────────────────────────

    public LyricsViewModel(
        SpotifyOAuthService oauthService,
        SpotifyNowPlayingService nowPlayingService,
        LyricsService lyricsService,
        LyricsSyncService syncService)
    {
        _oauthService = oauthService;
        _nowPlayingService = nowPlayingService;
        _lyricsService = lyricsService;
        _syncService = syncService;

        _nowPlayingService.TrackChanged += OnTrackChanged;
        _nowPlayingService.StateUpdated += OnStateUpdated;
        _syncService.SyncResultUpdated += OnSyncResultUpdated;
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    public void StartPolling()
    {
        _nowPlayingService.Start();
        _syncService.Start();
    }

    [RelayCommand]
    public void StopPolling()
    {
        _nowPlayingService.Stop();
        _syncService.Stop();
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private async void OnTrackChanged(object? sender, SpotifyCurrentlyPlayingItem? item)
    {
        if (item is null)
        {
            // Player idle
            TrackTitle = string.Empty;
            ArtistName = string.Empty;
            AlbumArtUrl = string.Empty;
            LyricLines = new();
            HasLyrics = false;
            IsTrackLoaded = false;
            _syncService.SetLyrics(null);
            return;
        }

        IsTrackLoaded = true;
        TrackTitle = item.Name ?? string.Empty;
        ArtistName = string.Join(", ", item.Artists?.Select(a => a.Name).Where(n => !string.IsNullOrWhiteSpace(n))
            ?? Enumerable.Empty<string>());
        AlbumArtUrl = item.Album?.Images?.OrderByDescending(img => img.Width).FirstOrDefault()?.Url
            ?? string.Empty;

        IsLoadingLyrics = true;
        HasLyrics = false;

        try
        {
            var lyricsData = await _lyricsService.GetLyricsAsync(
                item.Id,
                item.Name ?? string.Empty,
                item.Artists?.FirstOrDefault()?.Name ?? string.Empty,
                item.DurationMs);

            LyricLines = lyricsData?.Lines ?? new List<ILineInfo>();
            HasLyrics = LyricLines.Count > 0;
            _syncService.SetLyrics(lyricsData);
        }
        finally
        {
            IsLoadingLyrics = false;
        }
    }

    private void OnStateUpdated(object? sender, NowPlayingState state)
    {
        IsPlaying = state.IsPlaying;
        _syncService.UpdatePosition(state.PositionMs, state.IsPlaying);
    }

    private void OnSyncResultUpdated(object? sender, SyncResult result)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            CurrentLineIndex = result.LineIndex;
            CurrentSyllableIndex = result.SyllableIndex;
            LineProgress = result.LineProgress;
            SyllableProgress = result.SyllableProgress;
            CurrentLineText = result.CurrentLine?.Text ?? string.Empty;

            // Look ahead one line for sub-title display.
            if (result.LineIndex >= 0 && result.LineIndex + 1 < LyricLines.Count)
                NextLineText = LyricLines[result.LineIndex + 1].Text;
            else
                NextLineText = string.Empty;
        });
    }

    public void Dispose()
    {
        _nowPlayingService.TrackChanged -= OnTrackChanged;
        _nowPlayingService.StateUpdated -= OnStateUpdated;
        _syncService.SyncResultUpdated -= OnSyncResultUpdated;
    }
}
