using Android.App;
using Android.Content;

namespace Lyricify.Lyrics.App;

[Activity(NoHistory = true, Exported = true, LaunchMode = Android.Content.PM.LaunchMode.SingleTop)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "http",
    DataHost = "localhost",
    DataPort = "766",
    DataPathPrefix = "/callback")]
public class SpotifyWebAuthenticatorCallbackActivity : WebAuthenticatorCallbackActivity
{
}
