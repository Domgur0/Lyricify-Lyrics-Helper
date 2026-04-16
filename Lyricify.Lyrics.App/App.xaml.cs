namespace Lyricify.Lyrics.App;

public partial class App : Application
{
    private const string StartupErrorTitle = "应用启动失败";
    private const string StartupErrorMessage = "初始化失败，请重启应用或检查配置。";

    public App()
    {
        InitializeComponent();
        RegisterGlobalExceptionHandlers();
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

    private static Page CreateStartupErrorPage(Exception ex)
    {
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
                    }
                }
            }
        };
    }

    private static void ReportError(string title, Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[{title}] {ex}");
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
