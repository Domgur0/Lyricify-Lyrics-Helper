using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Models;
using Lyricify.Lyrics.Searchers;
using Lyricify.Lyrics.Searchers.Helpers;

namespace Lyricify.Lyrics.App.Services;

/// <summary>
/// Fetches lyrics for a Spotify track using two strategies:
/// <list type="number">
///   <item>Spotify's internal lyrics endpoint (requires <c>sp_dc</c> cookie).</item>
///   <item>LRCLIB public API as a free fallback.</item>
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
    /// <param name="title">Track title (used for LRCLIB fallback search).</param>
    /// <param name="artist">Primary artist name (used for LRCLIB fallback search).</param>
    /// <param name="durationMs">Track duration in milliseconds (used for LRCLIB match scoring).</param>
    /// <returns>Parsed <see cref="LyricsData"/>, or <c>null</c> when no lyrics are found.</returns>
    public async Task<LyricsData?> GetLyricsAsync(
        string trackId,
        string title,
        string artist,
        int durationMs)
    {
        if (_cache.TryGetValue(trackId, out var cached))
            return cached;

        // ── Strategy 1: Spotify internal lyrics (sp_dc) ───────────────────────
        var lyricsData = await TryGetSpotifyLyricsAsync(trackId);

        // ── Strategy 2: LRCLIB public API ─────────────────────────────────────
        if (lyricsData is null)
            lyricsData = await TryGetLrcLibLyricsAsync(title, artist, durationMs);

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
            // sp_dc not configured or expired – fall through to LRCLIB.
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<LyricsData?> TryGetLrcLibLyricsAsync(
        string title, string artist, int durationMs)
    {
        try
        {
            var metadata = new TrackMultiArtistMetadata
            {
                Title = title,
                Artists = new List<string> { artist },
                AlbumArtists = new List<string> { artist },
                DurationMs = durationMs,
            };

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
