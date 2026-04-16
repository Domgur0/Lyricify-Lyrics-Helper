using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lyricify.Lyrics.App.Services;
using Lyricify.Lyrics.Models;
using Lyricify.Lyrics.Providers.Web.Spotify;
using System.Threading;

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
    private int _trackLoadVersion;

    // ── Observable properties ─────────────────────────────────────────────────

    [ObservableProperty]
    private string _trackTitle = string.Empty;

    [ObservableProperty]
    private string _artistName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AlbumArtSource))]
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _lyricsStatusMessage = string.Empty;

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

    /// <summary>
    /// Album art shown in the header; falls back to a blank placeholder when idle.
    /// </summary>
    public ImageSource AlbumArtSource =>
        string.IsNullOrWhiteSpace(AlbumArtUrl) ? "music_note.svg" : AlbumArtUrl;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(LyricsStatusMessage);

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
        _nowPlayingService.AuthenticationFailed += OnAuthenticationFailed;
        _syncService.SyncResultUpdated += OnSyncResultUpdated;

        if (!_oauthService.HasValidToken)
            LyricsStatusMessage = "未登录，请长按封面进入设置登录";
        else
            LyricsStatusMessage = "未播放音乐";
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
        var requestVersion = Interlocked.Increment(ref _trackLoadVersion);

        if (item is null)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                // Player idle
                TrackTitle = string.Empty;
                ArtistName = string.Empty;
                AlbumArtUrl = string.Empty;
                LyricLines = new();
                HasLyrics = false;
                IsTrackLoaded = false;
                IsLoadingLyrics = false;
                CurrentLineText = string.Empty;
                NextLineText = string.Empty;
                _syncService.SetLyrics(null);
                LyricsStatusMessage = _oauthService.HasValidToken
                    ? "未播放音乐"
                    : "未登录，请长按封面进入设置登录";
            });
            return;
        }

        var trackTitle = item.Name ?? string.Empty;
        var artistName = item.Artists is null
            ? string.Empty
            : string.Join(", ", item.Artists.Select(a => a.Name).Where(n => !string.IsNullOrWhiteSpace(n)));
        var albumArtUrl = item.Album?.Images?.OrderByDescending(img => img.Width).FirstOrDefault()?.Url
            ?? string.Empty;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            IsTrackLoaded = true;
            TrackTitle = trackTitle;
            ArtistName = artistName;
            AlbumArtUrl = albumArtUrl;
            CurrentLineText = trackTitle;
            NextLineText = artistName;
            IsLoadingLyrics = true;
            HasLyrics = false;
            LyricsStatusMessage = string.Empty;
        });

        LyricsData? lyricsData = null;
        var trackId = item.Id;

        try
        {
            if (!string.IsNullOrWhiteSpace(trackId))
            {
                lyricsData = await _lyricsService.GetLyricsAsync(
                    trackId,
                    trackTitle,
                    item.Artists?.FirstOrDefault()?.Name ?? string.Empty,
                    item.DurationMs);
            }
        }
        catch (Exception)
        {
            lyricsData = null;
        }

        if (requestVersion != Volatile.Read(ref _trackLoadVersion))
            return;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (requestVersion != Volatile.Read(ref _trackLoadVersion))
                return;

            LyricLines = lyricsData?.Lines ?? new List<ILineInfo>();
            HasLyrics = LyricLines.Count > 0;
            _syncService.SetLyrics(lyricsData);
            if (!HasLyrics)
            {
                CurrentLineText = TrackTitle;
                NextLineText = ArtistName;
                LyricsStatusMessage = "未获取到歌词";
            }

            IsLoadingLyrics = false;
        });
    }

    partial void OnTrackTitleChanged(string value)
    {
        if (HasLyrics || string.IsNullOrWhiteSpace(value))
            return;
        CurrentLineText = value;
    }

    partial void OnArtistNameChanged(string value)
    {
        if (HasLyrics)
            return;
        NextLineText = value;
    }

    partial void OnHasLyricsChanged(bool value)
    {
        if (value)
            return;

        CurrentLineText = TrackTitle;
        NextLineText = ArtistName;
    }

    partial void OnLyricLinesChanged(List<ILineInfo> value)
    {
        if (value.Count > 0)
            return;

        if (IsTrackLoaded)
        {
            CurrentLineText = TrackTitle;
            NextLineText = ArtistName;
        }
    }

    private void OnStateUpdated(object? sender, NowPlayingState state)
    {
        IsPlaying = state.IsPlaying;
        _syncService.UpdatePosition(state.PositionMs, state.IsPlaying);
    }

    private void OnAuthenticationFailed(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            TrackTitle = string.Empty;
            ArtistName = string.Empty;
            AlbumArtUrl = string.Empty;
            LyricLines = new();
            HasLyrics = false;
            IsTrackLoaded = false;
            IsLoadingLyrics = false;
            LyricsStatusMessage = "未登录，请长按封面进入设置登录";
            _syncService.SetLyrics(null);
        });
    }

    private void OnSyncResultUpdated(object? sender, SyncResult result)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            CurrentLineIndex = result.LineIndex;
            CurrentSyllableIndex = result.SyllableIndex;
            LineProgress = result.LineProgress;
            SyllableProgress = result.SyllableProgress;
            CurrentLineText = result.CurrentLine?.Text ?? (HasLyrics ? string.Empty : TrackTitle);

            // Look ahead one line for sub-title display.
            if (result.LineIndex >= 0 && result.LineIndex + 1 < LyricLines.Count)
                NextLineText = LyricLines[result.LineIndex + 1].Text;
            else
                NextLineText = HasLyrics ? string.Empty : ArtistName;
        });
    }

    public void Dispose()
    {
        _nowPlayingService.TrackChanged -= OnTrackChanged;
        _nowPlayingService.StateUpdated -= OnStateUpdated;
        _nowPlayingService.AuthenticationFailed -= OnAuthenticationFailed;
        _syncService.SyncResultUpdated -= OnSyncResultUpdated;
    }
}
