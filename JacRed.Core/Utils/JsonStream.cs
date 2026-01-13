using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using ICSharpCode.SharpZipLib.BZip2;
using JacRed.Core.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace JacRed.Core.Utils;

/// <summary>
/// Утилита для чтения и записи JSON-файлов с автоматическим определением формата сжатия.
/// Поддерживает: BZip2 (.bz), GZip (.gz) и plain JSON.
/// Выбор метода сжатия зависит от сигнатуры файла (magic bytes), а не от расширения.
/// </summary>
public static class JsonStream
{
    /// <summary>
    /// Читает объект из файла. Автоматически определяет формат: BZip2, GZip или plain JSON.
    /// </summary>
    public static T Read<T>(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"[JsonStream.Read] Файл не найден: {path}");
            return default!;
        }

        try
        {
            using var fileStream = File.OpenRead(path);
            using var decompressedStream = GetDecompressionStream(fileStream, path);
            using var reader = new StreamReader(decompressedStream);
            using var jsonReader = new JsonTextReader(reader);

            var serializer = CreateSerializer();
            return serializer.Deserialize<T>(jsonReader);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[JsonStream.Read] Ошибка при чтении '{path}': {ex.Message}");
            return default!;
        }
    }

    /// <summary>
    /// Записывает объект в файл с использованием BZip2-сжатия.
    /// Всегда использует BZip2, независимо от расширения.
    /// </summary>
    public static void Write(string path, object data)
    {
        try
        {
            using var fileStream = File.Create(path);
            using var bzipStream = new BZip2OutputStream(fileStream) { IsStreamOwner = true };
            using var writer = new StreamWriter(bzipStream);
            using var jsonWriter = new JsonTextWriter(writer);

            var serializer = CreateSerializer();
            serializer.Serialize(jsonWriter, data);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[JsonStream.Write] Ошибка при записи '{path}': {ex.Message}");
            return;
        }

        try
        {
            var length = new FileInfo(path).Length;
            Console.WriteLine($"[JsonStream.Write] BZip2 → {path} ({length} байт)");
        }
        catch
        {
            // Игнорируем
        }
    }

    #region Вспомогательные методы

    private static Stream GetDecompressionStream(FileStream fileStream, string path)
    {
        var signature = new byte[2];
        fileStream.ReadExactly(signature);
        fileStream.Position = 0;

        var sigHex = $"{signature[0]:X2} {signature[1]:X2}";
        Console.WriteLine($"[JsonStream] Сигнатура '{path}': {sigHex}");

        return (signature[0], signature[1]) switch
        {
            (0x42, 0x5A) => new BZip2InputStream(fileStream),
            (0x1F, 0x8B) => new GZipStream(fileStream, CompressionMode.Decompress),
            _ => fileStream
        };
    }

    private static JsonSerializer CreateSerializer() => JsonSerializer.Create(new()
    {
        Error = (sender, args) =>
        {
            Console.WriteLine($"[JsonStream] Ошибка десериализации: {args.ErrorContext.Error.Message}");
            args.ErrorContext.Handled = true;
        },
        // 🔥 Ключевая настройка:
        SerializationBinder = new ConcurrentDictionarySerializationBinder()
    });

    #endregion

    /// <summary>
    /// Позволяет Newtonsoft.Json корректно десериализовывать ConcurrentDictionary.
    /// </summary>
    private class ConcurrentDictionarySerializationBinder : ISerializationBinder
    {
        public Type BindToType(string? assemblyName, string typeName)
        {
            if (typeName.StartsWith("System.Collections.Concurrent.ConcurrentDictionary"))
                return typeof(ConcurrentDictionary<string, TorrentInfo>);

            return Type.GetType($"{typeName}, {assemblyName}") ?? throw new InvalidOperationException($"Не удалось загрузить тип {typeName}");
        }

        public void BindToName(Type serializedType, out string? assemblyName, out string? typeName)
        {
            assemblyName = null;
            typeName = null;
        }
    }
}