using System.Collections;
using System.Text.Json;
using AmongUs.GameOptions;
using BepInEx;
using BepInEx.Unity.IL2CPP.Utils;
using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using InnerNet;
using MiraAPI.Roles;
using MiraAPI.Hud;
using MiraAPI.Keybinds;
using MiraAPI.GameOptions;
using UnityEngine;

namespace AmongUsDogsRoles.Testing;

// Explicit --hardening launch only. File commands exercise actual game/RPC code in
// isolated Local or explicitly enabled dogs sessions; absent from release installs.
public sealed class HardeningBridge(IntPtr ptr) : MonoBehaviour(ptr)
{
    public static bool SuspendEndings;
    public static object? LastEnd;
    public static string[] Winners = [];
    private string last = "";
    private float next;
    private static string Root => Paths.GameRootPath;
    public void Update()
    {
        Application.runInBackground = true;
        if (Time.realtimeSinceStartup < next) return;
        next = Time.realtimeSinceStartup + .2f;
        try
        {
            var path = Path.Combine(Root, "qa-command.json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                var d = doc.RootElement;
                var id = d.GetProperty("id").GetString()!;
                if (id != last)
                {
                    last = id;
                    try { Execute(d); Write("qa-response.json", new { id, ok = true }); }
                    catch (Exception e) { Write("qa-response.json", new { id, ok = false, error = e.ToString() }); }
                }
            }
            Write("qa-state.json", Snapshot());
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(Root, "qa-error.txt"), e.ToString()); }
    }
    [HideFromIl2Cpp] private static void Write(string name, object value) => File.WriteAllText(Path.Combine(Root, name), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    [HideFromIl2Cpp] public static object Snapshot()
    {
        var client = AmongUsClient.Instance;
        var me = PlayerControl.LocalPlayer;
        return new {
            scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
            version19 = Version19Checks.Snapshot(),
            gameVersion = Application.version, mode = client ? client.NetworkMode.ToString() : "none",
            map = GameOptionsManager.Instance?.currentNormalGameOptions?.MapId,
            impostorSetting = GameOptionsManager.Instance?.currentNormalGameOptions?.NumImpostors,
            adjustedImpostors = GameData.Instance ? GameOptionsManager.Instance.CurrentGameOptions.GetAdjustedNumImpostors(GameData.Instance!.PlayerCount) : -1,
            roleSettings = me && me.Data?.Role != null ? CustomRoleManager.CustomMiraRoles.Where(r=>r.GetType().Assembly==typeof(Plugin).Assembly).Select(r=>new {name=r.GetType().Name,displayName=r.RoleName,nativeName=TranslationController.Instance.GetString(((RoleBehaviour)r).StringName),count=r.GetCount(),chance=r.GetChance()}).ToArray() : null,
            tuning = me && me.Data?.Role != null ? SettingsControls.Snapshot() : null,
            playtestFixes = me && me.Data?.Role != null ? PlaytestFixChecks.Snapshot() : null,
            blastAssets = BlastProbe.AssetsSnapshot(),
            roleGuide = RoleGuideChecks.Snapshot(),
            captureTimer = CaptureTimerProbe.Snapshot(),
            effects = UnityEngine.Object.FindObjectsOfType<Renderer>().Where(r=>r.gameObject.activeInHierarchy && r.enabled && r.name.StartsWith("AmongUsDogsRoles")).Select(r=>r.name).Distinct().ToArray(),
            connected = client && client.AmConnected, host = client && client.AmHost,
            region = ServerManager.Instance ? ServerManager.Instance.CurrentRegion?.Name : null,
            address = client ? client.GetNetworkAddress() : null, port = client ? client.GetNetworkPort() : 0,
            gameId = client ? client.GameId : 0, ping = client ? client.Ping : 0,
            publicGame = client && client.IsGamePublic, testAllowed = TestState.AllowedSession,
            lobbyManager = GameStartManager.InstanceExists,
            disconnectReason = client ? client.LastDisconnectReason.ToString() : null,
            disconnectMessage = client ? client.LastCustomDisconnect : null,
            started = client && client.IsGameStarted, player = me ? (int)me.PlayerId : -1,
            meeting = (bool)MeetingHud.Instance, exile = (bool)ExileController.Instance,
            suspended = SuspendEndings, lastEnd = LastEnd, winners = Winners,
            canMove = me && me.CanMove, canAct = me && me.Data?.Role != null && RoundState.CanAct(me),
            dragged = me && RoundState.Dragged(me.PlayerId),
            captures = RoundState.Drags.Select(x => new {actor=x.Key,target=x.Value.Target}).ToArray(),
            // Stored expiry entries remain after their effect ends; use the HUD
            // and real retaliation attempts when testing active alert duration.
            alerts = RoundState.Alerts.Keys.ToArray(), uses = RoundState.AlertsUsed,
            marks = RoundState.Marks.Keys.ToArray(), reveals = RoundState.Revealed, tracks = RoundState.Tracks,
            markPositions = RoundState.Marks.Select(m=>new {id=m.Key,x=m.Value.x,y=m.Value.y}).ToArray(),
            jesterWinner = RoundState.JesterWinner,
            fakers = FakerState.Active.Select(x=>new {id=x.Key,x=x.Value.x,y=x.Value.y}).ToArray(),
            fakesUsed = FakerState.Used.Select(x=>(int)x).ToArray(),
            testChat = PreflightChatRecord.Messages.ToArray(),
            bodies = UnityEngine.Object.FindObjectsOfType<DeadBody>().Select(b=>new {id=b.ParentId,x=b.TruePosition.x,y=b.TruePosition.y,reported=b.Reported}).ToArray(),
            votes = MeetingHud.Instance ? MeetingHud.Instance.playerStates.ToArray().Select(v=>new {id=(byte)v.PlayerId,dead=v.AmDead,voted=v.DidVote,target=(byte)v.VotedForId,nameColor=ColorUtility.ToHtmlStringRGBA(v.NameText.color)}).ToArray() : null,
            camera = Camera.main ? new {x=Camera.main.transform.position.x,y=Camera.main.transform.position.y} : null,
            coronerArrow = CoronerArrowProbe.Snapshot(),
            blastProbe = BlastProbe.Snapshot(),
            nativeButtonTypes = me && me.Data?.Role != null && HudManager.Instance ? new ActionButton[] {HudManager.Instance.KillButton,HudManager.Instance.UseButton,HudManager.Instance.ReportButton,HudManager.Instance.ImpostorVentButton,HudManager.Instance.SabotageButton,HudManager.Instance.AbilityButton}.Select(b=>new {wrapper=new ActionButton(b.Pointer).GetType().Name,resolved=MiraHudCompatibility.ButtonType(new ActionButton(b.Pointer)).Name}).ToArray() : null,
            sabotageActive = ShipStatus.Instance && GameOptionsManager.Instance?.currentNormalGameOptions != null && ShipStatus.Instance.Systems.TryGetValue(GameOptionsManager.Instance.currentNormalGameOptions.MapId == 2 ? SystemTypes.Laboratory : SystemTypes.Reactor, out var reactor) && reactor.TryCast<ReactorSystemType>()?.IsActive == true,
            recording = TestMedia.Recording,
            chainLinks = UnityEngine.Object.FindObjectsOfType<SpriteRenderer>().Count(r => r.name == "AmongUsDogsRolesDragChain" && r.gameObject.activeInHierarchy),
            presentation = me && me.Data?.Role != null ? new {
                custom = RoleTuning.Custom, alertDuration = RoleTuning.AlertDuration,
                advancedVisible = OptionGroupSingleton<VeteranOptions>.Instance.GroupVisible(),
                reportVisible = HudManager.Instance && HudManager.Instance.ReportButton.isActiveAndEnabled,
                vanillaAbilityVisible = HudManager.Instance && HudManager.Instance.AbilityButton.isActiveAndEnabled,
                sabotageVisible = HudManager.Instance && HudManager.Instance.SabotageButton.isActiveAndEnabled,
                shadowVisible = HudManager.Instance && HudManager.Instance.ShadowQuad.gameObject.activeInHierarchy,
                taskText = HudManager.Instance ? HudManager.Instance.TaskPanel.taskText.text : "",
                buttons = CustomButtonManager.Buttons.Where(b=>b.GetType().Assembly==typeof(Plugin).Assembly && b.Button).Select(b=>new {
                    type=b.GetType().Name,visible=b.Button!.isActiveAndEnabled,label=b.Button.buttonLabelText.text,
                    timer=b.Timer,uses=b.UsesLeft,icon=b.Button.graphic.sprite.name,
                    canClick=b.CanClick(),
                    parent=b.Button.transform.parent.name
                }).ToArray()
            } : null,
            tasks = GameData.Instance ? new {total=GameData.Instance.TotalTasks,complete=GameData.Instance.CompletedTasks} : null,
            players = PlayerControl.AllPlayerControls.ToArray().Where(p=>p && p.Data?.Role!=null).Select(p=>new {
                id=p.PlayerId,owner=p.OwnerId,name=p.Data.PlayerName,role=p.Data.Role.GetType().Name,roleId=(int)p.Data.Role.Role,
                dead=p.Data.IsDead,disconnected=p.Data.Disconnected,x=p.GetTruePosition().x,y=p.GetTruePosition().y,
                visible=p.Visible,px=p.transform.position.x,py=p.transform.position.y,
                nameColor=ColorUtility.ToHtmlStringRGBA(p.cosmetics.nameText.color),
                resolvedNameColor=ColorUtility.ToHtmlStringRGBA(PlayerNameColor.Get(p.Data.Role)),
                bodyRotation=p.cosmetics.currentBodySprite.BodySprite.transform.eulerAngles.z,
                bodySprite=p.cosmetics.currentBodySprite.BodySprite.sprite.name,
                vx=p.MyPhysics.body.velocity.x,vy=p.MyPhysics.body.velocity.y,
                moveable=p.moveable,netEnabled=p.NetTransform.enabled,vent=p.inVent, tasks=p.Data.Tasks.ToArray().Select(t=>new {id=t.Id,done=t.Complete}).ToArray()
                ,isImpostor=p.Data.Role.IsImpostor,team=p.Data.Role.TeamType.ToString(),isKilling=p.isKilling,sequence=p.NetTransform.lastSequenceId
            }).ToArray()
        };
    }
    [HideFromIl2Cpp] private void Execute(JsonElement d)
    {
        var cmd=d.GetProperty("command").GetString();
        var client=AmongUsClient.Instance;
        if (cmd=="api")
        {
            Write("qa-api.json", new[]{typeof(AmongUsClient),typeof(GameStartManager),typeof(MeetingHud),typeof(EndGameManager),typeof(EndGameNavigation),typeof(PlayerControl),typeof(PlayerPhysics),typeof(KeyboardJoystick)}.ToDictionary(t=>t.Name,t=>t.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly).Select(m=>m.ToString()).ToArray())); return;
        }
        if(cmd=="quit") { Application.Quit(); return; }
        if(cmd=="blast-probe-reset") { BlastProbe.Reset(); TestMedia.Clean=false; return; }
        if(cmd=="region-checks") { RegionChecks.Run(); return; }
        if(cmd=="dogs-host" || cmd=="dogs-join")
        {
            if(!TestState.DogsRegion) throw new InvalidOperationException("Select the saved dogs region and launch with --dogs-test");
            if(client.AmConnected) throw new InvalidOperationException("Already connected");
            client.MainMenuScene="MatchMaking"; client.OnlineScene="OnlineGame";
            client.NetworkMode=NetworkModes.OnlineGame;
            if(cmd=="dogs-host") this.StartCoroutine(client.CoCreateOnlineGame());
            else this.StartCoroutine(client.CoJoinOnlineGameFromCode(d.GetProperty("gameId").GetInt32(),false));
            return;
        }
        if(cmd=="host" || cmd=="join") { this.StartCoroutine(Connect(cmd=="host")); return; }
        if (!TestState.AllowedSession) throw new InvalidOperationException("Local or explicitly allowed private dogs session required");
        if (cmd=="unload-unused") { BlastProbe.Unload=Resources.UnloadUnusedAssets(); return; }
        if (Version19Checks.Execute(d)) return;
        if (RoleGuideChecks.Execute(d)) return;
        if (SettingsControls.Execute(d)) return;
        if (PlaytestFixChecks.Execute(d)) return;
        if (KidnapperChecks.Execute(d)) return;
        if (PreflightControls.Execute(d)) return;
        var me=PlayerControl.LocalPlayer;
        switch(cmd)
        {
            case "walk":
                TestWalking.Hold(new Vector2(d.GetProperty("x").GetSingle(), d.GetProperty("y").GetSingle()), d.GetProperty("seconds").GetSingle());
                break;
            case "media":
                TestMedia.Capture(this, d.GetProperty("name").GetString()!, d.TryGetProperty("seconds", out var seconds) ? seconds.GetSingle() : 0);
                break;
            case "task-console":
                var console = UnityEngine.Object.FindObjectsOfType<Console>().First(c => !c.AllowImpostor && me.myTasks.ToArray().Any(t => c.FindTask(me) == t));
                console.CanUse(me.Data, out var canUseTask, out var couldUseTask);
                console.Use();
                Write("qa-console.json", new {canUseTask, couldUseTask, opened = (bool)Minigame.Instance});
                break;
            case "task-site":
                var site = UnityEngine.Object.FindObjectsOfType<Console>().First(c => !c.AllowImpostor && c.FindTask(me) != null);
                me.NetTransform.RpcSnapTo((Vector2)site.transform.position + new Vector2(0, -.2f));
                break;
            case "complete-received": foreach(var task in me.Data.Tasks.ToArray()) me.CompleteTask(task.Id); break;
            case "corpse":
                if(!client.AmHost) throw new InvalidOperationException("Host only");
                AbilityService.Kill(RoundState.Find(d.GetProperty("actor").GetByte())!, RoundState.Find(d.GetProperty("target").GetByte())!);
                break;
            case "tuning":
                if(!client.AmHost) throw new InvalidOperationException("Host only");
                if(d.TryGetProperty("custom",out var custom))
                    OptionGroupSingleton<RoleSetupOptions>.Instance.Children.OfType<MiraAPI.GameOptions.OptionTypes.ModdedToggleOption>().Single(o=>o.Title=="Customize abilities").SetValue(custom.GetBoolean());
                if(d.TryGetProperty("alertDuration",out var duration))
                    OptionGroupSingleton<VeteranOptions>.Instance.Children.OfType<MiraAPI.GameOptions.OptionTypes.ModdedNumberOption>().Single(o=>o.Title=="Alert duration").SetValue(duration.GetSingle());
                break;
            case "binding":
                var binding=d.GetProperty("name").GetString();
                if(binding=="primary") MiraGlobalKeybinds.PrimaryAbility.Invoke();
                else if(binding=="kill") VanillaKeybinding<global::KillButton>.Instance.Invoke();
                else if(binding=="report") VanillaKeybinding<ReportButton>.Instance.Invoke();
                else throw new InvalidOperationException("Unknown binding");
                break;
            case "button":
                var buttonName=d.GetProperty("name").GetString()+"Button";
                var button=CustomButtonManager.Buttons.Single(b=>b.GetType().Name==buttonName && b.GetType().Assembly==typeof(Plugin).Assembly);
                if(!button.CanClick()) throw new InvalidOperationException("Button cannot be clicked: "+buttonName);
                button.ClickHandler(); break;
            case "suspend": SuspendEndings=d.GetProperty("value").GetBoolean(); break;
            case "start": LastEnd=null; Winners=[]; GameStartManager.Instance.ReallyBegin(false); break;
            case "options":
                var opt=GameOptionsManager.Instance.currentNormalGameOptions;
                opt.NumImpostors=d.TryGetProperty("impostors",out var imps)?imps.GetInt32():1; opt.DiscussionTime=0;
                opt.VotingTime=d.TryGetProperty("voting",out var voting)?voting.GetInt32():30;
                opt.NumCommonTasks=0; opt.NumLongTasks=0; opt.NumShortTasks=1;
                if(d.TryGetProperty("map",out var map)) opt.MapId=map.GetByte();
                GameManager.Instance.LogicOptions.SyncOptions(); break;
            case "proceed": MeetingHud.Instance.HandleProceed(); break;
            case "navigate":
                var nav=UnityEngine.Object.FindObjectOfType<EndGameManager>().Navigation;
                var method=d.GetProperty("method").GetString()!;
                if(method is not ("NextGame" or "PlayAgain" or "OnPlayAgain")) throw new InvalidOperationException("Unsupported navigation");
                nav.GetType().GetMethod(method,Type.EmptyTypes)!.Invoke(nav,null); break;
            case "reset": TestRpc.Send(new(TestCommand.Reset)); break;
            case "role":
                var name=d.GetProperty("role").GetString()!;
                var type=typeof(Plugin).Assembly.GetType("AmongUsDogsRoles."+name+"Role");
                var role=type!=null ? RoleId.Get(type) : (ushort)Enum.Parse<AmongUs.GameOptions.RoleTypes>(name);
                TestRpc.Send(new(TestCommand.Role,d.GetProperty("player").GetByte(),role)); break;
            case "move":
                var p=RoundState.Find(d.GetProperty("player").GetByte())!;
                p.NetTransform.RpcSnapTo(new Vector2(d.GetProperty("x").GetSingle(),d.GetProperty("y").GetSingle())+(Vector2)p.transform.position-p.GetTruePosition()); break;
            case "ability":
                var ability=Enum.Parse<Ability>(d.GetProperty("ability").GetString()!);
                var target=d.TryGetProperty("target",out var t)?t.GetByte():(byte)255;
                var count=d.TryGetProperty("count",out var n)?n.GetInt32():1;
                for(var i=0;i<Math.Clamp(count,1,25);i++) AbilityRpc.Request(ability,target); break;
            case "shift": me.RpcShapeshift(RoundState.Find(d.GetProperty("target").GetByte()),false); break;
            case "meeting": me.CmdReportDeadBody(null); break;
            case "vote": MeetingHud.Instance.CmdCastVote(me.PlayerId,d.GetProperty("target").GetByte()); break;
            case "tasks": foreach(var task in me.Data.Tasks.ToArray()) if(!task.Complete) me.RpcCompleteTask(task.Id); break;
            case "report": me.CmdReportDeadBody(RoundState.Find(d.GetProperty("target").GetByte())!.Data); break;
            case "wall":
                var origin=me.GetTruePosition();
                for(float x=-4;x<=4;x+=.4f) for(float y=-4;y<=4;y+=.4f)
                {
                    var a=origin+new Vector2(x,y);
                    foreach(var dir in new[]{Vector2.right,Vector2.up})
                    {
                        var b=a+dir*1.2f;
                        if(!Physics2D.OverlapCircleAll(a,.08f,Constants.ShipAndObjectsMask).ToArray().Any(c=>!c.isTrigger) &&
                           !Physics2D.OverlapCircleAll(b,.08f,Constants.ShipAndObjectsMask).ToArray().Any(c=>!c.isTrigger) &&
                           PhysicsHelpers.AnythingBetween(a,b,Constants.ShipAndObjectsMask,false))
                        {Write("qa-wall.json",new {ax=a.x,ay=a.y,bx=b.x,by=b.y});return;}
                    }
                }
                throw new InvalidOperationException("No wall test pair found");
            case "quit": Application.Quit(); break;
            case "disconnect": client.ExitGame(DisconnectReasons.ExitGame); break;
            case "name": me.RpcSetName(d.GetProperty("value").GetString()); break;
            default: throw new InvalidOperationException("Unknown command: "+cmd);
        }
    }
    [HideFromIl2Cpp] private static IEnumerator Connect(bool host)
    {
        var client=AmongUsClient.Instance;
        if(client.AmConnected) throw new InvalidOperationException("Already connected");
        client.NetworkMode=NetworkModes.LocalGame;
        if(!host)
        {
            client.GameId=InnerNetServer.LocalGameId;
            yield return client.CoConnectToGameServer(MatchMakerModes.Client,Constants.LocalNetAddress,Constants.GamePlayPort,null);
            yield break;
        }
        client.MainMenuScene="MatchMaking";client.OnlineScene="OnlineGame";
        if(host) InnerNetServer.Instance.StartAsServer();
        client.SetEndpoint(Constants.LocalNetAddress,Constants.GamePlayPort,false);
        client.Connect(host?MatchMakerModes.HostAndClient:MatchMakerModes.Client,null);
        yield return client.WaitForConnectionOrFail();
    }
}

[HarmonyPatch(typeof(LogicGameFlowNormal),nameof(LogicGameFlowNormal.CheckEndCriteria))]
public static class HardeningEndGate
{
    [HarmonyPriority(Priority.First)]
    public static bool Prefix()=>!TestState.AllowedSession || !HardeningBridge.SuspendEndings;
}
[HarmonyPatch(typeof(AmongUsClient),nameof(AmongUsClient.OnGameEnd))]
public static class HardeningEndRecord
{
    public static void Prefix(EndGameResult endGameResult)
    {
        HardeningBridge.LastEnd=null;
        HardeningBridge.LastEnd=new {reason=endGameResult.GameOverReason.ToString(),state=HardeningBridge.Snapshot()};
    }
}
[HarmonyPatch(typeof(EndGameManager),nameof(EndGameManager.SetEverythingUp))]
public static class HardeningWinnerRecord
{
    [HarmonyPriority(Priority.Last)]
    public static void Postfix()=>HardeningBridge.Winners=EndGameResult.CachedWinners.ToArray().Select(p=>p.PlayerName).ToArray();
}
