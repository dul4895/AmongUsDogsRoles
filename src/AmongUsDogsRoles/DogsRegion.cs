using HarmonyLib;

namespace AmongUsDogsRoles;

// Bundled server registration makes an extracted game copy ready to join friends.
// Keep every unrelated region; never replace the user's entire regionInfo file.
public static class DogsRegion
{
    public const string Name = "dogs";
    public const string Host = "amongus.playpartygames.io";

    public static void Register(ServerManager manager, bool select)
    {
        var region = new StaticHttpRegionInfo(Name, StringNames.NoTranslation, Host,
            new[] { new ServerInfo("http-1", "https://" + Host, 443, false) }, null).Cast<IRegionInfo>();
        var currentIsDogs = IsDogs(manager.CurrentRegion);
        var regions = manager.AvailableRegions.ToArray().ToList();
        var index = regions.FindIndex(IsDogs);
        // Replace stale endpoints and collapse duplicates while preserving menu order.
        regions.RemoveAll(IsDogs);
        regions.Insert(index < 0 ? regions.Count : Math.Min(index, regions.Count), region);
        manager.AvailableRegions = regions.ToArray();
        if (select || currentIsDogs) manager.SetRegion(region);
    }

    private static bool IsDogs(IRegionInfo? region) =>
        string.Equals(region?.Name, Name, StringComparison.OrdinalIgnoreCase);
}

[HarmonyPatch(typeof(ServerManager), nameof(ServerManager.LoadServers))]
public static class DogsRegionReloadPatch
{
    public static void Postfix(ServerManager __instance) => DogsRegion.Register(__instance, false);
}

[HarmonyPatch(typeof(ServerManager), nameof(ServerManager.Awake))]
public static class DogsRegionStartupPatch
{
    // Select once per launch, after the game's saved/default region selection.
    public static void Postfix(ServerManager __instance) => DogsRegion.Register(__instance, true);
}
