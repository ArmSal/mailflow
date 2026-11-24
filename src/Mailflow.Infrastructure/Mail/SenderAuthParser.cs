using System.Text.RegularExpressions;
using Mailflow.Domain.Enums;

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
    public static SenderVerification Parse(IReadOnlyList<string>? authenticationResultsHeaders, string? fromAddress)
    {
        try
        {
            var fromDomain = DomainOf(fromAddress);
            if (fromDomain.Length == 0 || authenticationResultsHeaders is not { Count: > 0 } || authenticationResultsHeaders[0] is not { } header)
                return SenderVerification.Unknown;

            bool dmarcPass = false, dmarcFail = false, dkimPass = false, dkimFail = false;
            // First segment is the authserv-id; the rest are "method=result prop.name=value ..." clauses.
            foreach (var clause in StripComments(header).Split(';').Skip(1))
            {
                var m = ClauseRegex().Match(clause);
                if (!m.Success) continue;
                var method = m.Groups[1].Value.ToLowerInvariant();
                var result = m.Groups[2].Value.ToLowerInvariant();
                var props = new Dictionary<string, string>();
                foreach (Match p in PropRegex().Matches(m.Groups[3].Value))
                    props[p.Groups[1].Value.ToLowerInvariant()] = p.Groups[2].Value.Trim('"');

                if (method == "dmarc")
                {
                    // header.from, when present, must be the From domain we are judging.
                    if (props.TryGetValue("header.from", out var hf) && !Aligned(DomainOf(hf), fromDomain)) continue;
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

    /// <summary>Removes (possibly nested) parenthesised comments and turns folded whitespace into single spaces.</summary>
    static string StripComments(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        int depth = 0;
        foreach (var c in s)
        {
            if (c == '(') depth++;
            else if (c == ')') { if (depth > 0) depth--; }
            else if (depth == 0) sb.Append(char.IsWhiteSpace(c) ? ' ' : c);
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"^\s*([A-Za-z0-9-]+)\s*=\s*([A-Za-z]+)(.*)$", RegexOptions.Singleline)]
    private static partial Regex ClauseRegex();

    [GeneratedRegex(@"([A-Za-z0-9-]+\.[A-Za-z0-9-]+)\s*=\s*(""[^""]*""|\S+)")]
    private static partial Regex PropRegex();
}
