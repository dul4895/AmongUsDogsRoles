using System.Collections;
using BepInEx.Unity.IL2CPP.Utils;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using InnerNet;
using MiraAPI.Hud;
using MiraAPI.Roles;
using Reactor.Utilities;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AmongUsDogsRoles.Testing;

// Integration checks run inside the actual Unity game. These are not a mock world.
// F3 is deliberately opt-in and restricted to solo Practice.
public sealed class RuntimeChecks(IntPtr ptr) : MonoBehaviour(ptr)
{
    private static RuntimeChecks? instance;
    private static bool running;
    private static int passed, failed;
    private static string report = "";
    private static PlayerControl Me => PlayerControl.LocalPlayer;
    private static PlayerControl a = null!, b = null!, c = null!;
    private static Vector2 origin;
    private bool autoStarted;
    public void Awake() => instance = this;
    public void Update()
    {
        if (Input.GetKeyDown(KeyCode.F3)) Begin();
        if (!autoStarted && Environment.GetCommandLineArgs().Contains("--role-checks") &&
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu" && Time.realtimeSinceStartup > 12)
        {
            autoStarted = true;
            this.StartCoroutine(EnterPractice());
        }
    }
    [HideFromIl2Cpp]
    private static IEnumerator EnterPractice()
    {
        var client = AmongUsClient.Instance;
        client.NetworkMode = NetworkModes.FreePlay;
        InnerNetServer.Instance.StartAsLocalServer();
        client.MainMenuScene = "MainMenu";
        client.OnlineScene = "Tutorial";
        client.SetEndpoint(Constants.LocalNetAddress, Constants.GamePlayPort, false);
        client.Connect(MatchMakerModes.HostAndClient, null);
        yield return client.WaitForConnectionOrFail();
        var deadline = Time.realtimeSinceStartup + 40;
        while ((!TestState.CanEdit || !Me || Me.Data?.Role == null) && Time.realtimeSinceStartup < deadline) yield return null;
        yield return new WaitForSeconds(2);
        Begin();
    }
    [HideFromIl2Cpp]
    public static void Begin()
    {
        if (running || !TestState.CanEdit || !TutorialManager.InstanceExists || instance == null) return;
        running = true; passed = failed = 0; report = "";
        instance.StartCoroutine(Guard(Run()));
    }
    [HideFromIl2Cpp]
    private static IEnumerator Guard(IEnumerator routine)
    {
        while (true)
        {
            object? next;
            try { if (!routine.MoveNext()) break; next = routine.Current; }
            catch (Exception e) { Check("Suite completed without exception: " + e, false); break; }
            yield return next;
        }
        running = false;
        Log($"RESULT: {passed} passed, {failed} failed. These checks exercise the real game in Practice; cross-client voting still needs a Local match.");
        System.IO.File.WriteAllText(System.IO.Path.Combine(BepInEx.Paths.GameRootPath, "role-checks.txt"), report);
    }
    [HideFromIl2Cpp]
    private static void Log(string s) { report += s + "\n"; Logger<TestingPlugin>.Info("[RuntimeChecks] " + s); }
    [HideFromIl2Cpp]
    private static void Check(string label, bool ok) { if (ok) passed++; else failed++; Log((ok ? "PASS: " : "FAIL: ") + label); }
    [HideFromIl2Cpp]
    private static PlayerControl Dummy()
    {
        var p = Object.Instantiate(AmongUsClient.Instance.PlayerPrefab);
        p.PlayerId = (byte)GameData.Instance.GetAvailableId();
        var data = GameData.Instance.AddDummy(p);
        AmongUsClient.Instance.Spawn(data); AmongUsClient.Instance.Spawn(p);
        p.isDummy = true; p.GetComponent<DummyBehaviour>().enabled = true; p.NetTransform.enabled = false;
        p.SetName("QA " + p.PlayerId); p.SetColor((byte)(p.PlayerId % Palette.PlayerColors.Length));
        data.RpcSetTasks(new Il2CppStructArray<byte>(0));
        return p;
    }
    [HideFromIl2Cpp]
    private static void Set(PlayerControl p, ushort role) => TestRpc.Send(new(TestCommand.Role, p.PlayerId, role));
    [HideFromIl2Cpp]
    private static void Set<T>(PlayerControl p) where T : ICustomRole => Set(p, RoleId.Get<T>());
    [HideFromIl2Cpp]
    private static void Move(PlayerControl p, Vector2 pos) => p.NetTransform.RpcSnapTo(pos + (Vector2)p.transform.position - p.GetTruePosition());
    [HideFromIl2Cpp]
    private static void Prepare<T>() where T : ICustomRole
    {
        TestRpc.Send(new(TestCommand.Reset));
        if (Me.CurrentOutfitType == PlayerOutfitType.Shapeshifted) Me.Shapeshift(Me, false);
        Set<T>(Me);
        foreach (var p in new[] { a, b, c }) Set(p, (ushort)AmongUs.GameOptions.RoleTypes.Crewmate);
        Move(Me, origin); Move(a, origin + new Vector2(0.6f, 0)); Move(b, origin + new Vector2(0, 0.8f)); Move(c, origin + new Vector2(5, 0));
    }
    [HideFromIl2Cpp]
    private static void Click<T>() where T : CustomActionButton
    {
        var btn = CustomButtonManager.Buttons.OfType<T>().Single();
        var ready = btn.Button != null && btn.Button.gameObject.activeInHierarchy && btn.CanClick();
        if (!ready) Log($"Button diagnostics: {typeof(T).Name}, dead={Me.Data.IsDead}, moveable={Me.moveable}, CanMove={Me.CanMove}, timer={btn.Timer}, effect={btn.EffectActive}, role={Me.Data.Role}, targetDistance={Vector2.Distance(Me.GetTruePosition(), a.GetTruePosition())}");
        Check(typeof(T).Name + " is visible and clickable", ready);
        btn.ClickHandler();
    }
    [HideFromIl2Cpp]
    private static IEnumerator Run()
    {
        Log("Game=" + Application.version + "; mode=" + AmongUsClient.Instance.NetworkMode);
        origin = Me.GetTruePosition();
        a = a && a.Data != null ? a : Dummy(); b = b && b.Data != null ? b : Dummy(); c = c && c.Data != null ? c : Dummy();
        yield return new WaitForSeconds(0.5f);
        Prepare<EscapistRole>(); yield return new WaitForSeconds(0.5f);
        Click<MarkButton>(); yield return new WaitForSeconds(0.2f);
        Check("Escapist creates a mark", RoundState.Marks.ContainsKey(Me.PlayerId));
        Move(Me, origin + new Vector2(-1, 0)); yield return new WaitForSeconds(0.2f);
        Click<RecallButton>(); yield return new WaitForSeconds(0.3f);
        Log($"Recall distance={Vector2.Distance(Me.GetTruePosition(), origin)}; mark present={RoundState.Marks.ContainsKey(Me.PlayerId)}");
        Check("Escapist returns and consumes mark", Vector2.Distance(Me.GetTruePosition(), origin) < 0.12f && !RoundState.Marks.ContainsKey(Me.PlayerId));
        Check("Escapist cooldown enforced", !RoundState.Ready(Me.PlayerId, Ability.Mark));

        Prepare<PenguinRole>(); yield return new WaitForSeconds(0.5f);
        Click<DragButton>(); yield return new WaitForSeconds(0.3f);
        Check("Penguin captures target and immobilizes it", RoundState.Drags.ContainsKey(Me.PlayerId) && !RoundState.CanAct(a) && !a.moveable);
        Move(Me, origin + new Vector2(-0.5f, 0)); yield return new WaitForSeconds(0.3f);
        Check("Penguin captive follows captor", Vector2.Distance(Me.GetTruePosition(), a.GetTruePosition()) < 0.3f);
        Click<ReleaseButton>(); yield return new WaitForSeconds(0.2f);
        Check("Penguin release restores movement", !RoundState.Dragged(a.PlayerId) && a.moveable && !a.Data.IsDead);
        TestRpc.Send(new(TestCommand.Reset)); Move(a, Me.GetTruePosition() + new Vector2(0.6f, 0)); yield return new WaitForSeconds(0.3f);
        Click<DragButton>(); yield return new WaitForSeconds(0.2f);
        Log($"Execute capture={RoundState.Drags.GetValueOrDefault(Me.PlayerId).Target}, expected={a.PlayerId}, aPos={a.GetTruePosition()}, bPos={b.GetTruePosition()}");
        Click<ExecuteButton>(); yield return new WaitForSeconds(1.8f);
        Log($"Execute aDead={a.Data.IsDead} bDead={b.Data.IsDead} captures={RoundState.Drags.Count}");
        Check("Penguin executes only captive", a.Data.IsDead && !b.Data.IsDead && !RoundState.Drags.ContainsKey(Me.PlayerId));
        Prepare<PenguinRole>(); yield return new WaitForSeconds(0.4f); Click<KillButton>(); yield return new WaitForSeconds(0.5f);
        Check("Penguin ordinary kill works without capture", a.Data.IsDead);

        Prepare<ConsigliereRole>(); Set<JesterRole>(a); yield return new WaitForSeconds(0.5f);
        Click<InvestigateButton>(); yield return new WaitForSeconds(0.3f);
        Check("Consigliere reveals exact target role", RoundState.Revealed.GetValueOrDefault(a.PlayerId, "").Contains("Jester"));
        Click<KillButton>(); yield return new WaitForSeconds(0.5f);
        Check("Consigliere retains ordinary kill", a.Data.IsDead);

        Prepare<SheriffRole>(); Set(a, (ushort)AmongUs.GameOptions.RoleTypes.Impostor); yield return new WaitForSeconds(0.5f);
        Click<ShootButton>(); yield return new WaitForSeconds(2.2f);
        Check("Sheriff kills impostor and survives", a.Data.IsDead && !Me.Data.IsDead);
        Prepare<SheriffRole>(); yield return new WaitForSeconds(0.5f); Click<ShootButton>(); yield return new WaitForSeconds(2.2f);
        Check("Sheriff crew misfire kills only Sheriff", Me.Data.IsDead && !a.Data.IsDead);
        Prepare<SheriffRole>(); Set<JesterRole>(a); yield return new WaitForSeconds(0.5f); Click<ShootButton>(); yield return new WaitForSeconds(2.2f);
        Check("Sheriff kills Jester without Jester victory", a.Data.IsDead && !Me.Data.IsDead && RoundState.JesterWinner == 255);

        Prepare<VeteranRole>(); Set<SheriffRole>(a); yield return new WaitForSeconds(0.5f);
        Click<AlertButton>(); yield return new WaitForSeconds(0.2f);
        Check("Veteran activates alert and consumes one use", RoundState.Alerting(Me) && RoundState.AlertsUsed.GetValueOrDefault(Me.PlayerId) == 1);
        AbilityService.Handle(a, new Request(Ability.Shoot, Me.PlayerId)); yield return new WaitForSeconds(2.2f);
        Check("Alert Veteran kills attacking Sheriff and survives", a.Data.IsDead && !Me.Data.IsDead);
        foreach (var type in new[] { typeof(ConsigliereRole), typeof(PenguinRole) })
        {
            Prepare<VeteranRole>(); Set(a, RoleId.Get(type)); yield return new WaitForSeconds(0.5f);
            Click<AlertButton>(); yield return new WaitForSeconds(0.1f);
            AbilityService.Handle(a, new Request(type == typeof(PenguinRole) ? Ability.Drag : Ability.Investigate, Me.PlayerId));
            yield return new WaitForSeconds(0.5f);
            Check("Veteran retaliates against " + type.Name, a.Data.IsDead && !Me.Data.IsDead && !RoundState.Dragged(Me.PlayerId));
        }

        Prepare<CoronerRole>(); Set(b, (ushort)AmongUs.GameOptions.RoleTypes.Impostor); yield return new WaitForSeconds(0.4f);
        AbilityService.Kill(b, a); yield return new WaitForSeconds(2.2f);
        Check("Coroner setup records actual killer", RoundState.Killers.GetValueOrDefault(a.PlayerId, (byte)255) == b.PlayerId);
        Click<ExamineButton>(); yield return new WaitForSeconds(0.3f);
        Check("Coroner tracks killer without a meeting", RoundState.Tracks.GetValueOrDefault(Me.PlayerId, (byte)255) == b.PlayerId && !MeetingHud.Instance);
        var arrow = Object.FindObjectsOfType<SpriteRenderer>().FirstOrDefault(t => t.name == "AmongUsDogsRolesKillerArrow");
        Check("Coroner arrow visible", arrow != null && arrow.gameObject.activeInHierarchy);

        Prepare<BomberRole>(); Set(b, (ushort)AmongUs.GameOptions.RoleTypes.Impostor); yield return new WaitForSeconds(0.5f);
        Check("Bomber has no shapeshift ability", Me.Data.Role.TryCast<ShapeshifterRole>() == null);
        Click<DetonateButton>(); yield return new WaitForSeconds(2.2f);
        Check("Bomber blast kills self, nearby crew and teammate", Me.Data.IsDead && a.Data.IsDead && b.Data.IsDead);
        Check("Bomber leaves distant player alive", !c.Data.IsDead);

        Prepare<JesterRole>(); yield return new WaitForSeconds(0.5f);
        Check("Jester registered as neutral with no crew-task contribution", RoundState.Team(Me) == Faction.Neutral && !Me.Data.Role.TasksCountTowardProgress);
        VoteOutcomePatch.Prefix(Me.Data, true);
        Check("Tied Jester vote does not mark winner", RoundState.JesterWinner == 255);
        VoteOutcomePatch.Prefix(Me.Data, false);
        Check("Jester ejection handler identifies sole winner", RoundState.JesterWinner == Me.PlayerId);
        Prepare<EscapistRole>(); yield return new WaitForSeconds(0.3f);
        Check("Reset restores a living player and clean role state", !Me.Data.IsDead && RoundState.JesterWinner == 255 && RoundState.Drags.Count == 0);
    }
}
