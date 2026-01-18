using System.Text.Json.Serialization;

namespace JacRed.Core.Models.Api;

/// <summary>
///     Модель результата поиска торрента, возвращаемого API.
///     Содержит основную информацию о раздаче, включая метаданные, разрешения и мультимедиа-анализ.
/// </summary>
public class Result
{
    /// <summary>
    ///     Внутренний идентификатор трекера (например, rutracker, nnmclub).
    /// </summary>
    [JsonPropertyName("TrackerId")]
    public string TrackerId { get; set; } = null!;

    /// <summary>
    ///     Тип трекера: public / private. Для всех наших индексаторов возвращаем public.
    /// </summary>
    [JsonPropertyName("TrackerType")]
    public string TrackerType { get; set; } = "public";

    /// <summary>
    ///     Название трекера, с которого получена раздача (например, rutracker.org, nnm-club.me).
    /// </summary>
    [JsonPropertyName("Tracker")]
    public string Tracker { get; set; } = null!;

    /// <summary>
    ///     Уникальный идентификатор результата. Совместимо с Jackett (обычно Magnet или ссылка).
    /// </summary>
    [JsonPropertyName("Guid")]
    public string Guid { get; set; } = null!;

    /// <summary>
    ///     Ссылка для загрузки (обычно совпадает с MagnetUri, если нет прямой ссылки).
    /// </summary>
    [JsonPropertyName("Link")]
    public string Link { get; set; } = null!;

    /// <summary>
    ///     Ссылка на обсуждение/комментарии раздачи (детальная страница).
    /// </summary>
    [JsonPropertyName("Comments")]
    public string Comments { get; set; } = null!;

    /// <summary>
    ///     Ссылка на страницу раздачи на трекере. Используется для перехода к деталям.
    /// </summary>
    [JsonPropertyName("Details")]
    public string Details { get; set; } = null!;

    /// <summary>
    ///     Основное название раздачи, как указано на трекере. Может включать год, качество, рип и др.
    ///     Пример: "The Matrix 1999 BDRip 1080p".
    /// </summary>
    [JsonPropertyName("Title")]
    public string Title { get; set; } = null!;

    /// <summary>
    ///     Размер раздачи в байтах. Используется для фильтрации и сортировки.
    ///     Передаётся как double для поддержки очень больших значений.
    /// </summary>
    [JsonPropertyName("Size")]
    public double Size { get; set; }

    /// <summary>
    ///     Дата публикации раздачи на трекере. Может отличаться от года выпуска фильма.
    ///     Используется для сортировки по актуальности.
    /// </summary>
    [JsonPropertyName("PublishDate")]
    public DateTime PublishDate { get; set; }

    /// <summary>
    ///     Идентификаторы категорий трекера (например, фильм, сериал, музыка, игра).
    ///     Позволяет фильтровать результаты по типу контента.
    /// </summary>
    [JsonPropertyName("Category")]
    public HashSet<int> Category { get; set; } = new();

    /// <summary>
    ///     Человекочитаемое описание категории (например, "Фильмы", "ТВ"). Опционально.
    /// </summary>
    [JsonPropertyName("CategoryDesc")]
    public string CategoryDesc { get; set; } = null!;

    /// <summary>
    ///     Количество сидеров (пользователей, раздающих файл). Используется для оценки доступности раздачи.
    /// </summary>
    [JsonPropertyName("Seeders")]
    public int Seeders { get; set; }

    /// <summary>
    ///     Общее количество участников (сидеры + личи). Используется для расчёта соотношения лич/сид.
    /// </summary>
    [JsonPropertyName("Peers")]
    public int Peers { get; set; }

    /// <summary>
    ///     Количество загрузок/захватов (в Jackett называется Grabs). У нас нет данных — оставляем 0.
    /// </summary>
    [JsonPropertyName("Grabs")]
    public int Grabs { get; set; }

    /// <summary>
    ///     Магнит-хеш (btih) если удалось распарсить magnet.
    /// </summary>
    [JsonPropertyName("InfoHash")]
    public string? InfoHash { get; set; }

    /// <summary>
    ///     Magnet-ссылка для запуска загрузки раздачи через торрент-клиент. Обязательное поле.
    /// </summary>
    [JsonPropertyName("MagnetUri")]
    public string MagnetUri { get; set; } = null!;

    /// <summary>
    ///     Языки аудио- и субтитровых дорожек, извлечённые через ffprobe или парсинг названия.
    ///     Используется для фильтрации по языку (например, только "ru").
    /// </summary>
    [JsonPropertyName("languages")]
    public HashSet<string> Languages { get; set; } = new();

    /// <summary>
    ///     Дополнительная метаинформация о торренте: хэш, имя файла, количество файлов и др.
    ///     Могут быть использованы для валидации или более точного сопоставления с медиа.
    ///     Может быть null, если не была получена.
    /// </summary>
    [JsonPropertyName("info")]
    public TorrentInfo Info { get; set; } = null!;

    /// <summary>
    ///     Коэффициент скачивания (Jackett: DownloadVolumeFactor). Для публичных — 0.
    /// </summary>
    [JsonPropertyName("DownloadVolumeFactor")]
    public double DownloadVolumeFactor { get; set; } = 0;

    /// <summary>
    ///     Коэффициент аплоада (Jackett: UploadVolumeFactor). Для публичных — 1.
    /// </summary>
    [JsonPropertyName("UploadVolumeFactor")]
    public double UploadVolumeFactor { get; set; } = 1;

    /// <summary>
    ///     Минимальное ratio, требуемое трекером. Для публичных — 0.
    /// </summary>
    [JsonPropertyName("MinimumRatio")]
    public double MinimumRatio { get; set; } = 0;

    /// <summary>
    ///     Минимальное время сидинга (секунды). Для публичных — 0.
    /// </summary>
    [JsonPropertyName("MinimumSeedTime")]
    public long MinimumSeedTime { get; set; } = 0;

    /// <summary>
    ///     Описание/превью (для совместимости с Jackett).
    /// </summary>
    [JsonPropertyName("Description")]
    public string? Description { get; set; }
}