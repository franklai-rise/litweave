namespace LitWeave.Services;

public static class AppPaths
{
    public static string RootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LitWeave");

    public static string DatabasePath => Path.Combine(RootDirectory, "litweave.db");
    public static string ImagesDirectory => Path.Combine(RootDirectory, "images");
    public static string BackupsDirectory => Path.Combine(RootDirectory, "backups");
    public static string WebDataDirectory => Path.Combine(RootDirectory, "WebView2");
    public static string WebAssetsDirectory => Path.Combine(RootDirectory, "WebAssets");

    public static string DefaultWorkspaceDirectory
    {
        get
        {
            // During development, keep user-authored files beside the project.
            // A packaged installation falls back to LocalAppData automatically.
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 5 && directory is not null; i++, directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "LitWeave.sln")) ||
                    Directory.Exists(Path.Combine(directory.FullName, ".git")))
                    return Path.Combine(directory.FullName, "local", "workspace");
            }
            return Path.Combine(RootDirectory, "workspace");
        }
    }
}
