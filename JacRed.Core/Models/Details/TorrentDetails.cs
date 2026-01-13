namespace JacRed.Core.Models.Details;

/// <summary>
///     Полные детали торрента, включая медиа-специфичные поля.
///     Наследует базовые поля и добавляет детализацию по качеству, размеру и т.д.
/// </summary>
public class TorrentDetails : TorrentBaseDetails, ICloneable
{
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
    public string VideoType { get; set; } = null!;

    /// <summary>
    ///     Озвучка/перевод (например: "ColdFilm", "NewStudio", "Оригинал")
    /// </summary>
    public HashSet<string> Voices { get; set; } = new();

    /// <summary>
    ///     Номера сезонов, присутствующих в раздаче
    /// </summary>
    public HashSet<int> Seasons { get; set; } = new();

    /// <summary>
    ///     Создаёт полную копию объекта (поверхностное копирование)
    /// </summary>
    /// <returns>Клон объекта</returns>
    public object Clone()
    {
        return MemberwiseClone();
    }
}