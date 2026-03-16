using JacRett.Core.Interfaces;
using JacRett.Core.Utils;

namespace JacRett.Infrastructure.Services;

/// <summary>
///     Генератор стабильных ключей поиска на основе названий.
/// </summary>
public class KeyGenerator : IKeyGenerator
{
    /// <summary>
    ///     Строит ключ из локализованного и оригинального названия в формате "search_name:search_originalname".
    /// </summary>
    public string Build(string name, string originalName)
    {
        var searchName = StringConvert.SearchName(name);
        var searchOriginalName = StringConvert.SearchName(originalName);

        return $"{searchName}:{searchOriginalName}";
    }
}