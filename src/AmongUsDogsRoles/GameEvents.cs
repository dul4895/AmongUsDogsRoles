using HarmonyLib;
using MiraAPI.Events;
using MiraAPI.Events.Vanilla.Gameplay;
using MiraAPI.Events.Vanilla.Meeting;
using MiraAPI.Events.Vanilla.Usables;
using MiraAPI.GameEnd;
using MiraAPI.Hud;
using MiraAPI.Utilities;
using UnityEngine;

namespace AmongUsDogsRoles;

public static class GameEvents
{
    [RegisterEvent]
    public static void Intro(IntroBeginEvent _) => RoundState.Reset();

    [RegisterEvent]
    public static void RoundStart(RoundStartEvent e)
    {
        ExplosionPresentation.Preload();
        if (e.TriggeredByIntro && AmongUsClient.Instance.NetworkMode == NetworkModes.FreePlay) RoundState.Reset();
        RoundState.ClearRound();
        foreach (var player in PlayerControl.AllPlayerControls.ToArray())
        {
            if (player.Data.Role is JesterRole) RoundState.Jesters.Add(player.PlayerId);
            foreach (var ability in Enum.GetValues<Ability>())
                if (ability is not (Ability.Execute or Ability.Release or Ability.Recall or Ability.Unfake))
                    RoundState.ReadyAt[(player.PlayerId, RoundState.Slot(ability))] = Time.time + RoundState.InitialCooldown(ability);
        }
        foreach (var button in CustomButtonManager.Buttons)
            if (button.GetType().Assembly == typeof(Plugin).Assembly)
            {
                button.EffectActive = false;
                button.Timer = button is ExecuteButton or ReleaseButton or RecallButton or UnfakeButton ? 0 : button is DetonateButton ? RoundState.InitialCooldown(Ability.Detonate) : 10;
            }
    }
    [RegisterEvent]
    public static void Meeting(StartMeetingEvent _) => RoundState.ClearRound();

    [RegisterEvent]
    public static void Murdered(AfterMurderEvent e) => RoundState.Killers[e.Target.PlayerId] = e.Source.PlayerId;

    [RegisterEvent]
    public static void BeforeMurder(BeforeMurderEvent e)
    {
        if (RoundState.ResolvingBlast || RoundState.ResolvingRetaliation)
        {
            e.IgnoreDefense = true;
            e.IsIndirectAttack = true;
            return;
        }
        if (e.Source == e.Target || !RoundState.Alerting(e.Target)) return;
        e.Cancel();
        if (AmongUsClient.Instance.AmHost && RoundState.Alive(e.Source)) AbilityService.Retaliate(e.Source, e.Target);
    }
    [RegisterEvent]
    public static void Report(ReportBodyEvent e)
    {
        if (RoundState.Dragged(e.Reporter.PlayerId)) { e.Cancel(); return; }
        if (e.Target != null && e.Reporter.Data.Role is CoronerRole)
        {
            e.Cancel();
        }
    }
    [RegisterEvent]
    public static void Vent(EnterVentEvent e)
    {
        if (RoundState.Dragged(e.Player.PlayerId) || RoundState.Drags.ContainsKey(e.Player.PlayerId)) e.Cancel();
    }
    [RegisterEvent]
    public static void BeforeEnd(BeforeGameEndEvent e)
    {
        if (RoundState.ResolvingBlast || BlastOutcome.Holding) e.Cancel();
    }
    [RegisterEvent]
    public static void Ejected(EjectionEvent e)
    {
        var player = e.ExileController?.initData?.networkedPlayer;
        if (player == null || RoundState.JesterWinner != player.PlayerId) return;
        if (AmongUsClient.Instance.AmHost) CustomGameOver.Trigger<JesterGameOver>(new[] { player });
    }
}

public sealed class JesterGameOver : CustomGameOver
{
    private CachedPlayerData? winner;
    public override bool VerifyCondition(PlayerControl sender, NetworkedPlayerInfo[] winners)
    {
        if (!sender.IsHost() || winners.Length != 1 || winners[0] == null ||
            winners[0].PlayerId != RoundState.JesterWinner) return false;
        // Preserve cosmetics/name before the gameplay scene and its player data unload.
        winner = new CachedPlayerData(winners[0]);
        return true;
    }
    public override bool BeforeEndGameSetup(EndGameManager manager)
    {
        EndGameResult.CachedWinners.Clear();
        if (winner != null) EndGameResult.CachedWinners.Add(winner);
        return true;
    }
    public override void AfterEndGameSetup(EndGameManager manager)
    {
        manager.WinText.text = "Jester wins!";
        manager.WinText.color = new Color32(240, 135, 200, 255);
        manager.BackgroundBar.material.color = manager.WinText.color;
    }
}

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
public static class DragUpdatePatch
{
    public static void Postfix(PlayerControl __instance)
    {
        if (!__instance.AmOwner) return;
        foreach (var (actorId, captive) in RoundState.Drags.ToArray())
        {
            var actor = RoundState.Find(actorId);
            var target = RoundState.Find(captive.Target);
            if (!RoundState.InRound || !RoundState.Alive(actor) || !RoundState.Alive(target) || !RoundState.Mobile(actor!) || Time.time >= captive.Expires)
            {
                if (AmongUsClient.Instance.AmHost) StateRpc.Broadcast(new(StateKind.Release, actorId));
                // Local safety release also handles a host disconnect or meeting transition.
                RoundState.Release(actorId);
                continue;
            }
            target!.moveable = false;
            target.MyPhysics.body.velocity = Vector2.zero;
            target.NetTransform.enabled = false;
            // Every client follows the same networked captor. Do not send per-frame RPCs.
            DragPresentation.Follow(actor!, target);
        }
    }
}

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.MurderPlayer))]
public static class VanillaDeathHistoryPatch
{
    public static void Postfix(PlayerControl __instance, PlayerControl target)
    {
        if (target && target.Data.IsDead) RoundState.Killers[target.PlayerId] = __instance.PlayerId;
    }
}

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.VotingComplete))]
public static class VoteOutcomePatch
{
    public static void Prefix(NetworkedPlayerInfo exiled, bool tie)
    {
        if (Rules.JesterWins(exiled != null && exiled.Role is JesterRole, tie, exiled == null, exiled != null && !exiled.IsDead))
            RoundState.JesterWinner = exiled!.PlayerId;
    }
}

// A killed Jester becomes a vanilla ghost. Preserve neutral allegiance across that conversion.
[HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameEnd))]
public static class WinnerPatch
{
    [HarmonyPriority(Priority.Last)]
    public static void Postfix(EndGameResult endGameResult)
    {
        if (BlastOutcome.IsDraw) { EndGameResult.CachedWinners.Clear(); return; }
        if (RoundState.Jesters.Count == 0) return;
        EndGameResult.CachedWinners.Clear();
        foreach (var player in GameData.Instance.AllPlayers.ToArray())
        {
            if (player == null || player.Disconnected) continue;
            var won = RoundState.JesterWinner != 255 ? player.PlayerId == RoundState.JesterWinner :
                !RoundState.Jesters.Contains(player.PlayerId) && player.Role.DidWin(endGameResult.GameOverReason);
            if (won) EndGameResult.CachedWinners.Add(new CachedPlayerData(player));
        }
    }
}

// Keep fake tasks out of progress after a neutral is converted to a vanilla ghost.
// Simplified from TOU Mira's LogicGameFlowPatches.RecomputeTasksPatch.
[HarmonyPatch(typeof(GameData), nameof(GameData.RecomputeTaskCounts))]
public static class TaskProgressPatch
{
    public static bool Prefix(GameData __instance)
    {
        // Peers can receive task updates before the new round's manager/options
        // have spawned, while neutral IDs still belong to the previous round.
        if (RoundState.Jesters.Count == 0 || !GameManager.Instance || GameManager.Instance.IsHideAndSeek()) return true;
        var options = GameOptionsManager.Instance?.currentNormalGameOptions;
        if (options == null || __instance.AllPlayers == null) return true;
        __instance.TotalTasks = 0;
        __instance.CompletedTasks = 0;
        var ghostsDoTasks = options.GhostsDoTasks;
        foreach (var player in __instance.AllPlayers.ToArray())
        {
            if (player == null || player.Disconnected || player.Tasks == null || player.Role == null ||
                player.Role.IsImpostor || !player.Role.TasksCountTowardProgress || RoundState.Jesters.Contains(player.PlayerId) ||
                (player.IsDead && !ghostsDoTasks)) continue;
            foreach (var task in player.Tasks.ToArray())
            {
                __instance.TotalTasks++;
                if (task != null && task.Complete) __instance.CompletedTasks++;
            }
        }
        // No genuine crew tasks must not cause an immediate empty-task victory.
        __instance.TotalTasks = Math.Max(1, __instance.TotalTasks);
        return false;
    }
}

[HarmonyPatch(typeof(LogicGameFlowNormal), nameof(LogicGameFlowNormal.CheckEndCriteria))]
public static class BlastEndCheckPatch
{
    [HarmonyPriority(Priority.High)]
    public static bool Prefix() => !RoundState.ResolvingBlast &&
        (!AmongUsClient.Instance.AmHost || BlastOutcome.CheckEnd());
}

[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
public static class HudPatch
{
    [HarmonyPriority(Priority.Last)]
    public static void Postfix(HudManager __instance)
    {
        var player = PlayerControl.LocalPlayer;
        if (!player || player.Data == null) return;
        // Sniff has its own action/key; reporting is never an alias for it.
        if (player.Data.Role is CoronerRole && RoundState.Alive(player)) __instance.ReportButton.ToggleVisible(false);
        JesterTasks.UpdateText(__instance, player);
        AbilityFeedback.Update(__instance);
        DragPresentation.Update();
        FakerState.Hud(__instance);
    }
}
