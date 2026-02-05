using System.Web;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using MonoTorrent;

namespace JacRed.Infrastructure.Services;

public class TorrentMergerService : ITorrentMergerService
{
    public Task<List<TorrentDetails>> MergeAsync(IEnumerable<TorrentDetails> torrents)
    {
        var temp =
            new Dictionary<string, (TorrentDetails torrent, string? title, string? name, List<string> announceUrls)>();

        foreach (var torrent in torrents
                     .OrderByDescending(t => t.CreateTime)
                     .ThenBy(t => t.TrackerName == "selezen"))
        {
            if (string.IsNullOrWhiteSpace(torrent.Magnet))
            {
                var fallbackKey = $"nomagnet:{torrent.Url ?? Guid.NewGuid().ToString()}";
                if (!temp.ContainsKey(fallbackKey))
                    temp.Add(fallbackKey, ((TorrentDetails)torrent.Clone(), null, null, []));
                continue;
            }

            MagnetLink magnetLink;
            try
            {
                magnetLink = MagnetLink.Parse(torrent.Magnet);
            }
            catch
            {
                var fallbackKey = $"nomagnet:{torrent.Url ?? Guid.NewGuid().ToString()}";
                if (!temp.ContainsKey(fallbackKey))
                    temp.Add(fallbackKey, ((TorrentDetails)torrent.Clone(), null, null, []));
                continue;
            }

            var hex = magnetLink.InfoHashes.V1OrV2.ToHex();

            if (!temp.TryGetValue(hex, out var entry))
            {
                temp.Add(hex,
                    ((TorrentDetails)torrent.Clone(),
                        torrent.TrackerName == "kinozal" ? torrent.Title : null,
                        magnetLink.Name,
                        magnetLink.AnnounceUrls?.ToList() ?? []));
                continue;
            }

            if (!entry.torrent.TrackerName.Contains(torrent.TrackerName))
                entry.torrent.TrackerName += $", {torrent.TrackerName}";

            void UpdateMagnet()
            {
                var updated = BuildMagnet(hex, entry.name, entry.announceUrls);
                if (!string.IsNullOrWhiteSpace(updated))
                    entry.torrent.Magnet = updated;
            }

            if (string.IsNullOrWhiteSpace(entry.name) && !string.IsNullOrWhiteSpace(magnetLink.Name))
            {
                entry.name = magnetLink.Name;
                temp[hex] = entry;
                UpdateMagnet();
            }

            if (magnetLink.AnnounceUrls != null && magnetLink.AnnounceUrls.Count > 0)
            {
                entry.announceUrls.AddRange(magnetLink.AnnounceUrls);
                UpdateMagnet();
            }

            void UpdateTitle()
            {
                if (string.IsNullOrWhiteSpace(entry.title))
                    return;

                var title = entry.title;

                if (entry.torrent.Voices != null && entry.torrent.Voices.Count > 0)
                    title += $" | {string.Join(" | ", entry.torrent.Voices)}";

                entry.torrent.Title = title;
            }

            if (torrent.TrackerName == "kinozal")
            {
                entry.title = torrent.Title;
                temp[hex] = entry;
                UpdateTitle();
            }

            if (torrent.Voices != null && torrent.Voices.Count > 0)
            {
                if (entry.torrent.Voices == null)
                    entry.torrent.Voices = [..torrent.Voices];
                else
                    foreach (var v in torrent.Voices)
                        entry.torrent.Voices.Add(v);

                UpdateTitle();
            }

            if (torrent.TrackerName != "selezen")
            {
                if (torrent.Sid > entry.torrent.Sid)
                    entry.torrent.Sid = torrent.Sid;

                if (torrent.Pir > entry.torrent.Pir)
                    entry.torrent.Pir = torrent.Pir;
            }

            if (torrent.CreateTime > entry.torrent.CreateTime)
                entry.torrent.CreateTime = torrent.CreateTime;

            if (torrent.Languages != null && torrent.Languages.Count > 0)
            {
                if (entry.torrent.Languages == null)
                    entry.torrent.Languages = [];

                foreach (var v in torrent.Languages)
                    entry.torrent.Languages.Add(v);
            }

            if (entry.torrent.Ffprobe == null && torrent.Ffprobe != null)
                entry.torrent.Ffprobe = torrent.Ffprobe;

            temp[hex] = entry;
        }

        return Task.FromResult(temp.Select(i => i.Value.torrent).ToList());
    }

    private string? BuildMagnet(string infoHash, string? name, List<string> announceUrls)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
            return null;

        var magnet = $"magnet:?xt=urn:btih:{infoHash.ToLower()}";

        if (!string.IsNullOrWhiteSpace(name))
            magnet += $"&dn={HttpUtility.UrlEncode(name)}";

        if (announceUrls.Count > 0)
            foreach (var tr in announceUrls)
            {
                if (string.IsNullOrWhiteSpace(tr))
                    continue;

                var encodedTr = tr.Contains("/") || tr.Contains(":") ? HttpUtility.UrlEncode(tr) : tr;
                if (!magnet.Contains(encodedTr))
                    magnet += $"&tr={encodedTr}";
            }

        return magnet;
    }
}