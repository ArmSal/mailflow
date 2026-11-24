using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Mailflow.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Web.Pages.Rules;

public class EditModel(IAppDbContext db, IRuleTester tester) : PageModel
{
    public class RuleInput
    {
        [Required(ErrorMessage = "Le nom est requis."), StringLength(100)]
        public string Name { get; set; } = "";
        public MatchMode MatchMode { get; set; } = MatchMode.All;
        public bool StopProcessing { get; set; }
        public bool IsActive { get; set; } = true;
        public int? MailboxId { get; set; }
        public List<ConditionInput> Conditions { get; set; } = [];
        public List<ActionInput> Actions { get; set; } = [];
    }

    [BindProperty] public RuleInput Input { get; set; } = new();
    public int? RuleId { get; private set; }
    public List<Mailbox> Mailboxes { get; private set; } = [];
    public List<Category> Categories { get; private set; } = [];
    public List<Printer> Printers { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        await LoadListsAsync();
        RuleId = id;
        if (id is null) return Page();
        var r = await db.Rules.Include(x => x.Conditions).Include(x => x.Actions).FirstOrDefaultAsync(x => x.Id == id.Value);
        if (r is null) return NotFound();
        Input = new RuleInput
        {
            Name = r.Name, MatchMode = r.MatchMode, StopProcessing = r.StopProcessing, IsActive = r.IsActive, MailboxId = r.MailboxId,
            Conditions = r.Conditions.OrderBy(c => c.Id).Select(c => new ConditionInput { Field = c.Field, Operator = c.Operator, Value = c.Value, CaseSensitive = c.CaseSensitive }).ToList(),
            Actions = r.Actions.OrderBy(a => a.Id).Select(a => new ActionInput { Type = a.Type, CategoryId = a.CategoryId, PrinterId = a.PrinterId, Copies = a.Copies, ImapFolder = a.ImapFolder }).ToList(),
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        RuleId = id;
        Input.Name = (Input.Name ?? "").Trim();
        Input.Conditions ??= [];
        Input.Actions ??= [];
        if (Input.Name.Length == 0) ModelState.AddModelError("Input.Name", "Le nom est requis.");
        if (!Enum.IsDefined(Input.MatchMode)) ModelState.AddModelError("Input.MatchMode", "Mode invalide.");
        if (Input.MailboxId is int mb && !await db.Mailboxes.AnyAsync(m => m.Id == mb)) ModelState.AddModelError("Input.MailboxId", "Boîte mail invalide.");
        if (Input.Actions.Count == 0) ModelState.AddModelError("", "Ajoutez au moins une action.");

        for (var i = 0; i < Input.Conditions.Count; i++)
        {
            var c = Input.Conditions[i];
            var n = $"Condition {i + 1} : ";
            if (!Enum.IsDefined(c.Field) || !Enum.IsDefined(c.Operator)) { ModelState.AddModelError("", n + "champ ou opérateur invalide."); continue; }
            c.Value = (c.Value ?? "").Trim();
            if (c.Field != ConditionField.HasPdf && c.Value.Length == 0) ModelState.AddModelError("", n + "la valeur est requise.");
            if (c.Value.Length > 500) ModelState.AddModelError("", n + "valeur trop longue.");
            if (c.Operator == ConditionOperator.Regex && !IsValidRegex(c.Value)) ModelState.AddModelError("", n + "expression régulière invalide.");
        }

        for (var i = 0; i < Input.Actions.Count; i++)
        {
            var a = Input.Actions[i];
            var n = $"Action {i + 1} : ";
            if (!Enum.IsDefined(a.Type)) { ModelState.AddModelError("", n + "type invalide."); continue; }
            if (a.Type == ActionType.SaveToCategory && (a.CategoryId is null || !await db.Categories.AnyAsync(x => x.Id == a.CategoryId)))
                ModelState.AddModelError("", n + "choisissez une catégorie.");
            if (a.Type == ActionType.Print && (a.PrinterId is null || !await db.Printers.AnyAsync(x => x.Id == a.PrinterId)))
                ModelState.AddModelError("", n + "choisissez une imprimante.");
            if (a.Type == ActionType.Print && a.Copies is < 1 or > 99) ModelState.AddModelError("", n + "copies : 1 à 99.");
            a.ImapFolder = string.IsNullOrWhiteSpace(a.ImapFolder) ? null : a.ImapFolder.Trim();
            if (a.Type == ActionType.MoveToFolder && a.ImapFolder is null) ModelState.AddModelError("", n + "dossier IMAP requis.");
        }
        if (!ModelState.IsValid) { await LoadListsAsync(); return Page(); }

        Rule? r;
        if (id is not null)
        {
            r = await db.Rules.Include(x => x.Conditions).Include(x => x.Actions).FirstOrDefaultAsync(x => x.Id == id.Value);
            if (r is null) return NotFound();
            db.RuleConditions.RemoveRange(r.Conditions);
            db.RuleActions.RemoveRange(r.Actions);
            r.Conditions = [];
            r.Actions = [];
        }
        else
        {
            var max = await db.Rules.Select(x => (int?)x.Priority).MaxAsync() ?? 0;
            db.Rules.Add(r = new Rule { Priority = max + 1 });
        }
        r.Name = Input.Name;
        r.MatchMode = Input.MatchMode;
        r.StopProcessing = Input.StopProcessing;
        r.IsActive = Input.IsActive;
        r.MailboxId = Input.MailboxId;
        foreach (var c in Input.Conditions)
            r.Conditions.Add(new RuleCondition { Field = c.Field, Operator = c.Operator, Value = c.Value ?? "", CaseSensitive = c.CaseSensitive });
        foreach (var a in Input.Actions)
        {
            var print = a.Type == ActionType.Print;
            r.Actions.Add(new RuleAction
            {
                Type = a.Type,
                CategoryId = a.Type == ActionType.SaveToCategory ? a.CategoryId : null,
                PrinterId = print ? a.PrinterId : null,
                Copies = print ? a.Copies : null,
                ImapFolder = a.Type == ActionType.MoveToFolder ? a.ImapFolder : null,
            });
        }
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    public IActionResult OnPostAddCondition() =>
        Partial("_ConditionRow", new ConditionRowVm(Guid.NewGuid().ToString("N"), new ConditionInput()));

    public async Task<IActionResult> OnPostAddActionAsync() =>
        Partial("_ActionRow", new ActionRowVm(Guid.NewGuid().ToString("N"), new ActionInput { Copies = 1 },
            await db.Categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(),
            await db.Printers.OrderBy(p => p.DisplayName).ToListAsync()));

    public async Task<IActionResult> OnPostTestAsync(int id, CancellationToken ct)
    {
        if (!await db.Rules.AnyAsync(r => r.Id == id, ct)) return NotFound();
        IReadOnlyList<RuleTestMatch>? matches = null;
        string? error = null;
        try { matches = await tester.TestAsync(id, 50, ct); }
        catch (Exception ex) { error = ex.Message; }
        return this.PartialWith("_RuleTestResult", null, ("Matches", matches), ("Error", error));
    }

    private static bool IsValidRegex(string pattern)
    {
        try { _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100)); return true; }
        catch (ArgumentException) { return false; }
    }

    private async Task LoadListsAsync()
    {
        Mailboxes = await db.Mailboxes.OrderBy(m => m.DisplayName).ToListAsync();
        Categories = await db.Categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync();
        Printers = await db.Printers.OrderBy(p => p.DisplayName).ToListAsync();
    }
}
