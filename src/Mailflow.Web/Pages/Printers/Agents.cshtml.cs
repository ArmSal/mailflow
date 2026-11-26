using Mailflow.Application.Abstractions;
using Mailflow.Application.Agents;
using Mailflow.Domain.Entities;
using Mailflow.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Web.Pages.Printers;

public class AgentsModel(IAppDbContext db, IClock clock) : PageModel
{
    public const int MinPasswordLength = 12;

    public List<PrintAgent> Items { get; private set; } = [];
    [BindProperty] public string? Name { get; set; }
    /// <summary>The same password is typed into the agent's configuration. Blank = Mailflow generates one.</summary>
    [BindProperty] public string? Password { get; set; }
    /// <summary>Shown once, right after a generated password; only its hash is stored.</summary>
    public string? GeneratedPassword { get; private set; }
    /// <summary>What the page just did, for example "Agent created: Shop".</summary>
    public string? Notice { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var name = (Name ?? "").Trim();
        if (name.Length is 0 or > 100) Error = Loc.T("Le nom est requis (100 caractères maximum).");
        else if (await db.PrintAgents.AnyAsync(a => a.Name.ToLower() == name.ToLower())) Error = Loc.T("Un agent porte déjà ce nom.");
        else if (PasswordError(Password) is { } bad) Error = bad;
        else
        {
            var password = NewOrGenerated(out var generated);
            db.PrintAgents.Add(new PrintAgent { Name = name, TokenHash = AgentJobService.HashToken(password), CreatedAt = clock.UtcNow });
            await db.SaveChangesAsync();
            GeneratedPassword = generated ? password : null;
            Notice = Loc.T("Agent créé :") + " " + name;
            Name = Password = null;
            ModelState.Clear();
        }
        await LoadAsync();
        return Page();
    }

    /// <summary>Gives an existing agent a new password (the agent keeps its name, its printers and its queued jobs).</summary>
    public async Task<IActionResult> OnPostPasswordAsync(int id)
    {
        var agent = await db.PrintAgents.FindAsync(id);
        if (agent is not null)
        {
            if (PasswordError(Password) is { } bad) Error = bad;
            else
            {
                var password = NewOrGenerated(out var generated);
                agent.TokenHash = AgentJobService.HashToken(password);
                await db.SaveChangesAsync();
                GeneratedPassword = generated ? password : null;
                Notice = Loc.T("Mot de passe modifié pour l'agent :") + " " + agent.Name;
                Password = null;
                ModelState.Clear();
            }
        }
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var agent = await db.PrintAgents.FindAsync(id);
        if (agent is not null)
        {
            if (await db.Printers.AnyAsync(p => p.PrintAgentId == id))
                Error = Loc.T("Suppression impossible : une imprimante utilise cet agent.");
            else
            {
                db.PrintJobs.RemoveRange(await db.PrintJobs.Where(j => j.PrintAgentId == id).ToListAsync());
                db.PrintAgents.Remove(agent);
                await db.SaveChangesAsync();
            }
        }
        await LoadAsync();
        return Page();
    }

    private string? PasswordError(string? password)
    {
        var p = (password ?? "").Trim();
        return p.Length > 0 && p.Length < MinPasswordLength
            ? string.Format(Loc.T("Le mot de passe doit contenir au moins {0} caractères."), MinPasswordLength)
            : null;
    }

    private string NewOrGenerated(out bool generated)
    {
        var p = (Password ?? "").Trim();
        generated = p.Length == 0;
        return generated ? AgentJobService.NewToken() : p;
    }

    private async Task LoadAsync() => Items = await db.PrintAgents.OrderBy(a => a.Name).ToListAsync();
}
