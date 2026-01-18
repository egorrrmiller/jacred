using JacRed.Core.Models.Details;

namespace JacRed.Core.Models;

public class TorrentSearchPipelineResult
{
    public IReadOnlyCollection<TorrentDetails> Items { get; init; } = Array.Empty<TorrentDetails>();
    public bool UsedTrackerFallback { get; init; }
}