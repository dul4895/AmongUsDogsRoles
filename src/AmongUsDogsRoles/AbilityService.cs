using MiraAPI.GameOptions;
using MiraAPI.Networking;
using MiraAPI.Utilities;
using UnityEngine;

namespace AmongUsDogsRoles;

// All consequential decisions run once, on the host. UI cooldowns are advisory.
public static class AbilityService
{
    public static bool InReach(PlayerControl actor, PlayerControl target, bool allowImpostor = false) =>
        actor != target && RoundState.Alive(actor) && actor.Data.Role != null &&
        RoundState.Alive(target) && target.Data.Role != null && RoundState.Mobile(target) &&
        (allowImpostor || !target.Data.Role.IsImpostor) &&
        Vector2.Distance(actor.GetTruePosition(), target.GetTruePosition()) <= actor.Data.Role.GetAbilityDistance() &&
        !PhysicsHelpers.AnythingBetween(actor.GetTruePosition(), target.GetTruePosition(), Constants.ShipAndObjectsMask, false);

    public static bool Retaliate(PlayerControl actor, PlayerControl target)
    {
        if (actor == target || !RoundState.Alerting(target)) return false;
        RoundState.ResolvingRetaliation = true;
        try { Kill(target, actor); }
        finally { RoundState.ResolvingRetaliation = false; }
        return true;
    }
    public static void Kill(PlayerControl source, PlayerControl target, bool blast = false)
    {
        if (!RoundState.Alive(target)) return;
        // Host records immediately; AfterMurder also covers vanilla kills.
        source.RpcCustomMurder(target, MeetingCheck.OutsideMeeting, teleportMurderer: false, showKillAnim: !blast, playKillSound: !blast);
    }
    public static void Handle(PlayerControl actor, Request request)
    {
        if (request.Ability == Ability.Unfake)
        {
            if (!FakerState.CanUnfake(actor)) return;
            RoundState.Consume(actor, Ability.Kill);
            StateRpc.Broadcast(new(StateKind.Unfake, actor.PlayerId));
            return;
        }
        if (!RoundState.CanAct(actor)) return;
        var a = request.Ability;
        var role = actor.Data.Role;
        bool correctRole = a switch
        {
            Ability.Kill => role is ILightRole && role.IsImpostor,
            Ability.Drag or Ability.Release or Ability.Execute => role is PenguinRole,
            Ability.Detonate => role is BomberRole,
            Ability.Investigate => role is ConsigliereRole,
            Ability.Mark or Ability.Recall => role is EscapistRole,
            Ability.Alert => role is VeteranRole,
            Ability.Shoot => role is SheriffRole,
            Ability.Examine => role is CoronerRole,
            Ability.Fake => role is FakerRole,
            _ => false,
        };
        if (!correctRole) return;
        if (a is not (Ability.Release or Ability.Execute or Ability.Recall) && !RoundState.Ready(actor.PlayerId, a)) return;
        var target = RoundState.Find(request.Target);
        switch (a)
        {
            case Ability.Fake:
                if (FakerState.Used.Contains(actor.PlayerId)) return;
                var spot = actor.transform.position;
                StateRpc.Broadcast(new(StateKind.Fake, actor.PlayerId, X: spot.x, Y: spot.y));
                break;
            case Ability.Kill:
            case Ability.Shoot:
            case Ability.Investigate:
            case Ability.Drag:
                if (target == null || !InReach(actor, target, a is Ability.Shoot or Ability.Investigate)) return;
                if (RoundState.Drags.ContainsKey(actor.PlayerId)) return;
                if (a == Ability.Drag && (RoundState.Dragged(target.PlayerId) || RoundState.Drags.ContainsKey(target.PlayerId) || !RoundState.Ready(actor.PlayerId, Ability.Kill))) return;
                RoundState.Consume(actor, a);
                if (Retaliate(actor, target)) return;
                if (a == Ability.Kill) Kill(actor, target);
                else if (a == Ability.Shoot)
                    Kill(actor, Rules.SheriffShot(RoundState.Team(target), false) == ShotResult.SheriffDies ? actor : target);
                else if (a == Ability.Investigate)
                    StateRpc.Private(actor, new(StateKind.Reveal, actor.PlayerId, target.PlayerId));
                else
                {
                    RoundState.Consume(actor, Ability.Kill);
                    StateRpc.Broadcast(new(StateKind.Drag, actor.PlayerId, target.PlayerId, RoleTuning.DragDuration));
                }
                break;
            case Ability.Execute:
            case Ability.Release:
                if (!RoundState.Drags.TryGetValue(actor.PlayerId, out var captive)) return;
                target = RoundState.Find(captive.Target);
                StateRpc.Broadcast(new(StateKind.Release, actor.PlayerId));
                RoundState.Consume(actor, Ability.Drag);
                if (a == Ability.Execute && target != null && RoundState.Alive(target))
                {
                    RoundState.Consume(actor, Ability.Kill);
                    if (!Retaliate(actor, target)) Kill(actor, target);
                }
                break;
            case Ability.Alert:
                if (RoundState.AlertsUsed.GetValueOrDefault(actor.PlayerId) >= RoleTuning.AlertUses || RoundState.Alerting(actor)) return;
                RoundState.Consume(actor, a, RoleTuning.AlertDuration + RoleTuning.AlertCooldown);
                StateRpc.Broadcast(new(StateKind.Alert, actor.PlayerId, Value: RoleTuning.AlertDuration));
                break;
            case Ability.Mark:
                if (RoundState.Marks.ContainsKey(actor.PlayerId)) return;
                var pos = actor.GetTruePosition();
                StateRpc.Broadcast(new(StateKind.Mark, actor.PlayerId, X: pos.x, Y: pos.y));
                break;
            case Ability.Recall:
                if (!RoundState.Marks.TryGetValue(actor.PlayerId, out var mark)) return;
                // Marks use the collider center; SnapTo expects the transform position.
                var destination = mark + (Vector2)actor.transform.position - actor.GetTruePosition();
                StateRpc.Broadcast(new(StateKind.ClearMark, actor.PlayerId, X: destination.x, Y: destination.y));
                RoundState.Consume(actor, a);
                break;
            case Ability.Examine:
                var body = Helpers.GetBodyById(request.Target);
                if (body == null || body.Reported || Vector2.Distance(actor.GetTruePosition(), body.TruePosition) > actor.MaxReportDistance ||
                    PhysicsHelpers.AnythingBetween(actor.GetTruePosition(), body.TruePosition, Constants.ShipAndObjectsMask, false)) return;
                RoundState.Consume(actor, a);
                if (RoundState.Killers.TryGetValue(request.Target, out var killer)) StateRpc.Private(actor, new(StateKind.Track, actor.PlayerId, killer));
                break;
            case Ability.Detonate:
                RoundState.Consume(actor, a);
                var origin = actor.GetTruePosition();
                var radius = RoleTuning.BombRadius;
                StateRpc.Broadcast(new(StateKind.Blast, actor.PlayerId, Value: radius, X: origin.x, Y: origin.y));
                var victims = PlayerControl.AllPlayerControls.ToArray().Where(p => p != actor && p.Data != null &&
                    Rules.InBlast((p.GetTruePosition() - origin).sqrMagnitude, radius, !p.Data.IsDead, p.Data.Disconnected)).ToArray();
                // Resolve the complete snapshot before allowing parity / crew victory checks.
                RoundState.ResolvingBlast = true;
                try
                {
                    foreach (var victim in victims) Kill(actor, victim, true);
                    Kill(actor, actor, true);
                }
                finally { RoundState.ResolvingBlast = false; }
                break;
        }
    }
}
