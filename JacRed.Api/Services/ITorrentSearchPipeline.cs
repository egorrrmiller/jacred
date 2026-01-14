using System.Threading;
using System.Threading.Tasks;
using JacRed.Core.Models.Api;

namespace JacRed.Api.Services;

public interface ITorrentSearchPipeline
{
    Task<TorrentSearchPipelineResult> SearchAsync(
        TorrentSearchRequest request,
        CancellationToken cancellationToken = default);
}
