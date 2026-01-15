using JacRed.Api.Services;
using JacRed.Core.Interfaces;
using JacRed.Infrastructure.Services;
using JacRed.Infrastructure.Services.Trackers;
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
            .AddSingleton<IMediaAnalyzerService, MediaAnalyzerService>()
            .AddSingleton<ITorrentEnricher, TorrentEnricher>()
            .AddSingleton<ITorrentSearchService, TorrentSearchService>()
            .AddSingleton<ITracksDatabase, TracksDatabase>()
            .AddSingleton<ITorrentMergerService, TorrentMergerService>()
            .AddSingleton<IJackettFacadeService, JackettFacadeService>()
            .AddSingleton<ITorrentSearchPipeline, TorrentSearchPipeline>()
            .AddSingleton<ITrackerSearchService, TrackerSearchService>()
            .AddSingleton<ITrackerSearch, Animelayer>()
            .AddSingleton<ITrackerSearch, Baibako>()
            .AddSingleton<ITrackerSearch, Bitru>()
            .AddSingleton<ITrackerSearch, Kinozal>()
            .AddSingleton<ITrackerSearch, Lostfilm>()
            .AddSingleton<ITrackerSearch, Megapeer>()
            .AddSingleton<ITrackerSearch, NnmClub>()
            .AddSingleton<ITrackerSearch, Rutor>()
            .AddSingleton<ITrackerSearch, RuTrackerSearch>()
            .AddSingleton<ITrackerSearch, Selezen>()
            .AddSingleton<ITrackerSearch, Toloka>()
            .AddSingleton<ITrackerSearch, TorrentBy>()
            .AddSingleton<ITrackerCronProvider, RuTrackerCron>()
            .AddMemoryCache()
            .AddHttpClient();
    }
}
