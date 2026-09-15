using System.Text;
using System.Text.Json;
using System.IO.Compression;
using System.Security.Cryptography;
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
        EnsureBoardMetadata();
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
            CREATE TABLE IF NOT EXISTS board_metadata (
                board_id TEXT PRIMARY KEY REFERENCES canvases(id) ON DELETE CASCADE,
                is_pinned INTEGER NOT NULL DEFAULT 0,
                is_protected INTEGER NOT NULL DEFAULT 0,
                archived_at TEXT NULL,
                trashed_at TEXT NULL,
                created_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_board_metadata_state ON board_metadata(trashed_at, archived_at);
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
            MarkDataDirty(transaction);
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

    public IReadOnlyList<BoardSummary> ListBoards(string filter = "active")
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT c.json, c.updated_at, m.is_pinned, m.is_protected, m.archived_at, m.trashed_at
                FROM canvases c LEFT JOIN board_metadata m ON m.board_id = c.id
                WHERE c.id LIKE 'board:%'
                  AND (($filter = 'all') OR
                       ($filter = 'active' AND m.trashed_at IS NULL AND m.archived_at IS NULL) OR
                       ($filter = 'archived' AND m.trashed_at IS NULL AND m.archived_at IS NOT NULL) OR
                       ($filter = 'trash' AND m.trashed_at IS NOT NULL))
                ORDER BY m.is_pinned DESC, c.updated_at DESC
                """;
            command.Parameters.AddWithValue("$filter", string.IsNullOrWhiteSpace(filter) ? "active" : filter.ToLowerInvariant());
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
                    IsPinned = reader.IsDBNull(2) ? false : reader.GetInt32(2) != 0,
                    IsProtected = reader.IsDBNull(3) ? string.Equals(document.Name, "Operator Learning", StringComparison.OrdinalIgnoreCase) : reader.GetInt32(3) != 0,
                    ArchivedAt = ReadNullableDate(reader, 4),
                    TrashedAt = ReadNullableDate(reader, 5),
                    NodeCount = document.Nodes?.Count ?? 0,
                    EdgeCount = document.Edges?.Count ?? 0,
                    ThumbnailImageId = document.Nodes?.FirstOrDefault(node => !string.IsNullOrWhiteSpace(node.ImageId))?.ImageId,
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
            if (document.Id.StartsWith("board:", StringComparison.Ordinal) && IsBoardTrashed(document.Id))
                throw new InvalidOperationException("此白板已在回收站中，恢复后才能保存。");
            using var transaction = _connection.BeginTransaction();
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO canvases (id, root_collection_key, json, updated_at) VALUES ($id, $root, $json, $at) ON CONFLICT(id) DO UPDATE SET root_collection_key=$root, json=$json, updated_at=$at;";
            command.Parameters.AddWithValue("$id", document.Id);
            command.Parameters.AddWithValue("$root", document.RootCollectionKey ?? string.Empty);
            command.Parameters.AddWithValue("$json", json);
            command.Parameters.AddWithValue("$at", document.UpdatedAt.ToString("O"));
            command.ExecuteNonQuery();
            using var metadata = _connection.CreateCommand();
            metadata.Transaction = transaction;
            metadata.CommandText = """
                INSERT INTO board_metadata (board_id, is_protected, created_at)
                VALUES ($id, $protected, $created)
                ON CONFLICT(board_id) DO UPDATE SET created_at = board_metadata.created_at;
                INSERT INTO settings (key, value) VALUES ('data-dirty', '1')
                ON CONFLICT(key) DO UPDATE SET value = '1';
                """;
            metadata.Parameters.AddWithValue("$id", document.Id);
            metadata.Parameters.AddWithValue("$protected", string.Equals(document.Name, "Operator Learning", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
            metadata.Parameters.AddWithValue("$created", document.CreatedAt.ToString("O"));
            metadata.ExecuteNonQuery();
            transaction.Commit();
        }
    }

    public void DeleteBoard(string boardId)
    {
        lock (_gate)
        {
            if (!boardId.StartsWith("board:", StringComparison.Ordinal)) return;
            if (IsBoardProtected(boardId)) throw new InvalidOperationException("这个白板已启用防删除保护，请先在白板管理中解除保护。");
            using var transaction = _connection.BeginTransaction();
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE board_metadata SET trashed_at = $at, archived_at = NULL WHERE board_id = $id";
            command.Parameters.AddWithValue("$id", boardId);
            command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
            MarkDataDirty(transaction);
            transaction.Commit();
        }
    }

    public void RestoreBoard(string boardId)
    {
        lock (_gate)
        {
            EnsureBoardExists(boardId);
            using var transaction = _connection.BeginTransaction();
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE board_metadata SET trashed_at = NULL, archived_at = NULL WHERE board_id = $id";
            command.Parameters.AddWithValue("$id", boardId);
            command.ExecuteNonQuery();
            MarkDataDirty(transaction);
            transaction.Commit();
        }
    }

    public void ArchiveBoard(string boardId, bool archived)
    {
        lock (_gate)
        {
            EnsureBoardExists(boardId);
            if (IsBoardTrashed(boardId)) throw new InvalidOperationException("回收站中的白板不能直接归档。");
            using var transaction = _connection.BeginTransaction();
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE board_metadata SET archived_at = $at WHERE board_id = $id";
            command.Parameters.AddWithValue("$id", boardId);
            command.Parameters.AddWithValue("$at", archived ? DateTimeOffset.UtcNow.ToString("O") : DBNull.Value);
            command.ExecuteNonQuery();
            MarkDataDirty(transaction);
            transaction.Commit();
        }
    }

    public void SetBoardProtected(string boardId, bool isProtected)
    {
        lock (_gate)
        {
            EnsureBoardExists(boardId);
            using var transaction = _connection.BeginTransaction();
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE board_metadata SET is_protected = $protected WHERE board_id = $id";
            command.Parameters.AddWithValue("$id", boardId);
            command.Parameters.AddWithValue("$protected", isProtected ? 1 : 0);
            command.ExecuteNonQuery();
            MarkDataDirty(transaction);
            transaction.Commit();
        }
    }

    public void SetBoardPinned(string boardId, bool isPinned)
    {
        lock (_gate)
        {
            EnsureBoardExists(boardId);
            using var transaction = _connection.BeginTransaction();
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE board_metadata SET is_pinned = $pinned WHERE board_id = $id";
            command.Parameters.AddWithValue("$id", boardId);
            command.Parameters.AddWithValue("$pinned", isPinned ? 1 : 0);
            command.ExecuteNonQuery();
            MarkDataDirty(transaction);
            transaction.Commit();
        }
    }

    public bool IsBoardProtected(string boardId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT is_protected FROM board_metadata WHERE board_id = $id";
        command.Parameters.AddWithValue("$id", boardId);
        return Convert.ToInt32(command.ExecuteScalar() ?? 0) != 0;
    }

    private bool IsBoardTrashed(string boardId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT trashed_at FROM board_metadata WHERE board_id = $id";
        command.Parameters.AddWithValue("$id", boardId);
        return command.ExecuteScalar() is string value && !string.IsNullOrWhiteSpace(value);
    }

    private void EnsureBoardExists(string boardId)
    {
        if (!boardId.StartsWith("board:", StringComparison.Ordinal) || LoadCanvasById(boardId) is null)
            throw new InvalidOperationException("找不到指定白板。");
    }

    private void EnsureBoardMetadata()
    {
        lock (_gate)
        {
            using var transaction = _connection.BeginTransaction();
            using var select = _connection.CreateCommand();
            select.Transaction = transaction;
            select.CommandText = "SELECT id, json FROM canvases WHERE id LIKE 'board:%'";
            using var reader = select.ExecuteReader();
            var records = new List<(string Id, string Json)>();
            while (reader.Read()) records.Add((reader.GetString(0), reader.GetString(1)));
            reader.Close();
            foreach (var (id, json) in records)
            {
                var document = JsonSerializer.Deserialize<CanvasDocument>(json, JsonOptions);
                if (document is null) continue;
                using var insert = _connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO board_metadata (board_id, is_protected, created_at)
                    VALUES ($id, $protected, $created)
                    ON CONFLICT(board_id) DO UPDATE SET
                        is_protected = CASE WHEN $protected = 1 THEN 1 ELSE board_metadata.is_protected END;
                    """;
                insert.Parameters.AddWithValue("$id", id);
                insert.Parameters.AddWithValue("$protected", string.Equals(document.Name, "Operator Learning", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
                insert.Parameters.AddWithValue("$created", document.CreatedAt == default ? DateTimeOffset.UtcNow.ToString("O") : document.CreatedAt.ToString("O"));
                insert.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    private void MarkDataDirty(SqliteTransaction? transaction = null)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO settings (key, value) VALUES ('data-dirty', '1') ON CONFLICT(key) DO UPDATE SET value = '1';";
        command.ExecuteNonQuery();
    }

    private static DateTimeOffset? ReadNullableDate(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        return DateTimeOffset.TryParse(reader.GetString(ordinal), out var value) ? value : null;
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

    public BackupSummary CreateBackup(string kind = "manual")
    {
        var safeKind = string.Equals(kind, "auto", StringComparison.OrdinalIgnoreCase) ? "auto" : "manual";
        var id = $"{safeKind}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var directory = Path.Combine(_backupsDirectory, id);
        Directory.CreateDirectory(directory);
        var database = Path.Combine(directory, "litweave.db");
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "VACUUM INTO $path";
            command.Parameters.AddWithValue("$path", database);
            command.ExecuteNonQuery();
        }

        var imageDirectory = Path.Combine(directory, "images");
        Directory.CreateDirectory(imageDirectory);
        var copiedImages = 0;
        if (Directory.Exists(_imagesDirectory))
        {
            foreach (var source in Directory.EnumerateFiles(_imagesDirectory))
            {
                var destination = Path.Combine(imageDirectory, Path.GetFileName(source));
                File.Copy(source, destination, true);
                copiedImages++;
            }
        }
        var boardCount = ListBoards("all").Count;
        var checksums = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["litweave.db"] = ComputeSha256(database)
        };
        foreach (var image in Directory.EnumerateFiles(imageDirectory)) checksums[$"images/{Path.GetFileName(image)}"] = ComputeSha256(image);
        File.WriteAllText(Path.Combine(directory, "SHA256SUMS.txt"), string.Join(Environment.NewLine, checksums.Select(pair => $"{pair.Value}  {pair.Key}")) + Environment.NewLine, new UTF8Encoding(false));
        var manifest = new
        {
            format = "litweave-backup",
            version = 1,
            id,
            kind = safeKind,
            createdAt = DateTimeOffset.UtcNow,
            boardCount,
            imageCount = copiedImages,
            files = checksums.Keys.ToArray()
        };
        File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
        var health = VerifyBackup(directory);
        if (!health.IsHealthy) throw new InvalidOperationException($"备份校验失败：{health.HealthMessage}");
        if (safeKind == "auto")
        {
            SetSetting("last-auto-backup-at", health.CreatedAt.ToString("O"));
            SetSetting("data-dirty", "0");
            TrimAutomaticBackups(20);
        }
        return health;
    }

    public IReadOnlyList<BackupSummary> ListBackups()
    {
        if (!Directory.Exists(_backupsDirectory)) return [];
        var directories = Directory.EnumerateDirectories(_backupsDirectory).Select(VerifyBackup);
        var legacyFiles = Directory.EnumerateFiles(_backupsDirectory, "*.db", SearchOption.TopDirectoryOnly)
            .Select(path => new BackupSummary
            {
                Id = Path.GetFileNameWithoutExtension(path), Path = path, Kind = "manual",
                CreatedAt = new DateTimeOffset(File.GetCreationTimeUtc(path), TimeSpan.Zero),
                IsHealthy = string.IsNullOrWhiteSpace(GetIntegrityError(path)), HealthMessage = string.IsNullOrWhiteSpace(GetIntegrityError(path)) ? "校验通过" : "SQLite 副本需要检查"
            });
        return directories.Concat(legacyFiles)
            .OrderByDescending(summary => summary.CreatedAt)
            .ToList();
    }

    public BackupSummary VerifyBackup(string backupPath)
    {
        var directory = Path.GetFullPath(backupPath);
        if (File.Exists(directory))
        {
            var error = GetIntegrityError(directory);
            return new BackupSummary { Id = Path.GetFileNameWithoutExtension(directory), Path = directory, Kind = "manual", CreatedAt = new DateTimeOffset(File.GetCreationTimeUtc(directory), TimeSpan.Zero), IsHealthy = string.IsNullOrWhiteSpace(error), HealthMessage = string.IsNullOrWhiteSpace(error) ? "校验通过" : error };
        }
        var id = Path.GetFileName(directory);
        var createdAt = Directory.Exists(directory) && Directory.GetCreationTimeUtc(directory) is var created
            ? new DateTimeOffset(created, TimeSpan.Zero)
            : DateTimeOffset.MinValue;
        var kind = id.StartsWith("auto-", StringComparison.OrdinalIgnoreCase) ? "auto" : "manual";
        var boardCount = 0;
        var imageCount = 0;
        string? message = null;
        try
        {
            if (!Directory.Exists(directory)) throw new InvalidOperationException("备份目录不存在。");
            var database = Path.Combine(directory, "litweave.db");
            if (!File.Exists(database)) throw new InvalidOperationException("缺少 SQLite 数据库副本。");
            var integrity = GetIntegrityError(database);
            if (!string.IsNullOrWhiteSpace(integrity)) throw new InvalidOperationException(integrity);
            var manifestPath = Path.Combine(directory, "manifest.json");
            if (File.Exists(manifestPath))
            {
                using var json = JsonDocument.Parse(File.ReadAllText(manifestPath));
                if (json.RootElement.TryGetProperty("createdAt", out var at) && DateTimeOffset.TryParse(at.GetString(), out var parsed)) createdAt = parsed;
                if (json.RootElement.TryGetProperty("kind", out var kindElement)) kind = kindElement.GetString() ?? kind;
                if (json.RootElement.TryGetProperty("boardCount", out var boardElement)) boardCount = boardElement.GetInt32();
                if (json.RootElement.TryGetProperty("imageCount", out var imageElement)) imageCount = imageElement.GetInt32();
            }
            var imageDirectory = Path.Combine(directory, "images");
            if (Directory.Exists(imageDirectory)) imageCount = Math.Max(imageCount, Directory.EnumerateFiles(imageDirectory).Count());
            var checksumFile = Path.Combine(directory, "SHA256SUMS.txt");
            if (File.Exists(checksumFile))
            {
                foreach (var line in File.ReadLines(checksumFile))
                {
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) continue;
                    var path = Path.Combine(directory, parts[1].TrimStart('*').Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(path) || !string.Equals(parts[0], ComputeSha256(path), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"文件校验不一致：{parts[1]}");
                }
            }
            return new BackupSummary { Id = id, Path = directory, Kind = kind, CreatedAt = createdAt, BoardCount = boardCount, ImageCount = imageCount, IsHealthy = true, HealthMessage = "校验通过" };
        }
        catch (Exception exception)
        {
            message = exception.Message;
            return new BackupSummary { Id = id, Path = directory, Kind = kind, CreatedAt = createdAt, BoardCount = boardCount, ImageCount = imageCount, IsHealthy = false, HealthMessage = message };
        }
    }

    public string RestoreBackupToNewRoot(string backupPath, string destinationRoot)
    {
        var verified = VerifyBackup(backupPath);
        if (!verified.IsHealthy) throw new InvalidOperationException($"不能恢复不健康的备份：{verified.HealthMessage}");
        var source = Path.GetFullPath(backupPath);
        var destination = Path.GetFullPath(destinationRoot);
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("恢复目录必须与备份目录不同。");
        var currentRoot = Path.GetFullPath(Path.GetDirectoryName(_databasePath)!);
        if (string.Equals(currentRoot, destination, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("恢复必须写入新的数据目录，不能覆盖当前目录。");
        Directory.CreateDirectory(destination);
        var sourceDatabase = File.Exists(source) ? source : Path.Combine(source, "litweave.db");
        File.Copy(sourceDatabase, Path.Combine(destination, "litweave.db"), true);
        var sourceImages = Path.Combine(source, "images");
        var destinationImages = Path.Combine(destination, "images");
        if (Directory.Exists(sourceImages))
        {
            Directory.CreateDirectory(destinationImages);
            foreach (var image in Directory.EnumerateFiles(sourceImages)) File.Copy(image, Path.Combine(destinationImages, Path.GetFileName(image)), true);
        }
        if (Directory.Exists(source))
        {
            foreach (var name in new[] { "manifest.json", "SHA256SUMS.txt" })
            {
                var sourceFile = Path.Combine(source, name);
                if (File.Exists(sourceFile)) File.Copy(sourceFile, Path.Combine(destination, name), true);
            }
        }
        if (GetIntegrityError(Path.Combine(destination, "litweave.db")) is { } integrity) throw new InvalidOperationException($"恢复后校验失败：{integrity}");
        return destination;
    }

    public BackupSummary? MaybeCreateAutomaticBackup(bool hasChanges)
    {
        if (!hasChanges || !string.Equals(GetSetting("data-dirty"), "1", StringComparison.Ordinal)) return null;
        var last = GetSetting("last-auto-backup-at");
        if (DateTimeOffset.TryParse(last, out var previous) && DateTimeOffset.UtcNow - previous < TimeSpan.FromMinutes(30)) return null;
        return CreateBackup("auto");
    }

    private void TrimAutomaticBackups(int keep)
    {
        foreach (var summary in ListBackups().Where(summary => summary.Kind == "auto").Skip(keep))
        {
            try { Directory.Delete(summary.Path, true); } catch { /* cleanup is best effort */ }
        }
    }

    private static string ComputeSha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

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
