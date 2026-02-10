using JacRed.Core.Models.Details;

namespace JacRed.Core.Models;

public class TorrentSearchPipelineResult
{
    public IReadOnlyCollection<TorrentDetails> Items { get; init; } = [];
}