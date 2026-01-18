using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using ICSharpCode.SharpZipLib.BZip2;
using JacRed.Core.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace JacRed.Core.Utils;

/// <summary>
///     Утилита для чтения и записи JSON-файлов с поддержкой BZip2-сжатия.
///     Всегда использует BZip2 при записи. При чтении автоматически определяет формат по сигнатуре.
/// </summary>
public static class JsonStream
{
    /// <summary>
    ///     Читает объект из потока. Автоматически определяет: BZip2, GZip или plain JSON.
    ///     Поток остается открытым после выполнения.
    /// </summary>
    public static T Read<T>(Stream stream)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (!stream.CanRead) throw new ArgumentException("Stream must be readable.", nameof(stream));

        try
        {
            using var decompressedStream = GetDecompressionStream(stream);
            using var reader = new StreamReader(decompressedStream, detectEncodingFromByteOrderMarks: true,
                bufferSize: 1024, leaveOpen: true);
            using var jsonReader = new JsonTextReader(reader);

            var serializer = CreateSerializer();
            return serializer.Deserialize<T>(jsonReader);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[JsonStream.Read] Ошибка при чтении: {ex.Message}");
            return default!;
        }
    }

    /// <summary>
    ///     Записывает объект в поток с использованием BZip2-сжатия.
    ///     Поток остается открытым после выполнения.
    /// </summary>
    public static void Write(Stream stream, object data)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (!stream.CanWrite) throw new ArgumentException("Stream must be writable.", nameof(stream));

        try
        {
            using var bzipStream = new BZip2OutputStream(stream) { IsStreamOwner = false }; // BufferSize здесь НЕТ
            using var writer = new StreamWriter(bzipStream, Encoding.UTF8, 1024, true);
            using var jsonWriter = new JsonTextWriter(writer) { Formatting = Formatting.None };

            var serializer = CreateSerializer();
            serializer.Serialize(jsonWriter, data);

            // Обязательно вызываем Flush, чтобы данные записались в BZip2OutputStream
            jsonWriter.Flush();
            writer.Flush();
            bzipStream.Flush(); // Важно: BZip2OutputStream буферизует данные
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[JsonStream.Write] Ошибка при записи: {ex.Message}");
            return;
        }

        try
        {
            Console.WriteLine($"[JsonStream.Write] BZip2 → записано в поток ({stream.Position} байт)");
        }
        catch
        {
            // Игнорируем
        }
    }

    /// <summary>
    ///     Позволяет Newtonsoft.Json корректно десериализовывать ConcurrentDictionary.
    /// </summary>
    private class ConcurrentDictionarySerializationBinder : ISerializationBinder
    {
        public Type BindToType(string? assemblyName, string typeName)
        {
            if (typeName.StartsWith("System.Collections.Concurrent.ConcurrentDictionary"))
                return typeof(ConcurrentDictionary<string, TorrentInfo>);

            return Type.GetType($"{typeName}, {assemblyName}") ??
                   throw new InvalidOperationException($"Не удалось загрузить тип {typeName}");
        }

        public void BindToName(Type serializedType, out string? assemblyName, out string? typeName)
        {
            assemblyName = null;
            typeName = null;
        }
    }

    #region Вспомогательные методы

    private static Stream GetDecompressionStream(Stream fileStream)
    {
        var signature = new byte[2];
        fileStream.ReadExactly(signature);
        fileStream.Position = 0;

        var sigHex = $"{signature[0]:X2} {signature[1]:X2}";
        Console.WriteLine($"[JsonStream] Сигнатура потока: {sigHex}");

        return (signature[0], signature[1]) switch
        {
            (0x42, 0x5A) => new BZip2InputStream(fileStream) { IsStreamOwner = false },
            (0x1F, 0x8B) => new GZipStream(fileStream, CompressionMode.Decompress, true),
            _ => fileStream
        };
    }

    private static JsonSerializer CreateSerializer()
    {
        return JsonSerializer.Create(new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
            Error = (sender, args) =>
            {
                Console.WriteLine($"[JsonStream] Ошибка десериализации: {args.ErrorContext.Error.Message}");
                args.ErrorContext.Handled = true;
            },
            SerializationBinder = new ConcurrentDictionarySerializationBinder()
        });
    }

    #endregion
}