using Mailflow.Application.Processing;
using Xunit;

namespace Mailflow.Application.Tests;

public class AddressParsingTests
{
    [Theory]
    [InlineData("a@b.com", "a@b.com")]
    [InlineData("  a@b.com  ", "a@b.com")]
    [InlineData("Name <a@b.com>", "a@b.com")]
    [InlineData("\"Last, First\" <a@b.com>", "a@b.com")]
    // The display name contains a fake address; the real one is the last bracketed part.
    [InlineData("\"x <boss@allowed.com>\" <evil@evil.com>", "evil@evil.com")]
    public void Well_formed_senders_resolve_to_the_real_address(string from, string expected) =>
        Assert.Equal(expected, MessagePipeline.ExtractAddress(from));

    [Fact]
    public void Recipients_keep_lenient_parsing_so_several_addresses_still_match_To_rules() =>
        Assert.Equal("a@b.com, c@d.com", MessagePipeline.ExtractRecipient("a@b.com, c@d.com"));

    [Theory]
    [InlineData("evil@evil.com <boss@allowed.com")]       // unterminated bracket
    [InlineData("<boss@allowed.com> evil@evil.com")]       // trailing text after the bracket
    [InlineData("Name <a@b.com> (comment)")]
    [InlineData("a@b.com> <c@d.com")]                     // stray brackets
    [InlineData("a b@c.com")]                              // whitespace inside the address
    [InlineData("a@b@c.com")]                              // two @
    [InlineData("<>")]
    [InlineData("")]
    [InlineData("no-at-sign")]
    public void Ambiguous_or_malformed_senders_resolve_to_empty_so_nothing_matches(string from) =>
        Assert.Equal("", MessagePipeline.ExtractAddress(from));
}
