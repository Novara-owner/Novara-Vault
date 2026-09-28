using Microsoft.Data.Sqlite;

namespace Novara.Sync.Server.Storage;







public sealed class SqliteSpaceStore : ISpaceStore
{
    private const string DbFileName = "novara-sync.db";

    private static readonly string[] SchemaStatements =
    {
        "PRAGMA journal_mode=WAL;",
        """
        CREATE TABLE IF NOT EXISTS spaces (
            space_id        TEXT PRIMARY KEY,
            name            TEXT NOT NULL,
            created_at      TEXT NOT NULL,
            updated_at      TEXT NOT NULL,
            current_version INTEGER NOT NULL,
            keywrap_version INTEGER NOT NULL,
            keywrap_blob    TEXT NOT NULL DEFAULT '',
            enroll_hash     TEXT NOT NULL,
            read_token_hash TEXT NOT NULL DEFAULT '',
            quota_bytes     INTEGER NOT NULL,
            max_versions    INTEGER NOT NULL
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS devices (
            space_id     TEXT NOT NULL,
            device_id    TEXT NOT NULL,
            name         TEXT NOT NULL,
            token_hash   TEXT NOT NULL,
            created_at   TEXT NOT NULL,
            last_seen_at TEXT NULL,
            revoked      INTEGER NOT NULL,
            kind         INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY (space_id, device_id)
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS versions (
            space_id    TEXT NOT NULL,
            version     INTEGER NOT NULL,
            created_at  TEXT NOT NULL,
            device_id   TEXT NOT NULL,
            sha256      TEXT NOT NULL,
            size        INTEGER NOT NULL,
            is_conflict INTEGER NOT NULL,
            conflict_of INTEGER NOT NULL,
            retained    INTEGER NOT NULL,
            PRIMARY KEY (space_id, version)
        );
        """,
    };









    private static readonly (string Table, string Column, string Ddl)[] Migrations =
    {
        ("spaces", "read_token_hash",
            "ALTER TABLE spaces ADD COLUMN read_token_hash TEXT NOT NULL DEFAULT '';"),
        ("devices", "kind",
            "ALTER TABLE devices ADD COLUMN kind INTEGER NOT NULL DEFAULT 0;"),
    };

    private readonly string _root;
    private readonly string _connectionString;

    public SqliteSpaceStore(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("storage root is required", nameof(root));
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_root, DbFileName),
            Mode = SqliteOpenMode.ReadWriteCreate,


            Pooling = false,
        }.ToString();
        Initialize();
    }

    public string Root => _root;


    public static bool IsSafeId(string? id) => BlobLayout.IsSafeId(id);



    public bool SpaceExists(string spaceId)
    {
        if (!BlobLayout.IsSafeId(spaceId)) return false;
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM spaces WHERE space_id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", spaceId);
        return Guarded(() => cmd.ExecuteScalar() is not null);
    }

    public SpaceRecord? GetSpace(string spaceId)
    {
        if (!BlobLayout.IsSafeId(spaceId)) return null;
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = SpaceSelect + " WHERE space_id = $id;";
        cmd.Parameters.AddWithValue("$id", spaceId);

        using var reader = Guarded(() => cmd.ExecuteReader());
        return reader.Read() ? ReadSpace(reader) : null;
    }


    public IReadOnlyList<SpaceRecord> ListSpaces()
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = SpaceSelect + " ORDER BY created_at, space_id;";

        var spaces = new List<SpaceRecord>();
        using var reader = Guarded(() => cmd.ExecuteReader());
        while (reader.Read()) spaces.Add(ReadSpace(reader));
        return spaces;
    }










    public void DeleteSpace(string spaceId)
    {
        RequireSafeId(spaceId);

        using (var cn = Open())
        using (var tx = cn.BeginTransaction())
        {
            try
            {
                using var cmd = cn.CreateCommand();
                cmd.Transaction = tx;


                cmd.CommandText = """
                    DELETE FROM versions WHERE space_id = $id;
                    DELETE FROM devices  WHERE space_id = $id;
                    DELETE FROM spaces   WHERE space_id = $id;
                    """;
                cmd.Parameters.AddWithValue("$id", spaceId);
                cmd.ExecuteNonQuery();
                tx.Commit();
            }
            catch (SqliteException e)
            {
                tx.Rollback();
                throw new SpaceStoreException("failed to delete the space", e);
            }
        }

        try { BlobLayout.RemoveSpaceDirectory(_root, spaceId); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {


            throw new SpaceStoreException(
                $"the space was deleted but its payload files could not be removed: {BlobLayout.SpaceDirectory(_root, spaceId)}", e);
        }
    }

    public void SaveSpace(SpaceRecord space)
    {
        ArgumentNullException.ThrowIfNull(space);
        RequireSafeId(space.SpaceId);

        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO spaces (space_id, name, created_at, updated_at, current_version,
                                keywrap_version, keywrap_blob, enroll_hash, read_token_hash,
                                quota_bytes, max_versions)
            VALUES ($id, $name, $created, $updated, $version, $kwv, $kwb, $enroll, $read, $quota, $max)
            ON CONFLICT(space_id) DO UPDATE SET
                name = excluded.name,
                updated_at = excluded.updated_at,
                current_version = excluded.current_version,
                keywrap_version = excluded.keywrap_version,
                keywrap_blob = excluded.keywrap_blob,
                enroll_hash = excluded.enroll_hash,
                read_token_hash = excluded.read_token_hash,
                quota_bytes = excluded.quota_bytes,
                max_versions = excluded.max_versions;
            """;
        cmd.Parameters.AddWithValue("$id", space.SpaceId);
        cmd.Parameters.AddWithValue("$name", space.Name);
        cmd.Parameters.AddWithValue("$created", FormatTime(space.CreatedAt));
        cmd.Parameters.AddWithValue("$updated", FormatTime(space.UpdatedAt));
        cmd.Parameters.AddWithValue("$version", space.CurrentVersion);
        cmd.Parameters.AddWithValue("$kwv", space.KeyWrapVersion);
        cmd.Parameters.AddWithValue("$kwb", space.KeyWrapJson);
        cmd.Parameters.AddWithValue("$enroll", space.EnrollmentHash);
        cmd.Parameters.AddWithValue("$read", space.ReadTokenHash);
        cmd.Parameters.AddWithValue("$quota", space.QuotaBytes);
        cmd.Parameters.AddWithValue("$max", space.MaxVersions);
        Guarded(cmd.ExecuteNonQuery);
    }



    public IReadOnlyList<DeviceRecord> GetDevices(string spaceId)
    {
        if (!BlobLayout.IsSafeId(spaceId)) return Array.Empty<DeviceRecord>();
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            SELECT device_id, name, token_hash, created_at, last_seen_at, revoked, kind
            FROM devices WHERE space_id = $id ORDER BY created_at, device_id;
            """;
        cmd.Parameters.AddWithValue("$id", spaceId);

        var devices = new List<DeviceRecord>();
        using var reader = Guarded(() => cmd.ExecuteReader());
        while (reader.Read())
        {
            devices.Add(new DeviceRecord
            {
                DeviceId = reader.GetString(0),
                Name = reader.GetString(1),
                TokenHash = reader.GetString(2),
                CreatedAt = ParseTime(reader.GetString(3)),
                LastSeenAt = reader.IsDBNull(4) ? null : ParseTime(reader.GetString(4)),
                Revoked = reader.GetInt64(5) != 0,
                Kind = reader.GetInt64(6) == (long)DeviceKind.Editor ? DeviceKind.Editor : DeviceKind.Standard,
            });
        }
        return devices;
    }

    public DeviceRecord? GetDevice(string spaceId, string deviceId)
        => GetDevices(spaceId).FirstOrDefault(d => string.Equals(d.DeviceId, deviceId, StringComparison.Ordinal));

    public void SaveDevice(string spaceId, DeviceRecord device)
    {
        ArgumentNullException.ThrowIfNull(device);
        RequireSafeId(spaceId);

        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO devices (space_id, device_id, name, token_hash, created_at, last_seen_at, revoked, kind)
            VALUES ($space, $id, $name, $hash, $created, $seen, $revoked, $kind)
            ON CONFLICT(space_id, device_id) DO UPDATE SET
                name = excluded.name,
                token_hash = excluded.token_hash,
                last_seen_at = excluded.last_seen_at,
                revoked = excluded.revoked,
                kind = excluded.kind;
            """;
        cmd.Parameters.AddWithValue("$space", spaceId);
        cmd.Parameters.AddWithValue("$id", device.DeviceId);
        cmd.Parameters.AddWithValue("$name", device.Name);
        cmd.Parameters.AddWithValue("$hash", device.TokenHash);
        cmd.Parameters.AddWithValue("$created", FormatTime(device.CreatedAt));
        cmd.Parameters.AddWithValue("$seen", device.LastSeenAt is null ? DBNull.Value : FormatTime(device.LastSeenAt.Value));
        cmd.Parameters.AddWithValue("$revoked", device.Revoked ? 1 : 0);
        cmd.Parameters.AddWithValue("$kind", (long)device.Kind);
        Guarded(cmd.ExecuteNonQuery);
    }





    public void TouchDevice(string spaceId, string deviceId, DateTime lastSeenAt)
    {



        RequireSafeId(spaceId);

        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "UPDATE devices SET last_seen_at = $seen WHERE space_id = $space AND device_id = $id;";
        cmd.Parameters.AddWithValue("$seen", FormatTime(lastSeenAt));
        cmd.Parameters.AddWithValue("$space", spaceId);
        cmd.Parameters.AddWithValue("$id", deviceId);
        Guarded(cmd.ExecuteNonQuery);
    }



    public IReadOnlyList<VersionRecord> GetVersions(string spaceId)
    {
        if (!BlobLayout.IsSafeId(spaceId)) return Array.Empty<VersionRecord>();
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            SELECT version, created_at, device_id, sha256, size, is_conflict, conflict_of, retained
            FROM versions WHERE space_id = $id ORDER BY version;
            """;
        cmd.Parameters.AddWithValue("$id", spaceId);

        var versions = new List<VersionRecord>();
        using var reader = Guarded(() => cmd.ExecuteReader());
        while (reader.Read())
        {
            versions.Add(new VersionRecord
            {
                Version = reader.GetInt64(0),
                CreatedAt = ParseTime(reader.GetString(1)),
                DeviceId = reader.GetString(2),
                Sha256 = reader.GetString(3),
                Size = reader.GetInt64(4),
                IsConflict = reader.GetInt64(5) != 0,
                ConflictOf = reader.GetInt64(6),
                Retained = reader.GetInt64(7) != 0,
            });
        }
        return versions;
    }


    public void SaveVersions(string spaceId, IReadOnlyList<VersionRecord> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        RequireSafeId(spaceId);

        using var cn = Open();
        using var tx = cn.BeginTransaction();
        try
        {
            using (var delete = cn.CreateCommand())
            {
                delete.Transaction = tx;
                delete.CommandText = "DELETE FROM versions WHERE space_id = $id;";
                delete.Parameters.AddWithValue("$id", spaceId);
                delete.ExecuteNonQuery();
            }

            foreach (var version in versions.OrderBy(v => v.Version))
            {
                using var insert = cn.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = """
                    INSERT INTO versions (space_id, version, created_at, device_id, sha256, size,
                                          is_conflict, conflict_of, retained)
                    VALUES ($space, $version, $created, $device, $sha, $size, $conflict, $of, $retained);
                    """;
                insert.Parameters.AddWithValue("$space", spaceId);
                insert.Parameters.AddWithValue("$version", version.Version);
                insert.Parameters.AddWithValue("$created", FormatTime(version.CreatedAt));
                insert.Parameters.AddWithValue("$device", version.DeviceId);
                insert.Parameters.AddWithValue("$sha", version.Sha256);
                insert.Parameters.AddWithValue("$size", version.Size);
                insert.Parameters.AddWithValue("$conflict", version.IsConflict ? 1 : 0);
                insert.Parameters.AddWithValue("$of", version.ConflictOf);
                insert.Parameters.AddWithValue("$retained", version.Retained ? 1 : 0);
                insert.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch (SqliteException e)
        {
            tx.Rollback();
            throw new SpaceStoreException("failed to persist versions", e);
        }
    }



    public void WriteBlob(string spaceId, VersionRecord version, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(payload);
        RequireSafeId(spaceId);

        Directory.CreateDirectory(BlobLayout.BlobDirectory(_root, spaceId));
        var path = BlobLayout.BlobPath(_root, spaceId, version);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, payload);
        File.Move(temp, path, overwrite: true);
    }

    public byte[]? ReadBlob(string spaceId, VersionRecord version)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (!BlobLayout.IsSafeId(spaceId)) return null;

        var path = BlobLayout.BlobPath(_root, spaceId, version);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    public void DeleteBlob(string spaceId, VersionRecord version)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (!BlobLayout.IsSafeId(spaceId)) return;

        var path = BlobLayout.BlobPath(_root, spaceId, version);
        if (File.Exists(path)) File.Delete(path);
    }








    private const string SpaceSelect = """
        SELECT space_id, name, created_at, updated_at, current_version, keywrap_version,
               keywrap_blob, enroll_hash, read_token_hash, quota_bytes, max_versions
        FROM spaces
        """;


    private static SpaceRecord ReadSpace(SqliteDataReader reader) => new()
    {
        SpaceId = reader.GetString(0),
        Name = reader.GetString(1),
        CreatedAt = ParseTime(reader.GetString(2)),
        UpdatedAt = ParseTime(reader.GetString(3)),
        CurrentVersion = reader.GetInt64(4),
        KeyWrapVersion = reader.GetInt64(5),
        KeyWrapJson = reader.GetString(6),
        EnrollmentHash = reader.GetString(7),
        ReadTokenHash = reader.GetString(8),
        QuotaBytes = reader.GetInt64(9),
        MaxVersions = reader.GetInt32(10),
    };

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void Initialize()
    {
        try
        {
            using var connection = Open();
            foreach (var statement in SchemaStatements)
            {
                using var command = connection.CreateCommand();
                command.CommandText = statement;
                command.ExecuteNonQuery();
            }

            foreach (var (table, column, ddl) in Migrations)
            {
                if (HasColumn(connection, table, column)) continue;
                using var command = connection.CreateCommand();
                command.CommandText = ddl;
                command.ExecuteNonQuery();
            }
        }
        catch (SqliteException e)
        {
            throw new SpaceStoreException("failed to initialise the sync database", e);
        }
    }

    private static void RequireSafeId(string spaceId)
    {
        if (!BlobLayout.IsSafeId(spaceId)) throw new SpaceStoreException("space id contains unsupported characters");
    }


    private static bool HasColumn(SqliteConnection connection, string table, string column)
    {
        using var command = connection.CreateCommand();

        command.CommandText = $"SELECT 1 FROM pragma_table_info('{table}') WHERE name = $column LIMIT 1;";
        command.Parameters.AddWithValue("$column", column);
        return command.ExecuteScalar() is not null;
    }


    private static T Guarded<T>(Func<T> action)
    {
        try { return action(); }
        catch (SqliteException e) { throw new SpaceStoreException("corrupt metadata: sync database", e); }
    }

    private static void Guarded(Action action)
    {
        try { action(); }
        catch (SqliteException e) { throw new SpaceStoreException("corrupt metadata: sync database", e); }
    }

    private static string FormatTime(DateTime value)
        => value.ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture);

    private static DateTime ParseTime(string value)
        => DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
}
