using AmongUs.GameOptions;
using HarmonyLib;

namespace AmongUsDogsRoles;

[HarmonyPatch(typeof(IGameOptionsExtensions), nameof(IGameOptionsExtensions.GetAdjustedNumImpostors))]
public static class ImpostorCount
{
    [HarmonyPostfix]
    public static void Postfix(IGameOptions gameOptions, int playerCount, ref int __result)
    {
        if (gameOptions.GameMode != GameModes.Normal) return;
        // The vanilla small-lobby adjustment silently overrides the selected count.
        // Keep the host's choice, bounded only by supported roles and available players.
        __result = Math.Clamp(gameOptions.GetInt(Int32OptionNames.NumImpostors), 1,
            Math.Min(3, Math.Max(1, playerCount - 1)));
    }
}
