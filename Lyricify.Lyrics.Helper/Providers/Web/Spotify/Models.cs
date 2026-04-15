using Newtonsoft.Json;

namespace Lyricify.Lyrics.Providers.Web.Spotify
{
#nullable disable
    public class SearchResponse
    {
        [JsonProperty("tracks")]
        public SearchTracks Tracks { get; set; }
    }

    public class SearchTracks
    {
        [JsonProperty("items")]
        public List<SearchTrackItem> Items { get; set; }
    }

    public class SearchTrackItem
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("duration_ms")]
        public int DurationMs { get; set; }

        [JsonProperty("artists")]
        public List<SearchArtistItem> Artists { get; set; }

        [JsonProperty("album")]
        public SearchAlbumItem Album { get; set; }
    }

    public class SearchAlbumItem
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("artists")]
        public List<SearchArtistItem> Artists { get; set; }
    }

    public class SearchArtistItem
    {
        [JsonProperty("name")]
        public string Name { get; set; }
    }

    public class SpotifyTrackCandidate
    {
        public string Id { get; set; }

        public string Title { get; set; }

        public string ArtistName { get; set; }

        public string AlbumName { get; set; }

        public int? DurationMs { get; set; }
    }

    /// <summary>
    /// Response from GET /v1/me/player/currently-playing (Spotify Web API, requires user OAuth2 token).
    /// </summary>
    public class SpotifyCurrentlyPlayingResponse
    {
        [JsonProperty("is_playing")]
        public bool IsPlaying { get; set; }

        /// <summary>
        /// Playback position in milliseconds. May be null when nothing is active.
        /// </summary>
        [JsonProperty("progress_ms")]
        public int? ProgressMs { get; set; }

        /// <summary>
        /// The currently playing track. Null when the player is idle or playing a non-track type.
        /// </summary>
        [JsonProperty("item")]
        public SpotifyCurrentlyPlayingItem Item { get; set; }

        /// <summary>
        /// "track", "episode", "ad", or "unknown".
        /// </summary>
        [JsonProperty("currently_playing_type")]
        public string CurrentlyPlayingType { get; set; }
    }

    public class SpotifyCurrentlyPlayingItem
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("duration_ms")]
        public int DurationMs { get; set; }

        [JsonProperty("artists")]
        public List<SearchArtistItem> Artists { get; set; }

        [JsonProperty("album")]
        public SpotifyCurrentlyPlayingAlbum Album { get; set; }
    }

    public class SpotifyCurrentlyPlayingAlbum
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("images")]
        public List<SpotifyAlbumImage> Images { get; set; }
    }

    public class SpotifyAlbumImage
    {
        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("width")]
        public int Width { get; set; }

        [JsonProperty("height")]
        public int Height { get; set; }
    }
}
