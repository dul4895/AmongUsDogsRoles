using HarmonyLib;
using UnityEngine;

namespace AmongUsDogsRoles;

// Native SetKillTimer clamps to the normal kill cooldown. The independent
// post-meeting delay may be longer, so enforce it after native HUD updates too.
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
public static class MeetingKillCooldown
{
    public static void Postfix(PlayerControl __instance)
    {
        if (!__instance.AmOwner || !RoundState.InRound || __instance.Data?.Role?.IsImpostor != true) return;
        var remaining = RoundState.ReadyAt.GetValueOrDefault((__instance.PlayerId, Ability.Kill)) - Time.time;
        __instance.killTimer = Mathf.Max(__instance.killTimer, remaining);
    }
}

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckMurder))]
public static class MeetingKillValidation
{
    [HarmonyPriority(Priority.First)]
    public static bool Prefix(PlayerControl __instance) => !AmongUsClient.Instance.AmHost ||
        RoundState.Ready(__instance.PlayerId, Ability.Kill);
}
