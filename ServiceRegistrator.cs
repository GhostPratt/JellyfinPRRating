using JellyfinPRRating.Rating;
using JellyfinPRRating.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace JellyfinPRRating;

/// <summary>
/// Registers the plugin's services with the DI container.
/// </summary>
public class ServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<CommonSenseScraper>();
        serviceCollection.AddSingleton<KidsInMindScraper>();
        serviceCollection.AddSingleton<ParentPreviewsScraper>();
        serviceCollection.AddSingleton<DoveScraper>();
        serviceCollection.AddSingleton<IPrRatingCalculator, PrRatingCalculator>();
        serviceCollection.AddSingleton<PrTagService>();
        serviceCollection.AddHostedService<ItemAddedListener>();
    }
}