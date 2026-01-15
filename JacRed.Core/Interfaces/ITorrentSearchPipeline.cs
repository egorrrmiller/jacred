using JacRed.Core.Models;
using JacRed.Core.Models.Api;

namespace JacRed.Core.Interfaces;

public interface ITorrentSearchPipeline
{
    Task<TorrentSearchPipelineResult> SearchAsync(
        TorrentSearchRequest request,
        CancellationToken cancellationToken = default);
}