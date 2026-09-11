using System.Diagnostics;

namespace LitWeave.Services;

public interface IZoteroLauncher
{
    void ShowItem(string itemKey);
    void OpenPdf(string attachmentKey);
}

public sealed class ZoteroLauncher : IZoteroLauncher
{
    public void ShowItem(string itemKey) => OpenUri($"zotero://select/library/items/{Uri.EscapeDataString(itemKey)}");

    public void OpenPdf(string attachmentKey) => OpenUri($"zotero://open-pdf/library/items/{Uri.EscapeDataString(attachmentKey)}");

    private static void OpenUri(string uri)
    {
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }
}
