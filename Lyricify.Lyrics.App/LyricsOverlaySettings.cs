namespace Lyricify.Lyrics.App;

/// <summary>
/// Shared constants for the floating lyrics overlay that are consumed by both the
/// Android platform implementation and the cross-platform Settings UI.
/// </summary>
public static class LyricsOverlaySettings
{
    /// <summary>Ordered color palette shown in the overlay and Settings color pickers.</summary>
    public static readonly string[] PaletteHexColors = ["#E05252", "#39B4E8", "#52C57A", "#C9A84C", "#7B5CC7"];

    /// <summary>Default active-lyric color (light blue).</summary>
    public const string DefaultLyricColorHex = "#39B4E8";

    /// <summary>Returns true when <paramref name="hexColor"/> is one of <see cref="PaletteHexColors"/>.</summary>
    public static bool IsValidPaletteColor(string? hexColor)
        => hexColor is not null &&
           PaletteHexColors.Contains(hexColor, StringComparer.OrdinalIgnoreCase);
}
