using Mailflow.Application.Abstractions;

namespace Mailflow.Infrastructure.OAuth;

public enum OAuthFailure { Other, CodeExpired, Declined, WrongAccountType, Network }

/// <summary>Sign-in failure already classified (the message never carries tokens or codes).</summary>
public sealed class OAuthFailureException(OAuthFailure kind) : Exception($"OAuth failure: {kind}")
{
    public OAuthFailure Kind { get; } = kind;
}

/// <summary>Silent renewal is impossible: the user has to sign in again.</summary>
public sealed class OAuthInteractionRequiredException() : Exception("Interaction required.");

public sealed record DeviceCodeResult(string AccessToken, string NewCacheBlob, string? AccountUsername);

public sealed record SilentResult(string AccessToken, string NewCacheBlob);

/// <summary>Everything MSAL-specific sits behind this seam so the rest can be tested with a fake. Cache blobs are opaque strings.</summary>
internal interface IMsalClient
{
    Task<DeviceCodeResult> AcquireByDeviceCodeAsync(string clientId, string? cacheBlob, Func<DeviceCodeInfo, Task> onCode, CancellationToken ct);

    /// <exception cref="OAuthInteractionRequiredException"/>
    Task<SilentResult> AcquireSilentAsync(string clientId, string cacheBlob, string? username, CancellationToken ct);
}
