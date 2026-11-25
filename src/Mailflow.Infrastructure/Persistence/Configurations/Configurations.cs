using Mailflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mailflow.Infrastructure.Persistence.Configurations;

internal sealed class MailboxConfiguration : IEntityTypeConfiguration<Mailbox>
{
    public void Configure(EntityTypeBuilder<Mailbox> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.DisplayName).IsRequired().HasMaxLength(200);
        b.Property(x => x.Email).IsRequired().HasMaxLength(320);
        b.Property(x => x.Host).IsRequired().HasMaxLength(255);
        b.Property(x => x.Username).IsRequired().HasMaxLength(320);
        b.Property(x => x.EncryptedPassword).IsRequired();
        b.Property(x => x.InboxFolder).IsRequired().HasMaxLength(255);
    }
}

internal sealed class PrinterConfiguration : IEntityTypeConfiguration<Printer>
{
    public void Configure(EntityTypeBuilder<Printer> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.DisplayName).IsRequired().HasMaxLength(200);
        b.Property(x => x.WindowsName).IsRequired().HasMaxLength(500);
    }
}

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.LocalSubfolder).IsRequired().HasMaxLength(200);
        b.Property(x => x.ImapFolder).HasMaxLength(255);
        b.Property(x => x.Color).IsRequired().HasMaxLength(20);
    }
}

internal sealed class RuleConfiguration : IEntityTypeConfiguration<Rule>
{
    public void Configure(EntityTypeBuilder<Rule> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.HasMany(x => x.Conditions).WithOne().HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Actions).WithOne().HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Mailbox>().WithMany().HasForeignKey(x => x.MailboxId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RuleConditionConfiguration : IEntityTypeConfiguration<RuleCondition>
{
    public void Configure(EntityTypeBuilder<RuleCondition> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Value).IsRequired();
    }
}

internal sealed class RuleActionConfiguration : IEntityTypeConfiguration<RuleAction>
{
    public void Configure(EntityTypeBuilder<RuleAction> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.ImapFolder).HasMaxLength(255);
        b.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<Printer>().WithMany().HasForeignKey(x => x.PrinterId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class ProcessedMessageConfiguration : IEntityTypeConfiguration<ProcessedMessage>
{
    public void Configure(EntityTypeBuilder<ProcessedMessage> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.MessageId).IsRequired().HasMaxLength(998);
        b.Property(x => x.From).IsRequired().HasMaxLength(320);
        b.Property(x => x.Subject).IsRequired();
        b.HasIndex(x => new { x.MailboxId, x.MessageId }).IsUnique();
        b.HasOne<Mailbox>().WithMany().HasForeignKey(x => x.MailboxId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Rule>().WithMany().HasForeignKey(x => x.MatchedRuleId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Sha256).IsRequired().HasMaxLength(64);
        b.Property(x => x.OriginalName).IsRequired().HasMaxLength(500);
        b.Property(x => x.StoredPath).IsRequired();
        b.HasIndex(x => x.Sha256).IsUnique();
        b.HasOne<ProcessedMessage>().WithMany().HasForeignKey(x => x.ProcessedMessageId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Printer>().WithMany().HasForeignKey(x => x.PrinterId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class RunLogConfiguration : IEntityTypeConfiguration<RunLog>
{
    public void Configure(EntityTypeBuilder<RunLog> b)
    {
        b.HasKey(x => x.Id);
        b.HasOne<Mailbox>().WithMany().HasForeignKey(x => x.MailboxId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class SettingConfiguration : IEntityTypeConfiguration<Setting>
{
    public void Configure(EntityTypeBuilder<Setting> b)
    {
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasMaxLength(100);
        b.Property(x => x.Value).IsRequired();
    }
}
