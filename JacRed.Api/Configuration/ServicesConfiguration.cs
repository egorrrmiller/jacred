using JacRed.Core.Interfaces;
using JacRed.Infrastructure.Services;
using JacRed.Infrastructure.Services.Trackers.Aniliberty;
using JacRed.Infrastructure.Services.Trackers.RuTracker;
using Microsoft.Extensions.DependencyInjection;

namespace JacRed.Api.Configuration;

public static class ServicesConfiguration
{
    public static void RegisterServices(this IServiceCollection services)
    {
        services
            .AddSingleton<ITorrentRepository, TorrentRepository>()
            .AddSingleton<IContentCatalog, ContentCatalogService>()
            .AddSingleton<ICacheService, CacheService>()
            .AddSingleton<IKeyGenerator, KeyGenerator>()
            .AddSingleton<ITorrentEnricher, TorrentEnricher>()
            .AddSingleton<ITorrentSearchService, TorrentSearchService>()
            .AddSingleton<ITorrentMergerService, TorrentMergerService>()
            .AddSingleton<IJackettFacadeService, JackettFacadeService>()
            .AddSingleton<ITorrentSearchPipeline, TorrentSearchPipeline>()
            .AddSingleton<ITrackerSearchService, TrackerSearchService>()
            .AddSingleton<ITrackerSearch, RuTrackerSearch>()
            .AddSingleton<ITrackerSearch, AnilibertySearch>()
            //.AddHostedService<StaleTorrentRefreshService>()
            //.AddHostedService<TrackerCatalogPrefetchService>()
            .AddMemoryCache()
            .AddHttpClient();
    }
}
