using Mailflow.Domain.Entities;
using Mailflow.Domain.Enums;
using Mailflow.Web.Localization;

namespace Mailflow.Web.Pages.Rules;

public sealed record RuleTestMatch(string From, string Subject, DateTime ReceivedAt);

/// <summary>Read-only dry run of a saved rule against the last emails. Implemented by the integrator.</summary>
public interface IRuleTester
{
    /// <summary>Returns the emails (among the last <paramref name="lastEmails"/>) that match the rule.</summary>
    Task<IReadOnlyList<RuleTestMatch>> TestAsync(int ruleId, int lastEmails, CancellationToken ct);
}

public class ConditionInput
{
    public ConditionField Field { get; set; }
    public ConditionOperator Operator { get; set; }
    public string? Value { get; set; }
    public bool CaseSensitive { get; set; }
}

public class ActionInput
{
    public ActionType Type { get; set; }
    public int? CategoryId { get; set; }
    public int? PrinterId { get; set; }
    public int? Copies { get; set; }
    public string? ImapFolder { get; set; }
}

public sealed record ConditionRowVm(string Key, ConditionInput C);
public sealed record ActionRowVm(string Key, ActionInput A, IReadOnlyList<Category> Categories, IReadOnlyList<Printer> Printers);
public sealed record RuleListVm(IReadOnlyList<Rule> Rules, IReadOnlyDictionary<int, string> Mailboxes);

public static class RuleLabels
{
    public static IReadOnlyDictionary<ConditionField, string> Fields => new Dictionary<ConditionField, string>
    {
        [ConditionField.FromAddress] = Loc.T("Expéditeur (adresse)"),
        [ConditionField.FromDomain] = Loc.T("Expéditeur (domaine)"),
        [ConditionField.ToAddress] = Loc.T("Destinataire"),
        [ConditionField.Subject] = Loc.T("Objet"),
        [ConditionField.Body] = Loc.T("Corps du message"),
        [ConditionField.AttachmentName] = Loc.T("Nom de la pièce jointe"),
        [ConditionField.PdfText] = Loc.T("Texte du PDF"),
        [ConditionField.HasPdf] = Loc.T("Contient un PDF"),
    };

    public static IReadOnlyDictionary<ConditionOperator, string> Operators => new Dictionary<ConditionOperator, string>
    {
        [ConditionOperator.Contains] = Loc.T("contient"),
        [ConditionOperator.NotContains] = Loc.T("ne contient pas"),
        [ConditionOperator.Equals] = Loc.T("est égal à"),
        [ConditionOperator.StartsWith] = Loc.T("commence par"),
        [ConditionOperator.EndsWith] = Loc.T("se termine par"),
        [ConditionOperator.Regex] = Loc.T("expression régulière"),
    };

    public static IReadOnlyDictionary<ActionType, string> Actions => new Dictionary<ActionType, string>
    {
        [ActionType.SaveToCategory] = Loc.T("Classer dans une catégorie"),
        [ActionType.Print] = Loc.T("Imprimer"),
        [ActionType.MarkAsRead] = Loc.T("Marquer comme lu"),
        [ActionType.MoveToFolder] = Loc.T("Déplacer vers un dossier IMAP"),
        [ActionType.LeaveUnread] = Loc.T("Laisser non lu"),
        [ActionType.Skip] = Loc.T("Ignorer"),
    };
}
