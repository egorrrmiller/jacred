using JacRett.Core.Models.Details;

namespace JacRett.Core.Models;

public class TorrentSearchPipelineResult
{
    public IReadOnlyCollection<TorrentDetails> Items { get; init; } = [];
}