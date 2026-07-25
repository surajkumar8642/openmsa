using System.Globalization;
using Microsoft.Data.Sqlite;

namespace OpenMSA.Index;

public sealed class SqliteIndexAdapter : IIndexAdapter, IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteIndexAdapter(string databasePath)
    {
        _connection = new SqliteConnection($"Data Source={databasePath}");
        _connection.Open();
        Initialize();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    private void Initialize()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS index_records (
                resource_id TEXT NOT NULL,
                space_id TEXT NOT NULL,
                section TEXT NOT NULL,
                resource_type TEXT NOT NULL,
                receiver_subject_id TEXT,
                receiver_mobile_hash TEXT,
                bill_number TEXT,
                created_at TEXT NOT NULL,
                status TEXT NOT NULL,
                storage_object_ref TEXT NOT NULL,
                PRIMARY KEY (resource_id, space_id)
            );
            CREATE INDEX IF NOT EXISTS idx_index_records_space_receiver ON index_records(space_id, receiver_mobile_hash);
            CREATE INDEX IF NOT EXISTS idx_index_records_space_subject ON index_records(space_id, receiver_subject_id);
            CREATE INDEX IF NOT EXISTS idx_index_records_space_status ON index_records(space_id, status);
            """;
        cmd.ExecuteNonQuery();
    }

    public async Task UpsertAsync(IndexRecord record, CancellationToken cancellationToken = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO index_records (
                resource_id, space_id, section, resource_type, receiver_subject_id,
                receiver_mobile_hash, bill_number, created_at, status, storage_object_ref
            ) VALUES (
                @resource_id, @space_id, @section, @resource_type, @receiver_subject_id,
                @receiver_mobile_hash, @bill_number, @created_at, @status, @storage_object_ref
            )
            ON CONFLICT(resource_id, space_id) DO UPDATE SET
                section=@section,
                resource_type=@resource_type,
                receiver_subject_id=@receiver_subject_id,
                receiver_mobile_hash=@receiver_mobile_hash,
                bill_number=@bill_number,
                created_at=@created_at,
                status=@status,
                storage_object_ref=@storage_object_ref
            """;

        cmd.Parameters.AddWithValue("@resource_id", record.ResourceId);
        cmd.Parameters.AddWithValue("@space_id", record.SpaceId);
        cmd.Parameters.AddWithValue("@section", record.Section);
        cmd.Parameters.AddWithValue("@resource_type", record.ResourceType);
        cmd.Parameters.AddWithValue("@receiver_subject_id", record.ReceiverSubjectId as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@receiver_mobile_hash", record.ReceiverMobileHash as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bill_number", record.BillNumber as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@created_at", record.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@status", record.Status);
        cmd.Parameters.AddWithValue("@storage_object_ref", record.StorageObjectRef);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IndexRecord?> GetAsync(string spaceId, string resourceId, CancellationToken cancellationToken = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT resource_id, space_id, section, resource_type, receiver_subject_id, receiver_mobile_hash, bill_number, created_at, status, storage_object_ref
            FROM index_records
            WHERE space_id=@space_id AND resource_id=@resource_id
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@space_id", spaceId);
        cmd.Parameters.AddWithValue("@resource_id", resourceId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadRecord(reader);
    }

    public async Task<bool> ExistsResourceAsync(string spaceId, string resourceId, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(spaceId, resourceId, cancellationToken);
        return existing is not null;
    }

    public async Task<IReadOnlyList<IndexRecord>> QueryAsync(IndexQuery query, CancellationToken cancellationToken = default)
    {
        var limit = Math.Clamp(query.Limit, 1, 200);
        var filters = new List<string>
        {
            "space_id=@space_id"
        };

        var sql = new System.Text.StringBuilder(
            "SELECT resource_id, space_id, section, resource_type, receiver_subject_id, receiver_mobile_hash, bill_number, created_at, status, storage_object_ref FROM index_records WHERE ");
        var parameters = new List<SqliteParameter>
        {
            new("@space_id", query.SpaceId)
        };
        if (!string.IsNullOrWhiteSpace(query.Section)) parameters.Add(new("@section", query.Section));
        if (!string.IsNullOrWhiteSpace(query.ReceiverMobileHash)) parameters.Add(new("@receiver_mobile_hash", query.ReceiverMobileHash));
        if (!string.IsNullOrWhiteSpace(query.ReceiverSubjectId)) parameters.Add(new("@receiver_subject_id", query.ReceiverSubjectId));
        if (!string.IsNullOrWhiteSpace(query.BillNumber)) parameters.Add(new("@bill_number", query.BillNumber));

        if (!string.IsNullOrWhiteSpace(query.Section))
            filters.Add("section=@section");
        if (!string.IsNullOrWhiteSpace(query.ReceiverMobileHash))
            filters.Add("receiver_mobile_hash=@receiver_mobile_hash");
        if (!string.IsNullOrWhiteSpace(query.ReceiverSubjectId))
            filters.Add("receiver_subject_id=@receiver_subject_id");
        if (!string.IsNullOrWhiteSpace(query.BillNumber))
            filters.Add("bill_number=@bill_number");
        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            filters.Add("resource_id > @cursor");
            parameters.Add(new("@cursor", query.Cursor));
        }

        var sqlText = $"SELECT resource_id, space_id, section, resource_type, receiver_subject_id, receiver_mobile_hash, bill_number, created_at, status, storage_object_ref FROM index_records WHERE {string.Join(" AND ", filters)} ORDER BY created_at DESC LIMIT @limit";

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = sqlText;
        foreach (var p in parameters) cmd.Parameters.Add(p);
        cmd.Parameters.AddWithValue("@limit", limit);
        var rows = new List<IndexRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(ReadRecord(reader));
        }
        return rows;
    }

    private static IndexRecord ReadRecord(SqliteDataReader reader)
    {
        DateTimeOffset createdAt = DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        return new IndexRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            createdAt,
            reader.GetString(8),
            reader.GetString(9));
    }
}
