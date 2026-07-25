using OpenMSA.Core;
using OpenMSA.Policy;

namespace OpenMSA.Gateway;

public interface ISpaceResolver
{
    Task<string?> ResolveAsync(string spaceRef, CancellationToken cancellationToken = default);
}

public interface IManifestStore
{
    Task<bool> AddAsync(ManagedSpaceManifest manifest, CancellationToken cancellationToken = default);
    Task<ManagedSpaceManifest?> GetByIdAsync(string spaceId, CancellationToken cancellationToken = default);
    Task<ManagedSpaceManifest?> GetByReferenceAsync(string spaceRef, CancellationToken cancellationToken = default);
}

public interface IPolicyStore
{
    Task<PolicyDocument?> GetPolicyAsync(string spaceId, string section, CancellationToken cancellationToken = default);
}

public interface IAuditSink
{
    Task RecordAsync(AuditEvent @event, CancellationToken cancellationToken = default);
}

public interface IRateLimiter
{
    Task<bool> IsAllowedAsync(string key, string endpoint, CancellationToken cancellationToken = default);
}

public sealed record OperationRequestContext(
    string SpaceRef,
    string Section,
    string JwtToken,
    string? ResourceId = null,
    string? Cursor = null,
    int Limit = 50);
