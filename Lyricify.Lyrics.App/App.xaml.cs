using Lyricify.Lyrics.App.Services;

namespace Lyricify.Lyrics.App;

public partial class App : Application
{
    private const string StartupErrorTitle = "应用启动失败";
    private const string StartupErrorMessage = "初始化失败，请重启应用或检查配置。";

    /// <summary>
    /// Set when a JVM crash report file is found during startup.
    /// <see cref="SettingsPage"/> reads this to show a prompt to the user.
    /// </summary>
    internal static bool HasPendingCrashReport { get; private set; }

    public App()
    {
        InitializeComponent();
        RegisterGlobalExceptionHandlers();
        LoadCrashReportIfPresent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        try
        {
            // Always enter the main shell: login is performed from Settings.
            return new Window(new AppShell());
        }
        catch (Exception ex)
        {
            ReportError(StartupErrorTitle, ex);
            return new Window(CreateStartupErrorPage(ex));
        }
    }

    private void RegisterGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                ReportError("未处理异常", ex);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            ReportError("后台任务异常", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>
    /// If the JVM crash handler left a crash file from a previous run, import
    /// a summary into <see cref="AppLogService"/> as an error entry and set
    /// <see cref="HasPendingCrashReport"/> so the UI can prompt the user.
    /// The full crash content is already appended to the session log via the
    /// normal <see cref="AppLogService.Add"/> persistence path.
    /// The crash file is deleted after being imported to avoid repeated alerts.
    /// </summary>
    private static void LoadCrashReportIfPresent()
    {
        try
        {
#if ANDROID
            var path = MainApplication.CrashFilePath;
#else
            string? path = null;
#endif
            if (path is null || !File.Exists(path)) return;

            var content = File.ReadAllText(path);
            if (!string.IsNullOrWhiteSpace(content))
            {
                // Truncate to a sane size for the in-memory entry; the full
                // content will still appear in the exported session log file.
                const int MaxPreview = 3000;
                var preview = content.Length > MaxPreview
                    ? content[..MaxPreview] + $"{Environment.NewLine}... [truncated – export log for full details]"
                    : content;

                AppLogService.Current?.Add(
                    AppLogLevel.Error,
                    "CrashReport",
                    $"Previous session JVM crash:{Environment.NewLine}{preview}");
                HasPendingCrashReport = true;
            }

            File.Delete(path);
        }
        catch
        {
            // Non-critical.
        }
    }

    private static Page CreateStartupErrorPage(Exception ex)
    {
        var exportButton = new Button
        {
            Text = "导出日志",
            BackgroundColor = Color.FromArgb("#3A3A3A"),
            TextColor = Colors.White,
            CornerRadius = 8,
            Margin = new Thickness(0, 10, 0, 0),
        };
        exportButton.Clicked += async (_, _) => await ExportLogFromErrorPageAsync(exportButton);

        return new ContentPage
        {
            BackgroundColor = Color.FromArgb("#121212"),
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(20),
                Spacing = 10,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text = StartupErrorTitle,
                        TextColor = Color.FromArgb("#FF6B6B"),
                        FontSize = 20,
                        HorizontalTextAlignment = TextAlignment.Center
                    },
                    new Label
                    {
                        Text = StartupErrorMessage,
                        TextColor = Colors.White,
                        HorizontalTextAlignment = TextAlignment.Center
                    },
                    new Label
                    {
                        Text = ex.Message,
                        TextColor = Color.FromArgb("#B3FFFFFF"),
                        HorizontalTextAlignment = TextAlignment.Center
                    },
                    exportButton
                }
            }
        };
    }

    private static async Task ExportLogFromErrorPageAsync(Button button)
    {
        button.IsEnabled = false;
        try
        {
            var logService = AppLogService.Current;

            var persistedText = logService is not null
                ? await logService.ReadPersistedLogAsync()
                : string.Empty;

            var logText = !string.IsNullOrWhiteSpace(persistedText)
                ? persistedText
                : (logService?.ExportText() ?? "(Log service unavailable)");

            var fileName = $"lyricify-log-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt";
            var filePath = Path.Combine(FileSystem.CacheDirectory, fileName);
            await File.WriteAllTextAsync(filePath, logText);

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Lyricify Log",
                File = new ShareFile(filePath, "text/plain"),
            });
        }
        catch
        {
            // Nothing useful to show when we are already on an error page.
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private static void ReportError(string title, Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[{title}] {ex}");
        AppLogService.Current?.Add(AppLogLevel.Error, title, ex.ToString());
#if ANDROID
        TryShowAndroidErrorNotification(title, ex.Message);
#endif
    }

#if ANDROID
    private const string AndroidErrorChannelId = "lyricify_error_channel";
    private const int AndroidErrorNotificationId = 3001;

#pragma warning disable CA1416 // Validate platform compatibility
    private static void TryShowAndroidErrorNotification(string title, string message)
    {
        try
        {
            var context = global::Android.App.Application.Context;
            var manager = context.GetSystemService(global::Android.Content.Context.NotificationService)
                as global::Android.App.NotificationManager;
            if (manager is null) return;

            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                var channel = new global::Android.App.NotificationChannel(
                    AndroidErrorChannelId,
                    "Lyricify errors",
                    global::Android.App.NotificationImportance.High)
                {
                    Description = "Reports startup/runtime errors from Lyricify",
                };
                manager.CreateNotificationChannel(channel);
            }

            var notification = new global::Android.App.Notification.Builder(context, AndroidErrorChannelId)
                .SetContentTitle(title)
                .SetContentText(message)
                .SetStyle(new global::Android.App.Notification.BigTextStyle().BigText(message))
                .SetSmallIcon(global::Android.Resource.Drawable.StatNotifyError)
                .SetAutoCancel(true)
                .Build();

            manager.Notify(AndroidErrorNotificationId, notification);
        }
        catch
        {
            // Avoid secondary failures in error reporter.
        }
    }
#pragma warning restore CA1416
#endif
}
