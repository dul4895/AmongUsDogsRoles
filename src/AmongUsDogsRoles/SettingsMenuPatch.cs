using HarmonyLib;
using MiraAPI.Patches.Options;

namespace AmongUsDogsRoles;

[HarmonyPatch(typeof(RolesSettingsMenu), nameof(RolesSettingsMenu.OpenChancesTab))]
public static class SettingsMenuPatch
{
    private static MenuState? previousMenu;
    private static bool previousCustom;
    // Mira 0.5 caches role rows (including their advanced-settings buttons).
    // Use the same refresh queue Mira uses when changing game modes.
    private static readonly System.Reflection.PropertyInfo RefreshQueue =
        AccessTools.Property(typeof(MenuState), "QueuedRoleMenuRefresh");

    [HarmonyPriority(Priority.First)]
    public static void Prefix()
    {
        var menu = MenuState.Instance;
        if (!menu || menu.CurrentModIdx == 0 || menu.CurrentMod.PluginId != Plugin.Id) return;
        var custom = RoleTuning.Custom;
        if (previousMenu == menu && previousCustom != custom)
        {
            var queue = (Dictionary<int, bool>)RefreshQueue.GetValue(menu)!;
            queue[menu.CurrentModIdx] = true;
        }
        previousMenu = menu; previousCustom = custom;
    }
}

[HarmonyPatch(typeof(LobbyInfoPane), nameof(LobbyInfoPane.RefreshPane))]
public static class LobbyRefreshPatch
{
    // Mira 0.5 refreshes lobby presentation after every option sync, including
    // updates received in a round, when the lobby's objects no longer exist.
    // The settings have already been applied; normal lobby activation refreshes
    // the pane when the player returns.
    public static bool Prefix() => AmongUsClient.Instance && !AmongUsClient.Instance.IsGameStarted &&
        GameStartManager.InstanceExists;
}
