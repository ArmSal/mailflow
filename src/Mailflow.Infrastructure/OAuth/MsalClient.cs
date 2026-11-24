using Mailflow.Application.Abstractions;
using Microsoft.Identity.Client;

namespace Mailflow.Infrastructure.OAuth;

internal sealed class MsalClient : IMsalClient
{
    private const string Authority = "https://login.microsoftonline.com/common";
    private static readonly string[] Scopes = ["https://outlook.office.com/IMAP.AccessAsUser.All"]; // offline_access is added by MSAL itself

    private static (IPublicClientApplication App, Func<string> Blob) Build(string clientId, string? cacheBlob)
    {
        var app = PublicClientApplicationBuilder.Create(clientId).WithAuthority(Authority).Build();
        var blob = cacheBlob;
        app.UserTokenCache.SetBeforeAccess(a =>
        {
            if (!string.IsNullOrEmpty(blob)) a.TokenCache.DeserializeMsalV3(Convert.FromBase64String(blob));
        });
        app.UserTokenCache.SetAfterAccess(a =>
        {
            if (a.HasStateChanged) blob = Convert.ToBase64String(a.TokenCache.SerializeMsalV3());
        });
        return (app, () => blob ?? "");
    }

    public async Task<DeviceCodeResult> AcquireByDeviceCodeAsync(string clientId, string? cacheBlob, Func<DeviceCodeInfo, Task> onCode, CancellationToken ct)
    {
        var (app, blob) = Build(clientId, cacheBlob);
        try
        {
            var r = await app.AcquireTokenWithDeviceCode(Scopes,
                cb => onCode(new DeviceCodeInfo(cb.UserCode, cb.VerificationUrl, cb.ExpiresOn.UtcDateTime))).ExecuteAsync(ct);
            return new DeviceCodeResult(r.AccessToken, blob(), r.Account?.Username);
        }
        catch (MsalException ex) { throw new OAuthFailureException(Classify(ex)); }
        catch (HttpRequestException) { throw new OAuthFailureException(OAuthFailure.Network); }
    }

    public async Task<SilentResult> AcquireSilentAsync(string clientId, string cacheBlob, string? username, CancellationToken ct)
    {
        var (app, blob) = Build(clientId, cacheBlob);
        var accounts = await app.GetAccountsAsync();
        var account = accounts.FirstOrDefault(a => string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase))
            ?? accounts.FirstOrDefault();
        if (account is null) throw new OAuthInteractionRequiredException();
        try
        {
            var r = await app.AcquireTokenSilent(Scopes, account).ExecuteAsync(ct);
            return new SilentResult(r.AccessToken, blob());
        }
        catch (MsalUiRequiredException) { throw new OAuthInteractionRequiredException(); }
    }

    private static OAuthFailure Classify(MsalException ex)
    {
        var code = ex.ErrorCode ?? "";
        var text = ex.Message ?? "";
        if (code is "code_expired" or "device_code_expired" || text.Contains("AADSTS70019")) return OAuthFailure.CodeExpired;
        if (code is "authorization_declined" || text.Contains("AADSTS70007") || text.Contains("AADSTS70008")) return OAuthFailure.Declined;
        if (code is "invalid_client" or "unauthorized_client" || text.Contains("AADSTS70002") || text.Contains("AADSTS700016")
            || text.Contains("AADSTS50020") || text.Contains("AADSTS500011")) return OAuthFailure.WrongAccountType;
        if (code is "service_not_available" or "request_timeout" or "network_not_available") return OAuthFailure.Network;
        return OAuthFailure.Other;
    }
}
