using Android.App;
using Android.Content;
using Android.Hardware.Display;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Runtime;
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
    /// <summary>
    /// Bundles a resolved WindowManager with the exact context it came from.
    /// When <see cref="OwnsContext"/> is true, the context was created by this service
    /// (via CreateWindowContext) and must be disposed in <see cref="OnDestroy"/>.
    /// </summary>
    private readonly record struct WindowBinding(Context Context, IWindowManager WindowManager, bool OwnsContext);

    private const string LogTag = "LyricifyOverlay";
    private const string ChannelId = "lyricify_overlay";
    private const string ErrorChannelId = "lyricify_error";
    private const int NotificationId = 1001;
    private const int ErrorNotificationId = 1002;
    private const int ForegroundServiceTypeSpecialUseValue = 0x40000000;
    private const int DefaultDisplayId = 0;
    private const string PrefOverlayOpacity = "overlay_opacity";
    private const string PrefOverlayShouldRun = "overlay_should_run";
    private const string PrefOverlayPosX = "overlay_position_x";
    private const string PrefOverlayPosY = "overlay_position_y";
    private const string PrefOverlayLocked = "overlay_locked";
    private const string PrefLyricsFontSize = "lyrics_font_size";
    public const string ActionUnlockOverlay = "lyricify.overlay.action.UNLOCK";
    public const string ActionDisableOverlay = "lyricify.overlay.action.DISABLE";

    // Android 14+ (API 34) requires the service type to be passed to StartForeground.
    // Value matches ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE and the manifest declaration.
    private const global::Android.Content.PM.ForegroundService ForegroundServiceTypeSpecialUse =
        (global::Android.Content.PM.ForegroundService)ForegroundServiceTypeSpecialUseValue;

    private IWindowManager? _windowManager;
    private Context? _windowContext;
    private bool _ownsWindowContext;
    private LyricsOverlayView? _overlayView;
    private WindowManagerLayoutParams? _overlayLayoutParams;
    private LyricsViewModel? _viewModel;
    private bool _overlayLocked;
    private static int _isRunning;
    private static WeakReference<Context>? _preferredWindowContext;
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

    /// <summary>
    /// Records the latest visual context (typically Activity) from UI entry points,
    /// so the service can resolve WindowManager from a context that is actually
    /// bound to a display/window token.
    /// </summary>
    public static void SetPreferredWindowContext(Context? context)
    {
        if (context is null)
            return;
        _preferredWindowContext = new WeakReference<Context>(context);
    }

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

        // Resolve WindowManager from the best available runtime context.
        // Some devices/ROMs may return null for one context but not others.
        var windowBinding = ResolveWindowBinding();
        if (windowBinding is null)
        {
            FailAndStop("无法获取 WindowManager，请重启应用");
            return;
        }
        _windowContext = windowBinding.Value.Context;
        _windowManager = windowBinding.Value.WindowManager;
        _ownsWindowContext = windowBinding.Value.OwnsContext;

        // Resolve the shared ViewModel from the MAUI DI container.
        var services = IPlatformApplication.Current?.Services
            ?? global::Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services;
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
    {
        switch (intent?.Action)
        {
            case ActionUnlockOverlay:
                SetOverlayLocked(false);
                break;
            case ActionDisableOverlay:
                global::Microsoft.Maui.Storage.Preferences.Set(PrefOverlayShouldRun, false);
                StopSelf();
                break;
        }

        return StartCommandResult.Sticky;
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnDestroy()
    {
        Interlocked.Exchange(ref _isRunning, 0);

        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        RemoveOverlay();
        if (_windowContext is not null && _ownsWindowContext)
        {
            try { _windowContext.Dispose(); }
            catch (Exception ex)
            {
                Log.Debug(LogTag, $"Window context dispose failed ({ex.GetType().Name}): {ex}");
            }
        }
        _windowContext = null;
        _ownsWindowContext = false;
        base.OnDestroy();
    }

    /// <summary>
    /// Resolves a context + <see cref="IWindowManager"/> pair from service-related contexts.
    /// Service context is preferred. For Android 11+ we also try window contexts, which are
    /// better aligned with overlay window tokens on some ROMs.
    /// </summary>
    private WindowBinding? ResolveWindowBinding()
    {
        LogDiagnostic(
            $"ResolveWindowBinding begin: sdk={(int)Build.VERSION.SdkInt}, " +
            $"service={DescribeContext(this)}, base={DescribeContext(BaseContext)}, app={DescribeContext(ApplicationContext)}");

        if (_preferredWindowContext?.TryGetTarget(out var preferredContext) == true)
        {
            var fromPreferred = TryResolveDirect("preferred", preferredContext);
            if (fromPreferred is not null)
                return fromPreferred;

            var fromPreferredWindow = TryResolveFromWindowContext("preferred", preferredContext);
            if (fromPreferredWindow is not null)
                return fromPreferredWindow;
        }

        var currentActivity = Lyricify.Lyrics.App.MainActivity.Current;
        if (currentActivity is not null)
        {
            var fromActivity = TryResolveDirect("activity", currentActivity);
            if (fromActivity is not null)
                return fromActivity;

            var fromActivityWindow = TryResolveFromWindowContext("activity", currentActivity);
            if (fromActivityWindow is not null)
                return fromActivityWindow;
        }

        var fromServiceContext = TryResolveDirect("service", this);
        if (fromServiceContext is not null)
            return fromServiceContext;

        var fromServiceWindowContext = TryResolveFromWindowContext("service", this);
        if (fromServiceWindowContext is not null)
            return fromServiceWindowContext;

        var baseContext = BaseContext;
        var fromBaseContext = TryResolveDirect("base", baseContext);
        if (fromBaseContext is not null)
            return fromBaseContext;
        if (baseContext is not null)
        {
            var fromBaseWindowContext = TryResolveFromWindowContext("base", baseContext);
            if (fromBaseWindowContext is not null)
                return fromBaseWindowContext;
        }

        var applicationContext = ApplicationContext;
        var fromAppContext = TryResolveDirect("application", applicationContext);
        if (fromAppContext is not null)
            return fromAppContext;
        if (applicationContext is not null)
        {
            var fromAppWindowContext = TryResolveFromWindowContext("application", applicationContext);
            if (fromAppWindowContext is not null)
                return fromAppWindowContext;
        }

        LogDiagnostic("ResolveWindowBinding failed: all context paths returned null WindowManager.");
        return null;
    }

    /// <summary>
    /// Creates a dedicated window context (API 30+) and resolves WindowManager from it.
    /// This path is more reliable on some vendor ROMs where direct context lookup returns null.
    /// </summary>
    private WindowBinding? TryResolveFromWindowContext(string sourceName, Context sourceContext)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(30))
            return null;

        try
        {
            var windowContext = sourceContext.CreateWindowContext((int)WindowManagerTypes.ApplicationOverlay, null);
            var manager = TryGetWindowManager(windowContext, $"{sourceName} CreateWindowContext");
            if (manager is not null)
            {
                LogDiagnostic($"Resolved WindowManager from {sourceName} CreateWindowContext().");
                return new WindowBinding(windowContext, manager, true);
            }

            LogDiagnostic($"{sourceName} CreateWindowContext() returned null WindowManager.");
            windowContext.Dispose();
        }
        catch (Exception ex)
        {
            LogDiagnostic($"{sourceName} CreateWindowContext() failed.", ex);
        }

        var display = TryResolveDisplayForWindowContext(sourceName, sourceContext);
        if (display is null)
        {
            LogDiagnostic($"{sourceName} has no display for CreateDisplayContext fallback.");
            return null;
        }

        try
        {
            var displayContext = sourceContext.CreateDisplayContext(display);
            try
            {
                var windowContext = displayContext.CreateWindowContext((int)WindowManagerTypes.ApplicationOverlay, null);
                var manager = TryGetWindowManager(windowContext, $"{sourceName} display-window context");
                if (manager is not null)
                {
                    LogDiagnostic($"Resolved WindowManager from {sourceName} CreateDisplayContext().CreateWindowContext().");
                    return new WindowBinding(windowContext, manager, true);
                }

                LogDiagnostic($"{sourceName} display-window context returned null WindowManager.");
                windowContext.Dispose();
            }
            finally
            {
                displayContext.Dispose();
            }
        }
        catch (Exception ex)
        {
            LogDiagnostic($"{sourceName} CreateDisplayContext/CreateWindowContext fallback failed.", ex);
        }

        return null;
    }

    private Display? TryResolveDisplayForWindowContext(string sourceName, Context sourceContext)
    {
        try
        {
            var contextDisplay = sourceContext.Display;
            if (contextDisplay is not null)
            {
                LogDiagnostic($"Resolved display from {sourceName} context.Display.");
                return contextDisplay;
            }
        }
        catch (Exception ex)
        {
            LogDiagnostic($"{sourceName} context.Display lookup failed.", ex);
        }

        // Different ROMs may only expose DisplayManager from one of these contexts.
        DisplayManager? displayManager = sourceContext.GetSystemService(DisplayService) as DisplayManager;
        displayManager ??= GetSystemService(DisplayService) as DisplayManager;
        displayManager ??= ApplicationContext?.GetSystemService(DisplayService) as DisplayManager;
        if (displayManager is null)
        {
            LogDiagnostic($"{sourceName} DisplayManager lookup returned null.");
            return null;
        }

        var defaultDisplay = displayManager.GetDisplay(DefaultDisplayId);
        if (defaultDisplay is not null)
        {
            LogDiagnostic($"Resolved display from {sourceName} DisplayManager default display.");
            return defaultDisplay;
        }

        try
        {
            Display? fallbackDisplay = null;
            foreach (var display in displayManager.GetDisplays())
            {
                if (display is null)
                    continue;

                fallbackDisplay ??= display;
                if (display.State != DisplayState.Off)
                {
                    LogDiagnostic($"Resolved display from {sourceName} DisplayManager active displays.");
                    return display;
                }
            }

            if (fallbackDisplay is not null)
            {
                LogDiagnostic($"Resolved display from {sourceName} DisplayManager first available display.");
                return fallbackDisplay;
            }
        }
        catch (Exception ex)
        {
            LogDiagnostic($"{sourceName} DisplayManager.GetDisplays() failed.", ex);
        }

        return null;
    }

    private WindowBinding? TryResolveDirect(string sourceName, Context? context)
    {
        if (context is null)
        {
            LogDiagnostic($"{sourceName} context is null.");
            return null;
        }

        try
        {
            var manager = TryGetWindowManager(context, $"{sourceName} direct");
            if (manager is not null)
            {
                LogDiagnostic($"Resolved WindowManager directly from {sourceName} context ({DescribeContext(context)}).");
                return new WindowBinding(context, manager, false);
            }

            LogDiagnostic($"Direct WindowManager lookup returned null from {sourceName} context ({DescribeContext(context)}).");
            return null;
        }
        catch (Exception ex)
        {
            LogDiagnostic($"Direct WindowManager lookup threw from {sourceName} context ({DescribeContext(context)}).", ex);
            return null;
        }
    }

    private IWindowManager? TryGetWindowManager(Context context, string sourceName)
    {
        try
        {
            var serviceByName = context.GetSystemService(WindowService);
            if (serviceByName is IWindowManager manager)
                return manager;

            if (serviceByName is not null)
            {
                try
                {
                    return serviceByName.JavaCast<IWindowManager>();
                }
                catch (Exception ex)
                {
                    LogDiagnostic($"{sourceName} JavaCast<IWindowManager> failed for named service.", ex);
                }
            }
        }
        catch (Exception ex)
        {
            LogDiagnostic($"{sourceName} named WindowService lookup threw.", ex);
        }

        if (!OperatingSystem.IsAndroidVersionAtLeast(23))
            return null;

        foreach (var className in new[] { "android.view.WindowManager", "android.view.IWindowManager" })
        {
            try
            {
                using var serviceClass = Java.Lang.Class.ForName(className);
                var serviceByClass = context.GetSystemService(serviceClass);
                if (serviceByClass is IWindowManager manager)
                    return manager;

                if (serviceByClass is not null)
                {
                    try
                    {
                        return serviceByClass.JavaCast<IWindowManager>();
                    }
                    catch (Exception ex)
                    {
                        LogDiagnostic($"{sourceName} JavaCast<IWindowManager> failed for class service {className}.", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                LogDiagnostic($"{sourceName} class lookup threw for {className}.", ex);
            }
        }

        return null;
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

        // Use the context paired with WindowManager to keep the same window token
        // (never use Application.Context here, which can mismatch on Android 12+).
        var ctx = _windowContext ?? this;
        _overlayView = new LyricsOverlayView(ctx);

        var density = ctx.Resources!.DisplayMetrics!.Density;
        var overlayWidthPx = (int)(360 * density);
        var positionX = global::Microsoft.Maui.Storage.Preferences.Get(PrefOverlayPosX, 0);
        var positionY = global::Microsoft.Maui.Storage.Preferences.Get(PrefOverlayPosY, (int)(80 * density));

        _overlayLayoutParams = new WindowManagerLayoutParams(
            overlayWidthPx,
            ViewGroup.LayoutParams.WrapContent,
            OperatingSystem.IsAndroidVersionAtLeast(26)
                ? WindowManagerTypes.ApplicationOverlay
                : WindowManagerTypes.Phone,
            WindowManagerFlags.NotFocusable | WindowManagerFlags.NotTouchModal,
            global::Android.Graphics.Format.Translucent)
        {
            Gravity = GravityFlags.Top | GravityFlags.Start,
            X = positionX,
            Y = positionY,
            Alpha = global::Microsoft.Maui.Storage.Preferences.Get(PrefOverlayOpacity, 0.9f),
        };

        _overlayView.SetWindowContext(_windowManager, _overlayLayoutParams);
        _overlayView.PositionChanged += OnOverlayPositionChanged;
        _overlayView.CloseRequested += OnOverlayCloseRequested;
        _overlayView.LockStateChanged += OnOverlayLockStateChanged;
        _overlayView.FontSizeChanged += OnOverlayFontSizeChanged;
        _overlayView.SetTextSizeSp(global::Microsoft.Maui.Storage.Preferences.Get(PrefLyricsFontSize, 17));
        SetOverlayLocked(global::Microsoft.Maui.Storage.Preferences.Get(PrefOverlayLocked, false));
        try
        {
            _windowManager.AddView(_overlayView, _overlayLayoutParams);
            global::Microsoft.Maui.Storage.Preferences.Set(PrefOverlayShouldRun, true);
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
        catch (global::Android.Views.WindowManagerBadTokenException ex)
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
            _overlayView.PositionChanged -= OnOverlayPositionChanged;
            _overlayView.CloseRequested -= OnOverlayCloseRequested;
            _overlayView.LockStateChanged -= OnOverlayLockStateChanged;
            _overlayView.FontSizeChanged -= OnOverlayFontSizeChanged;
            _windowManager.RemoveView(_overlayView);
        }
        catch
        {
            // View may have already been removed.
        }
        _overlayView = null;
        _overlayLayoutParams = null;
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
            case nameof(LyricsViewModel.ArtistName):
            case nameof(LyricsViewModel.HasLyrics):
                _overlayView.UpdateLines(_viewModel.CurrentLineText, _viewModel.NextLineText);
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

    private void OnOverlayPositionChanged(int x, int y)
    {
        global::Microsoft.Maui.Storage.Preferences.Set(PrefOverlayPosX, x);
        global::Microsoft.Maui.Storage.Preferences.Set(PrefOverlayPosY, y);
    }

    private void OnOverlayCloseRequested()
    {
        global::Microsoft.Maui.Storage.Preferences.Set(PrefOverlayShouldRun, false);
        StopSelf();
    }

    private void OnOverlayLockStateChanged(bool isLocked) => SetOverlayLocked(isLocked);

    private void OnOverlayFontSizeChanged(float size)
    {
        global::Microsoft.Maui.Storage.Preferences.Set(PrefLyricsFontSize, size);
        _overlayView?.SetTextSizeSp(size);
    }

    private void SetOverlayLocked(bool isLocked)
    {
        _overlayLocked = isLocked;
        global::Microsoft.Maui.Storage.Preferences.Set(PrefOverlayLocked, _overlayLocked);
        if (_overlayLayoutParams is null || _windowManager is null || _overlayView is null)
            return;

        _overlayLayoutParams.Flags = _overlayLocked
            ? WindowManagerFlags.NotFocusable | WindowManagerFlags.NotTouchable
            : WindowManagerFlags.NotFocusable | WindowManagerFlags.NotTouchModal;
        _overlayLayoutParams.Alpha = _overlayLocked
            ? 1f
            : global::Microsoft.Maui.Storage.Preferences.Get(PrefOverlayOpacity, 0.9f);
        _overlayView.SetLocked(_overlayLocked);

        try
        {
            _windowManager.UpdateViewLayout(_overlayView, _overlayLayoutParams);
        }
        catch (Exception ex)
        {
            LogDiagnostic("Failed to update overlay lock state.", ex);
        }

        var manager = GetSystemService(NotificationService) as NotificationManager;
        manager?.Notify(NotificationId, BuildNotification(_viewModel?.TrackTitle ?? "Lyricify is running", _viewModel?.ArtistName ?? "Tap to open"));
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
        LogDiagnostic(reason, level: AppLogLevel.Error);
        LastStartupError = reason;
        global::Microsoft.Maui.Storage.Preferences.Set(PrefOverlayShouldRun, false);
        if (OperatingSystem.IsAndroidVersionAtLeast(24))
            StopForeground(StopForegroundFlags.Remove);
        else
            StopForeground(true);

        // Post a persistent error notification so the user can see the reason
        // even after navigating away from the app.
        PostErrorNotification(reason);

        // Notify any subscribed UI on the main thread.
        RunOnMainThread(() => OverlayStartResult?.Invoke(reason));

        StopSelf();
    }

    private static string DescribeContext(Context? context)
        => context is null ? "null" : $"{context.GetType().Name}@{context.GetHashCode():x}";

    private static void LogDiagnostic(string message, Exception? ex = null, AppLogLevel level = AppLogLevel.Warning)
    {
        var finalMessage = ex is null
            ? message
            : $"{message} ({ex.GetType().Name}: {ex.Message})";
        switch (level)
        {
            case AppLogLevel.Error:
                Log.Error(LogTag, finalMessage);
                break;
            case AppLogLevel.Warning:
            default:
                Log.Warn(LogTag, finalMessage);
                break;
        }
        AppLogService.Current?.Add(level, LogTag, finalMessage);
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

        var unlockIntent = new Intent(this, typeof(LyricsOverlayService));
        unlockIntent.SetAction(ActionUnlockOverlay);
        var unlockPendingIntent = PendingIntent.GetService(
            this,
            1,
            unlockIntent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var builder = new Notification.Builder(this, ChannelId)
            .SetContentTitle(title)
            .SetContentText(text)
            .SetSmallIcon(global::Android.Resource.Drawable.IcMediaPlay)
            .SetContentIntent(pendingIntent)
            .SetOngoing(true);

        if (_overlayLocked)
            builder.AddAction(global::Android.Resource.Drawable.IcLockIdleLock, "解锁悬浮歌词", unlockPendingIntent);

        return builder.Build()!;
    }
#pragma warning restore CA1416
}
