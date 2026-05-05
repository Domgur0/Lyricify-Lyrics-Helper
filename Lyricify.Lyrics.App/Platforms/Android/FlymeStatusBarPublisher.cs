using Android.App;
using Android.Content;
using Android.Util;

namespace Lyricify.Lyrics.App.Platforms.Android;

/// <summary>
/// Publishes Meizu/Flyme status-bar lyrics by sending a dedicated <see cref="Notification"/>
/// on each lyric-line change, following the Flyme notification contract described at
/// https://github.com/Moriafly/HiMoriafly/blob/main/docs/android-dev/flyme-lyrics-noti.md.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><c>FLAG_ALWAYS_SHOW_TICKER</c> keeps the ticker visible until the next update.</item>
///   <item><c>FLAG_ONLY_UPDATE_TICKER</c> refreshes only the status-bar ticker without
///         re-drawing the notification in the drawer.</item>
/// </list>
/// </remarks>
internal sealed class FlymeStatusBarPublisher : IDisposable
{
    private const string LogTag = "LyricifyFlyme";
    private const string FlymeShowTickerFlagName = "FLAG_ALWAYS_SHOW_TICKER";
    private const string FlymeUpdateTickerFlagName = "FLAG_ONLY_UPDATE_TICKER";
    private const int FlymeShowTickerFlagFallback = 0x1000000; // FLAG_ALWAYS_SHOW_TICKER
    private const int FlymeUpdateTickerFlagFallback = 0x2000000; // FLAG_ONLY_UPDATE_TICKER
    private const string FlymeTickerIconKey = "ticker_icon";
    private const string FlymeTickerIconSwitchKey = "ticker_icon_switch";

    private readonly record struct FlymeTickerFlagSet(int ShowTickerFlag, int UpdateTickerFlag)
    {
        public bool IsSupported => ShowTickerFlag > 0 && UpdateTickerFlag > 0;
    }

    // Resolved once per process lifetime; the ROM's class does not change at runtime.
    private static readonly Lazy<FlymeTickerFlagSet> _flags = new(ResolveFlymeTickerFlags);

    private readonly Context _context;
    private readonly string _channelId;
    private readonly int _notificationId;
    private bool _disposed;

    /// <summary>
    /// Initializes a new <see cref="FlymeStatusBarPublisher"/>.
    /// </summary>
    /// <param name="context">Android context used to obtain the notification service.</param>
    /// <param name="channelId">
    ///   Notification channel ID. Using the same channel as the app's primary media notification
    ///   is recommended so the ticker does not create a separate, visible notification entry.
    /// </param>
    /// <param name="notificationId">
    ///   Unique notification ID. Re-using this ID on subsequent <see cref="Publish"/> calls
    ///   updates the existing notification in-place.
    /// </param>
    public FlymeStatusBarPublisher(Context context, string channelId, int notificationId)
    {
        _context = context;
        _channelId = channelId;
        _notificationId = notificationId;
    }

    /// <summary>
    /// Sends or updates the Flyme status-bar lyrics notification with the given lyric line.
    /// Pass <c>null</c> or an empty string to cancel the notification.
    /// </summary>
    /// <param name="lyric">Current lyric line text, or <c>null</c>/empty to clear.</param>
    /// <param name="smallIconRes">
    ///   Small icon resource ID shown before the ticker text (recommended 16 × 16 dp).
    /// </param>
    public void Publish(string? lyric, int smallIconRes)
    {
        if (_disposed) return;

        var manager = _context.GetSystemService(Context.NotificationService) as NotificationManager;
        if (manager is null) return;

        if (string.IsNullOrWhiteSpace(lyric))
        {
            manager.Cancel(_notificationId);
            return;
        }

        var notification = BuildTickerNotification(lyric, smallIconRes);
        manager.Notify(_notificationId, notification);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            var manager = _context.GetSystemService(Context.NotificationService) as NotificationManager;
            manager?.Cancel(_notificationId);
        }
        catch { }
    }

#pragma warning disable CA1416 // Validate platform compatibility
    private Notification BuildTickerNotification(string lyric, int smallIconRes)
    {
        var flagSet = _flags.Value;

        var notification = new Notification.Builder(_context, _channelId)
            .SetSmallIcon(smallIconRes)
            .SetTicker(lyric)
            .Build()!;

        if (OperatingSystem.IsAndroidVersionAtLeast(19))
        {
            // Small icon shown before the ticker text in the status bar (16 × 16 dp recommended).
            notification.Extras?.PutInt(FlymeTickerIconKey, smallIconRes);
            notification.Extras?.PutBoolean(FlymeTickerIconSwitchKey, false);
        }

        notification.Flags = (NotificationFlags)(
            (int)notification.Flags |
            flagSet.ShowTickerFlag |   // FLAG_ALWAYS_SHOW_TICKER: hold ticker until next update
            flagSet.UpdateTickerFlag); // FLAG_ONLY_UPDATE_TICKER: skip notification-drawer redraw

        return notification;
    }
#pragma warning restore CA1416

    private static FlymeTickerFlagSet ResolveFlymeTickerFlags()
    {
        // FLAG_ALWAYS_SHOW_TICKER and FLAG_ONLY_UPDATE_TICKER are ROM-level extensions present
        // only in the android.app.Notification class on Meizu/Flyme devices. C# reflection on
        // the .NET binding cannot see them because the SDK is compiled against the standard AOSP
        // class, not the Flyme ROM. We use Java reflection via Java.Lang.Class instead.
        try
        {
            using var notifClass = Java.Lang.Class.ForName("android.app.Notification");

            using var showField = TryGetJavaPublicField(notifClass, FlymeShowTickerFlagName);
            using var updateField = TryGetJavaPublicField(notifClass, FlymeUpdateTickerFlagName);

            if (showField is not null && updateField is not null)
                return new FlymeTickerFlagSet(showField.GetInt(null), updateField.GetInt(null));

            Log.Debug(LogTag, "Flyme ticker flags not found in android.app.Notification — using hardcoded values.");
        }
        catch (Exception ex)
        {
            Log.Debug(LogTag, $"Flyme ticker flags unavailable ({ex.GetType().Name}): {ex.Message} — using hardcoded values.");
        }

        // Fall back to the well-known Meizu/Flyme constant values.
        // These are also the flags checked by the meizu-provider Xposed module
        // (FLAG_MEIZU_TICKER = FLAG_ALWAYS_SHOW_TICKER | FLAG_ONLY_UPDATE_TICKER).
        return new FlymeTickerFlagSet(FlymeShowTickerFlagFallback, FlymeUpdateTickerFlagFallback);
    }

    /// <summary>
    /// Returns the named public field of <paramref name="cls"/>, or <c>null</c> if the
    /// field does not exist on this ROM.
    /// </summary>
    private static Java.Lang.Reflect.Field? TryGetJavaPublicField(Java.Lang.Class cls, string fieldName)
    {
        try
        {
            return cls.GetField(fieldName);
        }
        catch (Java.Lang.NoSuchFieldException)
        {
            // Field absent on this ROM — not a Flyme/Meizu device, or field name changed.
            return null;
        }
        catch (Exception ex)
        {
            Log.Debug(LogTag, $"TryGetJavaPublicField({fieldName}) failed unexpectedly: {ex.GetType().Name} — {ex.Message}");
            return null;
        }
    }
}
