using JacRed.Core.Interfaces;
using JacRed.Core.Utils;

namespace JacRed.Infrastructure.Services;

public class KeyGenerator : IKeyGenerator
{
    /// <summary>Формирует ключ поиска на основе названий.</summary>
    public string Build(string name, string originalName)
    {
        var searchName = StringConvert.SearchName(name);
        var searchOriginalName = StringConvert.SearchName(originalName);

        return $"{searchName}:{searchOriginalName}";
    }
}
