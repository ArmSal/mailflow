using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Web.Auth;

public interface IAdminPasswordStore
{
    Task<string?> GetHashAsync(CancellationToken ct);
    Task SetHashAsync(string hash, CancellationToken ct);
}

/// <summary>Stores the hash in Setting 'AdminPasswordHash'.</summary>
public sealed class DbAdminPasswordStore(IAppDbContext db) : IAdminPasswordStore
{
    public const string Key = "AdminPasswordHash";

    public async Task<string?> GetHashAsync(CancellationToken ct)
        => (await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == Key, ct))?.Value;

    public async Task SetHashAsync(string hash, CancellationToken ct)
    {
        var s = await db.Settings.FirstOrDefaultAsync(x => x.Key == Key, ct);
        if (s is null) db.Settings.Add(new Setting { Key = Key, Value = hash });
        else s.Value = hash;
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Temporary fallback while no persistence is registered (hash lost on restart).</summary>
public sealed class InMemoryAdminPasswordStore : IAdminPasswordStore
{
    private volatile string? _hash;
    public Task<string?> GetHashAsync(CancellationToken ct) => Task.FromResult(_hash);
    public Task SetHashAsync(string hash, CancellationToken ct) { _hash = hash; return Task.CompletedTask; }
}
