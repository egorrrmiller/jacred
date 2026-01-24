using Microsoft.Extensions.Configuration;

namespace JacRed.Core.Models.Options.TrackerConfigs;

public class RuTrackerSettings
{
    /// <summary>
    ///     Точечный рефреш всех торрентов рутрекера в базе
    /// </summary>
    [ConfigurationKeyName("refresh")]
    public RefreshSettings Refresh { get; set; } = new();

    /// <summary>
    ///     Обновление популярных раздач по категориям
    /// </summary>
    [ConfigurationKeyName("popular")]
    public Popular Popular { get; set; } = new();

    /// <summary>
    ///     Данные для авторизации на трекере.
    /// </summary>
    [ConfigurationKeyName("authorization")]
    public Authorization Authorization { get; set; } = new();
}

public class RefreshSettings
{
    [ConfigurationKeyName("enable")] public bool Enable { get; set; } = false;

    [ConfigurationKeyName("timeout")] public long TimeOut { get; set; }

    [ConfigurationKeyName("older-than-min")]
    public long OlderThanMin { get; set; } = 120;

    [ConfigurationKeyName("limit")] public long Limit { get; set; } = 100;
}

public class Popular
{
    [ConfigurationKeyName("enable")] public bool Enable { get; set; } = false;

    [ConfigurationKeyName("timeout")] public long TimeOut { get; set; }

    [ConfigurationKeyName("max-pages")] public long MaxPages { get; set; }

    [ConfigurationKeyName("categories")] public IReadOnlyCollection<long> Categories { get; set; } = [];
}