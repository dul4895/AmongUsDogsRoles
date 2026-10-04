using HarmonyLib;
using Hazel;
using MiraAPI.Utilities;
using Reactor.Networking.Attributes;
using Reactor.Networking.Rpc;
using Reactor.Utilities.Attributes;
using UnityEngine;

namespace AmongUsDogsRoles;

public readonly record struct HackSnapshot(int Seed, byte[] Players, byte[] Status, byte[] Partners);

public static class HackerState
{
    public const float Duration = 15;
    public static float Until { get; private set; }
    public static int Seed { get; private set; }
    public static readonly Dictionary<byte, byte> Vitals = new();
    public static readonly Dictionary<byte, byte> Pairs = new();
    public static bool Active => Time.time < Until && RoundState.InRound;
    public static void Apply(HackSnapshot snapshot)
    {
        Clear(); Seed = snapshot.Seed; Until = Time.time + Duration;
        for (var i = 0; i < snapshot.Players.Length; i++)
        {
            Vitals[snapshot.Players[i]] = snapshot.Status[i];
            if (snapshot.Partners[i] != 255) Pairs[snapshot.Players[i]] = snapshot.Partners[i];
        }
    }
    public static void Clear()
    { CameraHackPresentation.Restore(); Until = 0; Vitals.Clear(); Pairs.Clear(); }
    public static bool ViewingCameras => Minigame.Instance &&
        (Minigame.Instance.TryCast<SurveillanceMinigame>() != null ||
         Minigame.Instance.TryCast<PlanetSurveillanceMinigame>() != null ||
         Minigame.Instance.TryCast<FungleSurveillanceMinigame>() != null);
    public static Camera[] FeedCameras()
    {
        if (!ViewingCameras) return [];
        var fungle = Minigame.Instance.TryCast<FungleSurveillanceMinigame>();
        if (fungle && fungle!.securityCamera) return [fungle.securityCamera.cam];
        return Minigame.Instance.GetComponentsInChildren<Camera>(true).Where(c => c.targetTexture).ToArray();
    }
    public static void Tick()
    {
        if (!Active) { if (Until != 0) Clear(); return; }
        if (!ViewingCameras) return;
        // All surveillance feeds render to textures, including the Fungle scope.
        // Surveillance feeds use disabled cameras and explicit Render(), so
        // Camera.allCameras (enabled cameras only) silently misses them.
        foreach (var camera in FeedCameras())
            if (camera.targetTexture && !camera.GetComponent<CameraHackPresentation>())
                camera.gameObject.AddComponent<CameraHackPresentation>();
    }
}

[RegisterCustomRpc(4)]
public sealed class HackRpc(Plugin plugin, uint id) : PlayerCustomRpc<Plugin, HackSnapshot>(plugin, id)
{
    public override RpcLocalHandling LocalHandling => RpcLocalHandling.Before;
    public override void Write(MessageWriter w, HackSnapshot d)
    {
        w.Write(d.Seed); w.Write((byte)d.Players.Length);
        for (var i = 0; i < d.Players.Length; i++) { w.Write(d.Players[i]); w.Write(d.Status[i]); w.Write(d.Partners[i]); }
    }
    public override HackSnapshot Read(MessageReader r)
    {
        var seed = r.ReadInt32(); var count = r.ReadByte();
        if (count > 15) throw new InvalidDataException("Invalid hack roster");
        var players = new byte[count]; var status = new byte[count]; var partners = new byte[count];
        for (var i = 0; i < count; i++) { players[i] = r.ReadByte(); status[i] = r.ReadByte(); partners[i] = r.ReadByte(); }
        return new(seed, players, status, partners);
    }
    public override void Handle(PlayerControl sender, HackSnapshot snapshot)
    { if (sender.IsHost() && RoundState.InRound) HackerState.Apply(snapshot); }
    public static void Start()
    {
        var players = GameData.Instance.AllPlayers.ToArray().OrderBy(p => p.PlayerId).ToArray();
        var seed = HashRandom.Next(int.MaxValue);
        var pairs = Rules.CameraPairs(players.Where(p => !p.IsDead && !p.Disconnected).Select(p => p.PlayerId).ToArray(), new System.Random(seed));
        Rpc<HackRpc>.Instance.Send(new(seed, players.Select(p => p.PlayerId).ToArray(),
            players.Select(p => (byte)(p.Disconnected ? 2 : p.IsDead ? 1 : 0)).ToArray(),
            players.Select(p => pairs.GetValueOrDefault(p.PlayerId, (byte)255)).ToArray()), true);
    }
}

// VitalsPanel owns presentation; never falsify NetworkedPlayerInfo.IsDead.
// Updating the panel preserves the pulse animation and Scientist battery logic.
[HarmonyPatch(typeof(VitalsMinigame), nameof(VitalsMinigame.Update))]
public static class HackedVitals
{
    public static bool Prefix(VitalsMinigame __instance)
    {
        if (!HackerState.Active) return true;
        var sabotaged = PlayerTask.PlayerHasTaskOfType<IHudOverrideTask>(PlayerControl.LocalPlayer);
        __instance.SabText.gameObject.SetActive(sabotaged);
        foreach (var panel in __instance.vitals)
        {
            panel.gameObject.SetActive(!sabotaged);
            if (!HackerState.Vitals.TryGetValue(panel.PlayerInfo.PlayerId, out var status)) continue;
            if (status == 2) { if (!panel.IsDiscon) panel.SetDisconnected(); }
            else if (status == 1) { if (!panel.IsDead || panel.IsDiscon) panel.SetDead(); }
            else if (panel.IsDead || panel.IsDiscon)
            {
                panel.SetAlive();
                // Native SetAlive only starts the heartbeat; it does not undo
                // the flags/background initialized by SetPlayer for a corpse.
                panel.IsDead = false; panel.IsDiscon = false;
                panel.Background.sprite = __instance.PanelPrefab.Background.sprite;
                panel.Cardio.gameObject.SetActive(true);
            }
        }
        return false;
    }
}
[HarmonyPatch(typeof(VitalsMinigame), nameof(VitalsMinigame.Begin))]
public static class HackedVitalsOpen
{ public static void Postfix(VitalsMinigame __instance) => HackedVitals.Prefix(__instance); }

[HarmonyPatch(typeof(MapCountOverlay), nameof(MapCountOverlay.Update))]
public static class HackedAdmin
{
    public static bool Collecting;
    public static readonly Dictionary<int, int> Counts = new();
    public static void Prefix()
    { Counts.Clear(); Collecting = HackerState.Active; }
    public static void Postfix(MapCountOverlay __instance)
    {
        Collecting = false;
        if (!HackerState.Active || __instance.isSab || Counts.Count == 0) return;
        var areas = __instance.CountAreas.ToArray();
        var actual = areas.Select(a => Counts.GetValueOrDefault(a.GetInstanceID(), a.myIcons.Count)).ToArray();
        // Stable per three-second interval, shared seed across observers. Real
        // room changes still propagate; most rooms stay completely accurate.
        var interval = (int)((HackerState.Duration - (HackerState.Until - Time.time)) / 3);
        var displayed = Rules.CorruptCounts(actual, HackerState.Seed ^ interval);
        for (var i = 0; i < areas.Length; i++) areas[i].UpdateCount(displayed[i]);
    }
    public static Exception? Finalizer(Exception? __exception) { Collecting = false; return __exception; }
}
[HarmonyPatch(typeof(CounterArea), nameof(CounterArea.UpdateCount))]
public static class HackedAdminCounts
{
    public static void Prefix(CounterArea __instance, [HarmonyArgument(0)] int count)
    { if (HackedAdmin.Collecting) HackedAdmin.Counts[__instance.GetInstanceID()] = count; }
}

// Change renderer colors and labels only during a surveillance camera render.
// Restore before the world/HUD renders. Movement, outfits, IDs and RPC data are
// untouched, and the paired player need not be visible in any camera feed.
[RegisterInIl2Cpp]
public sealed class CameraHackPresentation(IntPtr pointer) : MonoBehaviour(pointer)
{
    private static readonly List<(Material Material, Color Body, Color Back)> materials = [];
    private static readonly List<(TMPro.TextMeshPro Label, string Text)> names = [];
    public static int RenderCount { get; private set; }
    public void OnPreCull()
    {
        Restore();
        if (!HackerState.Active || !HackerState.ViewingCameras) return;
        RenderCount++;
        foreach (var (id, partner) in HackerState.Pairs)
        {
            var player = RoundState.Find(id); var other = RoundState.Find(partner);
            if (!RoundState.Alive(player) || !other || other!.Data == null || other.Data.Disconnected ||
                !player!.Visible || player.inVent) continue;
            var color = other.Data.DefaultOutfit.ColorId;
            if (color < 0 || color >= Palette.PlayerColors.Length) continue;
            var bodyRenderer = player.cosmetics.currentBodySprite.BodySprite;
            // BodyForms can be a sibling of the cosmetics object. Traverse the
            // player root and always include the current body material; some
            // native player shaders expose colors as uniforms, not Properties.
            foreach (var renderer in player.GetComponentsInChildren<SpriteRenderer>())
            {
                var material = renderer.material;
                if (renderer != bodyRenderer && (!material.HasProperty("_BodyColor") || !material.HasProperty("_BackColor"))) continue;
                materials.Add((material, material.GetColor("_BodyColor"), material.GetColor("_BackColor")));
                material.SetColor("_BodyColor", Palette.PlayerColors[color]);
                material.SetColor("_BackColor", Palette.ShadowColors[color]);
            }
            var label = player.cosmetics.nameText;
            names.Add((label, label.text)); label.text = other.Data.PlayerName; label.ForceMeshUpdate();
        }
    }
    public void OnPostRender() => Restore();
    public void OnDisable() => Restore();
    public void OnDestroy() => Restore();
    public static void Restore()
    {
        foreach (var (material, body, back) in materials)
            if (material) { material.SetColor("_BodyColor", body); material.SetColor("_BackColor", back); }
        foreach (var (label, text) in names) if (label) { label.text = text; label.ForceMeshUpdate(); }
        materials.Clear(); names.Clear();
    }
}
