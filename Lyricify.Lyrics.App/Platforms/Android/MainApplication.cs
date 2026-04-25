using Android.App;
using Android.Runtime;
using Lyricify.Lyrics.App.Services;

namespace Lyricify.Lyrics.App;

[Application]
public class MainApplication : MauiApplication
{
    /// <summary>
    /// Absolute path of the JVM crash report file written by
    /// <see cref="JvmCrashHandler"/> when the app is killed by an unhandled
    /// Java/JVM exception.  Checked by <see cref="App"/> on the next launch.
    /// </summary>
    internal static string? CrashFilePath { get; private set; }

    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    public override void OnCreate()
    {
        base.OnCreate();

        // Determine crash file path now (FilesDir is available after base.OnCreate).
        CrashFilePath = System.IO.Path.Combine(
            FilesDir!.AbsolutePath, "lyricify-crash.txt");

        // Chain a JVM-level uncaught-exception handler so that JNI / Java
        // crashes (which bypass AppDomain.UnhandledException) are persisted
        // to disk before the process dies.
        var previous = Java.Lang.Thread.DefaultUncaughtExceptionHandler;
        Java.Lang.Thread.DefaultUncaughtExceptionHandler =
            new JvmCrashHandler(CrashFilePath, previous);
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    // ── JVM crash handler ─────────────────────────────────────────────────────

    private sealed class JvmCrashHandler
        : Java.Lang.Object,
          Java.Lang.Thread.IUncaughtExceptionHandler
    {
        private readonly string _crashFilePath;
        private readonly Java.Lang.Thread.IUncaughtExceptionHandler? _previous;

        public JvmCrashHandler(
            string crashFilePath,
            Java.Lang.Thread.IUncaughtExceptionHandler? previous)
        {
            _crashFilePath = crashFilePath;
            _previous = previous;
        }

        public void UncaughtException(Java.Lang.Thread t, Java.Lang.Throwable e)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(
                    $"[{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC] " +
                    $"JVM CRASH on thread '{t.Name}'");
                sb.AppendLine(e.ToString());

                // Append the in-memory app log so the crash file is self-contained.
                if (AppLogService.Current is { } log)
                {
                    sb.AppendLine();
                    sb.AppendLine("--- In-memory log at crash time ---");
                    sb.AppendLine(log.ExportText());
                }

                System.IO.File.AppendAllText(_crashFilePath, sb.ToString());
            }
            catch
            {
                // Must not throw inside a crash handler.
            }

            // Invoke the previous handler (usually the Android runtime's handler)
            // so the system can display the "App stopped" dialog and generate a
            // tombstone for ADB logcat.
            _previous?.UncaughtException(t, e);
        }
    }
}
