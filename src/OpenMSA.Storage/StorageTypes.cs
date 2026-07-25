namespace OpenMSA.Storage;

public sealed record StoredObject(string ObjectId, string Etag, long SizeBytes, string ContentType);

public interface IStorageAdapter
{
    Task<StoredObject> PutAsync(string canonicalObjectId, byte[] content, string contentType, CancellationToken cancellationToken = default);
    Task<byte[]> GetAsync(string canonicalObjectId, CancellationToken cancellationToken = default);
    Task DeleteAsync(string canonicalObjectId, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string canonicalObjectId, CancellationToken cancellationToken = default);
}
