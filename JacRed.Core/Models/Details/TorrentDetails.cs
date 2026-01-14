using JacRed.Core.Models.Tracks;

namespace JacRed.Core.Models.Details;

/// <summary>
///     Полные детали торрента, включая базовые и медиа-специфичные поля.
/// </summary>
public class TorrentDetails : ICloneable
{
    /// <summary>
    /// Идентификатор раздачи.
    /// </summary>
    public Guid Id { get; set; }
    
    /// <summary>
    ///     Название трекера (например: "rutor", "kinozal"). Используется для идентификации источника и отображения иконки.
    /// </summary>
    public string TrackerName { get; set; } = null!;

    /// <summary>
    ///     Типы раздачи (например: ["serial", "hd"]). Определяют категорию контента и разрешение.
    /// </summary>
    public string[] Types { get; set; } = null!;

    /// <summary>
    ///     Ссылка на страницу раздачи на трекере. Позволяет перейти к оригиналу.
    /// </summary>
    public string Url { get; set; } = null!;

    /// <summary>
    ///     Отображаемое название раздачи, может включать озвучку, качество и другие детали.
    /// </summary>
    public string Title { get; set; } = null!;

    /// <summary>
    ///     Количество сидеров (раздающих) на трекере. Используется при сортировке по активности.
    /// </summary>
    public int Sid { get; set; }

    /// <summary>
    ///     Количество пиров (качающих) на трекере. Характеризует популярность и активность.
    /// </summary>
    public int Pir { get; set; }

    /// <summary>
    ///     Размер раздачи в человекочитаемом виде (например: "2.1 GB").
    /// </summary>
    public string? SizeName { get; set; } = null!;

    /// <summary>
    ///     Дата создания раздачи на трекере (по данным источника).
    /// </summary>
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    ///     Дата последнего обновления информации о раздаче в системе.
    /// </summary>
    public DateTime UpdateTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    ///     Время последней проверки доступности раздачи (магнет-ссылки).
    /// </summary>
    public DateTime CheckTime { get; set; } = DateTime.Now;

    /// <summary>
    ///     Magnet-ссылка на раздачу. Используется для передачи в торрент-клиент.
    /// </summary>
    public string? Magnet { get; set; } = null!;

    /// <summary>
    ///     Оригинальное имя файла или каталога из торрент-метаданных.
    /// </summary>
    public string? Name { get; set; } = null!;

    /// <summary>
    ///     Оригинальное название медиа (без озвучки и дополнений).
    /// </summary>
    public string? OriginalName { get; set; } = null!;

    /// <summary>
    ///     Год выпуска контента. Используется для фильтрации и сопоставления.
    /// </summary>
    public int Relased { get; set; }

    /// <summary>
    ///     Языки аудиодорожек, доступные в раздаче (например: "Русский", "English").
    /// </summary>
    public HashSet<string>? Languages { get; set; }

    /// <summary>
    ///     Данные о медиапотоках, полученные через ffprobe (видео, аудио, субтитры).
    /// </summary>
    public List<ffStream>? Ffprobe { get; set; }

    /// <summary>
    ///     Счётчик попыток получить ffprobe-данные. 0 - не начинали.
    /// </summary>
    public int FfprobeTryCount { get; set; }

    /// <summary>
    ///     Номер сезона, указанный на трекере (если это сериал).
    /// </summary>
    public string? SourceSeasonNumber { get; set; } = null!;

    /// <summary>
    ///     Порядок или диапазон сезонов, как указано на источнике (например: "2 из 5").
    /// </summary>
    public string? SourceSeasonOrder { get; set; } = null!;

    /// <summary>
    ///     Размер файла в гигабайтах (число с плавающей точкой)
    /// </summary>
    public double Size { get; set; }

    /// <summary>
    ///     Качество: 2160, 1080, 720 и т.д. (в пикселях по высоте)
    /// </summary>
    public int Quality { get; set; }

    /// <summary>
    ///     Тип видео: "WEB-DL", "BluRay", "HDTV", "CAM", и т.д.
    /// </summary>
    public string? VideoType { get; set; } = null!;

    /// <summary>
    ///     Озвучка/перевод (например: "ColdFilm", "NewStudio", "Оригинал")
    /// </summary>
    public HashSet<string>? Voices { get; set; }

    /// <summary>
    ///     Номера сезонов, присутствующих в раздаче
    /// </summary>
    public HashSet<int>? Seasons { get; set; }

    /// <summary>
    ///     Создаёт полную копию объекта (поверхностное копирование)
    /// </summary>
    /// <returns>Клон объекта</returns>
    public object Clone()
    {
        return MemberwiseClone();
    }
}
