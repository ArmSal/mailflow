using Mailflow.Application.Abstractions;
using Mailflow.Application.Agents;
using Mailflow.Application.Commands;
using Mailflow.Application.Tests.History;
using Mailflow.Domain.Entities;
using Mailflow.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Mailflow.Application.Tests;

public class RemotePrintingTests
{
    static void MakeRemote(TestDb db, int? agentId)
    {
        var p = db.Printers.Single();
        p.Kind = PrinterKind.PrintNode;
        p.PrintAgentId = agentId;
        db.SaveChanges();
    }

    [Fact]
    public async Task Remote_printer_queues_a_job_instead_of_printing_locally()
    {
        using var f = new Fixture();
        MakeRemote(f.Db, 7);
        f.Fetch(Fixture.Msg());

        var log = await f.Processor.RunAsync(f.Mailbox, default);

        Assert.Null(log.Error);
        await f.Printer.DidNotReceiveWithAnyArgs().PrintAsync(default!, default!, default, default, default);
        var job = Assert.Single(f.Db.PrintJobs);
        Assert.Equal((7, "C:/docs/a.pdf", "HP", 2, PrintJobStatus.Pending), (job.PrintAgentId, job.PdfPath, job.PrinterName, job.Copies, job.Status));
        var doc = Assert.Single(f.Db.Documents);
        Assert.Equal(doc.Id, job.DocumentId);
        Assert.Null(doc.PrintedAt);
        Assert.Null(doc.PrintError);
        Assert.Equal(1, doc.PrinterId);
        Assert.Equal(0, log.Printed);
    }

    [Fact]
    public async Task Remote_printer_without_agent_records_an_error_and_no_job()
    {
        using var f = new Fixture();
        MakeRemote(f.Db, null);
        f.Fetch(Fixture.Msg());

        await f.Processor.RunAsync(f.Mailbox, default);

        Assert.Empty(f.Db.PrintJobs);
        Assert.Contains("agent", Assert.Single(f.Db.Documents).PrintError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reprint_on_a_remote_printer_queues_a_job()
    {
        using var fx = new HistoryFixture();
        using (var setup = fx.NewDb())
        {
            var p = setup.Printers.Single(x => x.Id == 1);
            p.Kind = PrinterKind.PrintNode;
            p.PrintAgentId = 3;
            setup.SaveChanges();
        }
        var id = fx.AddDoc(1, new DateTime(2026, 4, 1), "a@f.fr", printError: "old", printerId: 2);
        using var db = fx.NewDb();
        var ps = Substitute.For<IPrinterService>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTime(2026, 4, 1, 8, 0, 0, DateTimeKind.Utc));

        var r = await new Reprint(db, ps, clock).ExecuteAsync(id, 1);

        Assert.True(r.Success);
        await ps.DidNotReceiveWithAnyArgs().PrintAsync(default!, default!, default, default, default);
        using var check = fx.NewDb();
        var job = Assert.Single(check.PrintJobs);
        Assert.Equal((3, id, "Win1", 2, true), (job.PrintAgentId, job.DocumentId, job.PrinterName, job.Copies, job.Duplex));
        var d = check.Documents.Single();
        Assert.Equal(1, d.PrinterId);
        Assert.Null(d.PrintError);
        Assert.Null(d.PrintedAt);
    }
}

public class AgentJobServiceTests
{
    static readonly DateTime Now = new(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc);

    static (AgentJobService svc, Fixture f, PrintAgent agent, string token) Make()
    {
        var f = new Fixture();
        f.Clock.UtcNow.Returns(Now);
        var token = AgentJobService.NewToken();
        var agent = new PrintAgent { Name = "Boutique", TokenHash = AgentJobService.HashToken(token), CreatedAt = Now };
        f.Db.PrintAgents.Add(agent);
        f.Db.SaveChanges();
        return (new AgentJobService(f.Db, f.Clock), f, agent, token);
    }

    static PrintJob Job(int agentId, int? documentId = null) => new()
    {
        PrintAgentId = agentId, PdfPath = "C:/docs/a.pdf", PrinterName = "HP", Copies = 2, Duplex = true, CreatedAt = Now, DocumentId = documentId,
    };

    [Fact]
    public async Task Valid_token_authenticates_and_stamps_last_seen_while_bad_ones_do_not()
    {
        var (svc, f, agent, token) = Make();
        using var _ = f;
        Assert.Null(await svc.AuthenticateAsync("Boutique", null, default));
        Assert.Null(await svc.AuthenticateAsync("Boutique", "", default));
        Assert.Null(await svc.AuthenticateAsync("Boutique", token + "x", default));
        Assert.Null(await svc.AuthenticateAsync(null, token, default));

        var found = await svc.AuthenticateAsync("Boutique", token, default);

        Assert.Equal(agent.Id, found!.Id);
        Assert.Equal(Now, found.LastSeenAt);
    }

    [Fact]
    public async Task The_agent_name_must_match_too_but_not_its_letter_case()
    {
        var (svc, f, agent, token) = Make();
        using var _f = f;
        f.Db.PrintAgents.Add(new PrintAgent { Name = "Entrepot", TokenHash = AgentJobService.HashToken("another-password-1"), CreatedAt = Now });
        f.Db.SaveChanges();

        Assert.Null(await svc.AuthenticateAsync("Unknown", token, default));
        Assert.Null(await svc.AuthenticateAsync("Entrepot", token, default)); // right password, but another agent's name
        Assert.Equal(agent.Id, (await svc.AuthenticateAsync("  boutique ", token, default))!.Id);
    }

    [Fact]
    public async Task Reported_printers_are_stored_cleaned_and_listed()
    {
        var (svc, f, agent, _) = Make();
        using var _f = f;

        await svc.SetPrintersAsync(agent, ["HP LaserJet", " ", "Brother", "HP LaserJet", new string('x', 300)], default);

        Assert.Equal(["HP LaserJet", "Brother"], AgentJobService.PrinterList(f.Db.PrintAgents.Single()));
        Assert.Equal(Now, f.Db.PrintAgents.Single().PrintersReportedAt);
        Assert.Empty(AgentJobService.PrinterList(new PrintAgent()));
    }

    [Fact]
    public void Token_is_stored_only_as_a_hash()
    {
        var (_, f, agent, token) = Make();
        using var _f = f;
        Assert.DoesNotContain(token, agent.TokenHash);
        Assert.True(AgentJobService.VerifyToken(token, agent.TokenHash));
        Assert.False(AgentJobService.VerifyToken(token + "x", agent.TokenHash));
    }

    [Fact]
    public void Hash_is_salted_so_the_same_password_gives_different_hashes()
    {
        var a = AgentJobService.HashToken("same-password-123");
        var b = AgentJobService.HashToken("same-password-123");
        Assert.NotEqual(a, b);
        Assert.True(AgentJobService.VerifyToken("same-password-123", b));
    }

    [Theory]
    [InlineData("")]
    [InlineData("h")]
    [InlineData("pbkdf2$x$y$z")]
    [InlineData("sha256$1$AA==$AA==")]
    public void Malformed_stored_hash_never_verifies(string stored) => Assert.False(AgentJobService.VerifyToken("anything", stored));

    [Fact]
    public async Task Claim_returns_oldest_pending_job_of_that_agent_only_and_marks_it_sent()
    {
        var (svc, f, agent, _) = Make();
        using var _f = f;
        f.Db.PrintJobs.AddRange(Job(agent.Id + 1), Job(agent.Id), Job(agent.Id));
        f.Db.SaveChanges();

        var first = await svc.ClaimNextAsync(agent, default);
        var second = await svc.ClaimNextAsync(agent, default);
        var third = await svc.ClaimNextAsync(agent, default);

        Assert.NotEqual(first!.Id, second!.Id);
        Assert.Equal(("HP", 2, true, "a.pdf"), (first.PrinterName, first.Copies, first.Duplex, first.FileName));
        Assert.Null(third);
        Assert.All(f.Db.PrintJobs.Where(j => j.PrintAgentId == agent.Id), j => Assert.Equal(PrintJobStatus.Sent, j.Status));
        Assert.Equal(PrintJobStatus.Pending, f.Db.PrintJobs.Single(j => j.PrintAgentId == agent.Id + 1).Status);
    }

    [Fact]
    public async Task Waiting_for_a_job_gives_up_after_the_wait_when_nothing_arrives()
    {
        var (svc, f, agent, _) = Make();
        using var _f = f;
        var pauses = 0;

        var job = await svc.ClaimNextAsync(agent, TimeSpan.FromSeconds(3), default, (_, _) => { pauses++; return Task.CompletedTask; });

        Assert.Null(job);
        Assert.Equal(3, pauses); // checked again once a second for 3 seconds
    }

    [Fact]
    public async Task A_job_queued_while_the_agent_waits_is_handed_over_at_once()
    {
        var (svc, f, agent, _) = Make();
        using var _f = f;
        var pauses = 0;

        var job = await svc.ClaimNextAsync(agent, TimeSpan.FromSeconds(25), default, (_, _) =>
        {
            if (++pauses == 2) { f.Db.PrintJobs.Add(Job(agent.Id)); f.Db.SaveChanges(); } // Mailflow queues a print after 2 s
            return Task.CompletedTask;
        });

        Assert.NotNull(job);
        Assert.Equal(2, pauses);
        Assert.Equal(PrintJobStatus.Sent, f.Db.PrintJobs.Single().Status);
    }

    [Fact]
    public async Task A_job_that_is_already_waiting_is_returned_without_any_pause()
    {
        var (svc, f, agent, _) = Make();
        using var _f = f;
        f.Db.PrintJobs.Add(Job(agent.Id));
        f.Db.SaveChanges();

        var job = await svc.ClaimNextAsync(agent, TimeSpan.FromSeconds(25), default, (_, _) => throw new InvalidOperationException("must not wait"));

        Assert.NotNull(job);
    }

    [Fact]
    public async Task The_wait_is_capped_so_a_request_cannot_be_held_for_ever()
    {
        var (svc, f, agent, _) = Make();
        using var _f = f;
        var pauses = 0;

        await svc.ClaimNextAsync(agent, TimeSpan.FromHours(1), default, (_, _) => { pauses++; return Task.CompletedTask; });

        Assert.Equal((int)AgentJobService.MaxWait.TotalSeconds, pauses);
    }

    [Fact]
    public async Task A_job_sent_but_never_completed_is_offered_again_after_five_minutes()
    {
        var (svc, f, agent, _) = Make();
        using var _f = f;
        f.Db.PrintJobs.Add(Job(agent.Id));
        f.Db.SaveChanges();
        await svc.ClaimNextAsync(agent, default);
        Assert.Null(await svc.ClaimNextAsync(agent, default));

        f.Clock.UtcNow.Returns(Now.AddMinutes(6));

        Assert.NotNull(await svc.ClaimNextAsync(agent, default));
    }

    [Fact]
    public async Task Success_marks_job_done_and_document_printed()
    {
        var (svc, f, agent, _) = Make();
        using var _f = f;
        var doc = new Document { ProcessedMessageId = 1, Sha256 = "x", OriginalName = "a.pdf", StoredPath = "C:/docs/a.pdf", CategoryId = 1, PrintError = "old" };
        f.Db.Documents.Add(doc);
        f.Db.SaveChanges();
        f.Db.PrintJobs.Add(Job(agent.Id, doc.Id));
        f.Db.SaveChanges();
        var claimed = await svc.ClaimNextAsync(agent, default);

        Assert.True(await svc.CompleteAsync(agent, claimed!.Id, true, null, default));

        Assert.Equal(PrintJobStatus.Done, f.Db.PrintJobs.Single().Status);
        Assert.Equal(Now, f.Db.Documents.Single().PrintedAt);
        Assert.Null(f.Db.Documents.Single().PrintError);
    }

    [Fact]
    public async Task Failure_marks_job_failed_and_stores_the_error_on_the_document()
    {
        var (svc, f, agent, _) = Make();
        using var _f = f;
        var doc = new Document { ProcessedMessageId = 1, Sha256 = "x", OriginalName = "a.pdf", StoredPath = "C:/docs/a.pdf", CategoryId = 1 };
        f.Db.Documents.Add(doc);
        f.Db.SaveChanges();
        f.Db.PrintJobs.Add(Job(agent.Id, doc.Id));
        f.Db.SaveChanges();
        var claimed = await svc.ClaimNextAsync(agent, default);

        Assert.True(await svc.CompleteAsync(agent, claimed!.Id, false, "paper jam", default));

        Assert.Equal(("paper jam", PrintJobStatus.Failed), (f.Db.PrintJobs.Single().Error, f.Db.PrintJobs.Single().Status));
        Assert.Equal("paper jam", f.Db.Documents.Single().PrintError);
        Assert.Null(f.Db.Documents.Single().PrintedAt);
    }

    [Fact]
    public async Task An_agent_cannot_complete_or_fetch_another_agents_job_or_one_not_sent()
    {
        var (svc, f, agent, _) = Make();
        using var _f = f;
        var other = new PrintAgent { Name = "Autre", TokenHash = "h", CreatedAt = Now };
        f.Db.PrintAgents.Add(other);
        f.Db.SaveChanges();
        f.Db.PrintJobs.Add(Job(other.Id));
        f.Db.SaveChanges();
        var id = f.Db.PrintJobs.Single().Id;

        Assert.False(await svc.CompleteAsync(agent, id, true, null, default));
        Assert.Null(await svc.GetClaimedAsync(agent, id, default));
        Assert.Equal(PrintJobStatus.Pending, f.Db.PrintJobs.Single().Status);

        f.Db.PrintJobs.Add(Job(agent.Id));
        f.Db.SaveChanges();
        var mine = f.Db.PrintJobs.Single(j => j.PrintAgentId == agent.Id).Id;
        Assert.Null(await svc.GetClaimedAsync(agent, mine, default)); // still Pending: not claimed yet
    }
}
