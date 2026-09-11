using System.Reflection;

namespace LitWeave.Services;

public static class EmbeddedWebAssets
{
    private const string ResourcePrefix = "LitWeave.Web/";

    public static string EnsureExtracted()
    {
        var target = Path.Combine(AppPaths.WebAssetsDirectory, typeof(EmbeddedWebAssets).Assembly.ManifestModule.ModuleVersionId.ToString("N"));
        Directory.CreateDirectory(target);
        foreach (var resourceName in typeof(EmbeddedWebAssets).Assembly.GetManifestResourceNames()
                     .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            var relative = resourceName[ResourcePrefix.Length..].Replace('/', Path.DirectorySeparatorChar);
            var filePath = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            if (File.Exists(filePath)) continue;
            using var input = typeof(EmbeddedWebAssets).Assembly.GetManifestResourceStream(resourceName);
            if (input is null) continue;
            using var output = File.Create(filePath);
            input.CopyTo(output);
        }
        return target;
    }
}
