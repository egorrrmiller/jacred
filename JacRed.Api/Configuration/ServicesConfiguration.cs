using System;
using System.Net;
using System.Net.Http;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Options;
using JacRed.Core.Utils;
using JacRed.Infrastructure.Services;
using JacRed.Infrastructure.Services.Trackers.Aniliberty;
using JacRed.Infrastructure.Services.Trackers.RuTracker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JacRed.Api.Configuration;

public static class ServicesConfiguration
{
    public static void RegisterServices(this IServiceCollection services)
    {
        services
            .AddSingleton<ITorrentRepository, TorrentRepository>()
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
            .AddMemoryCache();

        // Настройка HttpClient с поддержкой прокси
        services.AddHttpClient<HttpService>((sp, client) =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(HttpService.UserAgent);
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var config = sp.GetRequiredService<IOptions<Config>>().Value;
                var handler = new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate |
                                             DecompressionMethods.Brotli,
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                };

                // Настройка прокси
                if (config.Proxy?.List?.Count > 0)
                {
                    // Выбираем случайный прокси из списка для текущего хендлера.
                    // Хендлеры живут недолго (по умолчанию 2 минуты), поэтому ротация будет происходить автоматически.
                    var proxyUrl = config.Proxy.List[Random.Shared.Next(config.Proxy.List.Count)];
                    var proxy = new WebProxy(proxyUrl);

                    if (config.Proxy.UseAuth && !string.IsNullOrEmpty(config.Proxy.Username))
                        proxy.Credentials = new NetworkCredential(config.Proxy.Username, config.Proxy.Password);

                    proxy.BypassProxyOnLocal = config.Proxy.BypassOnLocal;
                    handler.Proxy = proxy;
                    handler.UseProxy = true;
                }

                return handler;
            });
    }
}