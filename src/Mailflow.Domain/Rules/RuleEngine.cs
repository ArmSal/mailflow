using Mailflow.Domain.Entities;
using Mailflow.Domain.Enums;

namespace Mailflow.Domain.Rules;

public sealed record RuleEvaluation(IReadOnlyList<Rule> MatchedRules, IReadOnlyList<RuleAction> Actions);

public sealed class RuleEngine(bool closestKeywordWins = true)
{
    public RuleEvaluation Evaluate(IEnumerable<Rule> rules, MatchContext context, int mailboxId)
    {
        var matched = new List<Rule>();
        foreach (var rule in rules.Where(r => r.IsActive && (r.MailboxId is null || r.MailboxId == mailboxId)).OrderBy(r => r.Priority))
        {
            if (Matches(rule, context))
            {
                matched.Add(rule);
                if (rule.StopProcessing) break;
            }
        }

        if (closestKeywordWins) matched = KeepClosestKeywordRule(matched, context);
        return new RuleEvaluation(matched, matched.SelectMany(r => r.Actions).ToList());
    }

    static bool Matches(Rule rule, MatchContext context)
    {
        if (rule.Conditions.Count == 0) return false;
        return rule.MatchMode == MatchMode.Any
            ? rule.Conditions.Any(c => ConditionEvaluator.Evaluate(c, context))
            : rule.Conditions.All(c => ConditionEvaluator.Evaluate(c, context));
    }

    // Among matched rules that rely on PdfText+Contains, only the one whose keyword is closest to the top stays.
    static List<Rule> KeepClosestKeywordRule(List<Rule> matched, MatchContext context)
    {
        var positions = new Dictionary<Rule, int>();
        foreach (var rule in matched)
        {
            var found = rule.Conditions
                .Where(c => c.Field == ConditionField.PdfText && c.Operator == ConditionOperator.Contains)
                .Select(c => ConditionEvaluator.PdfKeywordPosition(c, context))
                .Where(p => p >= 0).ToList();
            if (found.Count > 0) positions[rule] = found.Min();
        }
        if (positions.Count < 2) return matched;
        var winner = positions.OrderBy(p => p.Value).First().Key; // stable: priority order on ties
        return matched.Where(r => !positions.ContainsKey(r) || r == winner).ToList();
    }
}
