using JacRed.Api.Services;
using JacRed.Core.Interfaces;
using JacRed.Infrastructure.Services;
using JacRed.Infrastructure.Services.Trackers.Animelayer;
using JacRed.Infrastructure.Services.Trackers.Baibako;
using JacRed.Infrastructure.Services.Trackers.Bitru;
using JacRed.Infrastructure.Services.Trackers.Kinozal;
using JacRed.Infrastructure.Services.Trackers.Lostfilm;
using JacRed.Infrastructure.Services.Trackers.Megapeer;
using JacRed.Infrastructure.Services.Trackers.NnmClub;
using JacRed.Infrastructure.Services.Trackers.Rutor;
using JacRed.Infrastructure.Services.Trackers.RuTracker;
using JacRed.Infrastructure.Services.Trackers.Selezen;
using JacRed.Infrastructure.Services.Trackers.Toloka;
using JacRed.Infrastructure.Services.Trackers.TorrentBy;
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
            .AddSingleton<ITrackerSearch, AnimelayerSearch>()
            .AddSingleton<ITrackerSearch, BaibakoSearch>()
            .AddSingleton<ITrackerSearch, BitruSearch>()
            .AddSingleton<ITrackerSearch, KinozalSearch>()
            .AddSingleton<ITrackerSearch, LostfilmSearch>()
            .AddSingleton<ITrackerSearch, MegapeerSearch>()
            .AddSingleton<ITrackerSearch, NnmClubSearch>()
            .AddSingleton<ITrackerSearch, RutorSearch>()
            .AddSingleton<ITrackerSearch, RuTrackerSearch>()
            .AddSingleton<ITrackerSearch, SelezenSearch>()
            .AddSingleton<ITrackerSearch, TolokaSearch>()
            .AddSingleton<ITrackerSearch, TorrentBySearch>()
            .AddSingleton<ITrackerCronProvider, RuTrackerTopSeededSync>()
            .AddMemoryCache()
            .AddHttpClient();
    }
}
