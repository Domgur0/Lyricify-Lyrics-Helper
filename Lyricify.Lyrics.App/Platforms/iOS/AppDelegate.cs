using Foundation;
using UIKit;

namespace Lyricify.Lyrics.App;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    // ── Spotify OAuth2 callback ───────────────────────────────────────────────

    /// <summary>
    /// Intercepts the Spotify OAuth2 redirect URI (<c>lyricify://oauth/callback</c>)
    /// and forwards it to <see cref="WebAuthenticator"/> so the PKCE flow can complete.
    /// Register this custom scheme in Info.plist under CFBundleURLTypes.
    /// </summary>
    public override bool OpenUrl(UIApplication application, NSUrl url, NSDictionary options)
    {
        if (WebAuthenticator.Default.OpenUrl(url))
            return true;

        return base.OpenUrl(application, url, options);
    }
}
