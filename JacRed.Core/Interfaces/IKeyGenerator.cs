namespace JacRed.Core.Interfaces;

/// <summary>
///     Генерирует ключ вида "search_name:search_originalname"
/// </summary>
public interface IKeyGenerator
{
    string Build(string name, string originalname);
}