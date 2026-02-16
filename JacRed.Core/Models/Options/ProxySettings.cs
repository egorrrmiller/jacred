using Microsoft.Extensions.Configuration;

namespace JacRed.Core.Models.Options;

/// <summary>
///     Настройки прокси-серверов.
/// </summary>
public class ProxySettings
{
    /// <summary>
    ///     Игнорировать прокси для локальных адресов.
    /// </summary>
    [ConfigurationKeyName("bypass-on-local")]
    public bool BypassOnLocal { get; set; }

    /// <summary>
    ///     Список адресов прокси-серверов (например, "http://proxy:8080").
    /// </summary>
    [ConfigurationKeyName("list")]
    public List<string> List { get; set; } = [];

    /// <summary>
    ///     Пароль для авторизации на прокси (если требуется).
    /// </summary>
    [ConfigurationKeyName("password")]
    public string? Password { get; set; }

    /// <summary>
    ///     (Устарело/Не используется) Шаблон URL, для которых применять прокси.
    /// </summary>
    [ConfigurationKeyName("pattern")]
    public string? Pattern { get; set; }

    /// <summary>
    ///     Использовать ли авторизацию (логин/пароль) для прокси.
    /// </summary>
    [ConfigurationKeyName("use-auth")]
    public bool UseAuth { get; set; }

    /// <summary>
    ///     Имя пользователя для авторизации на прокси.
    /// </summary>
    [ConfigurationKeyName("username")]
    public string? Username { get; set; }
}