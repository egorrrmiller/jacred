using JacRed.Core.Models.Details;
using JacRed.Core.Models.Tracks;

namespace JacRed.Core.Interfaces;

public interface IMediaAnalyzerService
{
    public Task LoadExistingDataAsync();

    public bool ShouldAnalyze(string[] types);

    public Task<List<ffStream>> GetStreamsAsync(string magnet, string[]? types = null, bool onlyCache = false);

    public Task AnalyzeAsync(string magnet, string[]? types = null);

    public Task<HashSet<string>> ExtractLanguagesAsync(TorrentDetails torrent, List<ffStream>? streams = null);
}