using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Lyricify.Lyrics.Models;

namespace Lyricify.Lyrics.App.Platforms.Android;

/// <summary>
/// A lightweight <see cref="View"/> drawn directly by <see cref="WindowManager"/>.
/// Shows the active lyric line (large, highlighted) and the next line (smaller, dim).
/// Supports drag-to-reposition via touch events.
/// </summary>
internal sealed class LyricsOverlayView : LinearLayout
{
    private readonly TextView _currentLineView;
    private readonly TextView _nextLineView;

    // Drag state
    private float _dragStartX;
    private float _dragStartY;
    private int _initialWindowX;
    private int _initialWindowY;
    private WindowManagerLayoutParams? _layoutParams;
    private IWindowManager? _windowManager;

    // Colours
    private static readonly Color ActiveColor = Color.ParseColor("#1DB954");   // Spotify green
    private static readonly Color DimColor = Color.ParseColor("#B3FFFFFF");    // semi-transparent white

    public LyricsOverlayView(Context context) : base(context)
    {
        Orientation = Orientation.Vertical;
        SetBackgroundColor(Color.ParseColor("#CC000000")); // semi-transparent black

        var padding = (int)(12 * Resources!.DisplayMetrics!.Density);
        var cornerRadius = (int)(8 * Resources.DisplayMetrics.Density);
        SetPadding(padding, padding / 2, padding, padding / 2);

        // Clip children to rounded corners via outline.
        OutlineProvider = ViewOutlineProvider.Background;
        ClipToOutline = true;

        _currentLineView = new TextView(context)
        {
            TextSize = 16,  // sp – will be overridden by settings
        };
        _currentLineView.SetTextColor(ActiveColor);
        _currentLineView.SetTypeface(null, global::Android.Graphics.TypefaceStyle.Bold);
        _currentLineView.Gravity = GravityFlags.CenterHorizontal;
        _currentLineView.SetMaxLines(2);
        _currentLineView.SetSingleLine(false);

        _nextLineView = new TextView(context)
        {
            TextSize = 13,
        };
        _nextLineView.SetTextColor(DimColor);
        _nextLineView.Gravity = GravityFlags.CenterHorizontal;
        _nextLineView.SetMaxLines(1);

        AddView(_currentLineView);
        AddView(_nextLineView);
    }

    /// <summary>Updates the displayed lyric lines. Safe to call from any thread.</summary>
    public void UpdateLines(string currentLine, string nextLine)
    {
        if (Looper.MainLooper!.Thread != Java.Lang.Thread.CurrentThread())
        {
            Post(() => UpdateLines(currentLine, nextLine));
            return;
        }

        _currentLineView.Text = currentLine;
        _nextLineView.Text = nextLine;
        _nextLineView.Visibility = string.IsNullOrWhiteSpace(nextLine)
            ? ViewStates.Gone
            : ViewStates.Visible;
    }

    /// <summary>Updates karaoke-style progress highlight on the current line.</summary>
    public void UpdateProgress(double lineProgress)
    {
        // Interpolate text colour from dim → active as the line progresses.
        // A full gradient shader would require a custom draw; here we use a simple alpha.
        if (Looper.MainLooper!.Thread != Java.Lang.Thread.CurrentThread())
        {
            Post(() => UpdateProgress(lineProgress));
            return;
        }

        var alpha = (int)(0x66 + (0xFF - 0x66) * lineProgress);
        var blended = Color.Argb(alpha, ActiveColor.R, ActiveColor.G, ActiveColor.B);
        _currentLineView.SetTextColor(blended);
    }

    /// <summary>Attaches drag-to-move behaviour.</summary>
    public void SetWindowContext(IWindowManager windowManager, WindowManagerLayoutParams layoutParams)
    {
        _windowManager = windowManager;
        _layoutParams = layoutParams;
    }

    /// <summary>Adjusts the text size in sp units.</summary>
    public void SetTextSizeSp(float sp)
    {
        _currentLineView.SetTextSize(global::Android.Util.ComplexUnitType.Sp, sp);
        _nextLineView.SetTextSize(global::Android.Util.ComplexUnitType.Sp, sp * 0.8f);
    }

    // ── Touch handling ────────────────────────────────────────────────────────

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null || _windowManager is null || _layoutParams is null)
            return base.OnTouchEvent(e);

        switch (e.Action)
        {
            case MotionEventActions.Down:
                _dragStartX = e.RawX;
                _dragStartY = e.RawY;
                _initialWindowX = _layoutParams.X;
                _initialWindowY = _layoutParams.Y;
                return true;

            case MotionEventActions.Move:
                _layoutParams.X = _initialWindowX + (int)(e.RawX - _dragStartX);
                _layoutParams.Y = _initialWindowY + (int)(e.RawY - _dragStartY);
                _windowManager.UpdateViewLayout(this, _layoutParams);
                return true;
        }

        return base.OnTouchEvent(e);
    }
}
