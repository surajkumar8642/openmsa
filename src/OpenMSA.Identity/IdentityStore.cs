using System.Collections.Concurrent;

namespace OpenMSA.Identity;

public interface IIdentityStore
{
    Task<IdentityUser?> FindByMobileAsync(string normalizedMobile, CancellationToken cancellationToken = default);
    Task<IdentityUser?> FindBySubjectAsync(string subjectId, CancellationToken cancellationToken = default);
    Task<bool> AddUserAsync(IdentityUser user, CancellationToken cancellationToken = default);
}

public sealed class InMemoryIdentityStore : IIdentityStore
{
    private readonly ConcurrentDictionary<string, IdentityUser> _bySubject = new();
    private readonly ConcurrentDictionary<string, IdentityUser> _byMobile = new();

    public Task<IdentityUser?> FindByMobileAsync(string normalizedMobile, CancellationToken cancellationToken = default)
    {
        _byMobile.TryGetValue(normalizedMobile, out var user);
        return Task.FromResult(user);
    }

    public Task<IdentityUser?> FindBySubjectAsync(string subjectId, CancellationToken cancellationToken = default)
    {
        _bySubject.TryGetValue(subjectId, out var user);
        return Task.FromResult(user);
    }

    public Task<bool> AddUserAsync(IdentityUser user, CancellationToken cancellationToken = default)
    {
        var existing = _byMobile.ContainsKey(user.MobileNormalized) || _bySubject.ContainsKey(user.SubjectId);
        if (existing) return Task.FromResult(false);

        _bySubject[user.SubjectId] = user;
        _byMobile[user.MobileNormalized] = user;
        return Task.FromResult(true);
    }
}
