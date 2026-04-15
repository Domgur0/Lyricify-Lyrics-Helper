using Foundation;
using UIKit;

namespace Lyricify.Lyrics.App;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    // ── Spotify OAuth2 callback ───────────────────────────────────────────────

    /// <summary>
    /// MAUI's <see cref="WebAuthenticator"/> intercepts the Spotify OAuth2 redirect
    /// (<c>http://localhost:766/callback</c>) via a local loopback listener — no custom
    /// URL scheme is required for localhost redirects on iOS.
    ///
    /// This override is retained as a safety net: if the system ever routes an
    /// <c>http://localhost</c> URL through <c>openURL</c> it will be forwarded to
    /// <see cref="WebAuthenticator"/> correctly.
    /// </summary>
    public override bool OpenUrl(UIApplication application, NSUrl url, NSDictionary options)
    {
        if (WebAuthenticator.Default.OpenUrl(url))
            return true;

        return base.OpenUrl(application, url, options);
    }
}
