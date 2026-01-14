using System;
using System.Collections.Generic;
using JacRed.Core.Models.Details;

namespace JacRed.Api.Services;

public class TorrentSearchPipelineResult
{
    public IReadOnlyCollection<TorrentDetails> Items { get; init; } = Array.Empty<TorrentDetails>();
    public bool UsedTrackerFallback { get; init; }
}
