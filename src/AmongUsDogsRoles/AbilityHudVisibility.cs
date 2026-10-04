using HarmonyLib;

namespace AmongUsDogsRoles;

// Mira also calls button.SetActive(false, role) every physics tick for roles
// that do not own that button. That is NOT a request to hide the HUD. Keeping
// it as a per-button HUD flag strands Drag after a different impostor role,
// while the shared Kill button continues working.
[HarmonyPatch(typeof(HudManager), nameof(HudManager.SetHudActive),
    typeof(PlayerControl), typeof(RoleBehaviour), typeof(bool))]
public static class AbilityHudVisibility
{
    private static int hudId;
    private static bool visible = true;
    public static bool Visible => HudManager.Instance &&
        (HudManager.Instance.GetInstanceID() != hudId || visible);

    [HarmonyPriority(Priority.First)]
    public static void Prefix(HudManager __instance, PlayerControl localPlayer, bool isActive)
    {
        if (!localPlayer || !localPlayer.AmOwner) return;
        hudId = __instance.GetInstanceID();
        visible = isActive;
    }
}
