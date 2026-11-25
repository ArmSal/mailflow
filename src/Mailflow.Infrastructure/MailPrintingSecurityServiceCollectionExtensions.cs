using Mailflow.Application.Abstractions;
using Mailflow.Infrastructure.Mail;
using Mailflow.Infrastructure.Printing;
using Mailflow.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Mailflow.Infrastructure;

public static class MailPrintingSecurityServiceCollectionExtensions
{
    public static IServiceCollection AddMailPrintingSecurity(
        this IServiceCollection services,
        Action<SumatraOptions>? configureSumatra = null,
        Action<DpapiOptions>? configureDpapi = null)
    {
        services.AddOptions<SumatraOptions>();
        services.AddOptions<DpapiOptions>();
        if (configureSumatra is not null) services.Configure(configureSumatra);
        if (configureDpapi is not null) services.Configure(configureDpapi);
        services.AddSingleton<IMailClient, MailKitClient>();
        services.AddSingleton<IPrinterCatalog, WindowsPrinterCatalog>();
        services.AddSingleton<IPrinterService, SumatraPrinterService>();
        services.AddSingleton<ISecretStore, DpapiSecretStore>();
        services.AddSingleton<IMailboxConnectionFactory, Mail.MailboxConnectionFactory>();
        return services;
    }
}
