using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Core.Models;
using JacRed.Core.Utils;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JacRed.Api.Services;

/// <summary>
///     Инициализирует кэш с глобальным каталогом торрентов (ключ -> TorrentInfo).
///     Загружает данные из резервных файлов masterDb.bz и поддерживает фоллбэк на старый формат.
/// </summary>
public class CacheInitializer : IHostedService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<CacheInitializer> _logger;

    public CacheInitializer(IMemoryCache cache, ILogger<CacheInitializer> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("CacheInitializer: начали загрузку данных");

            var filesToCheck = new List<string>
            {
                "Data/masterDb.bz"
            };

            // Добавляем файлы за последние 10 дней
            for (var i = 0; i < 10; i++) filesToCheck.Add($"Data/masterDb_{DateTime.Today.AddDays(-i):dd-MM-yyyy}.bz");

            foreach (var file in filesToCheck.Where(File.Exists))
            {
                _logger.LogInformation("CacheInitializer: читаем файл {File}", file);
                try
                {
                    await using var fileStream = File.OpenRead(file);
                    var data = JsonStream.Read<ConcurrentDictionary<string, TorrentInfo>>(fileStream);

                    if (data is not { Count: > 0 }) return;

                    _cache.Set("catalog:all_keys", data);
                    _logger.LogInformation("CacheInitializer: успешно загружено {Count} записей из {File}",
                        data.Count, file);

                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "CacheInitializer: ошибка при чтении файла {File}", file);
                }
            }

            // Фоллбэк: старый формат (Dictionary<string, DateTime>)
            if (File.Exists("Data/masterDb.bz"))
                try
                {
                    _logger.LogInformation("CacheInitializer: попытка чтения в старом формате");
                    await using var fileStream = File.OpenRead("Data/masterDb.bz");
                    var legacyData = JsonStream.Read<Dictionary<string, DateTime>>(fileStream);

                    if (legacyData != null && legacyData.Count > 0)
                    {
                        var converted = new ConcurrentDictionary<string, TorrentInfo>();
                        foreach (var item in legacyData)
                            converted[item.Key] = new TorrentInfo
                            {
                                updateTime = item.Value,
                                fileTime = item.Value.ToFileTimeUtc()
                            };

                        _cache.Set("catalog:all_keys", converted);
                        _logger.LogInformation("CacheInitializer: конвертировано {Count} записей из старого формата",
                            converted.Count);

                        // Сохраняем в актуальном формате, если ещё не сделано
                        if (File.Exists("Data/masterDb_new.bz")) return;
                        await Task.Run(() =>
                        {
                            using var newFileStream = File.Create("Data/masterDb_new.bz");
                            JsonStream.Write(newFileStream, converted);
                        }, cancellationToken);
                        _logger.LogInformation("CacheInitializer: сохранён обновлённый masterDb в новом формате");
                    }

                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "CacheInitializer: ошибка при чтении старого формата");
                }

            // Если ничего не загрузилось — создаём пустой кэш
            var empty = new ConcurrentDictionary<string, TorrentInfo>();
            _cache.Set("catalog:all_keys", empty);
            _logger.LogInformation("CacheInitializer: инициализирован пустой кэш");

            // Удаляем устаревший временный файл
            if (File.Exists("lastsync.txt"))
            {
                File.Delete("lastsync.txt");
                _logger.LogInformation("CacheInitializer: удалён временный файл lastsync.txt");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CacheInitializer: критическая ошибка при инициализации кэша");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}