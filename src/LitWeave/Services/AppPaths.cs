namespace LitWeave.Services;

public static class AppPaths
{
    public static string RootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LitWeave");

    public static string DatabasePath => Path.Combine(RootDirectory, "litweave.db");
    public static string WebDataDirectory => Path.Combine(RootDirectory, "WebView2");
    public static string WebAssetsDirectory => Path.Combine(RootDirectory, "WebAssets");
}
