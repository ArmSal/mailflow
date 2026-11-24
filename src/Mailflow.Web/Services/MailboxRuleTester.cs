using Mailflow.Application.Abstractions;
using Mailflow.Application.Processing;
using Mailflow.Domain.Rules;
using Mailflow.Web.Pages.Rules;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Web.Services;

/// <summary>Read-only dry run: fetches recent emails (flags untouched) and reports which ones the saved rule matches.</summary>
public sealed class MailboxRuleTester(
    IAppDbContext db, IMailClient mail, ISecretStore secrets, IPdfTextReader pdfReader, RuleEngine engine, IClock clock) : IRuleTester
{
    public async Task<IReadOnlyList<RuleTestMatch>> TestAsync(int ruleId, int lastEmails, CancellationToken ct)
    {
        var rule = await db.Rules.AsNoTracking().Include(r => r.Conditions).FirstOrDefaultAsync(r => r.Id == ruleId, ct);
        if (rule is null) return [];
        rule.IsActive = true; // test an inactive rule too (untracked copy, never saved)

        var mailboxes = await db.Mailboxes.AsNoTracking()
            .Where(m => m.IsActive && (rule.MailboxId == null || m.Id == rule.MailboxId)).ToListAsync(ct);

        var matches = new List<RuleTestMatch>();
        foreach (var mb in mailboxes)
        {
            var connection = new MailboxConnection(mb.Host, mb.Port, mb.UseSsl, mb.Username, secrets.Unprotect(mb.EncryptedPassword));
            var messages = await mail.FetchSinceAsync(connection, mb.InboxFolder, clock.UtcNow.AddDays(-mb.LookbackDays), ct);
            foreach (var m in messages.OrderByDescending(x => x.ReceivedAt).Take(lastEmails))
            {
                var context = new MatchContext(MessagePipeline.ExtractAddress(m.From), MessagePipeline.ExtractAddress(m.To),
                    m.Subject, m.Body, m.Pdfs.Select(p => p.FileName).ToList(),
                    string.Join("\n", m.Pdfs.Select(p => pdfReader.ReadText(p.Content))), m.Pdfs.Count > 0);
                if (engine.Evaluate([rule], context, mb.Id).MatchedRules.Count > 0)
                    matches.Add(new RuleTestMatch(m.From, m.Subject, m.ReceivedAt));
            }
        }
        return matches.OrderByDescending(x => x.ReceivedAt).Take(lastEmails).ToList();
    }
}
