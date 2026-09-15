using System.Windows;
using System.Security.Cryptography;
using System.Text;
using LitWeave.Services;

namespace LitWeave;

public partial class App : Application
{
    internal LitWeaveRepository Repository { get; private set; } = null!;
    internal ZoteroClient ZoteroClient { get; private set; } = null!;
    internal ZoteroLauncher ZoteroLauncher { get; } = new();
    private Mutex? _dataMutex;
    private bool _ownsDataMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        var dataRootIndex = Array.FindIndex(e.Args, argument => string.Equals(argument, "--data-root", StringComparison.OrdinalIgnoreCase));
        if (dataRootIndex >= 0)
        {
            if (dataRootIndex == e.Args.Length - 1)
            {
                MessageBox.Show("The --data-root option needs a folder path.", "LitWeave startup", MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown();
                return;
            }

            try
            {
                AppPaths.ConfigureRootDirectory(e.Args[dataRootIndex + 1]);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                MessageBox.Show($"The requested LitWeave data folder is invalid.\n\n{exception.Message}", "LitWeave startup", MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown();
                return;
            }
        }

        var rootToken = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(AppPaths.RootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant())))[..24];
        _dataMutex = new Mutex(true, $"Local\\LitWeave-{rootToken}", out _ownsDataMutex);
        if (!_ownsDataMutex)
        {
            MessageBox.Show("这个 LitWeave 数据目录已经被另一个实例打开。请先关闭已有窗口，或使用不同的 --data-root。", "LitWeave 已在运行", MessageBoxButton.OK, MessageBoxImage.Information);
            _dataMutex.Dispose();
            _dataMutex = null;
            Shutdown();
            return;
        }

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
        if (_ownsDataMutex)
        {
            try { _dataMutex?.ReleaseMutex(); } catch (ApplicationException) { }
            _dataMutex?.Dispose();
        }
        base.OnExit(e);
    }
}
