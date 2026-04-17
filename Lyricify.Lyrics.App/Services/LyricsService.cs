using Lyricify.Lyrics.Decrypter.Krc;
using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Models;
using Lyricify.Lyrics.Searchers;
using Lyricify.Lyrics.Searchers.Helpers;

namespace Lyricify.Lyrics.App.Services;

/// <summary>
/// Fetches lyrics for a Spotify track using multiple strategies in priority order:
/// <list type="number">
///   <item>Spotify's internal lyrics endpoint (requires <c>sp_dc</c> cookie).</item>
///   <item>Netease Cloud Music (YRC syllable-level preferred, falls back to LRC).</item>
///   <item>QQ Music (LRC).</item>
///   <item>Kugou Music (KRC).</item>
///   <item>LRCLIB public API (LRC).</item>
/// </list>
/// Results are cached by track ID to avoid redundant network calls.
/// </summary>
public class LyricsService
{
    private readonly Dictionary<string, LyricsData?> _cache = new();

    /// <summary>
    /// Fetches and parses lyrics for the given track.
    /// </summary>
    /// <param name="trackId">Spotify track ID.</param>
    /// <param name="title">Track title (used for fallback searches).</param>
    /// <param name="artist">Primary artist name (used for fallback searches).</param>
    /// <param name="durationMs">Track duration in milliseconds (used for match scoring).</param>
    /// <returns>Parsed <see cref="LyricsData"/>, or <c>null</c> when no lyrics are found.</returns>
    public async Task<LyricsData?> GetLyricsAsync(
        string trackId,
        string title,
        string artist,
        int durationMs)
    {
        if (_cache.TryGetValue(trackId, out var cached))
            return cached;

        var metadata = new TrackMultiArtistMetadata
        {
            Title = title,
            Artists = new List<string> { artist },
            AlbumArtists = new List<string> { artist },
            DurationMs = durationMs,
        };

        // ── Strategy 1: Spotify internal lyrics (sp_dc) ───────────────────────
        var lyricsData = await TryGetSpotifyLyricsAsync(trackId);

        // ── Strategy 2: Netease Cloud Music ───────────────────────────────────
        if (lyricsData is null)
            lyricsData = await TryGetNeteaseAsync(metadata);

        // ── Strategy 3: QQ Music ──────────────────────────────────────────────
        if (lyricsData is null)
            lyricsData = await TryGetQQMusicAsync(metadata);

        // ── Strategy 4: Kugou Music ───────────────────────────────────────────
        if (lyricsData is null)
            lyricsData = await TryGetKugouAsync(metadata);

        // ── Strategy 5: LRCLIB public API ─────────────────────────────────────
        if (lyricsData is null)
            lyricsData = await TryGetLrcLibLyricsAsync(metadata);

        _cache[trackId] = lyricsData;
        return lyricsData;
    }

    /// <summary>Evicts a single track from the cache (e.g. after an explicit refresh).</summary>
    public void InvalidateCache(string trackId) => _cache.Remove(trackId);

    /// <summary>Clears the entire in-memory lyrics cache.</summary>
    public void ClearCache() => _cache.Clear();

    // ── Strategies ────────────────────────────────────────────────────────────

    private static async Task<LyricsData?> TryGetSpotifyLyricsAsync(string trackId)
    {
        try
        {
            var rawJson = await ProviderHelper.SpotifyApi.GetLyrics(trackId);
            if (string.IsNullOrWhiteSpace(rawJson)) return null;
            return ParseHelper.ParseLyrics(rawJson, LyricsRawTypes.Spotify);
        }
        catch (UnauthorizedAccessException)
        {
            // sp_dc not configured or expired – fall through.
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<LyricsData?> TryGetNeteaseAsync(TrackMultiArtistMetadata metadata)
    {
        try
        {
            var result = await SearchHelper.Search(
                metadata,
                Searchers.Searchers.Netease,
                CompareHelper.MatchType.Medium);

            if (result is not NeteaseSearchResult neteaseResult) return null;

            // Try the newer YRC (syllable-level) API first, fall back to the legacy LRC API.
            var lyricResult = await ProviderHelper.NeteaseApi.GetLyricNew(neteaseResult.Id);

            if (lyricResult is null)
                lyricResult = await ProviderHelper.NeteaseApi.GetLyric(neteaseResult.Id);

            if (lyricResult is null) return null;

            // Prefer YRC (syllable-level) → LRC (line-level).
            var yrcText = lyricResult.Yrc?.Lyric;
            if (!string.IsNullOrWhiteSpace(yrcText))
                return ParseHelper.ParseLyrics(yrcText, LyricsRawTypes.Yrc);

            var lrcText = lyricResult.Lrc?.Lyric;
            if (!string.IsNullOrWhiteSpace(lrcText))
                return ParseHelper.ParseLyrics(lrcText, LyricsRawTypes.Lrc);

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<LyricsData?> TryGetQQMusicAsync(TrackMultiArtistMetadata metadata)
    {
        try
        {
            var result = await SearchHelper.Search(
                metadata,
                Searchers.Searchers.QQMusic,
                CompareHelper.MatchType.Medium);

            if (result is not QQMusicSearchResult qqResult) return null;

            // GetLyric(mid) returns base64-decoded LRC text directly.
            var lyricResult = await ProviderHelper.QQMusicApi.GetLyric(qqResult.Mid);
            if (lyricResult is null || string.IsNullOrWhiteSpace(lyricResult.Lyric)) return null;

            return ParseHelper.ParseLyrics(lyricResult.Lyric, LyricsRawTypes.Lrc);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<LyricsData?> TryGetKugouAsync(TrackMultiArtistMetadata metadata)
    {
        try
        {
            var result = await SearchHelper.Search(
                metadata,
                Searchers.Searchers.Kugou,
                CompareHelper.MatchType.Medium);

            if (result is not KugouSearchResult kugouResult) return null;

            // Search for KRC lyrics using the song hash.
            var searchResp = await ProviderHelper.KugouApi.GetSearchLyrics(hash: kugouResult.Hash);
            var candidate = searchResp?.Candidates?.FirstOrDefault();
            if (candidate is null) return null;

            var krcText = await Helper.GetLyricsAsync(candidate.Id, candidate.AccessKey);
            if (string.IsNullOrWhiteSpace(krcText)) return null;

            return ParseHelper.ParseLyrics(krcText, LyricsRawTypes.Krc);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<LyricsData?> TryGetLrcLibLyricsAsync(TrackMultiArtistMetadata metadata)
    {
        try
        {
            var result = await SearchHelper.Search(
                metadata,
                Searchers.Searchers.LRCLIB,
                CompareHelper.MatchType.Medium);

            if (result is not LRCLIBSearchResult lrclibResult) return null;

            if (lrclibResult.Id <= 0)
                return null;

            var provider = await ProviderHelper.LRCLIBApi.GetById(lrclibResult.Id);

            if (provider is null) return null;

            // Prefer time-synced LRC; fall back to plain lyrics if unavailable.
            var lrcText = provider.SyncedLyrics ?? provider.PlainLyrics;
            if (string.IsNullOrWhiteSpace(lrcText)) return null;

            var rawType = string.IsNullOrWhiteSpace(provider.SyncedLyrics)
                ? LyricsRawTypes.Unknown
                : LyricsRawTypes.Lrc;

            return rawType == LyricsRawTypes.Unknown
                ? null
                : ParseHelper.ParseLyrics(lrcText, rawType);
        }
        catch
        {
            return null;
        }
    }
}
