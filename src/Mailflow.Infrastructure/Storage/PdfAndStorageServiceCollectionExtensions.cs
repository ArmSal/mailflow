using Mailflow.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Mailflow.Infrastructure;

public static class PdfAndStorageServiceCollectionExtensions
{
    public static IServiceCollection AddPdfAndStorage(this IServiceCollection services, Action<Storage.DocumentStoreOptions> configure)
    {
        services.Configure(configure);
        services.AddSingleton<IPdfTextReader, Pdf.PdfPigTextReader>();
        services.AddSingleton<IDocumentStore, Storage.FileSystemDocumentStore>();
        return services;
    }
}
