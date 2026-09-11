using MiraAPI.GameOptions;
using MiraAPI.Roles;
using MiraAPI.Utilities;
using UnityEngine;

namespace AmongUsDogsRoles;

public readonly record struct Captive(byte Target, float Expires, float Duration);
public static class RoundState
{
    public static readonly Dictionary<byte, Captive> Drags = new();
    public static readonly Dictionary<byte, float> Alerts = new();
    public static readonly Dictionary<byte, int> AlertsUsed = new();
    public static readonly Dictionary<byte, Vector2> Marks = new();
    public static readonly Dictionary<byte, byte> Killers = new();
    public static readonly Dictionary<byte, byte> Tracks = new();
    public static readonly Dictionary<(byte Player, Ability Ability), float> ReadyAt = new();
    public static readonly Dictionary<byte, string> Revealed = new();
    public static readonly HashSet<byte> Jesters = new();
    public static byte JesterWinner = 255;
    public static bool ResolvingBlast;
    public static bool ResolvingRetaliation;
    public static bool InRound => ShipStatus.Instance && !LobbyBehaviour.Instance && !MeetingHud.Instance && !ExileController.Instance &&
        (AmongUsClient.Instance.IsGameStarted || AmongUsClient.Instance.NetworkMode == NetworkModes.FreePlay);
    public static bool Alive(PlayerControl? p) => p && p!.Data != null && !p.Data.IsDead && !p.Data.Disconnected;
    public static bool Mobile(PlayerControl p) => !p.inVent && !p.walkingToVent && !p.onLadder && !p.inMovingPlat && !p.MyPhysics.Animations.IsPlayingAnyLadderAnimation();
    public static bool Dragged(byte id) => Drags.Values.Any(d => d.Target == id);
    public static bool CanAct(PlayerControl p) => Alive(p) && p.Data.Role != null && Rules.CanAct(true, true, InRound,
        Mobile(p) && p.moveable && !p.shapeshifting && (!p.AmOwner || p.CanMove), Dragged(p.PlayerId));
    public static bool Alerting(PlayerControl p) => Alive(p) && p.Data.Role is VeteranRole && Alerts.GetValueOrDefault(p.PlayerId) > Time.time;
    public static PlayerControl? Find(byte id) => PlayerControl.AllPlayerControls.ToArray().FirstOrDefault(p => p.PlayerId == id);
    public static Faction Team(PlayerControl p) => p.Data.Role is ICustomRole role && role.Team == ModdedRoleTeams.Custom ? Faction.Neutral : p.Data.Role.IsImpostor ? Faction.Impostor : Faction.Crew;
    public static float Cooldown(Ability ability) => ability switch
    {
        Ability.Drag => RoleTuning.DragCooldown,
        Ability.Detonate => RoleTuning.BombCooldown,
        Ability.Investigate => RoleTuning.InvestigateCooldown,
        Ability.Mark or Ability.Recall => RoleTuning.RecallCooldown,
        Ability.Alert => RoleTuning.AlertCooldown,
        Ability.Shoot => RoleTuning.ShootCooldown,
        Ability.Examine => RoleTuning.ExamineCooldown,
        Ability.Kill or Ability.Execute => GameOptionsManager.Instance.CurrentGameOptions.GetFloat(AmongUs.GameOptions.FloatOptionNames.KillCooldown),
        _ => 0,
    };
    public static Ability Slot(Ability a) => a switch { Ability.Execute => Ability.Kill, Ability.Recall => Ability.Mark, _ => a };
    // Bomber dies on use, so its configurable cooldown is the wait before
    // exploding each round, including after meetings. Basic mode retains grace.
    public static float InitialCooldown(Ability a) => a == Ability.Detonate && RoleTuning.Custom ? RoleTuning.BombCooldown : 10;
    public static bool Ready(byte player, Ability a) => Rules.Ready(Time.time, ReadyAt.GetValueOrDefault((player, Slot(a))));
    public static void Consume(PlayerControl player, Ability a, float? duration = null) =>
        StateRpc.Broadcast(new(StateKind.Cooldown, player.PlayerId, (byte)Slot(a), duration ?? Cooldown(a)));

    public static void Apply(Update d)
    {
        switch (d.Kind)
        {
            case StateKind.Fake: FakerState.Fake(d.Actor, new(d.X, d.Y)); break;
            case StateKind.Unfake: FakerState.Unfake(d.Actor); break;
            case StateKind.Drag: Drags[d.Actor] = new(d.Target, Time.time + d.Value, d.Value); break;
            case StateKind.Release: Release(d.Actor); break;
            case StateKind.Alert:
                Alerts[d.Actor] = Time.time + d.Value;
                AlertsUsed[d.Actor] = AlertsUsed.GetValueOrDefault(d.Actor) + 1;
                break;
            case StateKind.Mark: Marks[d.Actor] = new(d.X, d.Y); break;
            case StateKind.ClearMark:
                if (!Marks.Remove(d.Actor)) break;
                var recalling = Find(d.Actor);
                if (recalling && Alive(recalling))
                {
                    var destination = new Vector2(d.X, d.Y);
                    // A host-generated native snap can carry an older movement
                    // sequence than the moving owner's. Apply the accepted recall
                    // on every peer, then publish the owner's fresh sequence.
                    recalling!.NetTransform.ClearPositionQueues();
                    recalling.NetTransform.SnapTo(destination);
                    recalling.MyPhysics.body.velocity = Vector2.zero;
                    if (recalling.AmOwner) recalling.NetTransform.RpcSnapTo(destination);
                }
                AbilityFeedback.Recall(d.Actor);
                break;
            case StateKind.Blast:
                BlastOutcome.Begin();
                AbilityFeedback.Detonate(new Vector2(d.X, d.Y), d.Value);
                break;
            case StateKind.Track:
                Tracks[d.Actor] = d.Target;
                if (FakerState.Active.ContainsKey(d.Target)) FakerState.BodyTracks.Add(d.Actor);
                else FakerState.BodyTracks.Remove(d.Actor);
                break;
            case StateKind.Cooldown: ReadyAt[(d.Actor, (Ability)d.Target)] = Time.time + d.Value; break;
            case StateKind.Reveal:
                if (PlayerControl.LocalPlayer.PlayerId != d.Actor) break;
                var target = Find(d.Target);
                if (target == null) break;
                var name = target.Data.Role is ICustomRole role ? role.RoleName : TranslationController.Instance.GetString(target.Data.Role.StringName);
                Revealed[d.Target] = name;
                Helpers.CreateAndShowNotification($"{target.Data.PlayerName}: {name}", Color.white, new Vector3(0, 1, -20));
                break;
        }
    }
    public static void Release(byte actor)
    {
        DragPresentation.Release(actor);
        if (Drags.Remove(actor, out var captive) && Find(captive.Target) is { } p)
        {
            // Dragging bypasses vanilla movement replication. Its buffered positions
            // still refer to the capture site, so resuming without a reset replays
            // those positions before the owner's first fresh movement update.
            var dropPosition = (Vector2)p.transform.position;
            p.NetTransform.ClearPositionQueues();
            p.NetTransform.enabled = true;
            p.NetTransform.SnapTo(dropPosition);
            p.MyPhysics.body.velocity = Vector2.zero;
            p.moveable = true;
            // Publish the owner's drop point with a fresh movement sequence. Local
            // SnapTo above also protects peers until this reliable update arrives.
            if (p.AmOwner && Alive(p) && InRound) p.NetTransform.RpcSnapTo(dropPosition);
        }
    }
    public static void ClearRound()
    {
        AbilityFeedback.Clear();
        DragPresentation.Clear();
        foreach (var actor in Drags.Keys.ToArray()) Release(actor);
        Alerts.Clear(); Marks.Clear(); Tracks.Clear(); FakerState.BodyTracks.Clear();
    }
    public static void Reset()
    {
        BlastOutcome.Reset();
        FakerState.Reset();
        ClearRound(); AlertsUsed.Clear(); Killers.Clear(); ReadyAt.Clear(); Revealed.Clear(); Jesters.Clear();
        JesterWinner = 255; ResolvingBlast = false; ResolvingRetaliation = false;
    }
}
