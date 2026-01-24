using System;
using System.Net;
using System.Net.Http;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Options;
using JacRed.Core.Utils;
using JacRed.Infrastructure.Services;
using JacRed.Infrastructure.Services.Trackers.Aniliberty;
using JacRed.Infrastructure.Services.Trackers.RuTracker;
using JacRed.Api.Services;
using JacRed.Api.Services.RuTracker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JacRed.Api.Configuration;

public static class ServicesConfiguration
{
    public static void RegisterServices(this IServiceCollection services)
    {
        services
            .AddScoped<ITorrentRepository, TorrentRepository>()
            .AddScoped<IKeyGenerator, KeyGenerator>()
            .AddScoped<ITorrentEnricher, TorrentEnricher>()
            .AddScoped<ITorrentSearchService, TorrentSearchService>()
            .AddScoped<ITorrentMergerService, TorrentMergerService>()
            .AddScoped<IJackettFacadeService, JackettFacadeService>()
            .AddScoped<ITorrentSearchPipeline, TorrentSearchPipeline>()
            .AddScoped<ITrackerSearchService, TrackerSearchService>()
            .AddScoped<ITrackerSearch, RuTrackerSearch>()
            .AddScoped<ITrackerSearch, AnilibertySearch>()
            // крон сервисы
            .AddScoped<ITrackerRefreshProvider, RuTrackerPopularService>()
            .AddScoped<ITrackerRefreshProvider, RuTrackerRefreshService>()
            .AddHostedService<RuTrackerPopularHostedService>()
            .AddHostedService<RuTrackerRefreshHostedService>();

        // Singleton Services (должны жить все время работы приложения)
        services.AddSingleton<ICacheService, CacheService>();
        services.AddMemoryCache();

        services.AddHttpClient<HttpService>((sp, client) =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(HttpService.UserAgent);
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                // Здесь используем IOptionsMonitor, так как HttpClientFactory кэширует хендлеры
                // и IOptionsSnapshot может быть недоступен или некорректен в контексте фабрики.
                var config = sp.GetRequiredService<IOptionsMonitor<Config>>().CurrentValue;
                var handler = new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate |
                                             DecompressionMethods.Brotli,
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                };

                // Настройка прокси
                if (config.Proxy?.List?.Count > 0)
                {
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
