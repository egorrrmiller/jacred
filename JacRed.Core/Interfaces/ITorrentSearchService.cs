using JacRed.Core.Models;
using JacRed.Core.Models.Details;

namespace JacRed.Core.Interfaces;

public interface ITorrentSearchService
{
    Task<List<TorrentDetails>> SearchByTitleAsync(
        string title,
        string originalTitle,
        int? year = null,
        int? mediaType = null,
        bool exact = false);

    Task<List<TorrentDetails>> SearchByQueryAsync(
        string query,
        int? mediaType = null,
        bool exact = false);

    Task<List<TorrentQuality>> GetQualityInfoAsync(
        string name,
        string originalName,
        string? type = null,
        int page = 1,
        int take = 1000);
}