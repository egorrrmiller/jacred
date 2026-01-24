using JacRed.Core.Models;
using JacRed.Core.Models.Api;

namespace JacRed.Core.Interfaces;

/// <summary>
///     Конвейер поиска торрентов с применением всех шагов фильтрации/обработки.
/// </summary>
public interface ITorrentSearchPipeline
{
    /// <summary>
    ///     Выполняет поиск по запросу с учетом настроек и возвращает итоговый результат пайплайна.
    /// </summary>
    Task<TorrentSearchPipelineResult> SearchAsync(
        TorrentSearchRequest request);
}