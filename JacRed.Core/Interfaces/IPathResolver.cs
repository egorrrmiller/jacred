namespace JacRed.Core.Interfaces;

/// <summary>
/// Резолвит путь к файлу по ключу (например, Data/fdb/ab/cdef...)
/// </summary>
public interface IPathResolver
{
    string GenerateFilePath(string key);
}