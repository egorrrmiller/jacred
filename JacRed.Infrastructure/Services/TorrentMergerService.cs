using System.Web;
using JacRed.Core.Extensions;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using MonoTorrent;

namespace JacRed.Infrastructure.Services;

public class TorrentMergerService : ITorrentMergerService
{
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

    private TorrentDetails MergeGroup(List<TorrentDetails> group)
    {
        var first = group.First();
        var merged = (TorrentDetails)first.Clone();

        // === Инициализация с защитой от null ===
        var announceUrls = new HashSet<string>(first.Magnet.AnnounceUrls() ?? [], StringComparer.OrdinalIgnoreCase);
        var voices = new HashSet<string>(first.Voices, StringComparer.OrdinalIgnoreCase);
        var languages = new HashSet<string>(first.Languages, StringComparer.OrdinalIgnoreCase);
        var seasons = new HashSet<int>(first.Seasons);

        var titleOverride = first.TrackerName == "kinozal" ? first.Title : null;
        var torrentName = !string.IsNullOrWhiteSpace(first.Magnet.AnnounceName()) ? first.Magnet.AnnounceName() : null;

        // === Обработка остальных торрентов в группе ===
        foreach (var t in group.Skip(1))
        {
            // Announce URLs
            var tAnnounceUrls = t.Magnet.AnnounceUrls();
            if (tAnnounceUrls?.Any() == true)
                foreach (var url in tAnnounceUrls)
                    announceUrls.Add(url); // HashSet сам игнорирует дубли

            // Voices
            if (t.Voices?.Any() == true)
            {
                foreach (var v in t.Voices)
                    voices.Add(v);
                if (t.TrackerName == "kinozal")
                    titleOverride = t.Title;
            }

            // Languages
            if (t.Languages?.Any() == true)
                foreach (var lang in t.Languages)
                    languages.Add(lang);

            // Seasons
            if (t.Seasons?.Any() == true)
                foreach (var s in t.Seasons)
                    seasons.Add(s);

            // Title override (kinozal priority)
            if (t.TrackerName == "kinozal")
                titleOverride = t.Title;

            // Torrent name
            if (string.IsNullOrWhiteSpace(torrentName) && !string.IsNullOrWhiteSpace(t.Magnet.AnnounceName()))
                torrentName = t.Magnet.AnnounceName();

            // Ffprobe
            if (merged.Ffprobe == null && t.Ffprobe?.Any() == true)
                merged.Ffprobe = t.Ffprobe; // List<ffStream>

            // ffprobe_tryingdata
            if (merged.FfprobeTryCount == 0 && t.FfprobeTryCount > 0)
                merged.FfprobeTryCount = t.FfprobeTryCount;

            // relased
            if (merged.Relased == 0 && t.Relased > 0)
                merged.Relased = t.Relased;

            // quality
            if (merged.Quality == 0 && t.Quality > 0)
                merged.Quality = t.Quality;

            // videotype
            if (string.IsNullOrWhiteSpace(merged.VideoType) && !string.IsNullOrWhiteSpace(t.VideoType))
                merged.VideoType = t.VideoType;

            // size / sizeName
            if (merged.Size == 0.0 && t.Size > 0)
            {
                merged.Size = t.Size;
                merged.SizeName = t.SizeName;
            }

            // name / originalname
            if (string.IsNullOrWhiteSpace(merged.Name) && !string.IsNullOrWhiteSpace(t.Name))
                merged.Name = t.Name;

            if (string.IsNullOrWhiteSpace(merged.OriginalName) && !string.IsNullOrWhiteSpace(t.OriginalName))
                merged.OriginalName = t.OriginalName;

            // types
            if (merged.Types == null && t.Types?.Any() == true)
                merged.Types = t.Types;

            // _sn / _so
            if (string.IsNullOrWhiteSpace(merged.SourceSeasonNumber) &&
                !string.IsNullOrWhiteSpace(t.SourceSeasonNumber))
                merged.SourceSeasonNumber = t.SourceSeasonNumber;

            if (string.IsNullOrWhiteSpace(merged.SourceSeasonOrder) && !string.IsNullOrWhiteSpace(t.SourceSeasonOrder))
                merged.SourceSeasonOrder = t.SourceSeasonOrder;

            // S&P (кроме selezen)
            if (t.TrackerName != "selezen")
            {
                if (t.Sid > merged.Sid) merged.Sid = t.Sid;
                if (t.Pir > merged.Pir) merged.Pir = t.Pir;
            }

            // Время
            if (t.CreateTime > merged.CreateTime)
                merged.CreateTime = t.CreateTime;
        }

        // === Сборка магнита ===
        merged.Magnet = BuildMagnet(GetInfoHash(merged.Magnet), torrentName, announceUrls);

        // === Формирование заголовка ===
        if (!string.IsNullOrWhiteSpace(titleOverride))
        {
            merged.Title = titleOverride;
            if (voices.Any())
                merged.Title += $" | {string.Join(" | ", voices)}";
        }

        // === Финальное присваивание ===
        merged.Voices = voices;
        merged.Languages = languages;
        merged.Seasons = seasons;

        return merged;
    }

    private string GetInfoHash(string magnet)
    {
        try
        {
            return MagnetLink.Parse(magnet).InfoHashes.V1OrV2.ToHex();
        }
        catch
        {
            return magnet; // fallback
        }
    }

    private string BuildMagnet(string infoHash, string name, HashSet<string> announceUrls)
    {
        var magnet = $"magnet:?xt=urn:btih:{infoHash.ToLower()}";

        if (!string.IsNullOrWhiteSpace(name))
            magnet += $"&dn={HttpUtility.UrlEncode(name)}";

        foreach (var tr in announceUrls)
        {
            if (string.IsNullOrWhiteSpace(tr)) continue;
            var encodedTr = tr.Contains("/") || tr.Contains(":") ? HttpUtility.UrlEncode(tr) : tr;
            if (!magnet.Contains(encodedTr))
                magnet += $"&tr={encodedTr}";
        }

        return magnet;
    }
}