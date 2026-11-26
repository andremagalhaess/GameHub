using System.IO.Compression;
using System.Text.Json;
using GameHub.Core.Data;
using Microsoft.Data.Sqlite;

namespace GameHub.Core.Backup;

/// <summary>
/// Backup completo (.zip) com o banco SQLite (cópia consistente via API de backup do SQLite)
/// e as imagens em cache. A restauração substitui os dados atuais e ajusta os caminhos das imagens.
/// Observação: chaves de API são cifradas com DPAPI e só funcionam na mesma conta do Windows.
/// </summary>
public sealed class BackupService
{
    private const string ManifestName = "manifest.json";
    private const string DbEntry = "gamehub.db";
    private const string ImagesPrefix = "images/";

    private readonly AppPaths _paths;
    private readonly Database _db;

    public BackupService(AppPaths paths, Database db)
    {
        _paths = paths;
        _db = db;
    }

    public sealed record Manifest(int Format, string AppRoot, int SchemaVersion, DateTimeOffset CreatedAt);

    public async Task CreateAsync(string zipPath, CancellationToken ct = default)
    {
        var tmpDb = Path.Combine(Path.GetTempPath(), $"gamehub-backup-{Guid.NewGuid():N}.db");
        try
        {
            using (var src = _db.Open())
            using (var dst = new SqliteConnection($"Data Source={tmpDb};Pooling=False"))
            {
                dst.Open();
                src.BackupDatabase(dst);
            }

            var tmpZip = zipPath + ".tmp";
            await using (var fs = File.Create(tmpZip))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(tmpDb, DbEntry, CompressionLevel.Optimal);
                if (Directory.Exists(_paths.ImagesDir))
                    foreach (var file in Directory.EnumerateFiles(_paths.ImagesDir, "*", SearchOption.AllDirectories))
                    {
                        ct.ThrowIfCancellationRequested();
                        var rel = Path.GetRelativePath(_paths.ImagesDir, file).Replace('\\', '/');
                        zip.CreateEntryFromFile(file, ImagesPrefix + rel, CompressionLevel.NoCompression);
                    }
                var manifest = new Manifest(1, _paths.Root, _db.SchemaVersion, DateTimeOffset.UtcNow);
                var entry = zip.CreateEntry(ManifestName);
                await using var es = entry.Open();
                await JsonSerializer.SerializeAsync(es, manifest, cancellationToken: ct);
            }
            File.Move(tmpZip, zipPath, overwrite: true);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(tmpDb);
        }
    }

    /// <summary>Restaura um backup. Antes, cria automaticamente um backup de segurança do estado atual.</summary>
    public async Task<string> RestoreAsync(string zipPath, CancellationToken ct = default)
    {
        Manifest manifest;
        using (var check = ZipFile.OpenRead(zipPath))
        {
            var m = check.GetEntry(ManifestName) ?? throw new InvalidDataException("Arquivo não é um backup do GameHub (manifest ausente).");
            if (check.GetEntry(DbEntry) is null) throw new InvalidDataException("Backup sem banco de dados.");
            await using var ms = m.Open();
            manifest = await JsonSerializer.DeserializeAsync<Manifest>(ms, cancellationToken: ct)
                ?? throw new InvalidDataException("Manifest inválido.");
        }

        Directory.CreateDirectory(_paths.BackupsDir);
        var safety = Path.Combine(_paths.BackupsDir, $"antes-de-restaurar-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        await CreateAsync(safety, ct);

        var staging = Path.Combine(_paths.Root, "restore-staging");
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);
        ZipFile.ExtractToDirectory(zipPath, staging);
        var stagedDb = Path.Combine(staging, DbEntry);

        // Ajusta caminhos absolutos das imagens para a pasta deste PC.
        var oldImages = Path.Combine(manifest.AppRoot, "images");
        if (!string.Equals(oldImages, _paths.ImagesDir, StringComparison.OrdinalIgnoreCase))
        {
            using var c = new SqliteConnection($"Data Source={stagedDb};Pooling=False");
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = """
                UPDATE games SET
                  cover_path = REPLACE(cover_path, @old, @new),
                  background_path = REPLACE(background_path, @old, @new),
                  logo_path = REPLACE(logo_path, @old, @new),
                  icon_path = REPLACE(icon_path, @old, @new);
                UPDATE profile SET
                  avatar_path = REPLACE(avatar_path, @old, @new),
                  banner_path = REPLACE(banner_path, @old, @new);
                """;
            cmd.Parameters.AddWithValue("@old", oldImages);
            cmd.Parameters.AddWithValue("@new", _paths.ImagesDir);
            cmd.ExecuteNonQuery();

            // Memórias (a partir do esquema v3; backups antigos não têm a tabela).
            using var has = c.CreateCommand();
            has.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'memories';";
            if ((long)has.ExecuteScalar()! > 0)
            {
                using var mem = c.CreateCommand();
                mem.CommandText = "UPDATE memories SET image_path = REPLACE(image_path, @old, @new);";
                mem.Parameters.AddWithValue("@old", oldImages);
                mem.Parameters.AddWithValue("@new", _paths.ImagesDir);
                mem.ExecuteNonQuery();
            }
        }

        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) TryDelete(_db.FilePath + suffix);
        File.Move(stagedDb, _db.FilePath, overwrite: true);

        var stagedImages = Path.Combine(staging, "images");
        if (Directory.Exists(stagedImages))
        {
            if (Directory.Exists(_paths.ImagesDir)) Directory.Delete(_paths.ImagesDir, true);
            Directory.Move(stagedImages, _paths.ImagesDir);
        }
        Directory.Delete(staging, true);
        _db.Migrate(); // backup de versão antiga é atualizado para o esquema atual
        return safety;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }
}
