using Mailflow.Domain.Entities;
using Mailflow.Domain.Enums;

namespace Mailflow.Web.Pages.Rules;

public static class RuleSafety
{
    /// <summary>
    /// True when a rule prints but anyone could trigger it: nothing in its conditions pins the sender
    /// (all conditions must hold, so one sender condition is enough; with "any", every condition must be a sender one).
    /// </summary>
    public static bool PrintsWithoutSenderCondition(Rule rule)
    {
        if (!rule.Actions.Any(a => a.Type == ActionType.Print)) return false;
        static bool IsSender(RuleCondition c) => c.Field is ConditionField.FromAddress or ConditionField.FromDomain;
        return rule.MatchMode == MatchMode.All
            ? !rule.Conditions.Any(IsSender)
            : rule.Conditions.Count == 0 || !rule.Conditions.All(IsSender);
    }
}
