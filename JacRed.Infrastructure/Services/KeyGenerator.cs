using JacRed.Core.Interfaces;
using JacRed.Core.Utils;

namespace JacRed.Infrastructure.Services;

public class KeyGenerator : IKeyGenerator
{
    public string Build(string name, string originalName)
    {
        var searchName = StringConvert.SearchName(name);
        var searchOriginalName = StringConvert.SearchName(originalName);

        return $"{searchName}:{searchOriginalName}";
    }
}