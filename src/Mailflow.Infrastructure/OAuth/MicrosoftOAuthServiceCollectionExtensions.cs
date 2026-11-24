using Mailflow.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Mailflow.Infrastructure.OAuth;

public static class MicrosoftOAuthServiceCollectionExtensions
{
    public static IServiceCollection AddMicrosoftOAuth(this IServiceCollection services)
    {
        services.AddSingleton<IMsalClient, MsalClient>();
        services.AddSingleton<IOAuthSignInService, OAuthSignInService>();
        services.AddScoped<IMailboxConnectionFactory, MailboxConnectionFactory>();
        return services;
    }
}
