using System.Text;
using JacRed.Core.Enums;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using JacRed.Core.Models.Options;
using JacRed.Core.Utils;
using Microsoft.Extensions.Options;

namespace JacRed.Infrastructure.Services.Trackers;

public abstract class BaseTrackerSearch : ITrackerRefreshProvider
{
    protected readonly Config Config;
    protected readonly ICacheService CacheService;
    protected readonly HttpService HttpService;

    protected BaseTrackerSearch(IOptions<Config> config, HttpService httpService, ICacheService cacheService)
    {
        HttpService = httpService;
        CacheService = cacheService;
        Config = config.Value;
    }

    protected static readonly Encoding RuEncoding = Encoding.GetEncoding("windows-1251");

    public abstract TrackerType Tracker { get; }
    public abstract string TrackerName { get; }
    public abstract string Host { get; }

    public virtual Task<IReadOnlyCollection<TorrentDetails>> SearchAsync(string query)
    {
        return Task.FromResult<IReadOnlyCollection<TorrentDetails>>([]);
    }

    public virtual Task InvokeAsync()
    {
        return Task.CompletedTask;
    }
}