using System.Web;
using JacRed.Core.Extensions;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using MonoTorrent;

namespace JacRed.Infrastructure.Services;

/// <summary>
///     Объединяет дублирующиеся торренты, собирая лучшую информацию и единый magnet.
/// </summary>
public class TorrentMergerService : ITorrentMergerService
{
    /// <summary>
    ///     Схлопывает коллекцию торрентов по infohash, объединяя метаданные.
    /// </summary>
    public Task<List<TorrentDetails>> MergeAsync(IEnumerable<TorrentDetails> torrents)
    {
        var result = torrents
            .OrderByDescending(t => t.CreateTime)
            .ThenBy(t => t.TrackerName == "selezen")
            .GroupBy(t => GetInfoHash(t.Magnet))
            .Select(group => MergeGroup(group.ToList()))
            .ToList();

        return Task.FromResult(result);
    }

    /// <summary>
    ///     Объединяет одну группу дублей в единый объект.
    /// </summary>
    private TorrentDetails MergeGroup(List<TorrentDetails> group)
    {
        var first = group.First();
        var merged = (TorrentDetails)first.Clone();

        var announceUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(first.Magnet))
            foreach (var url in first.Magnet.AnnounceUrls() ?? [])
                announceUrls.Add(url);
        var voices = new HashSet<string>(first.Voices ?? [], StringComparer.OrdinalIgnoreCase);
        var languages = new HashSet<string>(first.Languages ?? [], StringComparer.OrdinalIgnoreCase);
        var seasons = new HashSet<int>(first.Seasons ?? []);

        var titleOverride = first.TrackerName == "kinozal" ? first.Title : null;
        var torrentName = !string.IsNullOrWhiteSpace(first.Magnet?.AnnounceName())
            ? first.Magnet.AnnounceName()
            : null;

        // Сведение метаданных
        foreach (var t in group.Skip(1))
        {
            var tAnnounceUrls = t.Magnet?.AnnounceUrls();
            if (tAnnounceUrls?.Any() == true)
                foreach (var url in tAnnounceUrls)
                    announceUrls.Add(url);

            if (t.Voices?.Any() == true)
            {
                foreach (var v in t.Voices)
                    voices.Add(v);
                if (t.TrackerName == "kinozal")
                    titleOverride = t.Title;
            }

            if (t.Languages?.Any() == true)
                foreach (var lang in t.Languages)
                    languages.Add(lang);

            if (t.Seasons?.Any() == true)
                foreach (var s in t.Seasons)
                    seasons.Add(s);

            if (t.TrackerName == "kinozal")
                titleOverride = t.Title;

            if (string.IsNullOrWhiteSpace(torrentName) && !string.IsNullOrWhiteSpace(t.Magnet?.AnnounceName()))
                torrentName = t.Magnet?.AnnounceName();

            if (merged.Relased == 0 && t.Relased > 0)
                merged.Relased = t.Relased;

            if (merged.Quality == 0 && t.Quality > 0)
                merged.Quality = t.Quality;

            if (string.IsNullOrWhiteSpace(merged.VideoType) && !string.IsNullOrWhiteSpace(t.VideoType))
                merged.VideoType = t.VideoType;

            if (merged.Size == 0.0 && t.Size > 0)
            {
                merged.Size = t.Size;
                merged.SizeName = t.SizeName;
            }

            if (string.IsNullOrWhiteSpace(merged.Name) && !string.IsNullOrWhiteSpace(t.Name))
                merged.Name = t.Name;

            if (string.IsNullOrWhiteSpace(merged.OriginalName) && !string.IsNullOrWhiteSpace(t.OriginalName))
                merged.OriginalName = t.OriginalName;

            if (merged.Types == null && t.Types?.Any() == true)
                merged.Types = t.Types;

            if (string.IsNullOrWhiteSpace(merged.SourceSeasonNumber) &&
                !string.IsNullOrWhiteSpace(t.SourceSeasonNumber))
                merged.SourceSeasonNumber = t.SourceSeasonNumber;

            if (string.IsNullOrWhiteSpace(merged.SourceSeasonOrder) && !string.IsNullOrWhiteSpace(t.SourceSeasonOrder))
                merged.SourceSeasonOrder = t.SourceSeasonOrder;

            if (t.TrackerName != "selezen")
            {
                if (t.Sid > merged.Sid) merged.Sid = t.Sid;
                if (t.Pir > merged.Pir) merged.Pir = t.Pir;
            }

            if (t.CreateTime > merged.CreateTime)
                merged.CreateTime = t.CreateTime;
        }

        var mergedMagnet = BuildMagnet(GetInfoHash(merged.Magnet), torrentName, announceUrls);
        if (!string.IsNullOrWhiteSpace(mergedMagnet))
            merged.Magnet = mergedMagnet;

        if (!string.IsNullOrWhiteSpace(titleOverride))
        {
            merged.Title = titleOverride;
            if (voices.Any())
                merged.Title += $" | {string.Join(" | ", voices)}";
        }

        merged.Voices = voices;
        merged.Languages = languages;
        merged.Seasons = seasons;

        return merged;
    }

    /// <summary>
    ///     Достаёт infohash из магнита, при ошибке возвращает исходную строку.
    /// </summary>
    private string GetInfoHash(string magnet)
    {
        try
        {
            return MagnetLink.Parse(magnet).InfoHashes.V1OrV2.ToHex();
        }
        catch
        {
            return magnet;
        }
    }

    /// <summary>
    ///     Собирает новую magnet-ссылку из infohash, имени и списка трекеров.
    /// </summary>
    private string? BuildMagnet(string infoHash, string name, HashSet<string> announceUrls)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
            return null;

        var magnet = $"magnet:?xt=urn:btih:{infoHash.ToLower()}";

        if (!string.IsNullOrWhiteSpace(name))
            magnet += $"&dn={HttpUtility.UrlEncode(name)}";

        foreach (var tr in announceUrls ?? [])
        {
            if (string.IsNullOrWhiteSpace(tr)) continue;
            var encodedTr = tr.Contains("/") || tr.Contains(":") ? HttpUtility.UrlEncode(tr) : tr;
            if (!magnet.Contains(encodedTr))
                magnet += $"&tr={encodedTr}";
        }

        return magnet;
    }
}