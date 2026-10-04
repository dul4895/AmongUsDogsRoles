using System.Text.Json;
using BepInEx;
using HarmonyLib;

namespace AmongUsDogsRoles.Testing;

// --region-test isolates native region loading/saving from the shared game profile.
[HarmonyPatch]
public static class RegionChecks
{
    private static string Fixture => Path.Combine(Paths.GameRootPath, "qa-regionInfo.json");
    private static bool Enabled => Environment.GetCommandLineArgs().Contains("--region-test");

    [HarmonyTargetMethods]
    public static IEnumerable<System.Reflection.MethodBase> Targets() =>
        new[] { AccessTools.Method(typeof(ServerManager), nameof(ServerManager.LoadServers)),
            AccessTools.Method(typeof(ServerManager), nameof(ServerManager.SaveServers)) };

    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    public static void Isolate(ServerManager __instance)
    {
        if (Enabled) __instance.serverInfoFileJson = Fixture;
    }

    public static void Run()
    {
        if (!Enabled || AmongUsClient.Instance.AmConnected)
            throw new InvalidOperationException("Disconnected --region-test client required");
        var manager = ServerManager.Instance;
        var results = new List<string>();
        void Check(string name, bool ok)
        {
            if (!ok) throw new InvalidOperationException(name);
            results.Add("PASS " + name);
        }
        bool Endpoint() => manager.CurrentRegion.TryCast<StaticHttpRegionInfo>() is { } region &&
            region.Name == DogsRegion.Name && region.PingServer == DogsRegion.Host &&
            region.Servers.Length == 1 && region.Servers[0].Ip == "https://" + DogsRegion.Host &&
            region.Servers[0].Port == 443 && !region.Servers[0].UseDtls;
        Check("startup automatically selects dogs with the correct HTTPS endpoint", Endpoint());
        Check("test profile isolated", manager.serverInfoFileJson == Fixture);
        Check("all three original regions retained", new[] {"North America", "Europe", "Asia"}.All(n => manager.AvailableRegions.Any(r => r.Name == n)));
        Check("one dogs entry", manager.AvailableRegions.Count(r => r.Name == DogsRegion.Name) == 1);

        var custom = new StaticHttpRegionInfo("Existing custom", StringNames.NoTranslation,
            "example.invalid", new[] {new ServerInfo("custom", "https://example.invalid", 443, false)}, null).Cast<IRegionInfo>();
        var stale = new StaticHttpRegionInfo("DOGS", StringNames.NoTranslation,
            "old.invalid", new[] {new ServerInfo("old", "https://old.invalid", 443, false)}, null).Cast<IRegionInfo>();
        manager.AvailableRegions = manager.AvailableRegions.ToArray().Concat(new[] {custom, stale}).ToArray();
        manager.SetRegion(stale);
        DogsRegion.Register(manager, false);
        Check("stale endpoint replaced and duplicate removed", Endpoint() && manager.AvailableRegions.Count(r => r.Name.Equals("dogs", StringComparison.OrdinalIgnoreCase)) == 1);
        Check("unrelated custom server retained", manager.AvailableRegions.Any(r => r.Name == custom.Name && r.PingServer == custom.PingServer));

        manager.SetRegion(manager.AvailableRegions.First(r => r.Name == "Europe"));
        manager.SaveServers();
        for (var i = 0; i < 3; i++) manager.LoadServers();
        Check("manual selection survives repeated region reloads", manager.CurrentRegion.Name == "Europe");
        Check("reload retains custom server and one dogs entry", manager.AvailableRegions.Length == 5 && manager.AvailableRegions.Any(r => r.Name == custom.Name));
        manager.SetRegion(manager.AvailableRegions.First(r => r.Name == "dogs"));
        Check("dogs still selectable after reload", Endpoint());
        File.WriteAllText(Path.Combine(Paths.GameRootPath, "qa-regions.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }
}
