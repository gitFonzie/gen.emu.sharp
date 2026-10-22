using SteamKit2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using common.utils;
using common.utils.Logging;
using gen.emu.shared;
using gen.emu.cfg.SteamNetwork;

namespace gen.emu.cfg;

public static class OwnedApps
{
  const int PACKAGES_BATCH_SIZE = 200;

  /// <summary>
  /// Collects the appids of all licenses owned by the logged in account.
  /// </summary>
  public static async Task<IReadOnlyList<uint>> GetOwnedAppIdsAsync()
  {
    var packageIds = await Auth.Instance.GetOwnedPackageIdsAsync().ConfigureAwait(false);
    if (packageIds.Count == 0)
    {
      return [];
    }

    var apps = Client.Instance.GetSteamClient.GetHandler<SteamApps>()
      ?? throw new InvalidOperationException("Null apps instance");

    var appIds = new SortedSet<uint>();
    foreach (var batch in packageIds.Chunk(PACKAGES_BATCH_SIZE))
    {
      var tokens = await apps.PICSGetAccessTokens([], batch).ToTask().ConfigureAwait(false);

      var requests = batch
        .Select(pkg => new SteamApps.PICSRequest(pkg, tokens.PackageTokens.TryGetValue(pkg, out var t) ? t : 0))
        .ToList();

      var res = await apps.PICSGetProductInfo([], requests).ToTask().ConfigureAwait(false);
      if (res is null || res.Failed)
      {
        throw new InvalidOperationException("Failed to get package info");
      }

      foreach (var result in res.Results ?? [])
      {
        foreach (var pkg in result.Packages.Values)
        {
          var appidsKv = pkg.KeyValues["appids"];
          foreach (var child in appidsKv.Children)
          {
            if (uint.TryParse(child.Value, out var appid) && appid != 0)
            {
              appIds.Add(appid);
            }
          }
        }
      }
    }

    Log.Instance.Write(Log.Kind.Info, $"Found {appIds.Count} apps in {packageIds.Count} owned licenses");

    await AppDetails.Instance.PrefetchAppAccessTokensAsync(appIds).ConfigureAwait(false);

    return appIds.ToList();
  }
}
