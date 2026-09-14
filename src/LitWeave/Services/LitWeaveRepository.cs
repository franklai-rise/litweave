using System.Text;
using System.Text.Json;
using System.IO.Compression;
using LitWeave.Models;
using Microsoft.Data.Sqlite;

namespace LitWeave.Services;

public sealed class LitWeaveRepository : IDisposable
{
    private const int MaxImageBytes = 15 * 1024 * 1024;
    private const long MaxBoardPackageBytes = 60L * 1024 * 1024;
    private const int MaxBoardPackageEntries = 128;
    private readonly SqliteConnection _connection;
    private readonly object _gate = new();
    private readonly string _databasePath;
    private readonly string _imagesDirectory;
    private readonly string _backupsDirectory;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    public LitWeaveRepository(string? databasePath = null)
    {
        _databasePath = databasePath ?? AppPaths.DatabasePath;
        var contentRoot = databasePath is null ? AppPaths.RootDirectory : Path.GetDirectoryName(_databasePath)!;
        _imagesDirectory = Path.Combine(contentRoot, "images");
        _backupsDirectory = Path.Combine(contentRoot, "backups");
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        _connection = new SqliteConnection($"Data Source={_databasePath};Cache=Shared;Pooling=False");
        _connection.Open();
        Initialize();
        MigrateLegacyBoards();
    }

    public static string? GetIntegrityError(string databasePath)
    {
        if (!File.Exists(databasePath)) return null;
        try
        {
            var builder = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
            using var connection = new SqliteConnection(builder.ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            using var reader = command.ExecuteReader();
            var messages = new List<string>();
            while (reader.Read() && messages.Count < 3) messages.Add(reader.GetString(0));
            return messages.Count == 1 && string.Equals(messages[0], "ok", StringComparison.OrdinalIgnoreCase)
                ? null
                : messages.Count == 0 ? "SQLite did not return an integrity result." : string.Join(" ", messages);
        }
        catch (Exception exception)
        {
            return exception.Message;
        }
    }

    private void Initialize()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;
            CREATE TABLE IF NOT EXISTS zotero_snapshots (
                id INTEGER PRIMARY KEY CHECK (id = 1), content_hash TEXT NOT NULL, refreshed_at TEXT NOT NULL, json TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS canvases (
                id TEXT PRIMARY KEY, root_collection_key TEXT NOT NULL, json TEXT NOT NULL, updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS refresh_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT, refreshed_at TEXT NOT NULL, content_hash TEXT NOT NULL,
                new_count INTEGER NOT NULL, updated_count INTEGER NOT NULL, removed_count INTEGER NOT NULL, status TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
    }

    public ZoteroSnapshot? LoadSnapshot()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT json FROM zotero_snapshots WHERE id = 1";
            var value = command.ExecuteScalar() as string;
            return string.IsNullOrWhiteSpace(value) ? null : JsonSerializer.Deserialize<ZoteroSnapshot>(value, JsonOptions);
        }
    }

    public DateTimeOffset? LoadLastRefresh()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT refreshed_at FROM zotero_snapshots WHERE id = 1";
            var value = command.ExecuteScalar() as string;
            return DateTimeOffset.TryParse(value, out var date) ? date : null;
        }
    }

    public void SaveSnapshot(ZoteroSnapshot snapshot, RefreshDiff diff, string status = "success")
    {
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        lock (_gate)
        {
            using var transaction = _connection.BeginTransaction();
            using (var command = _connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO zotero_snapshots (id, content_hash, refreshed_at, json) VALUES (1, $hash, $at, $json) ON CONFLICT(id) DO UPDATE SET content_hash=$hash, refreshed_at=$at, json=$json;";
                command.Parameters.AddWithValue("$hash", snapshot.ContentHash);
                command.Parameters.AddWithValue("$at", snapshot.RefreshedAt.ToString("O"));
                command.Parameters.AddWithValue("$json", json);
                command.ExecuteNonQuery();
            }
            using (var history = _connection.CreateCommand())
            {
                history.Transaction = transaction;
                history.CommandText = "INSERT INTO refresh_history (refreshed_at, content_hash, new_count, updated_count, removed_count, status) VALUES ($at, $hash, $new, $updated, $removed, $status);";
                history.Parameters.AddWithValue("$at", snapshot.RefreshedAt.ToString("O"));
                history.Parameters.AddWithValue("$hash", snapshot.ContentHash);
                history.Parameters.AddWithValue("$new", diff.NewItemKeys.Count);
                history.Parameters.AddWithValue("$updated", diff.UpdatedItemKeys.Count);
                history.Parameters.AddWithValue("$removed", diff.RemovedItemKeys.Count);
                history.Parameters.AddWithValue("$status", status);
                history.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    public CanvasDocument CreateBoard(string? name = null)
    {
        var document = new CanvasDocument
        {
            Id = $"board:{Guid.NewGuid():N}",
            Name = string.IsNullOrWhiteSpace(name) ? "Untitled board" : name.Trim(),
            RootCollectionKey = string.Empty,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        SaveCanvas(document);
        return document;
    }

    public IReadOnlyList<BoardSummary> ListBoards()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT json, updated_at FROM canvases ORDER BY updated_at DESC";
            using var reader = command.ExecuteReader();
            var boards = new List<BoardSummary>();
            while (reader.Read())
            {
                var document = JsonSerializer.Deserialize<CanvasDocument>(reader.GetString(0), JsonOptions);
                if (document is null || !document.Id.StartsWith("board:", StringComparison.Ordinal)) continue;
                boards.Add(new BoardSummary
                {
                    Id = document.Id,
                    Name = string.IsNullOrWhiteSpace(document.Name) ? "Untitled board" : document.Name,
                    IsLegacy = document.IsLegacy,
                    UpdatedAt = DateTimeOffset.TryParse(reader.GetString(1), out var updated) ? updated : document.UpdatedAt
                });
            }
            return boards;
        }
    }

    public CanvasDocument? LoadBoard(string boardId) => LoadCanvas(boardId);

    // v0.1 callers supplied a collection key. Keep the fallback for existing
    // database records and test fixtures while all new boards use board:* IDs.
    public CanvasDocument? LoadCanvas(string boardIdOrCollectionKey)
    {
        lock (_gate)
        {
            return LoadCanvasById(boardIdOrCollectionKey) ?? LoadCanvasById(CanvasId(boardIdOrCollectionKey));
        }
    }

    private CanvasDocument? LoadCanvasById(string id)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT json FROM canvases WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        var value = command.ExecuteScalar() as string;
        return string.IsNullOrWhiteSpace(value) ? null : JsonSerializer.Deserialize<CanvasDocument>(value, JsonOptions);
    }

    public void SaveCanvas(CanvasDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.Id))
            document.Id = string.IsNullOrWhiteSpace(document.RootCollectionKey) ? $"board:{Guid.NewGuid():N}" : CanvasId(document.RootCollectionKey);
        document.Name = string.IsNullOrWhiteSpace(document.Name) ? "Untitled board" : document.Name.Trim();
        document.UpdatedAt = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(document, JsonOptions);
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "INSERT INTO canvases (id, root_collection_key, json, updated_at) VALUES ($id, $root, $json, $at) ON CONFLICT(id) DO UPDATE SET root_collection_key=$root, json=$json, updated_at=$at;";
            command.Parameters.AddWithValue("$id", document.Id);
            command.Parameters.AddWithValue("$root", document.RootCollectionKey ?? string.Empty);
            command.Parameters.AddWithValue("$json", json);
            command.Parameters.AddWithValue("$at", document.UpdatedAt.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    public void DeleteBoard(string boardId)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM canvases WHERE id = $id AND id LIKE 'board:%'";
            command.Parameters.AddWithValue("$id", boardId);
            command.ExecuteNonQuery();
        }
    }

    public string SaveImage(string dataUrl)
    {
        var (mimeType, bytes) = ParseImageDataUrl(dataUrl);
        var extension = mimeType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            _ => throw new InvalidOperationException("Only PNG, JPEG, and WebP images are supported.")
        };
        if (!HasExpectedSignature(mimeType, bytes)) throw new InvalidOperationException("The image contents do not match its declared format.");
        Directory.CreateDirectory(_imagesDirectory);
        var imageId = $"{Guid.NewGuid():N}{extension}";
        using var stream = new FileStream(Path.Combine(_imagesDirectory, imageId), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        return imageId;
    }

    public string GetImageDataUrl(string imageId)
    {
        var safeName = Path.GetFileName(imageId);
        if (!string.Equals(safeName, imageId, StringComparison.Ordinal)) throw new InvalidOperationException("Invalid image identifier.");
        var path = Path.Combine(_imagesDirectory, safeName);
        if (!File.Exists(path)) throw new FileNotFoundException("The local LitWeave image is unavailable.", safeName);
        var mime = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => throw new InvalidOperationException("Unsupported saved image type.")
        };
        return $"data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}";
    }

    public string BuildExportJson(CanvasDocument document)
    {
        var images = document.Nodes.Where(node => !string.IsNullOrWhiteSpace(node.ImageId))
            .Select(node => node.ImageId!).Distinct(StringComparer.Ordinal)
            .ToDictionary(id => id, GetImageDataUrl, StringComparer.Ordinal);
        return JsonSerializer.Serialize(new { format = "litweave-board", version = 2, board = document, images }, JsonOptions);
    }

    public string GetWorkspaceDirectory()
    {
        var configured = GetSetting("workspace-directory");
        return string.IsNullOrWhiteSpace(configured) ? AppPaths.DefaultWorkspaceDirectory : configured;
    }

    public void SetWorkspaceDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("A workspace directory is required.");
        var fullPath = Path.GetFullPath(directory);
        Directory.CreateDirectory(fullPath);
        SetSetting("workspace-directory", fullPath);
    }

    public string SaveBoardSource(CanvasDocument document)
    {
        var root = GetWorkspaceDirectory();
        var boards = Path.Combine(root, "boards");
        Directory.CreateDirectory(boards);
        // The board id is stable while the display name is intentionally editable.
        // Keep a stable recovery file rather than leaving stale copies after rename.
        var path = Path.Combine(boards, $"{document.Id.Replace(':', '-')}.litweave");
        WriteBoardPackage(document, path);
        return path;
    }

    public int EnsureExistingBoardSources()
    {
        // This is deliberately one-time. Existing databases remain authoritative;
        // the packages are recovery copies, not a database migration.
        if (string.Equals(GetSetting("v021-source-export"), "complete", StringComparison.Ordinal)) return 0;
        var exported = 0;
        foreach (var board in ListBoards())
        {
            var document = LoadBoard(board.Id);
            if (document is null) continue;
            SaveBoardSource(document);
            exported++;
        }
        SetSetting("v021-source-export", "complete");
        return exported;
    }

    public void WriteBoardPackage(CanvasDocument document, string destination)
    {
        var directory = Path.GetDirectoryName(destination) ?? throw new InvalidOperationException("Invalid source file path.");
        Directory.CreateDirectory(directory);
        var temporary = destination + ".tmp";
        if (File.Exists(temporary)) File.Delete(temporary);
        using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
        {
            var manifest = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using (var writer = new StreamWriter(manifest.Open(), new UTF8Encoding(false)))
                writer.Write(JsonSerializer.Serialize(new { format = "litweave", version = 2, exportedAt = DateTimeOffset.UtcNow, boardId = document.Id, metadataFile = "items.json" }, JsonOptions));
            var boardEntry = archive.CreateEntry("board.json", CompressionLevel.Optimal);
            using (var writer = new StreamWriter(boardEntry.Open(), new UTF8Encoding(false))) writer.Write(JsonSerializer.Serialize(document, JsonOptions));
            var items = GetBoardMetadata(document);
            var metadataEntry = archive.CreateEntry("items.json", CompressionLevel.Optimal);
            using (var writer = new StreamWriter(metadataEntry.Open(), new UTF8Encoding(false))) writer.Write(JsonSerializer.Serialize(items, JsonOptions));
            foreach (var imageId in document.Nodes.Where(node => !string.IsNullOrWhiteSpace(node.ImageId)).Select(node => node.ImageId!).Distinct(StringComparer.Ordinal))
            {
                var source = Path.Combine(_imagesDirectory, Path.GetFileName(imageId));
                if (!File.Exists(source)) continue;
                archive.CreateEntryFromFile(source, $"assets/{Path.GetFileName(imageId)}", CompressionLevel.Optimal);
            }
        }
        File.Move(temporary, destination, true);
    }

    public CanvasDocument ImportBoardPackage(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) throw new FileNotFoundException("The selected LitWeave source file is unavailable.", sourcePath);
        var package = new FileInfo(sourcePath);
        if (package.Length <= 0 || package.Length > MaxBoardPackageBytes) throw new InvalidOperationException("The LitWeave source file is empty or exceeds the 60 MB safety limit.");

        using var archive = ZipFile.OpenRead(sourcePath);
        if (archive.Entries.Count > MaxBoardPackageEntries || archive.Entries.Sum(entry => entry.Length) > MaxBoardPackageBytes)
            throw new InvalidOperationException("The LitWeave source file exceeds the safe entry or unpacked-size limit.");
        var manifest = ReadPackageJson<BoardPackageManifest>(archive, "manifest.json") ?? throw new InvalidOperationException("The selected file is not a LitWeave source package.");
        if (!string.Equals(manifest.Format, "litweave", StringComparison.OrdinalIgnoreCase) || manifest.Version is < 1 or > 2)
            throw new InvalidOperationException("This LitWeave source version is not supported.");
        var document = ReadPackageJson<CanvasDocument>(archive, "board.json") ?? throw new InvalidOperationException("The LitWeave source package has no valid board.");
        document.ItemMetadata = manifest.Version >= 2 ? ReadPackageJson<List<ZoteroItem>>(archive, "items.json") ?? [] : [];
        document.Id = $"board:{Guid.NewGuid():N}";
        document.Name = string.IsNullOrWhiteSpace(document.Name) ? "Imported board" : $"{document.Name.Trim()} (imported)";
        document.IsLegacy = false;
        document.CreatedAt = DateTimeOffset.UtcNow;
        document.UpdatedAt = DateTimeOffset.UtcNow;

        var imageMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in document.Nodes.Where(node => !string.IsNullOrWhiteSpace(node.ImageId)))
        {
            var originalId = node.ImageId!;
            if (!imageMap.TryGetValue(originalId, out var importedId))
            {
                var entryName = $"assets/{Path.GetFileName(originalId)}";
                var entry = archive.GetEntry(entryName) ?? throw new InvalidOperationException($"The source package is missing image '{Path.GetFileName(originalId)}'.");
                if (entry.Length <= 0 || entry.Length > MaxImageBytes) throw new InvalidOperationException("An image in the source package is invalid or too large.");
                using var input = entry.Open();
                using var memory = new MemoryStream();
                input.CopyTo(memory);
                importedId = SaveImageBytes(Path.GetExtension(originalId), memory.ToArray());
                imageMap.Add(originalId, importedId);
            }
            node.ImageId = importedId;
        }
        SaveCanvas(document);
        return document;
    }

    private List<ZoteroItem> GetBoardMetadata(CanvasDocument document)
    {
        var keys = document.Nodes.Where(node => !string.IsNullOrWhiteSpace(node.ItemKey)).Select(node => node.ItemKey!).ToHashSet(StringComparer.Ordinal);
        var available = document.ItemMetadata.Concat(LoadSnapshot()?.Items ?? []).Where(item => keys.Contains(item.Key));
        return available.GroupBy(item => item.Key, StringComparer.Ordinal).Select(group => group.Last()).ToList();
    }

    private static T? ReadPackageJson<T>(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        if (entry is null || entry.Length <= 0 || entry.Length > MaxBoardPackageBytes) return default;
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return JsonSerializer.Deserialize<T>(reader.ReadToEnd(), JsonOptions);
    }

    private string SaveImageBytes(string extension, byte[] bytes)
    {
        if (bytes.Length <= 0 || bytes.Length > MaxImageBytes) throw new InvalidOperationException("The image is empty or exceeds the 15 MB safety limit.");
        var mimeType = extension.ToLowerInvariant() switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", _ => throw new InvalidOperationException("Unsupported image type in LitWeave source package.") };
        if (!HasExpectedSignature(mimeType, bytes)) throw new InvalidOperationException("The image contents do not match its declared format.");
        Directory.CreateDirectory(_imagesDirectory);
        var imageId = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        File.WriteAllBytes(Path.Combine(_imagesDirectory, imageId), bytes);
        return imageId;
    }

    private sealed class BoardPackageManifest
    {
        public string Format { get; set; } = string.Empty;
        public int Version { get; set; }
    }

    public string? GetSetting(string key)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT value FROM settings WHERE key = $key";
            command.Parameters.AddWithValue("$key", key);
            return command.ExecuteScalar() as string;
        }
    }

    public void SetSetting(string key, string value)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "INSERT INTO settings (key, value) VALUES ($key, $value) ON CONFLICT(key) DO UPDATE SET value=$value;";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            command.ExecuteNonQuery();
        }
    }

    private void MigrateLegacyBoards()
    {
        lock (_gate)
        {
            if (string.Equals(GetSetting("v2-board-migration"), "complete", StringComparison.Ordinal)) return;
            var legacy = new List<CanvasDocument>();
            {
                using var command = _connection.CreateCommand();
                command.CommandText = "SELECT json FROM canvases";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var document = JsonSerializer.Deserialize<CanvasDocument>(reader.GetString(0), JsonOptions);
                    if (document is not null && !document.Id.StartsWith("board:", StringComparison.Ordinal)) legacy.Add(document);
                }
            }
            if (legacy.Count > 0 && File.Exists(_databasePath))
            {
                Directory.CreateDirectory(_backupsDirectory);
                var backup = Path.Combine(_backupsDirectory, $"litweave-before-v02-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.db");
                // A SQLite-native backup includes committed WAL data; copying a
                // live .db file alone can silently omit recent board changes.
                using var backupCommand = _connection.CreateCommand();
                backupCommand.CommandText = "VACUUM INTO $backup";
                backupCommand.Parameters.AddWithValue("$backup", backup);
                backupCommand.ExecuteNonQuery();
            }
            foreach (var source in legacy)
            {
                var collectionPart = string.IsNullOrWhiteSpace(source.RootCollectionKey) ? "library" : source.RootCollectionKey;
                source.Id = $"board:legacy:{collectionPart}";
                source.Name = $"Legacy · {collectionPart}";
                source.IsLegacy = true;
                SaveCanvas(source);
            }
            SetSetting("v2-board-migration", "complete");
        }
    }

    private static string CanvasId(string rootCollectionKey) => string.IsNullOrWhiteSpace(rootCollectionKey) ? "personal:library" : $"personal:{rootCollectionKey}";

    private static (string MimeType, byte[] Bytes) ParseImageDataUrl(string dataUrl)
    {
        const string marker = ";base64,";
        if (string.IsNullOrWhiteSpace(dataUrl) || !dataUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The image must be supplied as a data URL.");
        var markerIndex = dataUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 5) throw new InvalidOperationException("Invalid image data URL.");
        var mime = dataUrl[5..markerIndex].ToLowerInvariant();
        byte[] bytes;
        try { bytes = Convert.FromBase64String(dataUrl[(markerIndex + marker.Length)..]); }
        catch (FormatException) { throw new InvalidOperationException("Invalid base64 image data."); }
        if (bytes.Length == 0 || bytes.Length > MaxImageBytes) throw new InvalidOperationException("Images must be between 1 byte and 15 MB.");
        return (mime, bytes);
    }

    private static bool HasExpectedSignature(string mime, byte[] bytes) => mime switch
    {
        "image/png" => bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
        "image/jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
        "image/webp" => bytes.Length >= 12 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP",
        _ => false
    };

    public void Dispose() => _connection.Dispose();
}
