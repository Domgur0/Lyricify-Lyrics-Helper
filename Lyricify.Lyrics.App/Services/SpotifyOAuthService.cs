using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace Lyricify.Lyrics.App.Services;

/// <summary>
/// Handles Spotify OAuth2 PKCE authentication.
/// Scopes required: <c>user-read-playback-state</c>.
/// </summary>
public class SpotifyOAuthService
{
    // ── Configuration ─────────────────────────────────────────────────────────
    // RedirectUri must exactly match the value registered in the Spotify dashboard.
    // Client ID is stored in user Preferences so it can be entered in the Settings
    // page at runtime — no rebuild required.
    private const string RedirectUri = "http://localhost:766/callback";
    private const string Scopes = "user-read-playback-state";

    private const string PrefClientId = "spotify_client_id";

    private const string AuthorizeUrl = "https://accounts.spotify.com/authorize";
    private const string TokenUrl = "https://accounts.spotify.com/api/token";

    // Preference keys
    private const string PrefAccessToken = "spotify_access_token";
    private const string PrefRefreshToken = "spotify_refresh_token";
    private const string PrefExpiresAt = "spotify_token_expires_at";

    private readonly HttpClient _http = new();
    private string? _codeVerifier;

    // ── Public state ──────────────────────────────────────────────────────────

    /// <summary>Cached, refreshed-on-demand access token for the Spotify Web API.</summary>
    public string? AccessToken => Preferences.Get(PrefAccessToken, null);

    /// <summary>True when a non-expired access token (or a refresh token) is stored.</summary>
    public bool HasValidToken =>
        !string.IsNullOrWhiteSpace(Preferences.Get(PrefRefreshToken, null));

    // ── Authorization ─────────────────────────────────────────────────────────

    /// <summary>
    /// Launches the Spotify login page and exchanges the authorization code for tokens.
    /// Uses the PKCE extension so no client secret is required in the app.
    /// </summary>
    public async Task AuthorizeAsync()
    {
        _codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(_codeVerifier);
        var state = GenerateRandomString(16);

        var authUri = new UriBuilder(AuthorizeUrl)
        {
            Query = string.Join("&",
                $"client_id={Uri.EscapeDataString(ClientId)}",
                "response_type=code",
                $"redirect_uri={Uri.EscapeDataString(RedirectUri)}",
                $"scope={Uri.EscapeDataString(Scopes)}",
                $"state={state}",
                "code_challenge_method=S256",
                $"code_challenge={codeChallenge}")
        }.Uri;

        // MAUI WebAuthenticator handles the browser + redirect interception.
        var result = await WebAuthenticator.Default.AuthenticateAsync(authUri, new Uri(RedirectUri));
        var code = result.Properties["code"];

        await ExchangeCodeAsync(code);
    }

    // ── Token refresh ─────────────────────────────────────────────────────────

    /// <summary>
    /// Ensures the stored access token is valid, refreshing it if necessary.
    /// Throws <see cref="UnauthorizedAccessException"/> when no refresh token exists.
    /// </summary>
    public async Task<string> EnsureValidAccessTokenAsync()
    {
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(
            Preferences.Get(PrefExpiresAt, 0L));

        if (DateTimeOffset.UtcNow < expiresAt.AddSeconds(-60))
        {
            // Token is still valid.
            return AccessToken!;
        }

        var refreshToken = Preferences.Get(PrefRefreshToken, null)
            ?? throw new UnauthorizedAccessException("No Spotify refresh token stored. Please log in again.");

        await RefreshAccessTokenAsync(refreshToken);
        return AccessToken!;
    }

    // ── Sign out ──────────────────────────────────────────────────────────────

    public void SignOut()
    {
        Preferences.Remove(PrefAccessToken);
        Preferences.Remove(PrefRefreshToken);
        Preferences.Remove(PrefExpiresAt);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task ExchangeCodeAsync(string code)
    {
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["client_id"] = ClientId,
            ["code_verifier"] = _codeVerifier!,
        });

        var response = await _http.PostAsync(TokenUrl, body);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        StoreTokens(json);
    }

    private async Task RefreshAccessTokenAsync(string refreshToken)
    {
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = ClientId,
        });

        var response = await _http.PostAsync(TokenUrl, body);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        StoreTokens(json);
    }

    private static void StoreTokens(string json)
    {
        var payload = JsonConvert.DeserializeObject<SpotifyTokenPayload>(json)
            ?? throw new InvalidOperationException("Invalid token response from Spotify.");

        Preferences.Set(PrefAccessToken, payload.AccessToken);
        if (!string.IsNullOrWhiteSpace(payload.RefreshToken))
            Preferences.Set(PrefRefreshToken, payload.RefreshToken);
        Preferences.Set(PrefExpiresAt,
            DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn).ToUnixTimeSeconds());
    }

    // ── PKCE helpers ──────────────────────────────────────────────────────────

    private static string GenerateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    private static string GenerateCodeChallenge(string codeVerifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string GenerateRandomString(int length)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var bytes = RandomNumberGenerator.GetBytes(length);
        return new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
    }

    // ── Inner model ───────────────────────────────────────────────────────────

    private sealed class SpotifyTokenPayload
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonProperty("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
