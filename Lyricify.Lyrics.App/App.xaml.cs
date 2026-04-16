namespace Lyricify.Lyrics.App;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Always enter the main shell: login is performed from Settings.
        return new Window(new AppShell());
    }
}
