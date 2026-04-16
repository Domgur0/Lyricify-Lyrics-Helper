using Android.App;
using Android.Content;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Util;
using Android.Views;
using Lyricify.Lyrics.App.Services;
using Lyricify.Lyrics.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Threading;

namespace Lyricify.Lyrics.App.Platforms.Android;

/// <summary>
/// A foreground <see cref="Service"/> that:
/// <list type="bullet">
///   <item>Keeps the Spotify polling and lyrics sync alive in the background.</item>
///   <item>Draws a <see cref="LyricsOverlayView"/> over all other apps via <c>WindowManager</c>.</item>
/// </list>
/// Start via <see cref="Context.StartForegroundService"/>.
/// Stop via <see cref="Context.StopService"/>.
/// </summary>
[Service(
    Exported = false,
    ForegroundServiceType = (global::Android.Content.PM.ForegroundService)ForegroundServiceTypeSpecialUseValue)]
public class LyricsOverlayService : Service
{
    private const string LogTag = "LyricifyOverlay";
    private const string ChannelId = "lyricify_overlay";
    private const string ErrorChannelId = "lyricify_error";
    private const int NotificationId = 1001;
    private const int ErrorNotificationId = 1002;
    private const int ForegroundServiceTypeSpecialUseValue = 0x40000000;

    // Android 14+ (API 34) requires the service type to be passed to StartForeground.
    // Value matches ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE and the manifest declaration.
    private const global::Android.Content.PM.ForegroundService ForegroundServiceTypeSpecialUse =
        (global::Android.Content.PM.ForegroundService)ForegroundServiceTypeSpecialUseValue;

    private IWindowManager? _windowManager;
    private LyricsOverlayView? _overlayView;
    private LyricsViewModel? _viewModel;
    private static int _isRunning;
    public static bool IsRunning => Interlocked.CompareExchange(ref _isRunning, 0, 0) == 1;

    /// <summary>
    /// Non-null when the last service startup attempt ended in failure.
    /// Cleared at the beginning of each <see cref="OnCreate"/> call.
    /// </summary>
    public static string? LastStartupError { get; private set; }

    /// <summary>
    /// Fired on the calling thread after <see cref="OnCreate"/> completes.
    /// Argument is <c>null</c> on success, or a human-readable error string on failure.
    /// </summary>
    public static event Action<string?>? OverlayStartResult;

    // ── Service lifecycle ─────────────────────────────────────────────────────

    public override void OnCreate()
    {
        base.OnCreate();
        Interlocked.Exchange(ref _isRunning, 1);
        LastStartupError = null;

        // Start in foreground immediately to satisfy the StartForegroundService contract.
        CreateNotificationChannel();
        var notification = BuildNotification("Lyricify is running", "Tap to open");
        // On Android 14+ (API 34) startForeground must declare the matching service type.
        if (OperatingSystem.IsAndroidVersionAtLeast(34))
            StartForeground(NotificationId, notification, ForegroundServiceTypeSpecialUse);
        else
            StartForeground(NotificationId, notification);

        // Use the Service's own context (not Application.Context) to obtain WindowManager.
        // On Android 12+ (API 31+), Application.Context is not a display/window context and
        // GetSystemService(WindowService) returns null from it. The Service context has a valid
        // window token and produces a WindowManager that can add and update overlay views.
        _windowManager = GetSystemService(WindowService) as IWindowManager;
        if (_windowManager is null)
        {
            FailAndStop("无法获取 WindowManager，请重启应用");
            return;
        }

        // Resolve the shared ViewModel from the MAUI DI container.
        var services = IPlatformApplication.Current?.Services
            ?? (this.Application as global::Microsoft.Maui.MauiApplication)?.Services;
        _viewModel = services?.GetService<LyricsViewModel>();
        if (_viewModel is null)
        {
            FailAndStop("无法获取歌词服务，请重启应用");
            return;
        }

        var overlayError = ShowOverlay();
        if (overlayError is not null)
        {
            FailAndStop(overlayError);
            return;
        }

        // Subscribe to sync updates.
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateOverlayFromViewModel();

        // Notify page and other observers that startup succeeded (on main thread).
        RunOnMainThread(() => OverlayStartResult?.Invoke(null));
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
        => StartCommandResult.Sticky;

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnDestroy()
    {
        Interlocked.Exchange(ref _isRunning, 0);

        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        RemoveOverlay();
        base.OnDestroy();
    }

    // ── Overlay management ────────────────────────────────────────────────────

    /// <summary>
    /// Creates and adds the overlay view to the window.
    /// Returns <c>null</c> on success, or a human-readable error string on failure.
    /// </summary>
    private string? ShowOverlay()
    {
        if (_windowManager is null) return "内部错误：WindowManager 为空";
        if (!global::Android.Provider.Settings.CanDrawOverlays(this))
            return "悬浮窗创建失败：缺少悬浮窗权限，请在设置中授予";

        // Use the Service context so the view shares the same window token as the
        // WindowManager obtained above; using Application.Context here would cause a
        // context/token mismatch when UpdateViewLayout is called on Android 12+.
        var ctx = this;
        _overlayView = new LyricsOverlayView(ctx);

        var density = ctx.Resources!.DisplayMetrics!.Density;
        var overlayWidthPx = (int)(360 * density);

        var layoutParams = new WindowManagerLayoutParams(
            overlayWidthPx,
            ViewGroup.LayoutParams.WrapContent,
            OperatingSystem.IsAndroidVersionAtLeast(26)
                ? WindowManagerTypes.ApplicationOverlay
                : WindowManagerTypes.Phone,
            WindowManagerFlags.NotFocusable | WindowManagerFlags.NotTouchModal,
            global::Android.Graphics.Format.Translucent)
        {
            Gravity = GravityFlags.Top | GravityFlags.Start,
            X = 0,
            Y = (int)(80 * density),
            Alpha = global::Microsoft.Maui.Storage.Preferences.Get("overlay_opacity", 0.9f),
        };

        _overlayView.SetWindowContext(_windowManager, layoutParams);
        try
        {
            _windowManager.AddView(_overlayView, layoutParams);
            return null;
        }
        catch (Java.Lang.IllegalStateException ex)
        {
            Log.Warn(LogTag, $"Failed to add overlay view: illegal state. {ex.Message}");
            _overlayView = null;
            return "悬浮窗创建失败：窗口状态异常，请重试";
        }
        catch (Java.Lang.SecurityException ex)
        {
            Log.Warn(LogTag, $"Failed to add overlay view: missing overlay permission. {ex.Message}");
            _overlayView = null;
            return "悬浮窗创建失败：缺少悬浮窗权限，请在设置中授予";
        }
        catch (Android.Views.WindowManagerBadTokenException ex)
        {
            Log.Warn(LogTag, $"Failed to add overlay view: invalid window token. {ex.Message}");
            _overlayView = null;
            return "悬浮窗创建失败：窗口令牌无效，请重启应用";
        }
    }

    private void RemoveOverlay()
    {
        if (_overlayView is null || _windowManager is null) return;
        try
        {
            _windowManager.RemoveView(_overlayView);
        }
        catch
        {
            // View may have already been removed.
        }
        _overlayView = null;
    }

    // ── ViewModel events ──────────────────────────────────────────────────────

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_overlayView is null || _viewModel is null) return;

        switch (e.PropertyName)
        {
            case nameof(LyricsViewModel.CurrentLineText):
            case nameof(LyricsViewModel.NextLineText):
                _overlayView.UpdateLines(_viewModel.CurrentLineText, _viewModel.NextLineText);
                break;

            case nameof(LyricsViewModel.LineProgress):
                _overlayView.UpdateProgress(_viewModel.LineProgress);
                break;

            case nameof(LyricsViewModel.TrackTitle):
                // Update notification with current track.
                var notification = BuildNotification(
                    _viewModel.TrackTitle,
                    _viewModel.ArtistName);
                var manager = GetSystemService(NotificationService) as NotificationManager;
                manager?.Notify(NotificationId, notification);
                break;
        }
    }

    private void UpdateOverlayFromViewModel()
    {
        if (_overlayView is null || _viewModel is null) return;
        _overlayView.UpdateLines(_viewModel.CurrentLineText, _viewModel.NextLineText);
        _overlayView.UpdateProgress(_viewModel.LineProgress);
    }

    /// <summary>
    /// Invokes <paramref name="action"/> on the Android main thread.
    /// Falls back to a direct call when the main looper is unavailable.
    /// </summary>
    private static void RunOnMainThread(Action action)
    {
        global::Android.OS.Handler? mainHandler = null;
        try { mainHandler = new global::Android.OS.Handler(global::Android.OS.Looper.MainLooper!); }
        catch { /* ignore */ }

        if (mainHandler is not null)
            mainHandler.Post(action);
        else
            action();
    }

    // ── Failure helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Stores <paramref name="reason"/> in <see cref="LastStartupError"/>,
    /// fires <see cref="OverlayStartResult"/> on the main thread,
    /// posts an error notification, then stops the service.
    /// </summary>
    private void FailAndStop(string reason)
    {
        Log.Warn(LogTag, reason);
        LastStartupError = reason;
        StopForeground(StopForegroundFlags.Remove);

        // Post a persistent error notification so the user can see the reason
        // even after navigating away from the app.
        PostErrorNotification(reason);

        // Notify any subscribed UI on the main thread.
        RunOnMainThread(() => OverlayStartResult?.Invoke(reason));

        StopSelf();
    }

    private void PostErrorNotification(string message)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;

        var pendingIntent = PendingIntent.GetActivity(
            this,
            0,
            new Intent(this, typeof(MainActivity)),
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var notification = new Notification.Builder(this, ErrorChannelId)
            .SetContentTitle("Lyricify 悬浮窗启动失败")
            .SetContentText(message)
            .SetSmallIcon(global::Android.Resource.Drawable.IcDialogAlert)
            .SetContentIntent(pendingIntent)
            .SetAutoCancel(true)
            .Build()!;

        var manager = GetSystemService(NotificationService) as NotificationManager;
        manager?.Notify(ErrorNotificationId, notification);
    }

    // ── Notification helpers ──────────────────────────────────────────────────

    private void CreateNotificationChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;

        var channel = new NotificationChannel(
            ChannelId,
            "Lyricify overlay",
            NotificationImportance.Low)
        {
            Description = "Shown while the floating lyrics window is active",
        };

        var errorChannel = new NotificationChannel(
            ErrorChannelId,
            "Lyricify errors",
            NotificationImportance.Default)
        {
            Description = "Alerts when the floating lyrics window fails to start",
        };

        var manager = GetSystemService(NotificationService) as NotificationManager;
        manager?.CreateNotificationChannel(channel);
        manager?.CreateNotificationChannel(errorChannel);
    }

#pragma warning disable CA1416 // Validate platform compatibility
    private Notification BuildNotification(string title, string text)
    {
        var pendingIntent = PendingIntent.GetActivity(
            this,
            0,
            new Intent(this, typeof(MainActivity)),
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        return new Notification.Builder(this, ChannelId)
            .SetContentTitle(title)
            .SetContentText(text)
            .SetSmallIcon(global::Android.Resource.Drawable.IcMediaPlay)
            .SetContentIntent(pendingIntent)
            .SetOngoing(true)
            .Build()!;
    }
#pragma warning restore CA1416
}
