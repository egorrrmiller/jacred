using JacRed.Core.Models;
using JacRed.Core.Models.AppConf;
using Newtonsoft.Json;

namespace JacRed.Core;

public class AppInit
{
    public TrackerSettings Anifilm = new("https://anifilm.net");

    public TrackerSettings Anilibria = new("https://api.anilibria.tv");

    public TrackerSettings Animelayer = new("http://animelayer.ru");

    public string apikey = null;

    public TrackerSettings Baibako = new("http://baibako.tv");

    public TrackerSettings Bitru = new("https://bitru.org");

    public string[] disable_trackers = new[]
    {
        "hdrezka",
        "anifilm",
        "anilibria"
    };

    public Evercache evercache = new()
    {
        enable = true,
        validHour = 1,
        maxOpenWriteTask = 2000,
        dropCacheTake = 200
    };

    public int fdbPathLevels = 2;

    public List<ProxySettings> globalproxy = new()
    {
        new ProxySettings
        {
            pattern = "\\.onion",
            list = new List<string>
            {
                "socks5://127.0.0.1:9050"
            }
        }
    };

    public TrackerSettings Kinozal = new("https://kinozal.tv");

    public string listenip = "any";

    public int listenport = 9117;

    public bool log = false;

    public TrackerSettings Lostfilm = new("https://www.lostfilm.tv");

    public int maxreadfile = 200;

    public TrackerSettings Megapeer = new("http://megapeer.vip");

    public bool mergeduplicates = true;

    public bool mergenumduplicates = true;

    public TrackerSettings NNMClub = new("https://nnmclub.to");

    public bool openstats = true;

    public bool opensync = true;

    public bool opensync_v1 = false;

    public ProxySettings proxy = new();

    public TrackerSettings Rezka = new("https://rezka.cc");

    public TrackerSettings Rutor = new("http://rutor.info");

    public TrackerSettings Rutracker = new("https://rutracker.org");

    public TrackerSettings Selezen = new("https://open.selezen.org");

    public string syncapi = null;

    public bool syncspidr = true;

    public bool syncsport = true;

    public string[] synctrackers = null;

    public int timeStatsUpdate = 90; // минут

    public int timeSync = 60; // минут

    public TrackerSettings Toloka = new("https://toloka.to");

    public TrackerSettings TorrentBy = new("https://torrent.by");

    public bool tracks = false;

    public int tracksdelay = 20_000;

    /// <summary>
    ///     0 - все
    ///     1 - день, месяц
    /// </summary>
    public int tracksmod = 0;

    public string[] tsuri = new[]
    {
        "http://127.0.0.1:8090"
    };

    public bool web = true;

    #region AppInit

    static AppInit()
    {
        void updateConf()
        {
            try
            {
                if (cacheconf.Item1 == null)
                    if (!File.Exists("init.conf"))
                    {
                        cacheconf.Item1 = new AppInit();

                        return;
                    }

                var lastWriteTime = File.GetLastWriteTime("init.conf");

                if (cacheconf.Item2 != lastWriteTime)
                {
                    cacheconf.Item1 = JsonConvert.DeserializeObject<AppInit>(File.ReadAllText("init.conf"));
                    cacheconf.Item2 = lastWriteTime;
                }
            }
            catch
            {
            }
        }

        updateConf();

        ThreadPool.QueueUserWorkItem(async _ =>
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                updateConf();
            }
        });
    }

    private static (AppInit, DateTime) cacheconf;

    public static AppInit conf => cacheconf.Item1;

    #endregion
}