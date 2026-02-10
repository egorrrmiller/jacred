using System.Text.RegularExpressions;
using BencodeNET.Parsing;
using BencodeNET.Torrents;

namespace JacRed.Core.Utils;

public static class BencodeTo
{
    #region Magnet

    public static string? Magnet(byte[]? torrent)
    {
        try
        {
            if (torrent == null)
                return null;

            var parser = new BencodeParser();
            var res = parser.Parse<Torrent>(torrent);

            var magnet = res.GetMagnetLink();
            if (res.OriginalInfoHash != null)
                magnet = Regex.Replace(magnet, @"urn:btih:[\w0-9]+", $"urn:btih:{res.OriginalInfoHash.ToLower()}",
                    RegexOptions.IgnoreCase);

            return magnet;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region SizeName

    public static string? SizeName(byte[]? torrent)
    {
        try
        {
            if (torrent == null)
                return null;

            var parser = new BencodeParser();
            var res = parser.Parse<Torrent>(torrent);

            return StringConvert.FormatSize(res.TotalSize);
        }
        catch
        {
            return null;
        }
    }

    #endregion

    public static long Size(byte[]? torrent)
    {
        try
        {
            if (torrent == null)
                return 0;

            var parser = new BencodeParser();
            var res = parser.Parse<Torrent>(torrent);

            return res.TotalSize;
        }
        catch
        {
            return 0;
        }
    }
}