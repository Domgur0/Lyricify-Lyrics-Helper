using Lyricify.Lyrics.App.ViewModels;
using Lyricify.Lyrics.Models;

namespace Lyricify.Lyrics.App.Views;

public partial class LyricsPage : ContentPage
{
    private readonly LyricsViewModel _viewModel;
    private int _lastScrolledLineIndex = -1;

    public LyricsPage(LyricsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.StartPollingCommand.Execute(null);

        // Subscribe to sync updates to drive live highlighting and auto-scroll.
        if (_viewModel is { } vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

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
        var idx = _viewModel.CurrentLineIndex;
        if (idx < 0 || idx == _lastScrolledLineIndex) return;
        if (_viewModel.LyricLines is not { Count: > 0 } lines) return;
        if (idx >= lines.Count) return;

        var item = lines[idx];
        LyricsCollection.ScrollTo(item, position: ScrollToPosition.Center, animate: true);
        _lastScrolledLineIndex = idx;
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
        ToggleAndroidOverlay();
#endif
    }

#if ANDROID
    private bool _overlayRunning;

    private const string OverlayButtonTextShow = "🪟  Show floating lyrics";
    private const string OverlayButtonTextHide = "🪟  Hide floating lyrics";

    private void ToggleAndroidOverlay()
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
                return;
            }

            var serviceIntent = new Android.Content.Intent(context, typeof(Lyricify.Lyrics.App.Platforms.Android.LyricsOverlayService));
            context.StartForegroundService(serviceIntent);
            _overlayRunning = true;
            (sender as Button)!.Text = OverlayButtonTextHide;
        }
        else
        {
            var serviceIntent = new Android.Content.Intent(context, typeof(Lyricify.Lyrics.App.Platforms.Android.LyricsOverlayService));
            context.StopService(serviceIntent);
            _overlayRunning = false;
            (sender as Button)!.Text = OverlayButtonTextShow;
        }
    }
#endif
}
