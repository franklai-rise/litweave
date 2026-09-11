using System.Windows;
using LitWeave.Services;

namespace LitWeave;

public partial class App : Application
{
    internal LitWeaveRepository Repository { get; private set; } = null!;
    internal ZoteroClient ZoteroClient { get; private set; } = null!;
    internal ZoteroLauncher ZoteroLauncher { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        Repository = new LitWeaveRepository();
        ZoteroClient = new ZoteroClient();
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ZoteroClient?.Dispose();
        Repository?.Dispose();
        base.OnExit(e);
    }
}
