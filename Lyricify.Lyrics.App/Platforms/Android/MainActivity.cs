using Android.App;
using Android.Content.PM;
using Android.OS;

namespace Lyricify.Lyrics.App;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges =
        ConfigChanges.ScreenSize |
        ConfigChanges.Orientation |
        ConfigChanges.UiMode |
        ConfigChanges.ScreenLayout |
        ConfigChanges.SmallestScreenSize |
        ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private static WeakReference<MainActivity>? _currentActivity;

    public static MainActivity? Current
    {
        get
        {
            if (_currentActivity?.TryGetTarget(out var activity) == true)
                return activity;
            return null;
        }
    }

    private static void SetCurrent(MainActivity activity)
        => _currentActivity = new WeakReference<MainActivity>(activity);

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetCurrent(this);
        Lyricify.Lyrics.App.Platforms.Android.LyricsOverlayService.SetPreferredWindowContext(this);

        // Request notification permission (Android 13+).
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            RequestPermissions(
                new[] { Android.Manifest.Permission.PostNotifications },
                requestCode: 1001);
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        SetCurrent(this);
        Lyricify.Lyrics.App.Platforms.Android.LyricsOverlayService.SetPreferredWindowContext(this);
    }

    protected override void OnDestroy()
    {
        if (Current == this)
            _currentActivity = null;
        base.OnDestroy();
    }
}
