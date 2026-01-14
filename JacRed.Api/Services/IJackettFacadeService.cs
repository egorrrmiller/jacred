using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Core.Models;
using JacRed.Core.Models.Api;

namespace JacRed.Api.Services;

public interface IJackettFacadeService
{
    Task<RootObject> SearchJackettAsync(
        string apikey,
        string query,
        string title,
        string titleOriginal,
        int year,
        Dictionary<string, string> category,
        int isSerial,
        string? userAgent,
        string queryString);

    Task<IReadOnlyCollection<V1TorrentResponse>> SearchTorrentsAsync(
        string search,
        string altname,
        bool exact,
        string? type,
        string? sort,
        string? tracker,
        string? voice,
        string? videotype,
        long relased,
        long quality,
        long season,
        CancellationToken cancellationToken);

    Task<Dictionary<string, Dictionary<int, TorrentQuality>>> GetQualityInfoAsync(
        string name,
        string originalName,
        string? type,
        int page,
        int take);

    DateTime GetLastUpdateDb();
}
