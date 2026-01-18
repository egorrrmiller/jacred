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
            //.AddSingleton<ITrackerSearch, AnimelayerSearch>()
            //.AddSingleton<ITrackerSearch, BaibakoSearch>()
            //.AddSingleton<ITrackerSearch, BitruSearch>()
            //.AddSingleton<ITrackerSearch, KinozalSearch>()
            //.AddSingleton<ITrackerSearch, LostfilmSearch>()
            //.AddSingleton<ITrackerSearch, MegapeerSearch>()
            //.AddSingleton<ITrackerSearch, NnmClubSearch>()
            //.AddSingleton<ITrackerSearch, RutorSearch>()
            .AddSingleton<ITrackerSearch, RuTrackerSearch>()
            //.AddSingleton<ITrackerSearch, SelezenSearch>()
            //.AddSingleton<ITrackerSearch, TolokaSearch>()
            //.AddSingleton<ITrackerSearch, TorrentBySearch>()
            .AddSingleton<ITrackerSearch, AnilibertySearch>()
            .AddSingleton<ITrackerCronProvider, RuTrackerTopSeededSync>()
            //.AddHostedService<StaleTorrentRefreshService>()
            //.AddHostedService<TrackerCatalogPrefetchService>()
            .AddMemoryCache()
            .AddHttpClient();
    }
}