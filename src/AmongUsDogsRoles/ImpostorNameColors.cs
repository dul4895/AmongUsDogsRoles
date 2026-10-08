using HarmonyLib;
using MiraAPI.Patches.Roles;
using UnityEngine;

namespace AmongUsDogsRoles;

[HarmonyPatch(typeof(PlayerNameColor), nameof(PlayerNameColor.Get), typeof(RoleBehaviour))]
public static class ImpostorNameColors
{
    [HarmonyPostfix]
    public static void Postfix([HarmonyArgument(0)] RoleBehaviour otherRole, ref Color __result)
    {
        // Mira can return a custom role's theme color before vanilla team coloring.
        // Keep role art/colors, but use vanilla red whenever both players are impostors.
        // No alive check: dead impostors and a faking Faker still know their teammates.
        var local = PlayerControl.LocalPlayer;
        if (FakerState.IsFaking(local) && otherRole && !otherRole.IsImpostor)
            __result = Color.white;
        if (local && local.Data?.Role?.IsImpostor == true && otherRole && otherRole.IsImpostor)
            __result = Palette.ImpostorRed;
    }
}

// Guard Mira's prefix itself: HarmonyX runs every prefix even when another
// prefix skips the vanilla method. Early v19 lookups can have no local player.
[HarmonyPatch(typeof(NameTagPatch), nameof(NameTagPatch.GetPatch))]
public static class MiraNameColorInitialization
{
    public static bool Prefix(RoleBehaviour __0, ref Color __1, ref bool __result)
    {
        var local = PlayerControl.LocalPlayer;
        if (local && local.Data?.Role != null && __0 && GameManager.Instance) return true;
        __1 = Color.white;
        __result = false;
        return false;
    }
}
