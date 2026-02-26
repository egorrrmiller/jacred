using System;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using JacRed.Api.Services.Media;
using JacRed.Api.Services.Refresh;
using JacRed.Api.Services.RuTracker;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Options;
using JacRed.Core.Utils;
using JacRed.Infrastructure.Persistence.Repositories;
using JacRed.Infrastructure.Services;
using JacRed.Infrastructure.Services.Search;
using JacRed.Infrastructure.Services.Trackers.Aniliberty;
using JacRed.Infrastructure.Services.Trackers.AnimeLayer;
using JacRed.Infrastructure.Services.Trackers.Kinozal;
using JacRed.Infrastructure.Services.Trackers.MegaPeer;
using JacRed.Infrastructure.Services.Trackers.NNMClub;
using JacRed.Infrastructure.Services.Trackers.RuTor;
using JacRed.Infrastructure.Services.Trackers.RuTracker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JacRed.Api.Configuration;

public static class ServicesConfiguration
{
    public static void RegisterServices(this IServiceCollection services)
    {
        services
            .AddScoped<ITorrentRepository, TorrentRepository>()
            .AddScoped<IQueriesRepository, QueriesRepository>()
            .AddScoped<ISubscriptionRepository, SubscriptionRepository>()
            .AddScoped<IKeyGenerator, KeyGenerator>()
            .AddScoped<ITorrentEnricher, TorrentEnricher>()
            .AddScoped<ILocalSearchService, LocalSearchService>()
            .AddScoped<ITorrentMergerService, TorrentMergerService>()
            .AddScoped<ITorrentMediaProbeService, TorrentMediaProbeService>()
            .AddScoped<IRemoteSearchService, RemoteSearchService>()
            .AddScoped<ISearchService, SearchService>()
            .AddScoped<IMediaResolverService, MediaResolverService>()
            .AddScoped<ISubscribeService, SubscribeService>()
            .AddScoped<ITrackerSearch, RuTrackerSearch>()
            .AddScoped<ITrackerSearch, AnilibertySearch>()
            .AddScoped<ITrackerSearch, RuTorSearch>()
            .AddScoped<ITrackerSearch, AnimeLayerSearch>()
            .AddScoped<ITrackerSearch, NNMClubSearch>()
            .AddScoped<ITrackerSearch, KinozalSearch>()
            .AddScoped<ITrackerSearch, MegaPeerSearch>()
            .AddScoped<ITrackerRefreshProvider, RuTrackerPopularService>()
            // крон сервисы
            .AddHostedService<TorrentMediaProbeHostedService>()
            .AddHostedService<RuTrackerPopularHostedService>()
            .AddHostedService<RefreshHostedService>();

        services.AddSingleton<ICacheService, CacheService>();
        services.AddMemoryCache();
        
        services.AddScoped<HttpService>();

        Action<HttpClient> configureClient = client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(HttpService.DefaultUserAgent);
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
            client.DefaultRequestHeaders.Add("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
        };

        services.AddHttpClient("Default", configureClient)
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var config = sp.GetRequiredService<IOptionsMonitor<Config>>().CurrentValue;
                var handler = new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
                    CheckCertificateRevocationList = false,
                    SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                };

                if (config.Proxy?.List?.Count > 0)
                {
                    var proxyItem = config.Proxy.List[Random.Shared.Next(config.Proxy.List.Count)];
                    var proxy = new WebProxy(proxyItem.Url);

                    if (!string.IsNullOrEmpty(proxyItem.Username))
                        proxy.Credentials = new NetworkCredential(proxyItem.Username, proxyItem.Password);

                    proxy.BypassProxyOnLocal = config.Proxy.BypassOnLocal;
                    handler.Proxy = proxy;
                    handler.UseProxy = true;
                }

                return handler;
            });

        services.AddHttpClient("DefaultNoRedirect", configureClient)
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var config = sp.GetRequiredService<IOptionsMonitor<Config>>().CurrentValue;
                var handler = new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
                    CheckCertificateRevocationList = false,
                    SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    AllowAutoRedirect = false
                };

                if (config.Proxy?.List?.Count > 0)
                {
                    var proxyItem = config.Proxy.List[Random.Shared.Next(config.Proxy.List.Count)];
                    var proxy = new WebProxy(proxyItem.Url);

                    if (!string.IsNullOrEmpty(proxyItem.Username))
                        proxy.Credentials = new NetworkCredential(proxyItem.Username, proxyItem.Password);

                    proxy.BypassProxyOnLocal = config.Proxy.BypassOnLocal;
                    handler.Proxy = proxy;
                    handler.UseProxy = true;
                }

                return handler;
            });

        services.AddHttpClient("NoProxy", configureClient)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
                CheckCertificateRevocationList = false,
                SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            });

        services.AddHttpClient("NoProxyNoRedirect", configureClient)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
                CheckCertificateRevocationList = false,
                SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                AllowAutoRedirect = false
            });
    }
}