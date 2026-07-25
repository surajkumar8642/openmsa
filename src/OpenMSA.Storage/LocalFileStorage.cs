using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace OpenMSA.Storage;

public sealed class LocalFileStorage : IStorageAdapter
{
    private readonly string _root;
    private static readonly Regex SafeObject = new("^[A-Za-z0-9._-]{4,128}$", RegexOptions.Compiled);

    public LocalFileStorage(string rootPath)
    {
        _root = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(_root);
    }

    public async Task<StoredObject> PutAsync(string canonicalObjectId, byte[] content, string contentType, CancellationToken cancellationToken = default)
    {
        ValidateObjectId(canonicalObjectId);
        var path = Path.Combine(_root, canonicalObjectId);
        await File.WriteAllBytesAsync(path, content, cancellationToken);
        var etag = ComputeEtag(content);
        return new StoredObject(canonicalObjectId, etag, content.Length, contentType);
    }

    public async Task<byte[]> GetAsync(string canonicalObjectId, CancellationToken cancellationToken = default)
    {
        ValidateObjectId(canonicalObjectId);
        var path = Path.Combine(_root, canonicalObjectId);
        return await File.ReadAllBytesAsync(path, cancellationToken);
    }

    public Task DeleteAsync(string canonicalObjectId, CancellationToken cancellationToken = default)
    {
        ValidateObjectId(canonicalObjectId);
        var path = Path.Combine(_root, canonicalObjectId);
        File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string canonicalObjectId, CancellationToken cancellationToken = default)
    {
        ValidateObjectId(canonicalObjectId);
        var path = Path.Combine(_root, canonicalObjectId);
        return Task.FromResult(File.Exists(path));
    }

    private static string ComputeEtag(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
    }

    private static void ValidateObjectId(string objectId)
    {
        if (string.IsNullOrWhiteSpace(objectId) || !SafeObject.IsMatch(objectId))
            throw new InvalidOperationException("Invalid object id.");
    }
}
