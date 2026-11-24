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
}
