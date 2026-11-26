using Mailflow.Domain.Entities;
using Mailflow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Infrastructure.Persistence;

public static class DbSeeder
{
    public const string DefaultCategoryName = "A trier";
    public const string DefaultFileNamePattern = "{Category}/{yyyy}/{MM}/{yyyy-MM-dd}_{Supplier}_{OriginalName}.pdf";

    /// <summary>Seeds defaults only into empty tables; never overwrites existing data.</summary>
    public static async Task SeedAsync(MailflowDbContext db, string documentsRoot, CancellationToken ct = default)
    {
        if (!await db.Categories.AnyAsync(ct))
        {
            db.Categories.Add(new Category { Name = DefaultCategoryName, LocalSubfolder = DefaultCategoryName });
            await db.SaveChangesAsync(ct);
        }

        if (!await db.Rules.AnyAsync(ct))
        {
            var category = await db.Categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Id).FirstAsync(ct);
            db.Rules.Add(new Rule
            {
                Name = "Par defaut",
                Priority = 1000,
                MatchMode = MatchMode.All,
                Actions =
                [
                    new RuleAction { Type = ActionType.SaveToCategory, CategoryId = category.Id },
                    new RuleAction { Type = ActionType.LeaveUnread },
                ],
            });
        }

        if (!await db.Settings.AnyAsync(ct))
        {
            db.Settings.AddRange(
                new Setting { Key = "DocumentsRoot", Value = documentsRoot },
                new Setting { Key = "FileNamePattern", Value = DefaultFileNamePattern });
        }

        await db.SaveChangesAsync(ct);
    }
}
