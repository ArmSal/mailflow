using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Mailflow.Domain.Entities;
using Mailflow.Domain.Enums;

namespace Mailflow.Domain.Rules;

public static class ConditionEvaluator
{
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public static bool Evaluate(RuleCondition condition, MatchContext context)
    {
        var values = ValuesOf(condition.Field, context);
        var cs = condition.CaseSensitive;
        if (condition.Operator == ConditionOperator.NotContains)
            return !values.Any(v => Test(ConditionOperator.Contains, v, condition.Value, cs));
        return values.Any(v => Test(condition.Operator, v, condition.Value, cs));
    }

    /// <summary>Index of the condition value in the normalized PDF text, or -1.</summary>
    public static int PdfKeywordPosition(RuleCondition condition, MatchContext context) =>
        Normalize(context.PdfText, condition.CaseSensitive).IndexOf(Normalize(condition.Value, condition.CaseSensitive), StringComparison.Ordinal);

    static IEnumerable<string> ValuesOf(ConditionField field, MatchContext c) => field switch
    {
        ConditionField.FromAddress => [c.FromAddress],
        ConditionField.FromDomain => [c.FromDomain],
        ConditionField.ToAddress => [c.ToAddress],
        ConditionField.Subject => [c.Subject],
        ConditionField.Body => [c.Body],
        ConditionField.AttachmentName => c.AttachmentNames,
        ConditionField.PdfText => [c.PdfText],
        ConditionField.HasPdf => [c.HasPdf ? "true" : "false"],
        _ => []
    };

    static bool Test(ConditionOperator op, string text, string value, bool caseSensitive)
    {
        var t = Normalize(text, caseSensitive);
        var v = Normalize(value, caseSensitive);
        switch (op)
        {
            case ConditionOperator.Contains: return t.Contains(v, StringComparison.Ordinal);
            case ConditionOperator.Equals: return t == v;
            case ConditionOperator.StartsWith: return t.StartsWith(v, StringComparison.Ordinal);
            case ConditionOperator.EndsWith: return t.EndsWith(v, StringComparison.Ordinal);
            case ConditionOperator.Regex:
                try
                {
                    var opts = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                    return new Regex(StripAccents(value), opts, RegexTimeout).IsMatch(StripAccents(text));
                }
                catch (RegexMatchTimeoutException) { return false; }
                catch (ArgumentException) { return false; }
            default: return false;
        }
    }

    static string Normalize(string s, bool caseSensitive)
    {
        var r = StripAccents(s);
        return caseSensitive ? r : r.ToLowerInvariant();
    }

    static string StripAccents(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
