using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Infrastructure.Persistence;

public class MailflowDbContext(DbContextOptions<MailflowDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Mailbox> Mailboxes => Set<Mailbox>();
    public DbSet<Printer> Printers => Set<Printer>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Rule> Rules => Set<Rule>();
    public DbSet<RuleCondition> RuleConditions => Set<RuleCondition>();
    public DbSet<RuleAction> RuleActions => Set<RuleAction>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<RunLog> RunLogs => Set<RunLog>();
    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(MailflowDbContext).Assembly);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Enum>().HaveConversion<string>();
    }
}

