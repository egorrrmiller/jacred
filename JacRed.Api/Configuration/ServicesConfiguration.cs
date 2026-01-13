using JacRed.Core.Interfaces;
using JacRed.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace JacRed.Api.Configuration;

public static class ServicesConfiguration
{
	public static void RegisterServices(this IServiceCollection services) => services
		.AddSingleton<ITorrentRepository, FileTorrentRepository>()
		.AddSingleton<IContentCatalog, ContentCatalogService>()
		.AddSingleton<ICacheService, CacheService>()
		.AddSingleton<IPathResolver, PathResolver>()
		.AddSingleton<IKeyGenerator, KeyGenerator>()
		.AddSingleton<IMediaAnalyzerService, MediaAnalyzerService>()
		.AddSingleton<ITorrentEnricher, TorrentEnricher>()
		.AddSingleton<ITorrentSearchService, TorrentSearchService>()
		.AddSingleton<ITracksDatabase, TracksDatabase>()
		.AddSingleton<ITorrentMergerService, TorrentMergerService>()
		.AddMemoryCache()
		.AddHttpClient();
}