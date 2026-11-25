using Mailflow.Domain.Entities;
using Mailflow.Domain.Enums;
using Mailflow.Web.Pages.Rules;

namespace Mailflow.Integration.Tests.Pages;

public class RuleSafetyTests
{
    private static Rule Make(MatchMode mode, ActionType action, params ConditionField[] fields) => new()
    {
        MatchMode = mode,
        Conditions = fields.Select(f => new RuleCondition { Field = f, Operator = ConditionOperator.Equals, Value = "x" }).ToList(),
        Actions = [new RuleAction { Type = action }],
    };

    [Fact] public void Print_rule_without_any_sender_condition_is_flagged() =>
        Assert.True(RuleSafety.PrintsWithoutSenderCondition(Make(MatchMode.All, ActionType.Print, ConditionField.Subject)));

    [Fact] public void Print_rule_without_conditions_is_flagged() =>
        Assert.True(RuleSafety.PrintsWithoutSenderCondition(Make(MatchMode.All, ActionType.Print)));

    [Theory]
    [InlineData(ConditionField.FromAddress)]
    [InlineData(ConditionField.FromDomain)]
    public void Match_all_with_a_sender_condition_is_fine(ConditionField sender) =>
        Assert.False(RuleSafety.PrintsWithoutSenderCondition(Make(MatchMode.All, ActionType.Print, ConditionField.Subject, sender)));

    [Fact] public void Match_any_lets_the_other_condition_alone_trigger_the_print() =>
        Assert.True(RuleSafety.PrintsWithoutSenderCondition(Make(MatchMode.Any, ActionType.Print, ConditionField.FromAddress, ConditionField.Subject)));

    [Fact] public void Match_any_with_only_sender_conditions_is_fine() =>
        Assert.False(RuleSafety.PrintsWithoutSenderCondition(Make(MatchMode.Any, ActionType.Print, ConditionField.FromAddress, ConditionField.FromDomain)));

    [Fact] public void Rules_that_do_not_print_are_never_flagged() =>
        Assert.False(RuleSafety.PrintsWithoutSenderCondition(Make(MatchMode.All, ActionType.SaveToCategory, ConditionField.Subject)));
}
