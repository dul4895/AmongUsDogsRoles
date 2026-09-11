using HarmonyLib;
using UnityEngine;

namespace AmongUsDogsRoles;

public static class JesterTasks
{
    // Keep this allegiance after the game replaces a dead Jester with a crew ghost.
    public static bool AreFake(PlayerControl? player) => player && player!.Data?.Role != null &&
        (player.Data.Role is JesterRole || RoundState.Jesters.Contains(player.PlayerId));

    public static void UpdateText(HudManager hud, PlayerControl player)
    {
        if (!RoundState.InRound || !AreFake(player)) return;
        var text = new Il2CppSystem.Text.StringBuilder();
        text.Append(player.Data.IsDead ? "<color=#F087C8>You were not voted out.</color>\nFake Tasks:\n" :
            "<color=#F087C8>Get voted out to win.</color>\nFake Tasks:\n");
        foreach (var task in player.myTasks)
            if (task && task.TryCast<NormalPlayerTask>() != null) task.AppendTaskText(text);
        hud.TaskPanel.SetTaskText(text.ToString());
    }
}

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcCompleteTask))]
public static class JesterSendTaskPatch
{
    public static bool Prefix(PlayerControl __instance) => !JesterTasks.AreFake(__instance);
}

// Also reject received completion messages, including ones sent directly by a peer.
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CompleteTask))]
public static class JesterCompleteTaskPatch
{
    public static bool Prefix(PlayerControl __instance) => !JesterTasks.AreFake(__instance);
}

[HarmonyPatch(typeof(Console), nameof(Console.CanUse))]
public static class JesterTaskConsolePatch
{
    [HarmonyPriority(Priority.Last)]
    public static void Postfix(Console __instance, NetworkedPlayerInfo pc, ref bool canUse, ref bool couldUse, ref float __result)
    {
        // AllowImpostor consoles repair sabotage; they are not assigned crew tasks.
        if (__instance.AllowImpostor || pc == null || !JesterTasks.AreFake(pc.Object)) return;
        canUse = couldUse = false;
        __result = float.MaxValue;
    }
}

[HarmonyPatch(typeof(Console), nameof(Console.Use))]
public static class JesterOpenTaskPatch
{
    public static bool Prefix(Console __instance) => __instance.AllowImpostor || !JesterTasks.AreFake(PlayerControl.LocalPlayer);
}
