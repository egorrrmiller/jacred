using Newtonsoft.Json.Linq;
using NpgsqlTypes;

namespace JacRed.Core.Models.Database;

/// <summary>
/// Раздачи (TorrentDetails). Поиск по произвольному тексту через search_tsv + trigram.
/// </summary>
public class Torrent
{
    /// <summary>
    /// Уникальный идентификатор раздачи.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Название трекера (например: "rutor", "kinozal").
    /// </summary>
    public string TrackerName { get; set; } = null!;

    /// <summary>
    /// Типы раздачи (например: ["serial", "hd"]).
    /// </summary>
    public string[] Types { get; set; } = null!;

    /// <summary>
    /// Ссылка на страницу раздачи на трекере.
    /// </summary>
    public string Url { get; set; } = null!;

    /// <summary>
    /// Отображаемое название раздачи.
    /// </summary>
    public string Title { get; set; } = null!;

    /// <summary>
    /// Количество сидеров.
    /// </summary>
    public int Sid { get; set; }

    /// <summary>
    /// Количество пиров.
    /// </summary>
    public int Pir { get; set; }

    /// <summary>
    /// Человекочитаемый размер (например: "2.1 GB").
    /// </summary>
    public string? SizeName { get; set; }

    /// <summary>
    /// Дата создания раздачи на трекере.
    /// </summary>
    public DateTime CreateTime { get; set; }

    /// <summary>
    /// Дата последнего обновления информации о раздаче в системе.
    /// </summary>
    public DateTime UpdateTime { get; set; }

    /// <summary>
    /// Время последней проверки доступности раздачи.
    /// </summary>
    public DateTime CheckTime { get; set; }

    /// <summary>
    /// Magnet-ссылка.
    /// </summary>
    public string? Magnet { get; set; }

    /// <summary>
    /// Оригинальное имя файла или каталога.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Оригинальное название медиа.
    /// </summary>
    public string? OriginalName { get; set; }

    /// <summary>
    /// Год выпуска контента.
    /// </summary>
    public int Relased { get; set; }

    /// <summary>
    /// Языки аудиодорожек.
    /// </summary>
    public string[]? Languages { get; set; }

    /// <summary>
    /// Данные о медиапотоках, полученные через ffprobe.
    /// </summary>
    public JToken? Ffprobe { get; set; }

    /// <summary>
    /// Счётчик попыток получить ffprobe-данные.
    /// </summary>
    public int FfprobeTryCount { get; set; }

    /// <summary>
    /// Номер сезона, указанный на трекере.
    /// </summary>
    public string? SourceSeasonNumber { get; set; }

    /// <summary>
    /// Порядок или диапазон сезонов.
    /// </summary>
    public string? SourceSeasonOrder { get; set; }

    /// <summary>
    /// Размер файла в гигабайтах.
    /// </summary>
    public double Size { get; set; }

    /// <summary>
    /// Качество: 2160, 1080, 720 и т.д.
    /// </summary>
    public int Quality { get; set; }

    /// <summary>
    /// Тип видео: "WEB-DL", "BluRay", "HDTV", "CAM".
    /// </summary>
    public string? VideoType { get; set; }

    /// <summary>
    /// Озвучка/перевод (например: "ColdFilm", "NewStudio").
    /// </summary>
    public string[]? Voices { get; set; }

    /// <summary>
    /// Номера сезонов, присутствующих в раздаче.
    /// </summary>
    public int[]? Seasons { get; set; }

    /// <summary>
    /// Полнотекстовый вектор для поиска по Title/Name/OriginalName.
    /// </summary>
    public NpgsqlTsVector? SearchTsv { get; set; }
}