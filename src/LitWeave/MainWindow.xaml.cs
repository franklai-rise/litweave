using System.Text.Json;
using System.Windows;
using System.ComponentModel;
using LitWeave.Models;
using LitWeave.Services;
using Microsoft.Web.WebView2.Core;

namespace LitWeave;

public partial class MainWindow : Window
{
    private static readonly string DiagnosticLogPath = Path.Combine(AppPaths.RootDirectory, "litweave.log");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly App _app;
    private readonly LitWeaveRepository _repository;
    private readonly ZoteroClient _zotero;
    private bool _ready;
    private bool _closeApproved;
    private bool _closePreparation;
    private TaskCompletionSource<bool>? _closeReady;

    public MainWindow()
    {
        InitializeComponent();
        _app = (App)Application.Current;
        _repository = _app.Repository;
        _zotero = _app.ZoteroClient;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += (_, _) => LitWeaveView.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            LogDiagnostic("Starting WebView2 initialization.");
            var userDataFolder = AppPaths.WebDataDirectory;
            Directory.CreateDirectory(userDataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await LitWeaveView.EnsureCoreWebView2Async(environment);
            LitWeaveView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            LitWeaveView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            LitWeaveView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            LitWeaveView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            var webRoot = EmbeddedWebAssets.EnsureExtracted();
            Directory.CreateDirectory(AppPaths.ImagesDirectory);
            // Use an assembly-versioned local origin so WebView2 cannot serve a
            // stale index.html or stylesheet after a new publish. The embedded
            // asset folder is already keyed by the same module version id.
            var webHost = $"app-{typeof(MainWindow).Assembly.ManifestModule.ModuleVersionId:N}.litweave";
            LitWeaveView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                webHost, webRoot, CoreWebView2HostResourceAccessKind.DenyCors);
            LitWeaveView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "images.litweave", AppPaths.ImagesDirectory, CoreWebView2HostResourceAccessKind.Allow);
            LitWeaveView.Source = new Uri($"https://{webHost}/index.html");
            _ready = true;
            LogDiagnostic($"WebView2 ready at {webHost}.");
        }
        catch (Exception ex)
        {
            LogDiagnostic($"Startup failed: {ex}");
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
            LogDiagnostic($"Zotero unavailable for {request?.Type ?? "unknown"}: {ex.Message}");
            Post(new BridgeResponse { Id = request?.Id ?? string.Empty, Ok = false,
                Error = new BridgeError { Code = "zotero_unavailable", Message = ex.Message } });
        }
        catch (Exception ex)
        {
            LogDiagnostic($"Bridge failed for {request?.Type ?? "unknown"}: {ex}");
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
                var recoveredSources = 0;
                try { recoveredSources = _repository.EnsureExistingBoardSources(); }
                catch (Exception ex) { LogDiagnostic($"Initial source export failed: {ex.Message}"); }
                BackupSummary? automaticBackup = null;
                try { automaticBackup = _repository.MaybeCreateAutomaticBackup(true); }
                catch (Exception ex) { LogDiagnostic($"Automatic backup failed: {ex.Message}"); }
                LogDiagnostic($"Zotero status: running={status.IsRunning}, api={status.ApiEnabled}, message={status.Message}");
                return new
                {
                    app = new { name = "LitWeave", subtitle = "Visual Literature Mapping for Zotero", version = "0.2.1-beta.1" },
                    storagePath = AppPaths.DatabasePath,
                    workspacePath = _repository.GetWorkspaceDirectory(),
                    backupPath = AppPaths.BackupsDirectory,
                    automaticBackup,
                    recoveredSources,
                    snapshot = _repository.LoadSnapshot(),
                    lastRefresh = _repository.LoadLastRefresh(),
                    status,
                    capabilities = new { jsonExport = true, svgExport = true, pngExport = true, pdfExport = true, ai = false, zoteroWrite = false }
                };
            }
            case "CheckZotero":
                return await _zotero.GetStatusAsync();
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
            case "ListBoards":
            {
                var filter = ReadString(payload, "filter") ?? "all";
                return new { boards = _repository.ListBoards(filter) };
            }
            case "CreateBoard":
            {
                var name = ReadString(payload, "name");
                return new { document = _repository.CreateBoard(name) };
            }
            case "LoadBoard":
            {
                var boardId = ReadString(payload, "boardId") ?? throw new InvalidOperationException("Missing board identifier.");
                return new { document = _repository.LoadBoard(boardId) };
            }
            case "SaveBoard":
            {
                var document = JsonSerializer.Deserialize<CanvasDocument>(payload.GetRawText(), JsonOptions)
                    ?? throw new InvalidOperationException("Invalid board document.");
                _repository.SaveCanvas(document);
                string? sourcePath = null;
                string? sourceError = null;
                try { sourcePath = _repository.SaveBoardSource(document); }
                catch (Exception ex) { sourceError = ex.Message; }
                return new { savedAt = document.UpdatedAt, sourcePath, sourceError };
            }
            case "DeleteBoard":
            {
                var boardId = ReadString(payload, "boardId") ?? throw new InvalidOperationException("Missing board identifier.");
                _repository.DeleteBoard(boardId);
                return new { deleted = true, trashed = true };
            }
            case "RestoreBoard":
            {
                var boardId = ReadString(payload, "boardId") ?? throw new InvalidOperationException("Missing board identifier.");
                _repository.RestoreBoard(boardId);
                return new { restored = true };
            }
            case "ArchiveBoard":
            {
                var boardId = ReadString(payload, "boardId") ?? throw new InvalidOperationException("Missing board identifier.");
                _repository.ArchiveBoard(boardId, ReadBoolean(payload, "archived"));
                return new { archived = ReadBoolean(payload, "archived") };
            }
            case "SetBoardProtected":
            {
                var boardId = ReadString(payload, "boardId") ?? throw new InvalidOperationException("Missing board identifier.");
                var value = ReadBoolean(payload, "protected");
                _repository.SetBoardProtected(boardId, value);
                return new { isProtected = value };
            }
            case "SetBoardPinned":
            {
                var boardId = ReadString(payload, "boardId") ?? throw new InvalidOperationException("Missing board identifier.");
                var value = ReadBoolean(payload, "pinned");
                _repository.SetBoardPinned(boardId, value);
                return new { isPinned = value };
            }
            case "ListBackups":
                return new { backups = _repository.ListBackups() };
            case "CreateBackup":
                return new { backup = _repository.CreateBackup(ReadString(payload, "kind") ?? "manual") };
            case "VerifyBackup":
            {
                var path = ReadString(payload, "path") ?? throw new InvalidOperationException("Missing backup path.");
                return new { backup = _repository.VerifyBackup(path) };
            }
            case "RestoreBackup":
            {
                var path = ReadString(payload, "path") ?? throw new InvalidOperationException("Missing backup path.");
                var destination = ReadString(payload, "destinationRoot") ?? throw new InvalidOperationException("Missing recovery destination.");
                return new { path = _repository.RestoreBackupToNewRoot(path, destination) };
            }
            case "GetAppInfo":
                return new { version = "0.2.1-beta.1", build = typeof(MainWindow).Assembly.ManifestModule.ModuleVersionId.ToString("N"), dataPath = AppPaths.RootDirectory, databasePath = AppPaths.DatabasePath, backupPath = AppPaths.BackupsDirectory, workspacePath = _repository.GetWorkspaceDirectory() };
            case "ImportBoardPackage":
                return ImportBoardPackage();
            case "GetBoardSession":
                return new { value = _repository.GetSetting("board-session") };
            case "SaveBoardSession":
            {
                var value = ReadString(payload, "value") ?? "{}";
                _repository.SetSetting("board-session", value);
                return new { saved = true };
            }
            case "PrepareToClose":
                _closeReady?.TrySetResult(ReadBoolean(payload, "saved"));
                return new { acknowledged = true };
            case "GetWorkspaceDirectory":
                return new { path = _repository.GetWorkspaceDirectory() };
            case "SetWorkspaceDirectory":
            {
                var path = ReadString(payload, "path") ?? throw new InvalidOperationException("Missing workspace directory.");
                _repository.SetWorkspaceDirectory(path);
                return new { path = _repository.GetWorkspaceDirectory() };
            }
            case "SaveImage":
            {
                var dataUrl = ReadString(payload, "dataUrl") ?? throw new InvalidOperationException("Missing image data.");
                var imageId = _repository.SaveImage(dataUrl);
                return new { imageId, imageUrl = $"https://images.litweave/{imageId}" };
            }
            case "GetImageData":
            {
                var imageId = ReadString(payload, "imageId") ?? throw new InvalidOperationException("Missing image identifier.");
                return new { dataUrl = _repository.GetImageDataUrl(imageId) };
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
            case "ExportCanvasPdf":
                return await ExportCanvasPdfAsync(payload);
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
            "litweave" => "LitWeave source (*.litweave)|*.litweave|All files (*.*)|*.*",
            _ => "LitWeave JSON (*.json)|*.json|All files (*.*)|*.*"
        };
        var extension = format switch { "svg" => ".svg", "png" => ".png", "litweave" => ".litweave", _ => ".json" };
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export LitWeave canvas",
            Filter = filter,
            InitialDirectory = Path.Combine(_repository.GetWorkspaceDirectory(), "exports"),
            FileName = defaultName + extension,
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return new { saved = false };
        Directory.CreateDirectory(Path.GetDirectoryName(dialog.FileName)!);
        if (format == "litweave" && payload.TryGetProperty("document", out var packageDocument))
        {
            var document = JsonSerializer.Deserialize<CanvasDocument>(packageDocument.GetRawText(), JsonOptions)
                ?? throw new InvalidOperationException("Invalid board source document.");
            _repository.WriteBoardPackage(document, dialog.FileName);
            return new { saved = true, path = dialog.FileName };
        }
        if (format == "json" && payload.TryGetProperty("document", out var documentElement))
        {
            var document = JsonSerializer.Deserialize<CanvasDocument>(documentElement.GetRawText(), JsonOptions)
                ?? throw new InvalidOperationException("Invalid board export document.");
            text = _repository.BuildExportJson(document);
        }
        if (format == "png")
        {
            var marker = ";base64,";
            var offset = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (offset < 0) throw new InvalidOperationException("Invalid PNG export data.");
            File.WriteAllBytes(dialog.FileName, Convert.FromBase64String(text[(offset + marker.Length)..]));
        }
        else File.WriteAllText(dialog.FileName, text, new System.Text.UTF8Encoding(false));
        return new { saved = true, path = dialog.FileName };
    }

    private object ImportBoardPackage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import LitWeave source board",
            Filter = "LitWeave source (*.litweave)|*.litweave|All files (*.*)|*.*",
            InitialDirectory = Path.Combine(_repository.GetWorkspaceDirectory(), "boards"),
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return new { imported = false };
        var document = _repository.ImportBoardPackage(dialog.FileName);
        return new { imported = true, document, path = dialog.FileName };
    }

    private async Task<object> ExportCanvasPdfAsync(JsonElement payload)
    {
        var svg = ReadString(payload, "svg") ?? throw new InvalidOperationException("Missing PDF drawing.");
        var defaultName = ReadString(payload, "defaultName") ?? "litweave-canvas";
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export LitWeave canvas as PDF",
            Filter = "PDF document (*.pdf)|*.pdf|All files (*.*)|*.*",
            InitialDirectory = Path.Combine(_repository.GetWorkspaceDirectory(), "exports"),
            FileName = defaultName + ".pdf",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return new { saved = false };
        Directory.CreateDirectory(Path.GetDirectoryName(dialog.FileName)!);
        var encodedSvg = JsonSerializer.Serialize(svg, JsonOptions);
        const string overlayId = "litweave-print-overlay";
        var overlayName = JsonSerializer.Serialize(overlayId, JsonOptions);
        var styleName = JsonSerializer.Serialize(overlayId + "-style", JsonOptions);
        var css = $"@media print {{ body > * {{ display:none !important; }} #{overlayId} {{ display:block !important; position:fixed !important; inset:0 !important; width:100vw !important; height:100vh !important; background:white !important; }} #{overlayId} svg {{ width:100% !important; height:100% !important; }} }}";
        var script = "(() => {document.getElementById(" + overlayName + ")?.remove();"
            + "document.getElementById(" + styleName + ")?.remove();"
            + "const style=document.createElement('style');style.id=" + styleName + ";style.textContent=" + JsonSerializer.Serialize(css, JsonOptions) + ";document.head.appendChild(style);"
            + "const overlay=document.createElement('div');overlay.id=" + overlayName + ";overlay.innerHTML=" + encodedSvg + ";document.body.appendChild(overlay);})();";
        try
        {
            await LitWeaveView.CoreWebView2.ExecuteScriptAsync(script);
            var settings = LitWeaveView.CoreWebView2.Environment.CreatePrintSettings();
            settings.Orientation = CoreWebView2PrintOrientation.Landscape;
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintHeaderAndFooter = false;
            if (!await LitWeaveView.CoreWebView2.PrintToPdfAsync(dialog.FileName, settings))
                throw new InvalidOperationException("LitWeave could not create the PDF file.");
        }
        finally
        {
            await LitWeaveView.CoreWebView2.ExecuteScriptAsync($"document.getElementById('{overlayId}')?.remove();document.getElementById('{overlayId}-style')?.remove();");
        }
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

    private static bool ReadBoolean(JsonElement payload, string property) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private async void OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (_closeApproved || !_ready || LitWeaveView.CoreWebView2 is null) return;
        eventArgs.Cancel = true;
        if (_closePreparation) return;
        _closePreparation = true;
        _closeReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await LitWeaveView.CoreWebView2.ExecuteScriptAsync("window.litweavePrepareToClose?.();");
            var completed = await Task.WhenAny(_closeReady.Task, Task.Delay(TimeSpan.FromSeconds(8)));
            if (completed == _closeReady.Task && _closeReady.Task.Result)
            {
                _closeApproved = true;
                Close();
                return;
            }
            MessageBox.Show(this,
                "LitWeave could not confirm that the active whiteboard was saved. The window remains open so you can retry saving or copy your local data first.",
                "LitWeave save confirmation", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception exception)
        {
            LogDiagnostic($"Close-save confirmation failed: {exception}");
            MessageBox.Show(this,
                "LitWeave could not confirm saving the active whiteboard. The window remains open to protect your work.",
                "LitWeave save confirmation", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _closePreparation = false;
            _closeReady = null;
        }
    }

    private void Post(BridgeResponse response)
    {
        if (!_ready || LitWeaveView.CoreWebView2 is null) return;
        LitWeaveView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(response, JsonOptions));
    }

    private static void LogDiagnostic(string message)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.RootDirectory);
            File.AppendAllText(DiagnosticLogPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never interfere with the UI or Zotero reads.
        }
    }
}
