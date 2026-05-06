using System.Linq;

namespace Lyricify.Lyrics.App.Views;

/// <summary>An app entry shown in the app picker list.</summary>
public sealed record InstalledAppInfo(string PackageName, string Label);

/// <summary>
/// Modal page that lets the user pick an installed app to add to the
/// compatibility-mode whitelist.
/// </summary>
public partial class AppPickerPage : ContentPage
{
    /// <summary>
    /// Raised when the user selects an app.
    /// The argument is the selected package name.
    /// </summary>
    public event EventHandler<string>? AppPicked;

    private List<InstalledAppInfo> _allApps = new();

    public AppPickerPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAppsAsync();
    }

    private async Task LoadAppsAsync()
    {
        List<InstalledAppInfo> apps = new();

#if ANDROID
        apps = await Task.Run(() =>
            Lyricify.Lyrics.App.Platforms.Android.MediaControllerNowPlayingService
                .GetInstalledApps()
                .Select(a => new InstalledAppInfo(a.PackageName, a.Label))
                .ToList());
#endif

        _allApps = apps;
        ApplyFilter(AppSearchBar.Text);
        LoadingIndicator.IsRunning = false;
        LoadingIndicator.IsVisible = false;
        AppsCollectionView.IsVisible = true;
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter(e.NewTextValue);
    }

    private void ApplyFilter(string? query)
    {
        var q = query?.Trim() ?? string.Empty;
        var filtered = string.IsNullOrEmpty(q)
            ? _allApps
            : _allApps.Where(a =>
                a.Label.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.PackageName.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

        AppsCollectionView.ItemsSource = filtered;
    }

    private async void OnAppSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not InstalledAppInfo appInfo) return;

        // Raise before popping so the caller can act synchronously if needed.
        AppPicked?.Invoke(this, appInfo.PackageName);

        await Navigation.PopModalAsync();

        // Clear selection after the page has been dismissed so any re-open
        // of the same picker instance starts with a clean state.
        ((CollectionView)sender).SelectedItem = null;
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}
