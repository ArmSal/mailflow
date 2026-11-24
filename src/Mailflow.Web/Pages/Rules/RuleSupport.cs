using Mailflow.Domain.Entities;
using Mailflow.Domain.Enums;

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
    public static readonly IReadOnlyDictionary<ConditionField, string> Fields = new Dictionary<ConditionField, string>
    {
        [ConditionField.FromAddress] = "Expéditeur (adresse)",
        [ConditionField.FromDomain] = "Expéditeur (domaine)",
        [ConditionField.ToAddress] = "Destinataire",
        [ConditionField.Subject] = "Objet",
        [ConditionField.Body] = "Corps du message",
        [ConditionField.AttachmentName] = "Nom de la pièce jointe",
        [ConditionField.PdfText] = "Texte du PDF",
        [ConditionField.HasPdf] = "Contient un PDF",
    };

    public static readonly IReadOnlyDictionary<ConditionOperator, string> Operators = new Dictionary<ConditionOperator, string>
    {
        [ConditionOperator.Contains] = "contient",
        [ConditionOperator.NotContains] = "ne contient pas",
        [ConditionOperator.Equals] = "est égal à",
        [ConditionOperator.StartsWith] = "commence par",
        [ConditionOperator.EndsWith] = "se termine par",
        [ConditionOperator.Regex] = "expression régulière",
    };

    public static readonly IReadOnlyDictionary<ActionType, string> Actions = new Dictionary<ActionType, string>
    {
        [ActionType.SaveToCategory] = "Classer dans une catégorie",
        [ActionType.Print] = "Imprimer",
        [ActionType.MarkAsRead] = "Marquer comme lu",
        [ActionType.MoveToFolder] = "Déplacer vers un dossier IMAP",
        [ActionType.LeaveUnread] = "Laisser non lu",
        [ActionType.Skip] = "Ignorer",
    };
}
