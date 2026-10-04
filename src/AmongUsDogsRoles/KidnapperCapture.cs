using BepInEx.Logging;
using UnityEngine;

namespace AmongUsDogsRoles;

public enum CaptureBlock : byte
{
    None, CannotAct, WrongRole, DragCooldown, KillCooldown, AlreadyHolding,
    InvalidTarget, TargetInTransit, TargetAlreadyCaptured, OutOfRange, Wall
}

public static class KidnapperCapture
{
    private static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("AmongUsDogsRoles Drag");
    private static float nextLocalLog;
    public static CaptureBlock LastResult { get; private set; }
    public static int Requests { get; private set; }

    // Same restrictions as the normal targeted-ability path, with explicit
    // rejection reasons. Never loosen host range, walls or cooldown checks.
    public static CaptureBlock Validate(PlayerControl actor, PlayerControl? target)
    {
        if (!RoundState.CanAct(actor)) return CaptureBlock.CannotAct;
        if (!RoleFacts.Is<PenguinRole>(actor.Data.Role)) return CaptureBlock.WrongRole;
        if (!RoundState.Ready(actor.PlayerId, Ability.Drag)) return CaptureBlock.DragCooldown;
        if (!RoundState.Ready(actor.PlayerId, Ability.Kill)) return CaptureBlock.KillCooldown;
        if (RoundState.Drags.ContainsKey(actor.PlayerId)) return CaptureBlock.AlreadyHolding;
        if (!RoundState.Alive(target) || target == actor || target!.Data.Role == null || target.Data.Role.IsImpostor)
            return CaptureBlock.InvalidTarget;
        if (!RoundState.Mobile(target)) return CaptureBlock.TargetInTransit;
        if (!AbilityService.CanCapture(target)) return CaptureBlock.TargetAlreadyCaptured;
        if (!(Vector2.Distance(actor.GetTruePosition(), target.GetTruePosition()) <= actor.Data.Role.GetAbilityDistance()))
            return CaptureBlock.OutOfRange;
        if (PhysicsHelpers.AnythingBetween(actor.GetTruePosition(), target.GetTruePosition(), Constants.ShipAndObjectsMask, false))
            return CaptureBlock.Wall;
        return CaptureBlock.None;
    }

    public static void Handle(PlayerControl actor, byte targetId)
    {
        var target = RoundState.Find(targetId);
        var reason = LastResult = Validate(actor, target);
        Requests++;
        Log.LogInfo($"request actor={actor.PlayerId} target={targetId} result={reason} role={actor.Data?.Role?.Role}");
        if (reason != CaptureBlock.None) return;
        RoundState.Consume(actor, Ability.Drag);
        if (AbilityService.Retaliate(actor, target!)) return;
        RoundState.Consume(actor, Ability.Kill);
        StateRpc.Broadcast(new(StateKind.Drag, actor.PlayerId, targetId, RoleTuning.DragDuration));
    }

    public static void LogLocalBlock(DragButton button)
    {
        if (Time.realtimeSinceStartup < nextLocalLog) return;
        nextLocalLog = Time.realtimeSinceStartup + 1;
        var me = PlayerControl.LocalPlayer;
        Log.LogInfo($"local blocked actor={me.PlayerId} visible={button.Button && button.Button!.isActiveAndEnabled} " +
            $"hud={AbilityHudVisibility.Visible} timer={button.Timer:F2} target={button.Target?.PlayerId} reason={Validate(me, button.Target)}");
    }
}
