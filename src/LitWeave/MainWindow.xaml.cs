using System.Text.Json;
using System.Windows;
using LitWeave.Models;
using LitWeave.Services;
using Microsoft.Web.WebView2.Core;

namespace LitWeave;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly App _app;
    private readonly LitWeaveRepository _repository;
    private readonly ZoteroClient _zotero;
    private bool _ready;

    public MainWindow()
    {
        InitializeComponent();
        _app = (App)Application.Current;
        _repository = _app.Repository;
        _zotero = _app.ZoteroClient;
        Loaded += OnLoaded;
        Closed += (_, _) => LitWeaveView.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var userDataFolder = AppPaths.WebDataDirectory;
            Directory.CreateDirectory(userDataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await LitWeaveView.EnsureCoreWebView2Async(environment);
            LitWeaveView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            LitWeaveView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            LitWeaveView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            LitWeaveView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            var webRoot = EmbeddedWebAssets.EnsureExtracted();
            LitWeaveView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "app.litweave", webRoot, CoreWebView2HostResourceAccessKind.DenyCors);
            LitWeaveView.Source = new Uri("https://app.litweave/index.html");
            _ready = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "LitWeave could not start", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        BridgeRequest? request = null;
        try
        {
            request = JsonSerializer.Deserialize<BridgeRequest>(e.WebMessageAsJson, JsonOptions);
            if (request is null) throw new InvalidOperationException("Empty bridge request.");
            var payload = await DispatchAsync(request.Type, request.Payload);
            Post(new BridgeResponse { Id = request.Id, Ok = true, Payload = payload });
        }
        catch (ZoteroUnavailableException ex)
        {
            Post(new BridgeResponse { Id = request?.Id ?? string.Empty, Ok = false,
                Error = new BridgeError { Code = "zotero_unavailable", Message = ex.Message } });
        }
        catch (Exception ex)
        {
            Post(new BridgeResponse { Id = request?.Id ?? string.Empty, Ok = false,
                Error = new BridgeError { Code = "native_error", Message = ex.Message } });
        }
    }

    private async Task<object?> DispatchAsync(string type, JsonElement payload)
    {
        switch (type)
        {
            case "GetAppState":
            {
                var status = await _zotero.GetStatusAsync();
                return new
                {
                    app = new { name = "LitWeave", subtitle = "Visual Literature Mapping for Zotero", version = "0.1.0" },
                    storagePath = AppPaths.DatabasePath,
                    snapshot = _repository.LoadSnapshot(),
                    lastRefresh = _repository.LoadLastRefresh(),
                    status,
                    capabilities = new { jsonExport = true, svgExport = true, pngExport = false, ai = false, zoteroWrite = false }
                };
            }
            case "RefreshZotero":
            {
                var result = await _zotero.RefreshAsync(_repository.LoadSnapshot());
                _repository.SaveSnapshot(result.Snapshot, result.Diff);
                return result;
            }
            case "LoadCanvas":
            {
                var root = ReadString(payload, "rootCollectionKey") ?? string.Empty;
                return new { document = _repository.LoadCanvas(root) };
            }
            case "SaveCanvas":
            {
                var document = JsonSerializer.Deserialize<CanvasDocument>(payload.GetRawText(), JsonOptions)
                    ?? throw new InvalidOperationException("Invalid canvas document.");
                _repository.SaveCanvas(document);
                return new { savedAt = document.UpdatedAt };
            }
            case "OpenZoteroItem":
            {
                var key = ReadString(payload, "itemKey") ?? throw new InvalidOperationException("Missing item key.");
                _app.ZoteroLauncher.ShowItem(key);
                return new { opened = true };
            }
            case "OpenZoteroPdf":
            {
                var key = ReadString(payload, "attachmentKey") ?? throw new InvalidOperationException("Missing attachment key.");
                _app.ZoteroLauncher.OpenPdf(key);
                return new { opened = true };
            }
            case "SaveCanvasExport":
                return SaveCanvasExport(payload);
            case "CaptureCanvasPng":
                return await CaptureCanvasPngAsync();
            case "SetLanguage":
            {
                var language = ReadString(payload, "language") ?? "zh-CN";
                _repository.SetSetting("language", language);
                return new { language };
            }
            default:
                throw new InvalidOperationException($"Unknown bridge message '{type}'.");
        }
    }

    private object SaveCanvasExport(JsonElement payload)
    {
        var format = (ReadString(payload, "format") ?? "json").ToLowerInvariant();
        var defaultName = ReadString(payload, "defaultName") ?? "litweave-canvas";
        var text = ReadString(payload, "content") ?? string.Empty;
        var filter = format switch
        {
            "svg" => "SVG image (*.svg)|*.svg|All files (*.*)|*.*",
            "png" => "PNG image (*.png)|*.png|All files (*.*)|*.*",
            _ => "LitWeave JSON (*.json)|*.json|All files (*.*)|*.*"
        };
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export LitWeave canvas",
            Filter = filter,
            FileName = defaultName + (format is "svg" ? ".svg" : format is "png" ? ".png" : ".json"),
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return new { saved = false };
        if (format == "png") throw new InvalidOperationException("PNG export is reserved for a later build; use SVG export for now.");
        File.WriteAllText(dialog.FileName, text, new System.Text.UTF8Encoding(false));
        return new { saved = true, path = dialog.FileName };
    }

    private async Task<object> CaptureCanvasPngAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export LitWeave canvas as PNG",
            Filter = "PNG image (*.png)|*.png|All files (*.*)|*.*",
            FileName = "litweave-canvas.png",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return new { saved = false };
        await using var stream = File.Create(dialog.FileName);
        await LitWeaveView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
        return new { saved = true, path = dialog.FileName };
    }

    private static string? ReadString(JsonElement payload, string property)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(property, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private void Post(BridgeResponse response)
    {
        if (!_ready || LitWeaveView.CoreWebView2 is null) return;
        LitWeaveView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(response, JsonOptions));
    }
}
