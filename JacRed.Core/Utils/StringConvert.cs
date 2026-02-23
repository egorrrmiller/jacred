using System.Text.RegularExpressions;

namespace JacRed.Core.Utils;

public static class StringConvert
{
    #region FindStartText

    public static string FindStartText(string data, string end, string? start = null)
    {
        try
        {
            return data.Substring(0, data.IndexOf(end));
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region FindLastText

    public static string FindLastText(string data, string start, string? end = null)
    {
        try
        {
            var res = data.Substring(data.IndexOf(start));

            if (end == null) return res;

            return FindStartText(res, end);
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region Remove

    public static string Remove(string data, string start, string end)
    {
        try
        {
            for (var i = 0; i < 10; i++)
            {
                var startIndex = data.IndexOf(start);

                if (startIndex == 0) break;

                var endIndex = data.IndexOf(end);

                if (endIndex == 0)
                {
                    data = data.Remove(startIndex);

                    break;
                }

                data = data.Remove(startIndex, endIndex - startIndex);
            }

            return data;
        }
        catch
        {
            return data;
        }
    }

    #endregion

    #region SearchName

    public static string SearchName(string val)
    {
        if (string.IsNullOrWhiteSpace(val))
            return null;

        val = val.ToLowerInvariant()
            .Replace("ё", "е")
            .Replace("щ", "ш")
            .Replace("n", "?")
            .Replace("e", "e");

        // Оставляем латиницу, кириллицу и цифры.
        val = Regex.Replace(val, "[^a-z0-9а-я]", "");

        return string.IsNullOrWhiteSpace(val) ? null : val;
    }

    #endregion

    #region FormatSize

    public static string FormatSize(long bytes)
    {
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var unitIndex = 0;
        double value = bytes;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }

    #endregion

    #region ParseQuality

    public static int ParseQuality(string? quality)
    {
        if (string.IsNullOrWhiteSpace(quality))
            return 0;

        var match = Regex.Match(quality, "(\\d{3,4})p", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var q))
            return q;

        if (int.TryParse(quality, out var numeric))
            return numeric;

        return 0;
    }

    #endregion

    #region ClearTitle

    public static string ClearTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        var cleared = Regex.Replace(title, @"[^a-zA-Zа-яА-ЯёЁ0-9\s]", " ");
        
        return Regex.Replace(cleared, @"\s+", " ").Trim();
    }

    #endregion
}