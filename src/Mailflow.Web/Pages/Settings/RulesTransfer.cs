using System.Text.Json;
using System.Text.RegularExpressions;
using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Mailflow.Domain.Enums;
using Mailflow.Web.Localization;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Web.Pages.Settings;

/// <summary>
/// Export / import of the rules and settings as JSON. Secrets (mailbox passwords, OAuth tokens, agent tokens) are never exported.
/// Mailboxes and printers are referenced by e-mail address / display name and are not created by an import.
/// </summary>
public static class RulesTransfer
{
    public const string Format = "mailflow-rules";
    public const int Version = 1;
    public const int MaxBytes = 1_000_000;
    /// <summary>The only settings that travel. The documents folder is specific to a PC and stays local.</summary>
    private static readonly string[] SettingKeys = [SettingsRules.PatternKey, SettingsRules.SenderCheckKey, SettingsRules.PrintEmailBodyKey];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public sealed record FileDto(string Format, int Version, Dictionary<string, string> Settings, List<CategoryDto> Categories, List<RuleDto> Rules);
    public sealed record CategoryDto(string Name, string LocalSubfolder, string? ImapFolder, string Color, int SortOrder);
    public sealed record RuleDto(string Name, int Priority, bool IsActive, string MatchMode, bool StopProcessing, string? Mailbox, List<ConditionDto> Conditions, List<ActionDto> Actions);
    public sealed record ConditionDto(string Field, string Operator, string Value, bool CaseSensitive);
    public sealed record ActionDto(string Type, string? Category, string? Printer, int? Copies, string? ImapFolder);

    public sealed record Analysis(int Categories, int Rules, List<string> SkippedNoMailbox, List<string> DeactivatedNoPrinter);

    public static async Task<string> ExportAsync(IAppDbContext db, CancellationToken ct)
    {
        var settings = await SettingsRules.LoadAsync(db, ct);
        var categories = await db.Categories.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);
        var mailboxes = await db.Mailboxes.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.Email, ct);
        var printers = await db.Printers.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);
        var rules = await db.Rules.AsNoTracking().Include(r => r.Conditions).Include(r => r.Actions).OrderBy(r => r.Priority).ThenBy(r => r.Id).ToListAsync(ct);
        string? CategoryName(int? id) => categories.FirstOrDefault(c => c.Id == id)?.Name;
        var dto = new FileDto(Format, Version,
            SettingKeys.Where(settings.ContainsKey).ToDictionary(k => k, k => settings[k]),
            categories.Select(c => new CategoryDto(c.Name, c.LocalSubfolder, c.ImapFolder, c.Color, c.SortOrder)).ToList(),
            rules.Select(r => new RuleDto(r.Name, r.Priority, r.IsActive, r.MatchMode.ToString(), r.StopProcessing,
                r.MailboxId is int mid ? mailboxes.GetValueOrDefault(mid) : null,
                r.Conditions.OrderBy(c => c.Id).Select(c => new ConditionDto(c.Field.ToString(), c.Operator.ToString(), c.Value, c.CaseSensitive)).ToList(),
                r.Actions.OrderBy(a => a.Id).Select(a => new ActionDto(a.Type.ToString(), CategoryName(a.CategoryId),
                    a.PrinterId is int pid ? printers.GetValueOrDefault(pid) : null, a.Copies, a.ImapFolder)).ToList())).ToList());
        return JsonSerializer.Serialize(dto, Json);
    }

    /// <summary>Parses and validates the file. Returns the first problem found (translated), or null.</summary>
    public static (FileDto? File, string? Error) Parse(string json)
    {
        var invalid = Loc.T("Fichier invalide : ce n'est pas un export Mailflow valide.");
        if (json.Length > MaxBytes) return (null, Loc.T("Fichier trop volumineux."));
        FileDto? f;
        try { f = JsonSerializer.Deserialize<FileDto>(json, Json); }
        catch (JsonException) { return (null, invalid); }
        if (f is null || f.Format != Format) return (null, invalid);
        if (f.Version != Version) return (null, Loc.T("Version de fichier non prise en charge : {0}.", f.Version));
        if (f.Settings is null || f.Categories is null || f.Rules is null) return (null, invalid);

        foreach (var (k, v) in f.Settings)
        {
            if (!SettingKeys.Contains(k)) return (null, Loc.T("Paramètre inconnu : {0}.", k));
            var e = k == SettingsRules.PatternKey ? SettingsRules.ValidatePattern(v)
                : k == SettingsRules.SenderCheckKey ? SettingsRules.ValidateSenderCheck(v)
                : v is "true" or "false" ? null : Loc.T("Valeur invalide pour le paramètre {0}.", k);
            if (e is not null) return (null, e);
        }
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in f.Categories)
        {
            if (string.IsNullOrWhiteSpace(c?.Name) || string.IsNullOrWhiteSpace(c.LocalSubfolder) || string.IsNullOrWhiteSpace(c.Color)
                || c.Name.Length > 200 || c.LocalSubfolder.Length > 200 || c.Color.Length > 20 || c.ImapFolder?.Length > 255
                || c.LocalSubfolder.Split('/', '\\').Any(p => p == "..") || Path.IsPathRooted(c.LocalSubfolder) || c.LocalSubfolder.Contains(':'))
                return (null, Loc.T("Catégorie invalide : {0}.", c?.Name ?? ""));
            if (!names.Add(c.Name.Trim())) return (null, Loc.T("Catégorie en double : {0}.", c.Name));
        }
        var ruleNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in f.Rules)
        {
            if (r is null || string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 200 || r.Conditions is null || r.Actions is null || !IsEnum<MatchMode>(r.MatchMode))
                return (null, Loc.T("Règle invalide : {0}.", r?.Name ?? ""));
            if (!ruleNames.Add(r.Name)) return (null, Loc.T("Règle en double : {0}.", r.Name));
            foreach (var c in r.Conditions)
                if (c is null || c.Value is null || !IsEnum<ConditionField>(c.Field) || !IsEnum<ConditionOperator>(c.Operator)
                    || (Enum.Parse<ConditionOperator>(c.Operator) == ConditionOperator.Regex && !IsValidRegex(c.Value)))
                    return (null, Loc.T("Règle « {0} » : condition invalide.", r.Name));
            foreach (var a in r.Actions)
            {
                if (a is null || !IsEnum<ActionType>(a.Type) || a.Copies is < 1 or > 99 || a.ImapFolder?.Length > 255)
                    return (null, Loc.T("Règle « {0} » : action invalide.", r.Name));
                if (a.Category is not null && !names.Contains(a.Category.Trim()))
                    return (null, Loc.T("Règle « {0} » : catégorie inconnue « {1} ».", r.Name, a.Category));
            }
        }
        return (f, null);
    }

    public static async Task<Analysis> AnalyzeAsync(IAppDbContext db, FileDto f, CancellationToken ct)
    {
        var mailboxes = (await db.Mailboxes.AsNoTracking().Select(m => m.Email).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var printers = (await db.Printers.AsNoTracking().Select(p => p.DisplayName).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var skipped = f.Rules.Where(r => r.Mailbox is not null && !mailboxes.Contains(r.Mailbox)).Select(r => r.Name).ToList();
        var deactivated = f.Rules.Where(r => !skipped.Contains(r.Name) && r.Actions.Any(a => a.Printer is not null && !printers.Contains(a.Printer))).Select(r => r.Name).ToList();
        return new Analysis(f.Categories.Count, f.Rules.Count - skipped.Count, skipped, deactivated);
    }

    /// <summary>Replaces rules, categories and the exported settings in one database transaction: all or nothing.</summary>
    public static async Task ApplyAsync(IAppDbContext db, FileDto f, CancellationToken ct)
    {
        var a = await AnalyzeAsync(db, f, ct);
        var dc = db as DbContext;
        await using var tx = dc is null ? null : await dc.Database.BeginTransactionAsync(ct);
        try
        {
            var mailboxes = await db.Mailboxes.AsNoTracking().ToListAsync(ct);
            var printers = await db.Printers.AsNoTracking().ToListAsync(ct);
            var cats = await db.Categories.ToListAsync(ct);
            var usedCategoryIds = (await db.Documents.Select(d => d.CategoryId).Distinct().ToListAsync(ct)).ToHashSet();

            db.Rules.RemoveRange(await db.Rules.Include(r => r.Conditions).Include(r => r.Actions).ToListAsync(ct));

            var byName = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in f.Categories)
            {
                var c = cats.FirstOrDefault(x => string.Equals(x.Name, d.Name.Trim(), StringComparison.OrdinalIgnoreCase));
                if (c is null) db.Categories.Add(c = new Category());
                c.Name = d.Name.Trim(); c.LocalSubfolder = d.LocalSubfolder; c.ImapFolder = d.ImapFolder; c.Color = d.Color; c.SortOrder = d.SortOrder;
                byName[c.Name] = c;
            }
            // Categories absent from the file go, unless documents are filed in them.
            db.Categories.RemoveRange(cats.Where(c => !byName.ContainsValue(c) && !usedCategoryIds.Contains(c.Id)));
            await db.SaveChangesAsync(ct); // gives new categories their ids

            foreach (var d in f.Rules.Where(r => !a.SkippedNoMailbox.Contains(r.Name)))
            {
                db.Rules.Add(new Rule
                {
                    Name = d.Name, Priority = d.Priority, StopProcessing = d.StopProcessing,
                    IsActive = d.IsActive && !a.DeactivatedNoPrinter.Contains(d.Name),
                    MatchMode = Enum.Parse<MatchMode>(d.MatchMode),
                    MailboxId = d.Mailbox is null ? null : mailboxes.First(m => string.Equals(m.Email, d.Mailbox, StringComparison.OrdinalIgnoreCase)).Id,
                    Conditions = d.Conditions.Select(c => new RuleCondition { Field = Enum.Parse<ConditionField>(c.Field), Operator = Enum.Parse<ConditionOperator>(c.Operator), Value = c.Value, CaseSensitive = c.CaseSensitive }).ToList(),
                    Actions = d.Actions.Select(x => new RuleAction
                    {
                        Type = Enum.Parse<ActionType>(x.Type), Copies = x.Copies, ImapFolder = x.ImapFolder,
                        PrinterId = x.Printer is null ? null : printers.FirstOrDefault(p => string.Equals(p.DisplayName, x.Printer, StringComparison.OrdinalIgnoreCase))?.Id,
                        CategoryId = x.Category is null ? null : byName[x.Category.Trim()].Id,
                    }).ToList(),
                });
            }
            foreach (var (k, v) in f.Settings) await SettingsRules.SaveAsync(db, k, v, ct);
            await db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static bool IsEnum<T>(string? v) where T : struct, Enum
        => Enum.TryParse<T>(v, out var e) && Enum.IsDefined(e) && !int.TryParse(v, out _);

    private static bool IsValidRegex(string pattern)
    {
        try { _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100)); return true; }
        catch (ArgumentException) { return false; }
    }
}
