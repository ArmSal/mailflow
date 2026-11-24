using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Mailflow.Domain.Enums;
using Mailflow.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Application.Processing;

public sealed record MessageOutcome(bool AlreadyProcessed, int DocumentsSaved, int Printed, string? Warning = null);

public sealed class MessagePipeline(
    IAppDbContext db, IMailClient mail, IPdfTextReader pdfReader, IDocumentStore store,
    IPrinterService printerService, IClock clock, RuleEngine ruleEngine)
{
    public const string DefaultCategoryName = "A trier";

    /// <summary>Throws when saving fails: nothing is recorded and the mailbox is untouched, so the next run retries.</summary>
    public async Task<MessageOutcome> ProcessAsync(Mailbox mailbox, MailboxConnection connection, MailMessage message, CancellationToken ct)
    {
        var key = string.IsNullOrWhiteSpace(message.MessageId) ? $"uid:{message.Uid}:{message.ReceivedAt:O}" : message.MessageId;
        if (await db.ProcessedMessages.AnyAsync(p => p.MailboxId == mailbox.Id && p.MessageId == key, ct))
            return new MessageOutcome(true, 0, 0);

        var rules = await db.Rules.Include(r => r.Conditions).Include(r => r.Actions).ToListAsync(ct);
        var categories = await db.Categories.ToListAsync(ct);
        var printers = await db.Printers.ToListAsync(ct);
        var allowed = await db.AllowedSenders.ToListAsync(ct);
        var maxAge = await MaxAgeHoursAsync(ct);

        var senderMode = PrintPolicy.ParseMode((await db.Settings.FirstOrDefaultAsync(x => x.Key == "PrintSenderCheck", ct))?.Value);
        var senderBlocked = PrintPolicy.SenderBlocksPrint(senderMode, message.SenderVerification);

        var from = ExtractAddress(message.From);
        var context = new MatchContext(from, ExtractRecipient(message.To), message.Subject, message.Body,
            message.Pdfs.Select(p => p.FileName).ToList(), ReadPdfText(message), message.Pdfs.Count > 0);
        var evaluation = ruleEngine.Evaluate(rules, context, mailbox.Id);

        var actions = evaluation.Actions;
        var skip = actions.Any(a => a.Type == ActionType.Skip);
        var categoryId = actions.FirstOrDefault(a => a.Type == ActionType.SaveToCategory && a.CategoryId != null)?.CategoryId;
        var category = categories.FirstOrDefault(c => c.Id == categoryId)
            ?? categories.FirstOrDefault(c => c.Name == DefaultCategoryName)
            ?? throw new InvalidOperationException($"Default category '{DefaultCategoryName}' is missing.");
        var printAction = actions.FirstOrDefault(a => a.Type == ActionType.Print);
        var printer = printAction?.PrinterId is int pid ? printers.FirstOrDefault(p => p.Id == pid) : null;
        // No rule matched = default rule: category "A trier", no print, leave unread.
        var mailboxAction = evaluation.MatchedRules.Count == 0
            ? new RuleAction { Type = ActionType.LeaveUnread }
            : actions.FirstOrDefault(a => a.Type is ActionType.MarkAsRead or ActionType.MoveToFolder or ActionType.LeaveUnread);
        var now = clock.UtcNow;
        var print = !skip && PrintPolicy.Allows(printAction != null, from, allowed, mailbox.StoreId, message.ReceivedAt, now, maxAge, printer);

        var tracked = new List<object>();
        var dc = db as DbContext;
        await using var tx = dc is null ? null : await dc.Database.BeginTransactionAsync(ct);
        int saved = 0, printed = 0;
        try
        {
            var processed = new ProcessedMessage
            {
                MailboxId = mailbox.Id, MessageId = key, ImapUid = message.Uid, ReceivedAt = message.ReceivedAt,
                From = message.From, SenderVerification = message.SenderVerification, Subject = message.Subject, MatchedRuleId = evaluation.MatchedRules.FirstOrDefault()?.Id,
                Status = skip ? MessageStatus.Skipped : MessageStatus.Processed, ProcessedAt = now
            };
            db.ProcessedMessages.Add(processed);
            tracked.Add(processed);
            await db.SaveChangesAsync(ct);

            if (!skip)
                foreach (var pdf in message.Pdfs)
                {
                    var stored = await store.SaveAsync(pdf.Content, category.LocalSubfolder, DomainOf(from), pdf.FileName, message.ReceivedAt, ct);
                    if (stored.WasDuplicate && await db.Documents.AnyAsync(d => d.Sha256 == stored.Sha256, ct)) continue;

                    var doc = new Document
                    {
                        ProcessedMessageId = processed.Id, Sha256 = stored.Sha256, OriginalName = pdf.FileName,
                        StoredPath = stored.Path, CategoryId = category.Id
                    };
                    if (print && senderBlocked)
                    {
                        doc.PrinterId = printer!.Id;
                        doc.PrintError = PrintPolicy.SenderBlockedMessage;
                    }
                    else if (print)
                    {
                        try
                        {
                            await printerService.PrintAsync(stored.Path, printer!.WindowsName, printAction!.Copies ?? printer.DefaultCopies, printer.Duplex, ct);
                            doc.PrintedAt = clock.UtcNow;
                            doc.PrinterId = printer.Id;
                            printed++;
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            doc.PrinterId = printer!.Id;
                            doc.PrintError = ex.Message;
                        }
                    }
                    db.Documents.Add(doc);
                    tracked.Add(doc);
                    saved++;
                }
            await db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync(CancellationToken.None);
            if (dc != null) foreach (var o in tracked) dc.Entry(o).State = EntityState.Detached;
            throw;
        }

        // Mailbox action only after everything above was saved and committed.
        string? warning = null;
        if (!skip)
        {
            try { await ApplyMailboxActionAsync(mailbox, connection, message, mailboxAction, category, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { warning = $"Mailbox action failed for '{message.Subject}': {ex.Message}"; }
        }
        return new MessageOutcome(false, saved, printed, warning);
    }

    async Task ApplyMailboxActionAsync(Mailbox mailbox, MailboxConnection connection, MailMessage message, RuleAction? action, Category category, CancellationToken ct)
    {
        var kind = action?.Type switch
        {
            ActionType.MarkAsRead => AfterProcessing.MarkRead,
            ActionType.MoveToFolder => AfterProcessing.Move,
            ActionType.LeaveUnread => AfterProcessing.Leave,
            _ => mailbox.DefaultAfterProcessing
        };
        if (kind == AfterProcessing.MarkRead)
            await mail.MarkAsReadAsync(connection, mailbox.InboxFolder, message.Uid, ct);
        else if (kind == AfterProcessing.Move)
        {
            var folder = action?.ImapFolder ?? category.ImapFolder;
            if (!string.IsNullOrWhiteSpace(folder))
                await mail.MoveAsync(connection, mailbox.InboxFolder, message.Uid, folder, ct);
        }
    }

    string ReadPdfText(MailMessage message)
    {
        var parts = new List<string>();
        foreach (var pdf in message.Pdfs)
        {
            try { parts.Add(pdfReader.ReadText(pdf.Content)); }
            catch (Exception) { /* unreadable PDF: no text, rules fall back to other fields */ }
        }
        return string.Join("\n", parts);
    }

    async Task<int> MaxAgeHoursAsync(CancellationToken ct)
    {
        var s = await db.Settings.FirstOrDefaultAsync(x => x.Key == "PrintMaxAgeHours", ct);
        return s != null && int.TryParse(s.Value, out var h) ? h : PrintPolicy.DefaultMaxAgeHours;
    }

    /// <summary>Lenient parsing for the To field (it may list several recipients); never used for security decisions.</summary>
    public static string ExtractRecipient(string s)
    {
        var lt = s.LastIndexOf('<');
        var gt = s.LastIndexOf('>');
        return (lt >= 0 && gt > lt ? s[(lt + 1)..gt] : s).Trim();
    }

    /// <summary>
    /// Returns the bare address of a "Name &lt;addr&gt;" or "addr" sender, or "" when the text is ambiguous or
    /// malformed, so that nothing (allow-list, rules) can match a half-parsed address.
    /// </summary>
    public static string ExtractAddress(string s)
    {
        s = s.Trim();
        var candidate = s;
        if (s.Contains('<') || s.Contains('>'))
        {
            var lt = s.LastIndexOf('<');
            if (lt < 0 || !s.EndsWith('>')) return "";
            candidate = s[(lt + 1)..^1].Trim();
        }
        return candidate.Length > 0
            && candidate.Count(c => c == '@') == 1
            && candidate.IndexOfAny(['<', '>', ' ', '\t', '\r', '\n']) < 0
            ? candidate : "";
    }

    static string DomainOf(string address)
    {
        var at = address.LastIndexOf('@');
        return at >= 0 ? address[(at + 1)..] : address;
    }
}
