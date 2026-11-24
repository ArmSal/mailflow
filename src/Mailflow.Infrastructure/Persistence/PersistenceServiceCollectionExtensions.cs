using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Mailflow.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<MailflowDbContext>(o => o.UseSqlite(connectionString));
        services.AddScoped<Application.Abstractions.IAppDbContext>(sp => sp.GetRequiredService<MailflowDbContext>());
        return services;
    }
}

/// <summary>Used by `dotnet ef` only.</summary>
internal sealed class MailflowDbContextFactory : IDesignTimeDbContextFactory<MailflowDbContext>
{
    public MailflowDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<MailflowDbContext>().UseSqlite("Data Source=mailflow.db").Options);
}
