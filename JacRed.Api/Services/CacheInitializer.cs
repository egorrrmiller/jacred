using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Core.Models;
using JacRed.Core.Utils;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JacRed.Api.Services;

/// <summary>
/// Инициализирует кэш с глобальным каталогом торрентов (ключ -> TorrentInfo).
/// Загружает данные из резервных файлов masterDb.bz и поддерживает фоллбэк на старый формат.
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

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("CacheInitializer: начали загрузку данных");

            var filesToCheck = new List<string>
            {
                "Data/masterDb.bz" // Приоритет — текущий файл
            };

            // Добавляем файлы за последние 60 дней
            for (var i = 0; i < 60; i++)
            {
                filesToCheck.Add($"Data/masterDb_{DateTime.Today.AddDays(-i):dd-MM-yyyy}.bz");
            }

            foreach (var file in filesToCheck)
            {
                if (!File.Exists(file))
                    continue;

                _logger.LogInformation("CacheInitializer: читаем файл {File}", file);
                var data = JsonStream.Read<ConcurrentDictionary<string, TorrentInfo>>(file);

                if (data != null && data.Count > 0)
                {
                    _cache.Set("catalog:all_keys", data);
                    _logger.LogInformation("CacheInitializer: успешно загружено {Count} записей из {File}", data.Count, file);
                    return Task.CompletedTask;
                }
            }

            // Фоллбэк: старый формат (Dictionary<string, DateTime>)
            if (File.Exists("Data/masterDb.bz"))
            {
                try
                {
                    _logger.LogInformation("CacheInitializer: попытка чтения в старом формате");
                    var legacyData = JsonStream.Read<Dictionary<string, DateTime>>("Data/masterDb.bz");

                    if (legacyData != null && legacyData.Count > 0)
                    {
                        var converted = new ConcurrentDictionary<string, TorrentInfo>();
                        foreach (var item in legacyData)
                        {
                            converted[item.Key] = new TorrentInfo
                            {
                                updateTime = item.Value,
                                fileTime = item.Value.ToFileTimeUtc()
                            };
                        }

                        _cache.Set("catalog:all_keys", converted);
                        _logger.LogInformation("CacheInitializer: конвертировано {Count} записей из старого формата", converted.Count);

                        // Сохраняем в актуальном формате, если ещё не сделано
                        if (!File.Exists("Data/masterDb_new.bz"))
                        {
                            JsonStream.Write("Data/masterDb_new.bz", converted);
                            _logger.LogInformation("CacheInitializer: сохранён обновлённый masterDb в новом формате");
                        }

                        return Task.CompletedTask;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "CacheInitializer: ошибка при чтении старого формата");
                }
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

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}