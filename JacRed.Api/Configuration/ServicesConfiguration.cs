using JacRed.Api.Services;
using JacRed.Api.Services.Trackers;
using JacRed.Core.Interfaces;
using JacRed.Infrastructure.Services;
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
            .AddSingleton<IMediaAnalyzerService, MediaAnalyzerService>()
            .AddSingleton<ITorrentEnricher, TorrentEnricher>()
            .AddSingleton<ITorrentSearchService, TorrentSearchService>()
            .AddSingleton<ITracksDatabase, TracksDatabase>()
            .AddSingleton<ITorrentMergerService, TorrentMergerService>()
            .AddSingleton<IJackettFacadeService, JackettFacadeService>()
            .AddSingleton<ITrackerSearchService, TrackerSearchService>()
            .AddSingleton<ITrackerSearchProvider, AnimelayerTrackerSearchProvider>()
            .AddSingleton<ITrackerSearchProvider, BaibakoTrackerSearchProvider>()
            .AddSingleton<ITrackerSearchProvider, BitruTrackerSearchProvider>()
            .AddSingleton<ITrackerSearchProvider, KinozalTrackerSearchProvider>()
            .AddSingleton<ITrackerSearchProvider, LostfilmTrackerSearchProvider>()
            .AddSingleton<ITrackerSearchProvider, MegapeerTrackerSearchProvider>()
            .AddSingleton<ITrackerSearchProvider, NNMClubTrackerSearchProvider>()
            .AddSingleton<ITrackerSearchProvider, RutorTrackerSearchProvider>()
            .AddSingleton<ITrackerSearchProvider, RutrackerTrackerSearchProvider>()
            .AddSingleton<ITrackerSearchProvider, SelezenTrackerSearchProvider>()
            .AddSingleton<ITrackerSearchProvider, TolokaTrackerSearchProvider>()
            .AddSingleton<ITrackerSearchProvider, TorrentByTrackerSearchProvider>()
            .AddMemoryCache()
            .AddHttpClient();
    }
}
