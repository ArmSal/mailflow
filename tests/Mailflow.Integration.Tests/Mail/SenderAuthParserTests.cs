using Mailflow.Domain.Enums;
using Mailflow.Infrastructure.Mail;

namespace Mailflow.Integration.Tests.Mail;

public class SenderAuthParserTests
{
    const string From = "billing@supplier.example";
    static SenderVerification P(string? header, string? from = From) =>
        SenderAuthParser.Parse(header is null ? [] : [header], from);

    const string GmailLike = "mx.example.com; dkim=pass header.i=@supplier.example header.s=sel header.b=abc; spf=pass (mx.example.com: domain of billing@supplier.example designates 1.2.3.4 as permitted sender) smtp.mailfrom=billing@supplier.example; dmarc=pass (p=REJECT sp=REJECT dis=NONE) header.from=supplier.example";
    const string OvhLike = "mail.example.net;\r\n\tdkim=pass (2048-bit key; unprotected) header.d=supplier.example header.i=@supplier.example header.b=\"xyz\";\r\n\tdkim-atps=neutral;\r\n\tspf=pass smtp.mailfrom=supplier.example";
    const string IcloudLike = "icloud.example.com; dkim=pass (2048-bit key) header.d=supplier.example header.i=@supplier.example header.b=q1; dmarc=pass header.from=supplier.example; spf=pass smtp.mailfrom=billing@supplier.example";

    [Fact] public void Gmail_like_passes() => Assert.Equal(SenderVerification.Pass, P(GmailLike));
    [Fact] public void Ovh_like_folded_dkim_passes() => Assert.Equal(SenderVerification.Pass, P(OvhLike));
    [Fact] public void Icloud_like_passes() => Assert.Equal(SenderVerification.Pass, P(IcloudLike));
    [Fact] public void Odd_casing_passes() => Assert.Equal(SenderVerification.Pass, P("MX.Example.COM; DKIM=PASS Header.D=SUPPLIER.Example", "Billing@Supplier.EXAMPLE"));
    [Fact] public void Spaces_around_equals_and_folding_pass() => Assert.Equal(SenderVerification.Pass, P("mx;\r\n   dmarc =\r\n  pass   header.from = supplier.example"));
    [Fact] public void Dkim_pass_with_signing_subdomain_is_aligned() => Assert.Equal(SenderVerification.Pass, P("mx; dkim=pass header.d=mail.supplier.example"));
    [Fact] public void Dkim_pass_for_parent_domain_of_a_subdomain_from_is_aligned() => Assert.Equal(SenderVerification.Pass, P("mx; dkim=pass header.d=supplier.example", "a@news.supplier.example"));
    [Fact] public void Dkim_uses_header_i_user_form_when_no_header_d() => Assert.Equal(SenderVerification.Pass, P("mx; dkim=pass header.i=billing@supplier.example"));
    [Fact] public void Dmarc_pass_alone_passes() => Assert.Equal(SenderVerification.Pass, P("mx; dmarc=pass header.from=supplier.example"));

    [Fact] public void Spf_pass_alone_is_not_enough() => Assert.Equal(SenderVerification.Unknown, P("mx; spf=pass smtp.mailfrom=supplier.example"));
    [Fact] public void Dkim_pass_for_unrelated_domain_is_unknown() => Assert.Equal(SenderVerification.Unknown, P("mx; dkim=pass header.d=attacker.example; spf=pass"));
    [Fact] public void Look_alike_suffix_is_not_a_subdomain() => Assert.Equal(SenderVerification.Unknown, P("mx; dkim=pass header.d=evilsupplier.example"));
    [Fact] public void Dmarc_pass_for_other_header_from_is_unknown() => Assert.Equal(SenderVerification.Unknown, P("mx; dmarc=pass header.from=attacker.example"));
    [Fact] public void Pass_inside_a_comment_is_ignored() => Assert.Equal(SenderVerification.Unknown, P("mx; spf=neutral (dkim=pass header.d=supplier.example)"));

    [Fact] public void Dmarc_fail_fails() => Assert.Equal(SenderVerification.Fail, P("mx; dkim=pass header.d=attacker.example; dmarc=fail (p=REJECT) header.from=supplier.example"));
    [Fact] public void Dkim_fail_alone_fails() => Assert.Equal(SenderVerification.Fail, P("mx; dkim=fail header.d=supplier.example"));
    [Fact] public void Dkim_fail_with_other_aligned_pass_passes() => Assert.Equal(SenderVerification.Pass, P("mx; dkim=fail header.d=supplier.example header.s=a; dkim=pass header.d=supplier.example header.s=b"));
    [Fact] public void Dkim_fail_with_only_unaligned_pass_fails() => Assert.Equal(SenderVerification.Fail, P("mx; dkim=fail header.d=supplier.example; dkim=pass header.d=attacker.example"));

    // Parser-differential attacks: some values (envelope sender, reasons) are copied from the message, so a sender can
    // put ';', '(' or '"' into them. They must never be read as separate clauses or hide later ones.
    [Fact] public void Fake_clause_hidden_in_a_quoted_envelope_sender_is_not_a_verdict() =>
        Assert.Equal(SenderVerification.Unknown, P("mx; spf=pass smtp.mailfrom=\"x;dkim=pass header.d=supplier.example;y\"@attacker.example; dkim=none"));
    [Fact] public void Escaped_quote_does_not_end_a_quoted_string_early() =>
        Assert.Equal(SenderVerification.Unknown, P("mx; spf=neutral reason=\"a\\\"; dkim=pass header.d=supplier.example; b\"; dkim=none"));
    [Fact] public void Open_parenthesis_inside_quotes_does_not_hide_a_later_dmarc_fail() =>
        Assert.Equal(SenderVerification.Fail, P("mx; dkim=pass reason=\"(\" header.d=supplier.example; dmarc=fail header.from=supplier.example"));
    [Fact] public void Malformed_header_is_never_a_pass() =>
        Assert.NotEqual(SenderVerification.Pass, P("mx; dkim=pass header.d=supplier.example reason=\"(\"; dkim=pass header.d=supplier.example \""));
    // A dmarc pass only counts when it names the From domain: an unusable or missing header.from must fail closed.
    [Fact] public void Dmarc_pass_with_an_unusable_header_from_is_not_a_pass() =>
        Assert.Equal(SenderVerification.Unknown, P("mx; dmarc=pass header.from=\"supplier.example attacker.example\""));
    [Fact] public void Dmarc_pass_without_header_from_is_not_a_pass() =>
        Assert.Equal(SenderVerification.Unknown, P("mx; dmarc=pass"));
    [Fact] public void Dmarc_pass_with_unusable_header_from_does_not_beat_an_aligned_dkim_fail() =>
        Assert.Equal(SenderVerification.Fail, P("mx; dmarc=pass header.from=\"a b\"; dkim=fail header.d=supplier.example"));
    [Fact] public void Encoded_words_in_the_header_fail_closed() =>
        Assert.Equal(SenderVerification.Unknown, P("mx; dkim=pass header.d=supplier.example header.b==?utf-8?q?abc?="));
    [Fact] public void Non_ascii_text_in_the_header_fails_closed() =>
        Assert.Equal(SenderVerification.Unknown, P("mx; dkim=pass header.d=supplier.example (café)"));
    [Fact] public void Domain_value_with_extra_text_is_not_trusted() =>
        Assert.Equal(SenderVerification.Unknown, P("mx; dkim=pass header.d=\"supplier.example attacker.example\""));

    [Theory]
    [InlineData("mx; none")]
    [InlineData("mx; dkim=none; dmarc=none")]
    [InlineData("mx; dkim=neutral header.d=supplier.example")]
    [InlineData("mx; dkim=temperror; dmarc=temperror")]
    [InlineData("mx; dkim=permerror; dmarc=permerror")]
    [InlineData("garbage ;;; === ((( ;")]
    [InlineData("")]
    [InlineData("   ")]
    public void Inconclusive_results_are_unknown(string header) => Assert.Equal(SenderVerification.Unknown, P(header));

    [Fact]
    public void Only_the_first_header_is_trusted_forged_pass_below_a_real_one()
    {
        var real = "mx.example.com; dkim=none; dmarc=none";
        var forged = "mx.example.com; dkim=pass header.d=supplier.example; dmarc=pass header.from=supplier.example";
        Assert.Equal(SenderVerification.Unknown, SenderAuthParser.Parse([real, forged], From));
    }

    [Fact]
    public void First_header_fail_wins_over_a_later_pass() =>
        Assert.Equal(SenderVerification.Fail, SenderAuthParser.Parse(["mx; dmarc=fail header.from=supplier.example", "x; dmarc=pass header.from=supplier.example"], From));

    [Fact]
    public void First_header_pass_is_kept_when_a_lower_one_says_fail() =>
        Assert.Equal(SenderVerification.Pass, SenderAuthParser.Parse([GmailLike, "x; dmarc=fail"], From));

    [Fact] public void Null_list_is_unknown() => Assert.Equal(SenderVerification.Unknown, SenderAuthParser.Parse(null, From));
    [Fact] public void Empty_list_is_unknown() => Assert.Equal(SenderVerification.Unknown, SenderAuthParser.Parse([], From));
    [Fact] public void Null_header_is_unknown() => Assert.Equal(SenderVerification.Unknown, SenderAuthParser.Parse([null!], From));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-at-sign")]
    [InlineData("user@")]
    public void Missing_from_domain_is_unknown(string? from) => Assert.Equal(SenderVerification.Unknown, P(GmailLike, from));

    // Trusted authserv-id: only a header written by the mailbox's own provider counts. A header forged by the sender
    // carries whatever id the sender likes, so with an id configured anything else is ignored.
    static SenderVerification T(string? trusted, params string[] headers) => SenderAuthParser.Parse(headers, From, trusted);
    const string Genuine = "mx.provider.example; dkim=pass header.d=supplier.example";
    const string Forged = "attacker.example; dkim=pass header.d=supplier.example";

    [Fact] public void Trusted_id_matching_the_header_passes() => Assert.Equal(SenderVerification.Pass, T("mx.provider.example", Genuine));
    [Fact] public void Trusted_id_comparison_ignores_case() => Assert.Equal(SenderVerification.Pass, T("MX.Provider.EXAMPLE", Genuine));
    [Fact] public void Forged_header_with_another_id_is_ignored() => Assert.Equal(SenderVerification.Unknown, T("mx.provider.example", Forged));
    [Fact] public void Forged_header_above_the_genuine_one_is_skipped() => Assert.Equal(SenderVerification.Pass, T("mx.provider.example", Forged, Genuine));
    [Fact] public void Genuine_fail_is_not_overridden_by_a_forged_pass_below_it() =>
        Assert.Equal(SenderVerification.Fail, T("mx.provider.example", "mx.provider.example; dkim=fail header.d=supplier.example", "mx.provider.example; dkim=pass header.d=supplier.example"));
    [Fact] public void Malformed_header_with_the_trusted_id_is_never_a_pass() =>
        Assert.NotEqual(SenderVerification.Pass, T("mx.provider.example", "mx.provider.example; dkim=pass header.d=supplier.example reason=\"(\"; dkim=pass header.d=supplier.example \""));
    [Fact] public void Header_without_an_id_is_not_trusted() => Assert.Equal(SenderVerification.Unknown, T("mx.provider.example", "dkim=pass header.d=supplier.example"));
    [Fact] public void Id_that_only_contains_the_trusted_one_is_not_a_match() => Assert.Equal(SenderVerification.Unknown, T("mx.provider.example", "evil-mx.provider.example; dkim=pass header.d=supplier.example"));
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void No_trusted_id_keeps_reading_the_topmost_header(string? trusted) => Assert.Equal(SenderVerification.Pass, T(trusted, Forged));
}
