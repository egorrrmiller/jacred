using Microsoft.Extensions.Configuration;

namespace JacRed.Core.Models.Options.TrackerConfigs;

public class RuTrackerSettings : BaseTrackerConfig
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

    [ConfigurationKeyName("timeout")] public int TimeOut { get; set; }

    [ConfigurationKeyName("older-than-min")]
    public long OlderThanMin { get; set; } = 120;

    [ConfigurationKeyName("limit")] public int Limit { get; set; } = 100;
}

public class Popular
{
    [ConfigurationKeyName("enable")] public bool Enable { get; set; } = false;

    [ConfigurationKeyName("timeout")] public int TimeOut { get; set; }

    [ConfigurationKeyName("max-pages")] public int MaxPages { get; set; }

    [ConfigurationKeyName("categories")] public IReadOnlyCollection<int> Categories { get; set; } = [];
}