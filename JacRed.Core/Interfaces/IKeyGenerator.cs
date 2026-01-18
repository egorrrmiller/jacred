namespace JacRed.Core.Interfaces;

/// <summary>
///     Формирует стабильный ключ для поиска/хранилища на основе пары названий.
/// </summary>
public interface IKeyGenerator
{
    /// <summary>
    ///     Строит ключ из локализованного и оригинального названия в формате "search_name:search_originalname".
    /// </summary>
    string Build(string name, string originalName);
}