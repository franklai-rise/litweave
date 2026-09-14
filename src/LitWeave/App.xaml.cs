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
        var integrityError = LitWeaveRepository.GetIntegrityError(AppPaths.DatabasePath);
        if (!string.IsNullOrWhiteSpace(integrityError))
        {
            MessageBox.Show(
                "LitWeave detected a problem in its local whiteboard database and will not write to it. " +
                "Your Zotero library has not been changed. Copy %LOCALAPPDATA%\\LitWeave to a safe folder and recover from a backup or .litweave source file.\n\n" +
                $"SQLite check: {integrityError}",
                "LitWeave data protection", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
            return;
        }
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
