using System.Text.RegularExpressions;
using Mailflow.Domain.Enums;
using MimeKit.Cryptography;

namespace Mailflow.Infrastructure.Mail;

/// <summary>
/// Reads the receiving mail server's own verdict (Authentication-Results, RFC 8601) to tell whether the From
/// address can be trusted. LIMITS: the verdict is only as trustworthy as the receiving server; a provider that
/// adds no such header gives Unknown (so Strict mode blocks printing for that mailbox); and if the receiving
/// server adds no header, one forged by the sender cannot be told apart from a real one.
/// Only the first (topmost, most recently added) header is trusted: lower ones may have been written by the sender.
/// </summary>
public static partial class SenderAuthParser
{
    /// <param name="trustedAuthservId">
    /// The authserv-id the mailbox's provider puts first in its own header. When set, the topmost header with exactly that id
    /// is the only one believed (a sender can forge any header, but not the provider's id once the provider strips copies of it).
    /// When not set, the topmost header is read as before and the caller must not rely on a Pass (see PrintPolicy).
    /// </param>
    public static SenderVerification Parse(IReadOnlyList<string>? authenticationResultsHeaders, string? fromAddress, string? trustedAuthservId = null)
    {
        try
        {
            var fromDomain = DomainOf(fromAddress);
            if (fromDomain.Length == 0 || authenticationResultsHeaders is not { Count: > 0 }) return SenderVerification.Unknown;

            AuthenticationResults? parsed = null;
            var trusted = trustedAuthservId?.Trim();
            if (string.IsNullOrEmpty(trusted))
            {
                // A failed parse can still leave a half-filled result behind: a malformed header must never be read.
                if (!TryParseHeader(authenticationResultsHeaders[0], out parsed)) return SenderVerification.Unknown;
            }
            else
                foreach (var h in authenticationResultsHeaders)
                    if (TryParseHeader(h, out var candidate) && string.Equals(candidate.AuthenticationServiceIdentifier?.Trim(), trusted, StringComparison.OrdinalIgnoreCase))
                    {
                        parsed = candidate;
                        break;
                    }
            if (parsed is null) return SenderVerification.Unknown;

            bool dmarcPass = false, dmarcFail = false, dkimPass = false, dkimFail = false;
            foreach (var clause in parsed.Results)
            {
                var method = clause.Method.ToLowerInvariant();
                var result = clause.Result.ToLowerInvariant();
                var props = new Dictionary<string, string>();
                foreach (var p in clause.Properties)
                    if (IsPlainDomainValue(p.Value))
                        props[$"{p.PropertyType}.{p.Property}".ToLowerInvariant()] = p.Value.Trim();

                if (method == "dmarc")
                {
                    // A dmarc verdict only counts when it names the From domain we are judging. A missing header.from, or one
                    // we rejected as not a plain domain, must never count: fail closed.
                    if (!props.TryGetValue("header.from", out var hf) || !Aligned(DomainOf(hf), fromDomain)) continue;
                    if (result == "pass") dmarcPass = true;
                    else if (result == "fail") dmarcFail = true;
                }
                else if (method == "dkim")
                {
                    if (result == "pass")
                    {
                        var d = props.TryGetValue("header.d", out var hd) ? DomainOf(hd)
                            : props.TryGetValue("header.i", out var hi) ? DomainOf(hi) : "";
                        if (Aligned(d, fromDomain)) dkimPass = true;
                    }
                    else if (result == "fail") dkimFail = true;
                }
            }

            if (dmarcPass) return SenderVerification.Pass;
            if (dmarcFail) return SenderVerification.Fail;
            if (dkimPass) return SenderVerification.Pass;
            return dkimFail ? SenderVerification.Fail : SenderVerification.Unknown;
        }
        catch (Exception)
        {
            return SenderVerification.Unknown;
        }
    }

    static bool TryParseHeader(string? header, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AuthenticationResults? parsed)
    {
        parsed = null;
        if (header is null) return false;
        // The header value reaches us already decoded (RFC 2047 encoded words are expanded), so what we parse could differ
        // from the raw bytes. Real verdicts are plain ASCII: refuse anything else instead of guessing.
        if (header.Any(c => c > 127) || header.Contains("=?", StringComparison.Ordinal)) return false;
        // MimeKit's RFC 8601 parser understands quoted strings, escapes and comments. A hand-written split on ';'
        // can be fooled by values copied from the message (e.g. a quoted envelope sender) into seeing fake clauses.
        return AuthenticationResults.TryParse(System.Text.Encoding.UTF8.GetBytes(header), out parsed);
    }

    /// <summary>Relaxed alignment: equal, or one is a subdomain of the other.</summary>
    static bool Aligned(string a, string b) =>
        a.Length > 0 && b.Length > 0 && (a == b || a.EndsWith("." + b, StringComparison.Ordinal) || b.EndsWith("." + a, StringComparison.Ordinal));

    /// <summary>Lower-case domain of "user@domain", "@domain" or a bare "domain"; "" when none.</summary>
    static string DomainOf(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = s.Trim().TrimEnd('>', '.');
        return s[(s.LastIndexOf('@') + 1)..].Trim().ToLowerInvariant();
    }

    /// <summary>Only plain "domain", "@domain" or "user@domain" values are used for alignment; anything else is ignored.</summary>
    static bool IsPlainDomainValue(string? v) => v is { Length: > 0 and <= 320 } && PlainDomainRegex().IsMatch(v.Trim());

    [GeneratedRegex(@"^[A-Za-z0-9._+-]*@?[A-Za-z0-9.-]+$")]
    private static partial Regex PlainDomainRegex();
}
