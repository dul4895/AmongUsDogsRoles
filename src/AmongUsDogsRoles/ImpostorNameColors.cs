using HarmonyLib;
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
        if (local && local.Data?.Role?.IsImpostor == true && otherRole && otherRole.IsImpostor)
            __result = Palette.ImpostorRed;
    }
}
