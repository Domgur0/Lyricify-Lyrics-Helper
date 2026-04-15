using Lyricify.Lyrics.App.Services;
using Lyricify.Lyrics.App.Views;

namespace Lyricify.Lyrics.App;

public partial class App : Application
{
    private readonly SpotifyOAuthService _oauthService;

    public App(SpotifyOAuthService oauthService)
    {
        InitializeComponent();
        _oauthService = oauthService;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Navigate to login if we have no stored token, otherwise go straight to lyrics.
        var page = _oauthService.HasValidToken
            ? (Page)new AppShell()
            : new NavigationPage(new LoginPage());

        return new Window(page);
    }
}
