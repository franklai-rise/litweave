using System.Text.Json;
using LitWeave.Models;
using Microsoft.Data.Sqlite;

namespace LitWeave.Services;

public sealed class LitWeaveRepository : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    public LitWeaveRepository(string? databasePath = null)
    {
        var path = databasePath ?? AppPaths.DatabasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connection = new SqliteConnection($"Data Source={path};Cache=Shared;Pooling=False");
        _connection.Open();
        Initialize();
    }

    private void Initialize()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;
            CREATE TABLE IF NOT EXISTS zotero_snapshots (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                content_hash TEXT NOT NULL,
                refreshed_at TEXT NOT NULL,
                json TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS canvases (
                id TEXT PRIMARY KEY,
                root_collection_key TEXT NOT NULL,
                json TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS refresh_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                refreshed_at TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                new_count INTEGER NOT NULL,
                updated_count INTEGER NOT NULL,
                removed_count INTEGER NOT NULL,
                status TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
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
                command.CommandText = """
                    INSERT INTO zotero_snapshots (id, content_hash, refreshed_at, json)
                    VALUES (1, $hash, $at, $json)
                    ON CONFLICT(id) DO UPDATE SET content_hash=$hash, refreshed_at=$at, json=$json;
                    """;
                command.Parameters.AddWithValue("$hash", snapshot.ContentHash);
                command.Parameters.AddWithValue("$at", snapshot.RefreshedAt.ToString("O"));
                command.Parameters.AddWithValue("$json", json);
                command.ExecuteNonQuery();
            }

            using (var history = _connection.CreateCommand())
            {
                history.Transaction = transaction;
                history.CommandText = """
                    INSERT INTO refresh_history (refreshed_at, content_hash, new_count, updated_count, removed_count, status)
                    VALUES ($at, $hash, $new, $updated, $removed, $status);
                    """;
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

    public CanvasDocument? LoadCanvas(string rootCollectionKey)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT json FROM canvases WHERE id = $id";
            command.Parameters.AddWithValue("$id", CanvasId(rootCollectionKey));
            var value = command.ExecuteScalar() as string;
            return string.IsNullOrWhiteSpace(value) ? null : JsonSerializer.Deserialize<CanvasDocument>(value, JsonOptions);
        }
    }

    public void SaveCanvas(CanvasDocument document)
    {
        document.UpdatedAt = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(document, JsonOptions);
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO canvases (id, root_collection_key, json, updated_at)
                VALUES ($id, $root, $json, $at)
                ON CONFLICT(id) DO UPDATE SET root_collection_key=$root, json=$json, updated_at=$at;
                """;
            command.Parameters.AddWithValue("$id", CanvasId(document.RootCollectionKey));
            command.Parameters.AddWithValue("$root", document.RootCollectionKey);
            command.Parameters.AddWithValue("$json", json);
            command.Parameters.AddWithValue("$at", document.UpdatedAt.ToString("O"));
            command.ExecuteNonQuery();
        }
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
            command.CommandText = """
                INSERT INTO settings (key, value) VALUES ($key, $value)
                ON CONFLICT(key) DO UPDATE SET value=$value;
                """;
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            command.ExecuteNonQuery();
        }
    }

    private static string CanvasId(string rootCollectionKey) =>
        string.IsNullOrWhiteSpace(rootCollectionKey) ? "personal:library" : $"personal:{rootCollectionKey}";

    public void Dispose() => _connection.Dispose();
}
