using Mailflow.Application.Abstractions;
using Mailflow.Application.Agents;
using Mailflow.Domain.Entities;
using Mailflow.Web.Pages.Printers;
using NSubstitute;

namespace Mailflow.Integration.Tests.Pages;

public sealed class AgentsPageTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc);
    private readonly SqliteFixture _fx = new();
    public void Dispose() => _fx.Dispose();

    private static AgentsModel Page(TestDb db)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return new AgentsModel(db, clock, new Mailflow.Web.Agent.AgentListenerInfo(false, null)).With();
    }

    private async Task<AgentsModel> Create(string name, string? password)
    {
        using var db = _fx.NewDb();
        var page = Page(db);
        page.Name = name;
        page.Password = password;
        await page.OnPostCreateAsync();
        return page;
    }

    [Fact]
    public async Task Chosen_password_is_stored_hashed_and_not_shown_again()
    {
        var page = await Create("Boutique", "my-own-password-1");

        Assert.Null(page.Error);
        Assert.Null(page.GeneratedPassword);
        using var db = _fx.NewDb();
        var agent = db.PrintAgents.Single();
        Assert.True(AgentJobService.VerifyToken("my-own-password-1", agent.TokenHash));
        Assert.DoesNotContain("my-own-password-1", agent.TokenHash);
    }

    [Fact]
    public async Task Blank_password_generates_one_that_is_shown_once()
    {
        var page = await Create("Boutique", null);

        Assert.NotNull(page.GeneratedPassword);
        using var db = _fx.NewDb();
        Assert.True(AgentJobService.VerifyToken(page.GeneratedPassword!, db.PrintAgents.Single().TokenHash));
    }

    [Theory]
    [InlineData("Boutique")]
    [InlineData("boutique")]
    [InlineData("  BOUTIQUE ")]
    public async Task Two_agents_cannot_share_a_name_even_in_another_letter_case(string second)
    {
        await Create("Boutique", "my-own-password-1");
        var page = await Create(second, "my-own-password-2");

        Assert.NotNull(page.Error);
        using var db = _fx.NewDb();
        Assert.Single(db.PrintAgents);
    }

    [Fact]
    public async Task Short_password_is_refused()
    {
        var page = await Create("Boutique", "short");
        Assert.NotNull(page.Error);
        using var db = _fx.NewDb();
        Assert.Empty(db.PrintAgents);
    }

    [Fact]
    public async Task Changing_the_password_replaces_the_hash_and_keeps_the_agent()
    {
        await Create("Boutique", "old-password-123");
        int id;
        using (var db = _fx.NewDb()) id = db.PrintAgents.Single().Id;
        using var db2 = _fx.NewDb();
        var page = Page(db2);
        page.Password = "brand-new-password-1";

        await page.OnPostPasswordAsync(id);

        Assert.Null(page.Error);
        using var check = _fx.NewDb();
        var agent = check.PrintAgents.Single();
        Assert.Equal("Boutique", agent.Name);
        Assert.True(AgentJobService.VerifyToken("brand-new-password-1", agent.TokenHash));
        Assert.False(AgentJobService.VerifyToken("old-password-123", agent.TokenHash));
    }

    [Fact]
    public async Task Changing_the_password_with_a_blank_one_generates_it_and_a_short_one_is_refused()
    {
        await Create("Boutique", "old-password-123");
        int id;
        using (var db = _fx.NewDb()) id = db.PrintAgents.Single().Id;

        using (var db2 = _fx.NewDb())
        {
            var page = Page(db2);
            page.Password = "short";
            await page.OnPostPasswordAsync(id);
            Assert.NotNull(page.Error);
        }
        using (var db3 = _fx.NewDb())
        {
            var page = Page(db3);
            await page.OnPostPasswordAsync(id);
            Assert.NotNull(page.GeneratedPassword);
            using var check = _fx.NewDb();
            Assert.True(AgentJobService.VerifyToken(page.GeneratedPassword!, check.PrintAgents.Single().TokenHash));
        }
    }
}
