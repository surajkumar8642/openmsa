namespace OpenMSA.Index;

public sealed record IndexRecord(
    string ResourceId,
    string SpaceId,
    string Section,
    string ResourceType,
    string? ReceiverSubjectId,
    string? ReceiverMobileHash,
    string? BillNumber,
    DateTimeOffset CreatedAtUtc,
    string Status,
    string StorageObjectRef);

public sealed record IndexQuery(
    string SpaceId,
    string? Section,
    string? ReceiverMobileHash,
    string? ReceiverSubjectId,
    string? BillNumber,
    int Limit,
    string? Cursor);

public interface IIndexAdapter
{
    Task UpsertAsync(IndexRecord record, CancellationToken cancellationToken = default);
    Task<IndexRecord?> GetAsync(string spaceId, string resourceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IndexRecord>> QueryAsync(IndexQuery query, CancellationToken cancellationToken = default);
    Task<bool> ExistsResourceAsync(string spaceId, string resourceId, CancellationToken cancellationToken = default);
}
