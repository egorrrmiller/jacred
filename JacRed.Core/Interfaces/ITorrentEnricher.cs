using JacRed.Core.Models.Details;

namespace JacRed.Core.Interfaces;

public interface ITorrentEnricher
{
	TorrentDetails EnrichAndConvert(TorrentBaseDetails torrent);

	Task<TorrentDetails> EnrichAndConvertAsync(TorrentBaseDetails torrent);
}