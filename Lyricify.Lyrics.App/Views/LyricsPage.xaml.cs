using Lyricify.Lyrics.App.ViewModels;
using Lyricify.Lyrics.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Lyricify.Lyrics.App.Views;

public partial class LyricsPage : ContentPage
{
    private LyricsViewModel? _viewModel;
    private int _lastScrolledLineIndex = -1;
    private CancellationTokenSource? _albumArtLongPressCts;
    private const int AlbumArtLongPressMs = 650;
    private const string PrefOverlayEnabled = "overlay_enabled";
    private const string PrefOverlayShouldRun = "overlay_should_run";

    /// <summary>
    /// Parameterless constructor used by MAUI Shell <c>DataTemplate</c> resolution.
    /// </summary>
    public LyricsPage()
    {
        InitializeComponent();
        var viewModel = IPlatformApplication.Current?.Services.GetService<LyricsViewModel>();
        if (viewModel is not null)
            SetViewModel(viewModel);
    }

    public LyricsPage(LyricsViewModel viewModel)
    {
        InitializeComponent();
        SetViewModel(viewModel);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        EnsureViewModel();
        if (_viewModel is null)
        {
            StatusMessageLabel.Text = "页面初始化失败，请重启应用";
            StatusMessageLabel.IsVisible = true;
            return;
        }

        _viewModel.StartPollingCommand.Execute(null);
        OverlayToggleButton.IsVisible = DeviceInfo.Current.Platform == DevicePlatform.Android
            && Preferences.Get(PrefOverlayEnabled, false);
#if ANDROID
        _overlayRunning = Lyricify.Lyrics.App.Platforms.Android.LyricsOverlayService.IsRunning;
        OverlayToggleButton.Text = _overlayRunning ? OverlayButtonTextHide : OverlayButtonTextShow;
        var shouldRestoreOverlay = Preferences.Get(PrefOverlayShouldRun, false);

        // Show any previously recorded startup error (e.g. app navigated away during start).
        if (!_overlayRunning && Lyricify.Lyrics.App.Platforms.Android.LyricsOverlayService.LastStartupError is { } lastErr)
        {
            StatusMessageLabel.Text = lastErr;
            StatusMessageLabel.IsVisible = true;
        }

        // SYSTEM_ALERT_WINDOW runtime gate exists from API 23; below that no runtime permission prompt is required.
        var hasOverlayPermission = !OperatingSystem.IsAndroidVersionAtLeast(23)
            || Android.Provider.Settings.CanDrawOverlays(Platform.CurrentActivity);
        if (_retryOverlayStartAfterPermission && hasOverlayPermission)
        {
            _retryOverlayStartAfterPermission = false;
            StartAndroidOverlay();
        }
        else if (!_overlayRunning && OverlayToggleButton.IsVisible && shouldRestoreOverlay && hasOverlayPermission)
        {
            StartAndroidOverlay();
        }
#endif

        // Subscribe to sync updates to drive live highlighting and auto-scroll.
        if (_viewModel is { } vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _albumArtLongPressCts?.Cancel();
        _albumArtLongPressCts?.Dispose();
        _albumArtLongPressCts = null;

        if (_viewModel is { } vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        // On Android the overlay keeps everything running in the background.
#if !ANDROID
        _viewModel.StopPollingCommand.Execute(null);
#endif
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LyricsViewModel.CurrentLineIndex))
        {
            ScrollToCurrentLine();
            HighlightCurrentLine();
        }
    }

    // ── Scroll to active line ─────────────────────────────────────────────────

    private void ScrollToCurrentLine()
    {
        if (_viewModel is null) return;

        var idx = _viewModel.CurrentLineIndex;
        if (idx < 0)
        {
            LyricsCollection.SelectedItem = null;
            _lastScrolledLineIndex = -1;
            return;
        }
        if (_viewModel.LyricLines is not { Count: > 0 } lines) return;
        if (idx >= lines.Count) return;

        var item = lines[idx];
        if (!Equals(LyricsCollection.SelectedItem, item))
            LyricsCollection.SelectedItem = item;

        if (idx != _lastScrolledLineIndex)
        {
            LyricsCollection.ScrollTo(item, position: ScrollToPosition.Center, animate: true);
            _lastScrolledLineIndex = idx;
        }
    }

    // ── Highlight active line (simple colour approach) ────────────────────────

    private void HighlightCurrentLine()
    {
        // For a production app, use a custom DataTemplateSelector or a trigger.
        // Here we rely on the ViewModel's CurrentLineIndex binding and let
        // iOS render a distinct row via a converter (see LyricsLineColorConverter below).
        // The CollectionView itself re-queries item colour via DataTrigger on each cell.
    }

    // ── Android overlay toggle ────────────────────────────────────────────────

    private void OnToggleOverlayClicked(object sender, EventArgs e)
    {
#if ANDROID
        ToggleAndroidOverlay(sender);
#endif
    }

    private async void OnAlbumArtPressed(object sender, EventArgs e)
    {
        _albumArtLongPressCts?.Cancel();
        _albumArtLongPressCts?.Dispose();
        _albumArtLongPressCts = new CancellationTokenSource();
        var token = _albumArtLongPressCts.Token;

        try
        {
            await Task.Delay(AlbumArtLongPressMs, token);
            if (token.IsCancellationRequested) return;
            OpenSettingsTab();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnAlbumArtReleased(object sender, EventArgs e)
    {
        _albumArtLongPressCts?.Cancel();
        _albumArtLongPressCts?.Dispose();
        _albumArtLongPressCts = null;
    }

    private void OpenSettingsTab()
    {
        if (Shell.Current is not Shell shell) return;
        var settingsTab = shell.Items.FirstOrDefault(i =>
            string.Equals(i.Title, "Settings", StringComparison.OrdinalIgnoreCase));
        if (settingsTab is not null)
            shell.CurrentItem = settingsTab;
    }

    private void EnsureViewModel()
    {
        if (_viewModel is not null) return;

        var services = IPlatformApplication.Current?.Services
            ?? Handler?.MauiContext?.Services
            ?? Application.Current?.Handler?.MauiContext?.Services;

        var viewModel = services?.GetService(typeof(LyricsViewModel)) as LyricsViewModel;
        if (viewModel is not null)
            SetViewModel(viewModel);
    }

    private void SetViewModel(LyricsViewModel viewModel)
    {
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

#if ANDROID
    private bool _overlayRunning;
    private bool _retryOverlayStartAfterPermission;

    private const string OverlayButtonTextShow = "🪟  Show floating lyrics";
    private const string OverlayButtonTextHide = "🪟  Hide floating lyrics";
    private const string OverlayPermissionHint = "请授予悬浮窗权限后再次点击按钮";

#pragma warning disable CA1416 // Validate platform compatibility
    private void ToggleAndroidOverlay(object sender)
    {
        var context = Platform.CurrentActivity
            ?? throw new InvalidOperationException("No current Android activity.");

        if (!_overlayRunning)
        {
            // Check SYSTEM_ALERT_WINDOW permission.
            if (!Android.Provider.Settings.CanDrawOverlays(context))
            {
                var intent = new Android.Content.Intent(
                    Android.Provider.Settings.ActionManageOverlayPermission,
                    Android.Net.Uri.Parse($"package:{context.PackageName}"));
                context.StartActivity(intent);
                _retryOverlayStartAfterPermission = true;
                StatusMessageLabel.Text = OverlayPermissionHint;
                StatusMessageLabel.IsVisible = true;
                return;
            }

            StartAndroidOverlay();
        }
        else
        {
            var serviceIntent = new Android.Content.Intent(context, typeof(Lyricify.Lyrics.App.Platforms.Android.LyricsOverlayService));
            context.StopService(serviceIntent);
            Preferences.Set(PrefOverlayShouldRun, false);
            _overlayRunning = false;
            (sender as Button)!.Text = OverlayButtonTextShow;
        }
    }

    private void StartAndroidOverlay()
    {
        var context = Platform.CurrentActivity
            ?? throw new InvalidOperationException("No current Android activity.");
        Lyricify.Lyrics.App.Platforms.Android.LyricsOverlayService.SetPreferredWindowContext(context);

        // Subscribe to service startup result before launching so we don't miss the event.
        Action<string?>? handler = null;
        handler = result =>
        {
            Lyricify.Lyrics.App.Platforms.Android.LyricsOverlayService.OverlayStartResult -= handler;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (result is null)
                {
                    _overlayRunning = true;
                    Preferences.Set(PrefOverlayShouldRun, true);
                    OverlayToggleButton.Text = OverlayButtonTextHide;
                    StatusMessageLabel.IsVisible = false;
                }
                else
                {
                    _overlayRunning = false;
                    Preferences.Set(PrefOverlayShouldRun, false);
                    OverlayToggleButton.Text = OverlayButtonTextShow;
                    StatusMessageLabel.Text = result;
                    StatusMessageLabel.IsVisible = true;
                }
            });
        };
        Lyricify.Lyrics.App.Platforms.Android.LyricsOverlayService.OverlayStartResult += handler;

        var serviceIntent = new Android.Content.Intent(context, typeof(Lyricify.Lyrics.App.Platforms.Android.LyricsOverlayService));
        try
        {
            context.StartForegroundService(serviceIntent);
            // UI updates will arrive via the OverlayStartResult event.
        }
        catch (Exception ex)
        {
            // StartForegroundService itself threw – clean up the subscription.
            Lyricify.Lyrics.App.Platforms.Android.LyricsOverlayService.OverlayStartResult -= handler;
            _overlayRunning = false;
            OverlayToggleButton.Text = OverlayButtonTextShow;
            StatusMessageLabel.Text = $"启动悬浮窗失败：{ex.Message}";
            StatusMessageLabel.IsVisible = true;
        }
    }
#pragma warning restore CA1416
#endif
}
