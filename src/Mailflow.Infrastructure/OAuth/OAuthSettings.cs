using Mailflow.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Infrastructure.OAuth;

internal static class OAuthSettings
{
    public const string ClientIdKey = "OAuthMicrosoftClientId";

    public static async Task<string?> GetClientIdAsync(IAppDbContext db, CancellationToken ct)
    {
        var v = await db.Settings.AsNoTracking().Where(s => s.Key == ClientIdKey).Select(s => s.Value).FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }
}
