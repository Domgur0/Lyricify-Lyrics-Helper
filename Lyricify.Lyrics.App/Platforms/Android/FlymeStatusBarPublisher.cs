using Android.App;
using Android.Content;

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
    private const int FlymeShowTickerFlag = 0x1000000; // FLAG_ALWAYS_SHOW_TICKER
    private const int FlymeUpdateTickerFlag = 0x2000000; // FLAG_ONLY_UPDATE_TICKER
    private const string FlymeTickerIconKey = "ticker_icon";
    private const string FlymeTickerIconSwitchKey = "ticker_icon_switch";

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
            FlymeShowTickerFlag |   // FLAG_ALWAYS_SHOW_TICKER: hold ticker until next update
            FlymeUpdateTickerFlag); // FLAG_ONLY_UPDATE_TICKER: skip notification-drawer redraw

        return notification;
    }
#pragma warning restore CA1416
}
