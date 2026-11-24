using System.Diagnostics;
using Mailflow.Domain.Entities;
using Mailflow.Domain.Enums;
using Mailflow.Domain.Rules;

namespace Mailflow.Domain.Tests;

public class RuleEngineTests
{
    static MatchContext Ctx(string from = "compta@fournisseur.fr", string to = "store@example.com", string subject = "Facture 123",
        string body = "Bonjour, voici la facture", string[]? attachments = null, string pdf = "FACTURE n° 1", bool hasPdf = true) =>
        new(from, to, subject, body, attachments ?? ["facture_123.pdf"], pdf, hasPdf);

    static RuleCondition Cond(ConditionField f, ConditionOperator o, string v, bool cs = false) =>
        new() { Field = f, Operator = o, Value = v, CaseSensitive = cs };

    static Rule R(int priority, params RuleCondition[] conds) =>
        new() { Id = priority, Name = "r" + priority, Priority = priority, Conditions = [.. conds], Actions = [new RuleAction { Type = ActionType.SaveToCategory, CategoryId = priority }] };

    static bool Eval(RuleCondition c, MatchContext? ctx = null) => ConditionEvaluator.Evaluate(c, ctx ?? Ctx());

    [Theory]
    [InlineData(ConditionField.FromAddress, ConditionOperator.Contains, "fournisseur", "zzz")]
    [InlineData(ConditionField.FromAddress, ConditionOperator.Equals, "compta@fournisseur.fr", "compta")]
    [InlineData(ConditionField.FromAddress, ConditionOperator.StartsWith, "compta@", "fournisseur")]
    [InlineData(ConditionField.FromAddress, ConditionOperator.EndsWith, ".fr", "compta")]
    [InlineData(ConditionField.FromAddress, ConditionOperator.Regex, @"^compta@\w+\.fr$", @"^\d+$")]
    [InlineData(ConditionField.FromDomain, ConditionOperator.Contains, "fourn", "zzz")]
    [InlineData(ConditionField.FromDomain, ConditionOperator.Equals, "fournisseur.fr", "fournisseur")]
    [InlineData(ConditionField.FromDomain, ConditionOperator.StartsWith, "fournisseur", ".fr")]
    [InlineData(ConditionField.FromDomain, ConditionOperator.EndsWith, ".fr", "fournisseur")]
    [InlineData(ConditionField.FromDomain, ConditionOperator.Regex, @"^fournisseur\.(fr|com)$", @"\.com$")]
    [InlineData(ConditionField.ToAddress, ConditionOperator.Contains, "store", "zzz")]
    [InlineData(ConditionField.ToAddress, ConditionOperator.Equals, "store@example.com", "store")]
    [InlineData(ConditionField.ToAddress, ConditionOperator.StartsWith, "store", "example")]
    [InlineData(ConditionField.ToAddress, ConditionOperator.EndsWith, "example.com", "store")]
    [InlineData(ConditionField.ToAddress, ConditionOperator.Regex, @"store@", @"^example")]
    [InlineData(ConditionField.Subject, ConditionOperator.Contains, "facture", "devis")]
    [InlineData(ConditionField.Subject, ConditionOperator.Equals, "facture 123", "facture")]
    [InlineData(ConditionField.Subject, ConditionOperator.StartsWith, "facture", "123")]
    [InlineData(ConditionField.Subject, ConditionOperator.EndsWith, "123", "facture")]
    [InlineData(ConditionField.Subject, ConditionOperator.Regex, @"Facture \d+", @"Devis \d+")]
    [InlineData(ConditionField.Body, ConditionOperator.Contains, "voici", "zzz")]
    [InlineData(ConditionField.Body, ConditionOperator.Equals, "bonjour, voici la facture", "bonjour")]
    [InlineData(ConditionField.Body, ConditionOperator.StartsWith, "bonjour", "facture")]
    [InlineData(ConditionField.Body, ConditionOperator.EndsWith, "la facture", "bonjour")]
    [InlineData(ConditionField.Body, ConditionOperator.Regex, @"voici\s+la", @"^voici")]
    [InlineData(ConditionField.AttachmentName, ConditionOperator.Contains, "facture", "devis")]
    [InlineData(ConditionField.AttachmentName, ConditionOperator.Equals, "facture_123.pdf", "facture")]
    [InlineData(ConditionField.AttachmentName, ConditionOperator.StartsWith, "facture_", "123")]
    [InlineData(ConditionField.AttachmentName, ConditionOperator.EndsWith, ".pdf", ".docx")]
    [InlineData(ConditionField.AttachmentName, ConditionOperator.Regex, @"_\d+\.pdf$", @"^\d+")]
    [InlineData(ConditionField.PdfText, ConditionOperator.Contains, "facture", "devis")]
    [InlineData(ConditionField.PdfText, ConditionOperator.Equals, "facture n° 1", "facture")]
    [InlineData(ConditionField.PdfText, ConditionOperator.StartsWith, "facture", "n°")]
    [InlineData(ConditionField.PdfText, ConditionOperator.EndsWith, "n° 1", "facture")]
    [InlineData(ConditionField.PdfText, ConditionOperator.Regex, @"n°\s*\d", @"^\d")]
    [InlineData(ConditionField.HasPdf, ConditionOperator.Equals, "true", "false")]
    [InlineData(ConditionField.HasPdf, ConditionOperator.Contains, "tru", "fal")]
    [InlineData(ConditionField.HasPdf, ConditionOperator.StartsWith, "tr", "fa")]
    [InlineData(ConditionField.HasPdf, ConditionOperator.EndsWith, "ue", "se")]
    [InlineData(ConditionField.HasPdf, ConditionOperator.Regex, "^true$", "^false$")]
    public void Field_operator_matches_and_does_not_match(ConditionField f, ConditionOperator o, string good, string bad)
    {
        Assert.True(Eval(Cond(f, o, good)));
        Assert.False(Eval(Cond(f, o, bad)));
    }

    [Theory]
    [InlineData(ConditionField.FromAddress, "zzz", "fournisseur")]
    [InlineData(ConditionField.FromDomain, "zzz", "fournisseur")]
    [InlineData(ConditionField.ToAddress, "zzz", "store")]
    [InlineData(ConditionField.Subject, "devis", "facture")]
    [InlineData(ConditionField.Body, "zzz", "voici")]
    [InlineData(ConditionField.AttachmentName, "devis", "facture")]
    [InlineData(ConditionField.PdfText, "devis", "facture")]
    [InlineData(ConditionField.HasPdf, "false", "true")]
    public void NotContains_is_the_negation_of_Contains(ConditionField f, string absent, string present)
    {
        Assert.True(Eval(Cond(f, ConditionOperator.NotContains, absent)));
        Assert.False(Eval(Cond(f, ConditionOperator.NotContains, present)));
    }

    [Fact]
    public void HasPdf_false_context() =>
        Assert.True(Eval(Cond(ConditionField.HasPdf, ConditionOperator.Equals, "false"), Ctx(hasPdf: false)));

    [Fact]
    public void NotContains_on_attachments_requires_no_attachment_to_match() =>
        Assert.False(Eval(Cond(ConditionField.AttachmentName, ConditionOperator.NotContains, "facture"), Ctx(attachments: ["a.pdf", "facture.pdf"])));

    [Fact]
    public void Accents_and_case_are_ignored_by_default()
    {
        Assert.True(Eval(Cond(ConditionField.Subject, ConditionOperator.Contains, "FACTURÉ"), Ctx(subject: "facture")));
        Assert.True(Eval(Cond(ConditionField.Subject, ConditionOperator.Equals, "facture"), Ctx(subject: "FACTURÉ")));
        Assert.True(Eval(Cond(ConditionField.Subject, ConditionOperator.Regex, "facturé"), Ctx(subject: "Facture")));
    }

    [Fact]
    public void CaseSensitive_flag_is_respected()
    {
        Assert.False(Eval(Cond(ConditionField.Subject, ConditionOperator.Contains, "facture", cs: true), Ctx(subject: "Facture 1")));
        Assert.True(Eval(Cond(ConditionField.Subject, ConditionOperator.Contains, "Facture", cs: true), Ctx(subject: "Facture 1")));
        Assert.False(Eval(Cond(ConditionField.Subject, ConditionOperator.Regex, "facture", cs: true), Ctx(subject: "Facture 1")));
    }

    [Fact]
    public void Regex_timeout_is_false_and_fast()
    {
        var evil = Cond(ConditionField.Body, ConditionOperator.Regex, "^(a+)+$");
        var sw = Stopwatch.StartNew();
        var result = Eval(evil, Ctx(body: new string('a', 40) + "!"));
        Assert.False(result);
        Assert.True(sw.ElapsedMilliseconds < 2000);
    }

    [Fact]
    public void Invalid_regex_is_false() =>
        Assert.False(Eval(Cond(ConditionField.Body, ConditionOperator.Regex, "(unclosed")));

    [Fact]
    public void MatchMode_All_and_Any()
    {
        var yes = Cond(ConditionField.Subject, ConditionOperator.Contains, "facture");
        var no = Cond(ConditionField.Subject, ConditionOperator.Contains, "devis");
        var engine = new RuleEngine();
        var all = R(1, yes, no); all.MatchMode = MatchMode.All;
        var any = R(2, yes, no); any.MatchMode = MatchMode.Any;
        Assert.Empty(engine.Evaluate([all], Ctx(), 1).MatchedRules);
        Assert.Single(engine.Evaluate([any], Ctx(), 1).MatchedRules);
    }

    [Fact]
    public void Rules_run_by_priority_ascending_and_actions_are_merged()
    {
        var c = Cond(ConditionField.Subject, ConditionOperator.Contains, "facture");
        var result = new RuleEngine().Evaluate([R(5, c), R(1, c), R(3, c)], Ctx(), 1);
        Assert.Equal([1, 3, 5], result.MatchedRules.Select(r => r.Priority));
        Assert.Equal([1, 3, 5], result.Actions.Select(a => a.CategoryId!.Value));
    }

    [Fact]
    public void StopProcessing_stops_after_that_rule()
    {
        var c = Cond(ConditionField.Subject, ConditionOperator.Contains, "facture");
        var stop = R(2, c); stop.StopProcessing = true;
        var result = new RuleEngine().Evaluate([R(1, c), stop, R(3, c)], Ctx(), 1);
        Assert.Equal([1, 2], result.MatchedRules.Select(r => r.Priority));
    }

    [Fact]
    public void StopProcessing_on_a_non_matching_rule_does_not_stop()
    {
        var c = Cond(ConditionField.Subject, ConditionOperator.Contains, "facture");
        var stop = R(1, Cond(ConditionField.Subject, ConditionOperator.Contains, "devis")); stop.StopProcessing = true;
        Assert.Single(new RuleEngine().Evaluate([stop, R(2, c)], Ctx(), 1).MatchedRules);
    }

    [Fact]
    public void Inactive_and_other_mailbox_rules_are_skipped()
    {
        var c = Cond(ConditionField.Subject, ConditionOperator.Contains, "facture");
        var inactive = R(1, c); inactive.IsActive = false;
        var other = R(2, c); other.MailboxId = 9;
        var mine = R(3, c); mine.MailboxId = 1;
        var all = R(4, c);
        var result = new RuleEngine().Evaluate([inactive, other, mine, all], Ctx(), 1);
        Assert.Equal([3, 4], result.MatchedRules.Select(r => r.Priority));
    }

    [Fact]
    public void Closest_pdf_keyword_wins()
    {
        var facture = R(1, Cond(ConditionField.PdfText, ConditionOperator.Contains, "facture"));
        var bl = R(2, Cond(ConditionField.PdfText, ConditionOperator.Contains, "bon de livraison"));
        var other = R(3, Cond(ConditionField.Subject, ConditionOperator.Contains, "facture"));

        var r1 = new RuleEngine().Evaluate([bl, facture, other], Ctx(pdf: "FACTURE 12 ... voir bon de livraison n° 7"), 1);
        Assert.Equal([1, 3], r1.MatchedRules.Select(r => r.Priority));

        var r2 = new RuleEngine().Evaluate([bl, facture, other], Ctx(pdf: "Bon de livraison 7 ... facture"), 1);
        Assert.Equal([2, 3], r2.MatchedRules.Select(r => r.Priority));
    }

    [Fact]
    public void Closest_keyword_can_be_disabled()
    {
        var facture = R(1, Cond(ConditionField.PdfText, ConditionOperator.Contains, "facture"));
        var bl = R(2, Cond(ConditionField.PdfText, ConditionOperator.Contains, "bon de livraison"));
        var result = new RuleEngine(closestKeywordWins: false).Evaluate([facture, bl], Ctx(pdf: "facture ... bon de livraison"), 1);
        Assert.Equal(2, result.MatchedRules.Count);
    }
}
