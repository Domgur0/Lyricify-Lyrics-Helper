using Android.App;
using Android.Runtime;
using Android.Service.Notification;

namespace Lyricify.Lyrics.App.Platforms.Android;

/// <summary>
/// A minimal <see cref="NotificationListenerService"/> stub whose sole purpose is to
/// allow this package to be passed as the <c>notificationListener</c> component to
/// <c>MediaSessionManager.GetActiveSessions()</c>.
/// <para>
/// The user must grant notification-listener access via
/// <c>Settings → Apps → Special app access → Notification access</c> before
/// <c>GetActiveSessions</c> will return results.
/// </para>
/// </summary>
/// <remarks>
/// The <see cref="RegisterAttribute"/> fixes a <see cref="Java.Lang.ClassNotFoundException"/>
/// at runtime: because the Android system (not our own code) binds this service it looks
/// up the class by the exact name declared in AndroidManifest.xml.  Without the attribute
/// the .NET for Android toolchain generates an Android Callable Wrapper whose name is
/// derived from the C# namespace, which does not match the manifest entry.
/// </remarks>
[Register("com/lyricify/lyrics/app/MediaNotificationListenerService")]
[Service(
    Exported = true,
    Permission = "android.permission.BIND_NOTIFICATION_LISTENER_SERVICE",
    Label = "Lyricify")]
[IntentFilter(new[] { "android.service.notification.NotificationListenerService" })]
public class MediaNotificationListenerService : NotificationListenerService
{
    // No additional logic needed – the service only has to exist and be declared
    // so that Android accepts our ComponentName in GetActiveSessions().
}
