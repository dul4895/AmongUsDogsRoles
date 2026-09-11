using HarmonyLib;
using Hazel;
using MiraAPI.Hud;
using MiraAPI.Roles;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AmongUsDogsRoles;

// Fake death uses the vanilla dead flag, so meetings, targeting and parity agree.
// The original role is retained; this is not a ghost role or a murder RPC.
public static class FakerState
{
    public static readonly Dictionary<byte, Vector2> Active = new();
    public static readonly HashSet<byte> Used = new();
    public static readonly HashSet<byte> BodyTracks = new();
    public static int LocalFakeFrame = -1;
    public static bool MeetingEndRequested { get; private set; }
    public static bool DeferEliminationUntilMeeting { get; private set; }
    public static bool OnlyFakedImpostors => GameData.Instance &&
        GameData.Instance.AllPlayers.ToArray().Any(p => p != null && !p.Disconnected && p.IsDead &&
            p.Role is FakerRole && Active.ContainsKey(p.PlayerId)) &&
        !GameData.Instance.AllPlayers.ToArray().Any(p => p != null && !p.Disconnected && !p.IsDead && p.Role != null && p.Role.IsImpostor);

    public static bool EndAtMeeting()
    {
        if (!AmongUsClient.Instance || !AmongUsClient.Instance.AmHost || !AmongUsClient.Instance.IsGameStarted ||
            !GameManager.Instance) return false;
        DeferEliminationUntilMeeting = false;
        if (!OnlyFakedImpostors) return false;
        if (!MeetingEndRequested)
        {
            MeetingEndRequested = true;
            GameManager.Instance.RpcEndGame(GameOverReason.CrewmatesByVote, false);
        }
        return true;
    }
    public static bool IsFaking(PlayerControl? p) => p && Active.ContainsKey(p!.PlayerId);
    public static bool CanUnfake(PlayerControl p) => p && p.Data != null && !p.Data.Disconnected &&
        p.Data.Role is FakerRole && p.Data.IsDead && IsFaking(p) && RoundState.InRound && !MeetingEndRequested && !BlastOutcome.Drawing;

    public static void Fake(byte id, Vector2 position)
    {
        var p = RoundState.Find(id);
        if (!RoundState.Alive(p) || p!.Data.Role is not FakerRole || !Used.Add(id)) return;
        Active[id] = position;
        if (p.AmOwner) LocalFakeFrame = Time.frameCount;
        var body = Object.Instantiate(GameManager.Instance.GetDeadBody(p.Data.Role));
        body.ParentId = id;
        foreach (var renderer in body.bodyRenderers) p.SetPlayerMaterialColors(renderer);
        p.SetPlayerMaterialColors(body.bloodSplatter);
        var bodyPosition = (Vector3)position + p.KillAnimations[0].BodyOffset;
        bodyPosition.z = bodyPosition.y / 1000f;
        body.transform.position = bodyPosition;
        RoundState.Killers[id] = id;
        p.Die(DeathReason.Kill, false);
        DeferEliminationUntilMeeting = OnlyFakedImpostors;
        if (AmongUsClient.Instance.AmHost) p.Data.MarkDirty();
        Pin(p, position);
        RefreshHud(p);
    }

    public static void Unfake(byte id)
    {
        var p = RoundState.Find(id);
        if (!p || p!.Data == null || p.Data.Disconnected || !Active.Remove(id, out var position)) return;
        DeferEliminationUntilMeeting = false;
        foreach (var body in Object.FindObjectsOfType<DeadBody>())
            if (body.ParentId == id) Object.Destroy(body.gameObject);
        p.NetTransform.enabled = true;
        p.NetTransform.ClearPositionQueues();
        p.Revive();
        // Revive restores physics/ghost rendering; explicitly retain the original
        // custom role even if base-game role bookkeeping changed during a meeting.
        RoleManager.Instance.SetRole(p, (AmongUs.GameOptions.RoleTypes)RoleId.Get<FakerRole>());
        p.NetTransform.SnapTo(position);
        p.MyPhysics.body.velocity = Vector2.zero;
        p.moveable = true;
        p.Visible = true;
        if (p.AmOwner) p.NetTransform.RpcSnapTo(position);
        if (AmongUsClient.Instance.AmHost) p.Data.MarkDirty();
        RoundState.Killers.Remove(id);
        foreach (var coroner in BodyTracks.ToArray())
            if (RoundState.Tracks.GetValueOrDefault(coroner, (byte)255) == id)
            { BodyTracks.Remove(coroner); RoundState.Tracks.Remove(coroner); }
        RefreshHud(p);
    }

    private static void RefreshHud(PlayerControl p)
    {
        if (!p.AmOwner || !HudManager.Instance) return;
        p.Data.Role.SpawnTaskHeader(p);
        HudManager.Instance.SetHudActive(p, p.Data.Role, true);
        foreach (var button in CustomButtonManager.Buttons)
            if (button.GetType().Assembly == typeof(Plugin).Assembly)
                button.SetActive(true, p.Data.Role);
    }

    private static void Pin(PlayerControl p, Vector2 position)
    {
        p.moveable = false;
        p.MyPhysics.body.velocity = Vector2.zero;
        p.NetTransform.enabled = false;
        p.Visible = false;
        if (!RoundState.InRound) return;
        p.transform.position = new Vector3(position.x, position.y, position.y / 1000f);
        if (p.AmOwner && Camera.main)
        {
            var camera = Camera.main.GetComponent<FollowerCamera>();
            if (camera) { camera.Target = p; camera.Locked = false; camera.SnapToTarget(); }
        }
    }

    public static void Tick()
    {
        if (!AmongUsClient.Instance || !AmongUsClient.Instance.AmConnected || LobbyBehaviour.Instance)
        { if (Active.Count > 0 || Used.Count > 0) Reset(); return; }
        foreach (var (id, position) in Active.ToArray())
        {
            var p = RoundState.Find(id);
            if (!p || p!.Data == null || p.Data.Disconnected)
            { Active.Remove(id); continue; }
            Pin(p, position);
        }
    }

    public static void Hud(HudManager hud)
    {
        var me = PlayerControl.LocalPlayer;
        if (!IsFaking(me)) return;
        hud.SabotageButton.ToggleVisible(false);
        hud.ReportButton.ToggleVisible(false);
        hud.ImpostorVentButton.ToggleVisible(false);
        hud.UseButton.ToggleVisible(false);
        hud.AbilityButton.ToggleVisible(false);
        if (RoundState.InRound)
        {
            hud.ShadowQuad.gameObject.SetActive(true);
            hud.Chat.SetVisible(false);
            // A Faker must not gain spectator information about real ghosts.
            foreach (var p in PlayerControl.AllPlayerControls)
                if (p.Data != null && p.Data.IsDead) p.Visible = false;
        }
    }

    public static Vector2 BodyPosition(byte id)
    {
        var body = MiraAPI.Utilities.Helpers.GetBodyById(id);
        return body ? body!.TruePosition : Active[id];
    }

    public static void Reset()
    {
        foreach (var id in Active.Keys)
        {
            var p = RoundState.Find(id);
            if (p) { p!.NetTransform.enabled = true; p.moveable = true; }
        }
        Active.Clear(); Used.Clear(); BodyTracks.Clear(); LocalFakeFrame = -1; MeetingEndRequested = false;
        DeferEliminationUntilMeeting = false;
    }
}

// This host boundary is reached after the game's report/emergency validation,
// before the meeting is broadcast to peers or voting/cutscenes can proceed.
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcStartMeeting))]
public static class FakerMeetingEndPatch
{
    public static bool Prefix() => !FakerState.EndAtMeeting();
}

// Do not cancel an already-selected native elimination ending: that can prevent
// the normal task check from being reached. While a lone Faker may return, retain
// timed sabotage and native task checks and defer just player-count elimination.
[HarmonyPatch(typeof(LogicGameFlowNormal), nameof(LogicGameFlowNormal.CheckEndCriteria))]
public static class FakerRoundEndPatch
{
    public static bool Prefix(LogicGameFlowNormal __instance, bool __runOriginal)
    {
        if (!__runOriginal) return false;
        if (!AmongUsClient.Instance.AmHost || !RoundState.InRound || !FakerState.DeferEliminationUntilMeeting ||
            !FakerState.OnlyFakedImpostors || FakerState.MeetingEndRequested || RoundState.JesterWinner != 255) return true;
        if (RoundState.ResolvingBlast) return false;
        // Life support is separate from the critical-reactor interface. This
        // follows the game's sabotage types, also used by TOU's flow checks.
        if (ShipStatus.Instance.Systems.TryGetValue(SystemTypes.LifeSupp, out var system) &&
            system.TryCast<LifeSuppSystemType>() is { Countdown: < 0f } lifeSupport)
        {
            __instance.EndGameForSabotage();
            lifeSupport.Countdown = 10000f;
            return false;
        }
        foreach (var value in ShipStatus.Instance.Systems.Values)
        {
            var critical = value.TryCast<ICriticalSabotage>();
            if (critical == null || critical.Countdown >= 0f) continue;
            __instance.EndGameForSabotage();
            critical.ClearSabotage();
            return false;
        }
        GameManager.Instance.CheckEndGameViaTasks();
        return false;
    }
}

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
public static class FakerMovementPatch
{
    public static void Postfix(PlayerControl __instance)
    { if (__instance.AmOwner) FakerState.Tick(); }
}

[HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.CalculateLightRadius))]
public static class FakerVisionPatch
{
    public static void Postfix(ShipStatus __instance, NetworkedPlayerInfo player, ref float __result)
    {
        if (player != null && FakerState.Active.ContainsKey(player.PlayerId))
            __result = __instance.MaxLightRadius * GameOptionsManager.Instance.currentNormalGameOptions.ImpostorLightMod;
    }
}

[HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.UpdateSystem), typeof(SystemTypes), typeof(PlayerControl), typeof(MessageReader))]
public static class FakerSystemPatch
{
    public static bool Prefix(PlayerControl player) => !FakerState.IsFaking(player);
}
[HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.UpdateSystem), typeof(SystemTypes), typeof(PlayerControl), typeof(byte))]
public static class FakerByteSystemPatch
{
    public static bool Prefix(PlayerControl player) => !FakerState.IsFaking(player);
}
[HarmonyPatch(typeof(SabotageSystemType), nameof(SabotageSystemType.UpdateSystem))]
public static class FakerSabotageSystemPatch
{
    public static bool Prefix(PlayerControl player) => !FakerState.IsFaking(player);
}
[HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.RpcCloseDoorsOfType))]
public static class FakerDoorPatch
{
    public static bool Prefix() => !FakerState.IsFaking(PlayerControl.LocalPlayer);
}
[HarmonyPatch(typeof(MapBehaviour), nameof(MapBehaviour.ShowSabotageMap))]
public static class FakerSabotageMapPatch
{
    public static bool Prefix() => !FakerState.IsFaking(PlayerControl.LocalPlayer);
}
[HarmonyPatch(typeof(MapBehaviour), nameof(MapBehaviour.ShowNormalMap))]
public static class FakerMapPatch
{
    public static bool Prefix() => !FakerState.IsFaking(PlayerControl.LocalPlayer);
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcSendChat))]
public static class FakerSendChatPatch
{
    public static bool Prefix(PlayerControl __instance) => !FakerState.IsFaking(__instance);
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcSendQuickChat))]
public static class FakerQuickChatPatch
{
    public static bool Prefix(PlayerControl __instance) => !FakerState.IsFaking(__instance);
}
[HarmonyPatch(typeof(ChatController), nameof(ChatController.AddChat))]
public static class FakerReceiveChatPatch
{
    public static bool Prefix(PlayerControl sourcePlayer) =>
        !FakerState.IsFaking(PlayerControl.LocalPlayer) || !sourcePlayer || !sourcePlayer.Data.IsDead;
}
