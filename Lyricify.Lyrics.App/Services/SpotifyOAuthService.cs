using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace Lyricify.Lyrics.App.Services;

/// <summary>
/// Handles Spotify OAuth2 PKCE authentication.
/// Scopes required: <c>user-read-playback-state</c>.
///
/// Both the Client ID and Client Secret are stored in user <see cref="Preferences"/>
/// so they can be entered at runtime in the Settings page — no rebuild required.
///
/// If a Client Secret is provided the token requests use HTTP Basic authentication
/// (as required by Spotify for apps registered without PKCE-only mode).
/// If no secret is stored, the pure PKCE flow is used (secret-free).
/// </summary>
public class SpotifyOAuthService
{
    // ── Fixed configuration ───────────────────────────────────────────────────
    // RedirectUri must exactly match the value registered in the Spotify dashboard.
    private const string RedirectUri = "lyricify://callback";
    private const string Scopes = "user-read-playback-state";

    private const string AuthorizeUrl = "https://accounts.spotify.com/authorize";
    private const string TokenUrl = "https://accounts.spotify.com/api/token";

    // ── Preference keys ───────────────────────────────────────────────────────
    private const string PrefClientId = "spotify_client_id";
    private const string PrefClientSecret = "spotify_client_secret";
    private const string PrefAccessToken = "spotify_access_token";
    private const string PrefRefreshToken = "spotify_refresh_token";
    private const string PrefExpiresAt = "spotify_token_expires_at";

    private readonly HttpClient _http = new();
    private string? _codeVerifier;

    // ── Public state ──────────────────────────────────────────────────────────

    /// <summary>Cached, refreshed-on-demand access token for the Spotify Web API.</summary>
    public string? AccessToken => Preferences.Get(PrefAccessToken, null);

    /// <summary>True when a Client ID has been stored in settings.</summary>
    public bool HasClientId => !string.IsNullOrWhiteSpace(Preferences.Get(PrefClientId, null));

    /// <summary>True when a non-expired access token (or a refresh token) is stored.</summary>
    public bool HasValidToken =>
        !string.IsNullOrWhiteSpace(Preferences.Get(PrefRefreshToken, null));

    /// <summary>UTC time when the current access token expires, or <c>null</c> when not set.</summary>
    public DateTimeOffset? TokenExpiresAt
    {
        get
        {
            var ts = Preferences.Get(PrefExpiresAt, 0L);
            return ts > 0 ? DateTimeOffset.FromUnixTimeSeconds(ts) : null;
        }
    }

    // ── Credential helpers ────────────────────────────────────────────────────

    private string ClientId =>
        Preferences.Get(PrefClientId, string.Empty)
        ?? string.Empty;

    private string? ClientSecret =>
        Preferences.Get(PrefClientSecret, null) is { Length: > 0 } s ? s : null;

    // ── Authorization ─────────────────────────────────────────────────────────

    /// <summary>
    /// Launches the Spotify login page and exchanges the authorization code for tokens.
    /// Uses PKCE so no client secret is strictly required, but if one is stored it is
    /// included via HTTP Basic authentication as some app registrations require it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no Client ID has been saved in Settings.
    /// </exception>
    public async Task AuthorizeAsync()
    {
        if (!HasClientId)
            throw new InvalidOperationException(
                "Spotify Client ID is not set. Please enter it in Settings before signing in.");

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

        // MAUI WebAuthenticator handles browser sign-in and callback interception.
        var result = await WebAuthenticator.Default.AuthenticateAsync(
            authUri, new Uri(RedirectUri));
        var code = result.Properties["code"];

        await ExchangeCodeAsync(code);
    }

    // ── Token refresh ─────────────────────────────────────────────────────────

    /// <summary>
    /// Ensures the stored access token is valid, refreshing it if necessary.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">
    /// Thrown when no refresh token exists (user must log in again).
    /// </exception>
    public async Task<string> EnsureValidAccessTokenAsync()
    {
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(
            Preferences.Get(PrefExpiresAt, 0L));

        if (DateTimeOffset.UtcNow < expiresAt.AddSeconds(-60))
            return AccessToken!;

        var refreshToken = Preferences.Get(PrefRefreshToken, null)
            ?? throw new UnauthorizedAccessException(
                "No Spotify refresh token stored. Please log in again.");

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
        var body = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["client_id"] = ClientId,
            ["code_verifier"] = _codeVerifier!,
        };

        var response = await PostTokenAsync(body);
        StoreTokens(response);
    }

    private async Task RefreshAccessTokenAsync(string refreshToken)
    {
        var body = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = ClientId,
        };

        var response = await PostTokenAsync(body);
        StoreTokens(response);
    }

    /// <summary>
    /// Posts to the Spotify token endpoint.
    /// When a <see cref="ClientSecret"/> is stored, the request uses HTTP Basic
    /// authentication (required by app registrations that are not PKCE-only).
    /// </summary>
    private async Task<string> PostTokenAsync(Dictionary<string, string> body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
        {
            Content = new FormUrlEncodedContent(body),
        };

        // Add Basic auth header when a client secret is available.
        if (ClientSecret is { } secret)
        {
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{ClientId}:{secret}"));
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Basic", credentials);
        }

        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
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
