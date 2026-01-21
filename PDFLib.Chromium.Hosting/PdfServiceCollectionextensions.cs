using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PDFLib.Chromium.Hosting;

/// <summary>
/// Extension methods for adding <see cref="ChromiumBrowser"/> to the service collection.
/// </summary>
public static class PdfServiceCollectionExtensions
{
    /// <summary>
    /// Adds the <see cref="ChromiumBrowser"/> as a singleton service and its hosted service for lifecycle management.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the service to.</param>
    /// <param name="configuration">The configuration section to use for <see cref="BrowserOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddPdfService(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BrowserOptions>(configuration);
        return services.AddPdfService();
    }

    /// <summary>
    /// Adds the <see cref="ChromiumBrowser"/> as a singleton service and its hosted service for lifecycle management.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the service to.</param>
    /// <param name="setupAction">An action to configure the <see cref="BrowserOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddPdfService(this IServiceCollection services, Action<BrowserOptions> setupAction)
    {
        services.Configure(setupAction);
        return services.AddPdfService();
    }

    /// <summary>
    /// Adds the <see cref="ChromiumBrowser"/> as a singleton service and its hosted service for lifecycle management.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the service to.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddPdfService(this IServiceCollection services)
    {
        services.AddSingleton<ChromiumBrowser>(sp => ChromiumBrowser.Instance);
        services.AddSingleton<PdfService>();
        services.AddHostedService<PdfService>(sp => sp.GetRequiredService<PdfService>());
        return services;
    }
}
