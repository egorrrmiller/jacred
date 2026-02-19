using JacRed.Core.Interfaces;
using JacRed.Core.Models.Database;
using JacRed.Core.Utils;

namespace JacRed.Infrastructure.Services;

public class SubscribeService : ISubscribeService
{
    private readonly IMediaResolverService _mediaResolver;
    private readonly ISubscriptionRepository _repository;
    private readonly IQueriesRepository _queriesRepository;

    public SubscribeService(
        IMediaResolverService mediaResolver,
        ISubscriptionRepository repository,
        IQueriesRepository queriesRepository)
    {
        _mediaResolver = mediaResolver;
        _repository = repository;
        _queriesRepository = queriesRepository;
    }

    public async Task SubscribeAsync(long tmdbId, string uid)
    {
        var (search, altname) = await _mediaResolver.ResolveKpImdb(tmdbId.ToString(), null);
        var trackerQuery = StringConvert.ClearTitle($"{search} {altname}".Trim());

        if (string.IsNullOrWhiteSpace(trackerQuery))
            return;

        // Гарантируем, что запрос существует в таблице queries
        await _queriesRepository.TrackSearchQueryAsync(tmdbId, trackerQuery);

        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            Uid = uid,
            TmdbId = tmdbId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _repository.AddAsync(subscription);
    }

    public async Task UnSubscribeAsync(long tmdbId, string uid)
    {
        await _repository.RemoveAsync(tmdbId, uid);
    }

    public async Task<bool> CheckSubscribeAsync(long tmdbId, string uid)
    {
        return await _repository.ExistsAsync(tmdbId, uid);
    }
}