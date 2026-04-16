using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;

namespace Lyricify.Lyrics.App.Platforms.Android;

/// <summary>
/// A lightweight Android view drawn directly by <see cref="WindowManager"/>.
/// Shows the active lyric line (large, highlighted) and the next line (smaller, dim).
/// Supports drag-to-reposition via touch events.
/// </summary>
internal sealed class LyricsOverlayView : LinearLayout
{
    private readonly LinearLayout _controlsRow;
    private readonly TextView _currentLineView;
    private readonly TextView _nextLineView;
    private readonly TextView _lockButton;
    private readonly TextView _closeButton;
    private readonly TextView _settingsButton;

    // Drag state
    private float _dragStartX;
    private float _dragStartY;
    private int _initialWindowX;
    private int _initialWindowY;
    private WindowManagerLayoutParams? _layoutParams;
    private IWindowManager? _windowManager;
    private bool _isLocked;
    private bool _controlsVisible;
    private readonly float[] _fontSizes = [14f, 17f, 20f, 24f];
    private int _fontSizeIndex = 1;
    private float _currentFontSizeSp = 17f;
    private readonly float _dragThresholdPx;

    // Colours
    private static readonly global::Android.Graphics.Color ActiveColor = global::Android.Graphics.Color.ParseColor("#39B4E8");
    private static readonly global::Android.Graphics.Color DimColor = global::Android.Graphics.Color.ParseColor("#B3FFFFFF");    // semi-transparent white
    private static readonly global::Android.Graphics.Color ControlsColor = global::Android.Graphics.Color.ParseColor("#CCFFFFFF");

    public event Action? CloseRequested;
    public event Action<bool>? LockStateChanged;
    public event Action<float>? FontSizeChanged;
    public event Action<int, int>? PositionChanged;

    public LyricsOverlayView(Context context) : base(context)
    {
        Orientation = Orientation.Vertical;
        Gravity = GravityFlags.CenterHorizontal;
        SetBackgroundColor(global::Android.Graphics.Color.ParseColor("#CC000000"));

        Clickable = true;
        Focusable = false;

        var density = Resources!.DisplayMetrics!.Density;
        _dragThresholdPx = 4 * density;
        var padding = (int)(12 * density);
        SetPadding(padding, padding, padding, padding / 2);

        // Clip children to rounded corners via outline.
        OutlineProvider = ViewOutlineProvider.Background;
        ClipToOutline = true;

        var actionsRow = new LinearLayout(context)
        {
            Orientation = Orientation.Horizontal,
            Gravity = GravityFlags.End,
        };
        actionsRow.LayoutParameters = new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);

        _lockButton = CreateIconTextButton(context, "🔒");
        _settingsButton = CreateIconTextButton(context, "⚙");
        _closeButton = CreateIconTextButton(context, "✕");

        _lockButton.Click += (_, _) =>
        {
            SetLocked(!_isLocked);
            LockStateChanged?.Invoke(_isLocked);
        };
        _settingsButton.Click += (_, _) =>
        {
            CycleFontSize();
            FontSizeChanged?.Invoke(_currentFontSizeSp);
        };
        _closeButton.Click += (_, _) => CloseRequested?.Invoke();

        actionsRow.AddView(_lockButton);
        actionsRow.AddView(_settingsButton);
        actionsRow.AddView(_closeButton);

        _currentLineView = new TextView(context)
        {
            TextSize = 16,
        };
        _currentLineView.SetTextColor(ActiveColor);
        _currentLineView.SetTypeface(null, global::Android.Graphics.TypefaceStyle.Bold);
        _currentLineView.Gravity = GravityFlags.CenterHorizontal;
        ConfigureMarquee(_currentLineView);

        _nextLineView = new TextView(context)
        {
            TextSize = 13,
        };
        _nextLineView.SetTextColor(DimColor);
        _nextLineView.Gravity = GravityFlags.CenterHorizontal;
        ConfigureMarquee(_nextLineView);

        var lyricRow = new LinearLayout(context)
        {
            Orientation = Orientation.Vertical,
            Gravity = GravityFlags.CenterHorizontal,
        };
        lyricRow.LayoutParameters = new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        lyricRow.AddView(_currentLineView);
        lyricRow.AddView(_nextLineView);

        _controlsRow = new LinearLayout(context)
        {
            Orientation = Orientation.Horizontal,
            Gravity = GravityFlags.CenterHorizontal,
        };
        _controlsRow.LayoutParameters = new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = (int)(6 * density),
        };
        _controlsRow.Visibility = ViewStates.Gone;
        _controlsRow.AddView(actionsRow);

        AddView(lyricRow);
        AddView(_controlsRow);
        SetTextSizeSp(_currentFontSizeSp);
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
        var blended = global::Android.Graphics.Color.Argb(alpha, ActiveColor.R, ActiveColor.G, ActiveColor.B);
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
        _currentFontSizeSp = sp;
        _fontSizeIndex = GetClosestFontSizeIndex(sp);
        _currentLineView.SetTextSize(global::Android.Util.ComplexUnitType.Sp, sp);
        _nextLineView.SetTextSize(global::Android.Util.ComplexUnitType.Sp, sp * 0.8f);
    }

    public void SetLocked(bool isLocked)
    {
        _isLocked = isLocked;
        _lockButton.Text = _isLocked ? "🔓" : "🔒";
        _controlsVisible = !_isLocked && _controlsVisible;
        _controlsRow.Visibility = (!_isLocked && _controlsVisible) ? ViewStates.Visible : ViewStates.Gone;
        SetBackgroundColor(_isLocked
            ? global::Android.Graphics.Color.Transparent
            : global::Android.Graphics.Color.ParseColor("#CC000000"));
    }

    public bool IsLocked => _isLocked;

    // ── Touch handling ────────────────────────────────────────────────────────

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null || _windowManager is null || _layoutParams is null)
            return base.OnTouchEvent(e);

        switch (e.Action)
        {
            case MotionEventActions.Down:
                if (_isLocked)
                    return false;
                _dragStartX = e.RawX;
                _dragStartY = e.RawY;
                _initialWindowX = _layoutParams.X;
                _initialWindowY = _layoutParams.Y;
                return true;

            case MotionEventActions.Move:
                if (_isLocked)
                    return false;
                _layoutParams.X = _initialWindowX + (int)(e.RawX - _dragStartX);
                _layoutParams.Y = _initialWindowY + (int)(e.RawY - _dragStartY);
                try
                {
                    _windowManager.UpdateViewLayout(this, _layoutParams);
                }
                catch (Java.Lang.IllegalArgumentException)
                {
                    // Thrown when the view is no longer attached to a window
                    // (e.g. the overlay service was stopped during a drag gesture).
                }
                catch (Java.Lang.IllegalStateException)
                {
                    // Thrown when the view's window token has become invalid.
                }
                return true;
            case MotionEventActions.Up:
                if (!_isLocked)
                {
                    var deltaX = Math.Abs(e.RawX - _dragStartX);
                    var deltaY = Math.Abs(e.RawY - _dragStartY);
                    if (deltaX <= _dragThresholdPx && deltaY <= _dragThresholdPx)
                        ToggleControlsVisibility();
                    else
                        PositionChanged?.Invoke(_layoutParams.X, _layoutParams.Y);
                }
                PerformClick();
                return true;
        }

        return base.OnTouchEvent(e);
    }

    public override bool PerformClick()
    {
        base.PerformClick();
        return true;
    }

    private static void ConfigureMarquee(TextView textView)
    {
        textView.SetSingleLine(true);
        textView.SetHorizontallyScrolling(true);
        textView.Ellipsize = TextUtils.TruncateAt.Marquee;
        textView.SetMarqueeRepeatLimit(-1);
        textView.Selected = true;
    }

    private TextView CreateIconTextButton(Context context, string text)
    {
        var button = new TextView(context)
        {
            Text = text,
            TextSize = 20,
            Gravity = GravityFlags.Center,
        };
        var buttonPadding = (int)(8 * Resources!.DisplayMetrics!.Density);
        button.SetTextColor(ControlsColor);
        button.SetPadding(buttonPadding, buttonPadding, buttonPadding, buttonPadding);
        button.Clickable = true;
        return button;
    }

    private void ToggleControlsVisibility()
    {
        if (_isLocked)
            return;

        _controlsVisible = !_controlsVisible;
        _controlsRow.Visibility = _controlsVisible ? ViewStates.Visible : ViewStates.Gone;
    }

    private int GetClosestFontSizeIndex(float value)
    {
        var closestIndex = 0;
        var closestDistance = float.MaxValue;
        for (var i = 0; i < _fontSizes.Length; i++)
        {
            var distance = Math.Abs(_fontSizes[i] - value);
            if (distance >= closestDistance)
                continue;
            closestDistance = distance;
            closestIndex = i;
        }
        return closestIndex;
    }

    private void CycleFontSize()
    {
        _fontSizeIndex = (_fontSizeIndex + 1) % _fontSizes.Length;
        SetTextSizeSp(_fontSizes[_fontSizeIndex]);
    }
}
