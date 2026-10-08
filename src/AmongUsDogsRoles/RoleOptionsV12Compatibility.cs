using AmongUs.GameOptions;
using HarmonyLib;
using MiraAPI.Patches.Roles;
using MiraAPI.Roles;

namespace AmongUsDogsRoles;

// MiraAPI 0.5 patches V11; v19 now routes settings through V12. Reuse Mira's
// count/chance logic so allocation, the lobby UI, and guesses see host settings.
[HarmonyPatch(typeof(RoleOptionsCollectionV12))]
public static class RoleOptionsV12Compatibility
{
    [HarmonyPrefix, HarmonyPatch(nameof(RoleOptionsCollectionV12.GetNumPerGame))]
    public static bool Count(RoleTypes role, ref int __result) =>
        RoleOptionsCollectionPatch.GetNumPrefix(role, ref __result);

    [HarmonyPrefix, HarmonyPatch(nameof(RoleOptionsCollectionV12.GetChancePerGame))]
    public static bool Chance(RoleTypes role, ref int __result) =>
        RoleOptionsCollectionPatch.GetChancePrefix(role, ref __result);

    [HarmonyPostfix, HarmonyPatch(nameof(RoleOptionsCollectionV12.AnyRolesEnabled))]
    public static void Enabled(ref bool __result)
    {
        __result |= CustomRoleManager.CustomMiraRoles.Any(role =>
            !role.Configuration.HideSettings && role.GetCount() > 0 && role.GetChance() > 0);
    }
}
