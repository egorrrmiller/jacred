using Microsoft.Extensions.Configuration;

namespace JacRed.Core.Models.Options;

/// <summary>
///     Общие настройки для конкретного трекера.
/// </summary>
public class Tracker
{
    /// <summary>
    ///     Данные для авторизации на трекере.
    /// </summary>
    [ConfigurationKeyName("authorization")]
    public Authorization Authorization { get; set; } = new();
}