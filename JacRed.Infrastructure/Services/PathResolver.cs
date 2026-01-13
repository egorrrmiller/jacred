using JacRed.Core;
using JacRed.Core.Interfaces;
using JacRed.Core.Utils;

namespace JacRed.Infrastructure.Services;

public class PathResolver : IPathResolver
{
    public string GenerateFilePath(string key)
    {
        var md5key = HashTo.Md5(key);

        if (AppInit.conf.fdbPathLevels == 2)
        {
            Directory.CreateDirectory($"Data/fdb/{md5key.Substring(0, 2)}");
            return $"Data/fdb/{md5key.Substring(0, 2)}/{md5key.Substring(2)}";
        }

        Directory.CreateDirectory($"Data/fdb/{md5key[0]}");
        return $"Data/fdb/{md5key[0]}/{md5key}";
    }
}