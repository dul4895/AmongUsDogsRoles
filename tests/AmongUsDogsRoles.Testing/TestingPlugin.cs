using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Hazel;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MiraAPI.Hud;
using MiraAPI.Roles;
using MiraAPI.Utilities;
using Reactor;
using Reactor.Networking;
using Reactor.Networking.Attributes;
using Reactor.Networking.Rpc;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AmongUsDogsRoles.Testing;

[BepInPlugin(Id, "AmongUsDogsRoles Testing", "0.1.0")]
[BepInDependency("dogs.amongusdogsroles")]
[BepInDependency(ReactorPlugin.Id)]
[ReactorModFlags(ModFlags.RequireOnAllClients)]
public sealed class TestingPlugin : BasePlugin
{
    public const string Id = "dogs.amongusdogsroles.testing";
    public override void Load()
    {
        AddComponent<TestingPanel>();
        AddComponent<RuntimeChecks>();
        if (Environment.GetCommandLineArgs().Contains("--hardening"))
        {
            AddComponent<HardeningBridge>();
            new Harmony("dogs.amongusdogsroles.hardening").PatchAll(typeof(HardeningBridge).Assembly);
        }
        Log.LogInfo("F2 opens testing. Online controls require --dogs-test and the private dogs endpoint.");
    }
}

public enum TestCommand : byte { Reset, Revive, Role, RawRole }
public readonly record struct TestUpdate(TestCommand Command, byte Player = 255, ushort Role = 0);

// Only a host in an allowed test session can issue changes; every test client applies them.
[RegisterCustomRpc(1)]
public sealed class TestRpc(TestingPlugin plugin, uint id) : PlayerCustomRpc<TestingPlugin, TestUpdate>(plugin, id)
{
    public override RpcLocalHandling LocalHandling => RpcLocalHandling.Before;
    public override void Write(MessageWriter w, TestUpdate d) { w.Write((byte)d.Command); w.Write(d.Player); w.Write(d.Role); }
    public override TestUpdate Read(MessageReader r) => new((TestCommand)r.ReadByte(), r.ReadByte(), r.ReadUInt16());
    public override void Handle(PlayerControl sender, TestUpdate d)
    {
        if (!TestState.AllowedSession || !sender.IsHost()) return;
        if (d.Command == TestCommand.Reset) { TestState.Reset(); return; }
        var player = RoundState.Find(d.Player);
        if (!player || player!.Data == null || player.Data.Disconnected) return;
        if (d.Command is TestCommand.Revive or TestCommand.Role or TestCommand.RawRole)
        {
            foreach (var body in Object.FindObjectsOfType<DeadBody>())
                if (body.ParentId == d.Player) Object.Destroy(body.gameObject);
            if (player.Data.IsDead) player.Revive();
            var role = d.Command != TestCommand.Revive ? d.Role : TestState.AliveRoles.GetValueOrDefault(d.Player, (ushort)AmongUs.GameOptions.RoleTypes.Crewmate);
            RoleManager.Instance.SetRole(player, (AmongUs.GameOptions.RoleTypes)role);
            TestState.AliveRoles[d.Player] = role;
            if (player.Data.Role is JesterRole) RoundState.Jesters.Add(d.Player);
            else RoundState.Jesters.Remove(d.Player);
            if (player.AmOwner && d.Command != TestCommand.RawRole)
            {
                if (HudManager.Instance) HudManager.Instance.SetHudActive(player, player.Data.Role, true);
                TestState.ResetButtons();
            }
        }
    }
    public static void Send(TestUpdate d)
    {
        if (TestState.CanEdit) Rpc<TestRpc>.Instance.Send(d, true);
    }
}

public static class TestState
{
    public static readonly Dictionary<byte, ushort> AliveRoles = new();
    public static bool LocalSession => AmongUsClient.Instance &&
        AmongUsClient.Instance.NetworkMode is NetworkModes.FreePlay or NetworkModes.LocalGame;
    public static bool DogsEnabled => Environment.GetCommandLineArgs().Contains("--dogs-test");
    public static bool DogsRegion => DogsEnabled && ServerManager.Instance &&
        ServerManager.Instance.CurrentRegion?.TryCast<StaticHttpRegionInfo>() is { } region &&
        region.Name == "dogs" && region.PingServer == "amongus.playpartygames.io" &&
        region.Servers.Length > 0 && region.Servers.All(s => s.Ip == "https://amongus.playpartygames.io" && s.Port == 443);
    public static bool DogsSession => DogsRegion && AmongUsClient.Instance && AmongUsClient.Instance.AmConnected &&
        AmongUsClient.Instance.NetworkMode == NetworkModes.OnlineGame && !AmongUsClient.Instance.IsGamePublic &&
        AmongUsClient.Instance.GetNetworkAddress() == "46.224.176.196" && AmongUsClient.Instance.GetNetworkPort() == 22023;
    public static bool AllowedSession => LocalSession || DogsSession;
    public static bool CanEdit => AllowedSession && AmongUsClient.Instance.AmHost && RoundState.InRound;
    public static void Reset()
    {
        RoundState.Reset();
        foreach (var p in PlayerControl.AllPlayerControls)
            if (p.Data?.Role is JesterRole) RoundState.Jesters.Add(p.PlayerId);
        ResetButtons();
    }
    public static void ResetButtons()
    {
        foreach (var button in CustomButtonManager.Buttons)
            if (button.GetType().Assembly == typeof(Plugin).Assembly)
            {
                button.EffectActive = false;
                button.Timer = 0;
                if (button.LimitedUses) button.SetUses(button.MaxUses);
                else button.Button?.SetInfiniteUses();
            }
        if (PlayerControl.LocalPlayer) PlayerControl.LocalPlayer.SetKillTimer(0);
    }
}

public sealed class TestingPanel(IntPtr ptr) : MonoBehaviour(ptr)
{
    private bool visible = true;
    private byte actorId = 255, targetId = 255;
    private string status = "Enter Practice / Freeplay, then press F2.";
    private Vector2 scroll;
    private static readonly Type[] Roles = [typeof(PenguinRole), typeof(BomberRole), typeof(ConsigliereRole), typeof(EscapistRole), typeof(FakerRole), typeof(HackerRole), typeof(VeteranRole), typeof(SheriffRole), typeof(CoronerRole), typeof(JesterRole)];
    public void Update()
    {
        if (Input.GetKeyDown(KeyCode.F2)) visible = !visible;
        if (Input.GetKeyDown(KeyCode.F4) && TestState.AllowedSession && PlayerControl.LocalPlayer)
        {
            var me = PlayerControl.LocalPlayer;
            var snapshot = new {
                player = me.PlayerId, host = AmongUsClient.Instance.AmHost, canMove = me.CanMove,
                canAct = RoundState.CanAct(me), dragged = RoundState.Dragged(me.PlayerId),
                reveals = RoundState.Revealed, tracks = RoundState.Tracks, jesterWinner = RoundState.JesterWinner,
                players = PlayerControl.AllPlayerControls.ToArray().Where(p => p && p.Data?.Role != null).Select(p => new {
                    id = p.PlayerId, role = p.Data.Role.GetType().Name, dead = p.Data.IsDead,
                    x = p.GetTruePosition().x, y = p.GetTruePosition().y, moveable = p.moveable
                }).ToArray()
            };
            System.IO.File.WriteAllText(System.IO.Path.Combine(BepInEx.Paths.GameRootPath, "test-state.json"),
                System.Text.Json.JsonSerializer.Serialize(snapshot, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        Application.runInBackground = true;
        if (!TestState.AllowedSession || !ShipStatus.Instance) { TestState.AliveRoles.Clear(); return; }
        foreach (var p in PlayerControl.AllPlayerControls)
            if (p.Data?.Role != null && !p.Data.IsDead) TestState.AliveRoles[p.PlayerId] = (ushort)p.Data.Role.Role;
    }
    public void OnGUI()
    {
        if (!visible || TestMedia.Clean) return;
        GUILayout.BeginArea(new Rect(12, 30, 380, Math.Min(Screen.height - 45, 680)), GUI.skin.box);
        scroll = GUILayout.BeginScrollView(scroll);
        GUILayout.Label("AmongUsDogsRoles Testing — F2 to hide");
        if (!Environment.GetCommandLineArgs().Contains("--hardening"))
            GUILayout.Label("F1: Reactor debugger");
        if (!TestState.CanEdit)
        {
            GUILayout.Label("Start Practice, Local, or an explicitly enabled private dogs test.\nOnly the host controls this panel.");
        }
        else DrawControls();
        GUILayout.Space(6);
        GUILayout.Label(status);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }
    [HideFromIl2Cpp]
    private void Button(string label, Action action)
    {
        if (!GUILayout.Button(label)) return;
        try { action(); status = label; }
        catch (Exception e) { status = e.Message; Reactor.Utilities.Logger<TestingPlugin>.Error(e); }
    }
    private static string Describe(PlayerControl? p) => !p ? "none" : $"{p!.Data.PlayerName}: {(p.Data.Role is ICustomRole role ? role.RoleName : p.Data.Role?.GetType().Name.Replace("Role", ""))} {(p.Data.IsDead ? "(dead)" : "")}";
    private void DrawControls()
    {
        if (!PlayerControl.LocalPlayer || PlayerControl.LocalPlayer.Data?.Role == null) return;
        if (Environment.GetCommandLineArgs().Contains("--hardening"))
            Button(HardeningBridge.SuspendEndings ? "Automatic endings: PAUSED — click to enable" : "Automatic endings: ON — click to pause",
                () => HardeningBridge.SuspendEndings = !HardeningBridge.SuspendEndings);
        var players = PlayerControl.AllPlayerControls.ToArray().Where(p => p && p.Data?.Role != null && !p.Data.Disconnected).OrderBy(p => p.PlayerId).ToArray();
        if (players.Length == 0) return;
        if (!players.Any(p => p.PlayerId == actorId)) actorId = PlayerControl.LocalPlayer.PlayerId;
        if (!players.Any(p => p.PlayerId == targetId)) targetId = players.FirstOrDefault(p => p.PlayerId != actorId)?.PlayerId ?? actorId;
        var actor = RoundState.Find(actorId)!;
        var target = RoundState.Find(targetId)!;
        Button("Actor: " + Describe(actor) + "  [next]", () => actorId = Next(players, actorId));
        Button("Target: " + Describe(target) + "  [next]", () => targetId = Next(players, targetId));
        Button("Select myself as actor", () => actorId = PlayerControl.LocalPlayer.PlayerId);
        GUILayout.Label("Assign role to ACTOR (also revives)");
        for (var i = 0; i < Roles.Length; i += 2)
        {
            GUILayout.BeginHorizontal();
            foreach (var type in Roles.Skip(i).Take(2))
                Button(RoleNames.For(type), () => Assign(actor, RoleId.Get(type)));
            GUILayout.EndHorizontal();
        }
        GUILayout.BeginHorizontal();
        Button("Crewmate", () => Assign(actor, (ushort)AmongUs.GameOptions.RoleTypes.Crewmate));
        Button("Impostor", () => Assign(actor, (ushort)AmongUs.GameOptions.RoleTypes.Impostor));
        GUILayout.EndHorizontal();
        Button("Reset ALL role state, cooldowns and uses", () => TestRpc.Send(new(TestCommand.Reset)));
        Button("Revive actor / restore last living role", () => { TestRpc.Send(new(TestCommand.Reset)); TestRpc.Send(new(TestCommand.Revive, actorId)); });
        if (TutorialManager.InstanceExists) Button("Spawn crewmate dummy beside me", SpawnDummy);
        if (TutorialManager.InstanceExists) Button("Run in-game checks (F3)", RuntimeChecks.Begin);
        Button("Move target beside actor", () => target.NetTransform.RpcSnapTo(actor.GetTruePosition() + new Vector2(0.55f, 0)));
        GUILayout.Label("Run ACTOR's real ability against TARGET\nRange, cooldowns and role restrictions still apply.");
        foreach (var ability in Abilities(actor))
            Button(ability.ToString(), () => AbilityService.Handle(actor, new Request(ability, targetId)));
        Button("Setup corpse: actor kills target", () => AbilityService.Kill(actor, target));
        GUILayout.Label("Use Sniff on a dead target for Coroner.\nKamikaze uses its dedicated Explode button.\nFor Jester, enable game endings and vote normally.");
    }
    private static byte Next(PlayerControl[] ps, byte id) => ps[(Array.FindIndex(ps, p => p.PlayerId == id) + 1) % ps.Length].PlayerId;
    private static void Assign(PlayerControl p, ushort role)
    {
        TestRpc.Send(new(TestCommand.Reset));
        TestRpc.Send(new(TestCommand.Role, p.PlayerId, role));
    }
    private static Ability[] Abilities(PlayerControl p) => p.Data.Role switch
    {
        PenguinRole => [Ability.Drag, Ability.Execute, Ability.Release, Ability.Kill],
        BomberRole => [Ability.Detonate, Ability.Kill],
        ConsigliereRole => [Ability.Investigate, Ability.Kill],
        EscapistRole => [Ability.Mark, Ability.Recall, Ability.Kill],
        VeteranRole => [Ability.Alert], SheriffRole => [Ability.Shoot], CoronerRole => [Ability.Examine], _ => [],
    };
    private void SpawnDummy()
    {
        // Based on Reactor.Debugger GameTab.SpawnDummy, with deterministic plain cosmetics.
        var p = Object.Instantiate(AmongUsClient.Instance.PlayerPrefab);
        p.PlayerId = (byte)GameData.Instance.GetAvailableId();
        var data = GameData.Instance.AddDummy(p);
        AmongUsClient.Instance.Spawn(data);
        AmongUsClient.Instance.Spawn(p);
        p.isDummy = true;
        p.transform.position = PlayerControl.LocalPlayer.transform.position + new Vector3(0.55f, 0, 0);
        p.GetComponent<DummyBehaviour>().enabled = true;
        p.NetTransform.enabled = false;
        p.SetName("Test " + p.PlayerId);
        p.SetColor((byte)(p.PlayerId % Palette.PlayerColors.Length));
        data.RpcSetTasks(new Il2CppStructArray<byte>(0));
        TestRpc.Send(new(TestCommand.Role, p.PlayerId, (ushort)AmongUs.GameOptions.RoleTypes.Crewmate));
        targetId = p.PlayerId;
    }
}
