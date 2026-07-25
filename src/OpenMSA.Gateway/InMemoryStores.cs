using System.Collections.Concurrent;
using OpenMSA.Core;
using OpenMSA.Policy;

namespace OpenMSA.Gateway;

public sealed class InMemoryPolicyStore : IPolicyStore
{
    private readonly ConcurrentDictionary<string, PolicyDocument> _policies = new();
    public Task<PolicyDocument?> GetPolicyAsync(string spaceId, string section, CancellationToken cancellationToken = default)
    {
        if (!_policies.TryGetValue($"{spaceId}:{section}", out var policy))
        {
            _policies.TryGetValue($"global:{section}", out policy);
        }
        return Task.FromResult(policy);
    }

    public void Add(string spaceId, string sectionRef, PolicyDocument policy)
        => _policies[$"{spaceId}:{sectionRef}"] = policy;
}

public sealed class InMemoryManifestStore : IManifestStore
{
    private readonly ConcurrentDictionary<string, ManagedSpaceManifest> _spaces = new();
    private readonly ConcurrentDictionary<string, string> _alias = new();

    public Task<bool> AddAsync(ManagedSpaceManifest manifest, CancellationToken cancellationToken = default)
    {
        if (!_spaces.TryAdd(manifest.Metadata.SpaceId, manifest))
            return Task.FromResult(false);

        _alias[manifest.Metadata.SpaceId] = manifest.Metadata.SpaceId;
        _alias[manifest.Metadata.Name.ToLowerInvariant()] = manifest.Metadata.SpaceId;
        return Task.FromResult(true);
    }

    public Task<ManagedSpaceManifest?> GetByIdAsync(string spaceId, CancellationToken cancellationToken = default)
    {
        _spaces.TryGetValue(spaceId, out var manifest);
        return Task.FromResult(manifest);
    }

    public Task<ManagedSpaceManifest?> GetByReferenceAsync(string spaceRef, CancellationToken cancellationToken = default)
    {
        if (_alias.TryGetValue(spaceRef, out var spaceId))
            return GetByIdAsync(spaceId, cancellationToken);

        if (_alias.TryGetValue(spaceRef.ToLowerInvariant(), out var alt))
            return GetByIdAsync(alt, cancellationToken);

        return Task.FromResult<ManagedSpaceManifest?>(null);
    }
}

public sealed class InMemorySpaceResolver : ISpaceResolver
{
    private readonly IManifestStore _manifestStore;
    public InMemorySpaceResolver(IManifestStore manifestStore) => _manifestStore = manifestStore;

    public async Task<string?> ResolveAsync(string spaceRef, CancellationToken cancellationToken = default)
    {
        var byId = await _manifestStore.GetByIdAsync(spaceRef, cancellationToken);
        if (byId is not null) return spaceRef;
        var byAlias = await _manifestStore.GetByReferenceAsync(spaceRef, cancellationToken);
        return byAlias?.Metadata.SpaceId;
    }
}

public sealed class NoopAuditSink : IAuditSink
{
    public List<AuditEvent> Events { get; } = [];
    public Task RecordAsync(AuditEvent @event, CancellationToken cancellationToken = default)
    {
        Events.Add(@event);
        return Task.CompletedTask;
    }
}

public sealed class FixedWindowRateLimiter : IRateLimiter
{
    private readonly Func<string, string, bool> _checker;
    public FixedWindowRateLimiter(Func<string, string, bool>? checker = null)
        => _checker = checker ?? ((_, __) => true);

    public Task<bool> IsAllowedAsync(string key, string endpoint, CancellationToken cancellationToken = default)
        => Task.FromResult(_checker(key, endpoint));
}
