using System.Text.Json;
using AmongUs.GameOptions;
using MiraAPI.GameOptions;
using MiraAPI.GameOptions.OptionTypes;
using MiraAPI.Roles;
using UnityEngine;
using Reactor.Utilities.Attributes;
using Il2CppInterop.Runtime.Attributes;

namespace AmongUsDogsRoles.Testing;

public static class PlaytestFixChecks
{
    // Keep the probe usable against the previous release for before/after runs.
    private static object? NewProperty(string type, string property) => typeof(Plugin).Assembly
        .GetType("AmongUsDogsRoles." + type)?.GetProperty(property)?.GetValue(null);
    public static object Snapshot() => new
    {
        captureRequests = NewProperty("KidnapperCapture", "Requests"), captureResult = NewProperty("KidnapperCapture", "LastResult")?.ToString(),
        abilityHud = NewProperty("AbilityHudVisibility", "Visible"),
        hack = HackerState.Active, hackRemaining = Mathf.Max(0, HackerState.Until - Time.time),
        vitalsSnapshot = HackerState.Vitals, pairs = HackerState.Pairs, cameraRenders = CameraHackPresentation.RenderCount,
        renderedIdentities = CameraIdentityProbe.Last,
        worldIdentities = CameraIdentityProbe.ReadPlayers(),
        cameras = HackerState.ViewingCameras ? UnityEngine.Object.FindObjectsOfType<Camera>(true).Select(c=>new {c.name, active=c.isActiveAndEnabled, texture=(bool)c.targetTexture, hook=(bool)c.GetComponent<CameraHackPresentation>()}).ToArray() : null,
        admin = UnityEngine.Object.FindObjectsOfType<MapCountOverlay>().SelectMany(m=>m.CountAreas.ToArray())
            .Select(a=>new {room=a.RoomType.ToString(), count=a.myIcons.Count, actual=HackedAdmin.Counts.GetValueOrDefault(a.GetInstanceID(),-1)}).ToArray(),
        minigame = Minigame.Instance ? Minigame.Instance.GetIl2CppType().Name : null,
        vitals = Minigame.Instance?.TryCast<VitalsMinigame>()?.vitals.ToArray().Select(p => new { id=p.PlayerInfo.PlayerId, dead=p.IsDead, disconnected=p.IsDiscon }).ToArray(),
        guesses = RoleTuning.MeetingGuesses, meetingCooldown = RoleTuning.MeetingKillCooldown,
        canGuess = MeetingGuesses.CanUse(PlayerControl.LocalPlayer), picker = GuessPicker.Blocks,
        killTimer = PlayerControl.LocalPlayer ? PlayerControl.LocalPlayer.killTimer : 0,
        cooldowns = RoundState.ReadyAt.Where(p=>PlayerControl.LocalPlayer && p.Key.Player==PlayerControl.LocalPlayer.PlayerId)
            .ToDictionary(p=>p.Key.Ability.ToString(),p=>Mathf.Max(0,p.Value-Time.time)),
        visibleRoles = PlayerControl.LocalPlayer && PlayerControl.LocalPlayer.Data?.Role != null ? PlayerControl.AllPlayerControls.ToArray()
            .Where(p=>p.Data?.Role is ICustomRole).Select(p=>new {id=p.PlayerId, visible=((ICustomRole)p.Data.Role).CanLocalPlayerSeeRole(p)}).ToArray() : null,
        wrappers = PlayerControl.LocalPlayer && PlayerControl.LocalPlayer.Data?.Role != null ? new
        { kidnapper = RoleFacts.Is<PenguinRole>(new RoleBehaviour(PlayerControl.LocalPlayer.Data.Role.Pointer)) } : null
    };
    public static bool Execute(JsonElement d)
    {
        switch (d.GetProperty("command").GetString())
        {
            case "meeting-options":
                if (!AmongUsClient.Instance.AmHost) throw new InvalidOperationException("Host only");
                var group=OptionGroupSingleton<RoleSetupOptions>.Instance;
                if(d.TryGetProperty("guesses",out var guesses)) group.Children.OfType<ModdedToggleOption>()
                    .Single(o=>o.Title=="Impostors can guess roles in meetings").SetValue(guesses.GetBoolean());
                if(d.TryGetProperty("cooldown",out var cooldown)) group.Children.OfType<ModdedNumberOption>()
                    .Single(o=>o.Title=="Kill cooldown after meetings").SetValue(cooldown.GetSingle());
                return true;
            case "guess":
                var name=d.GetProperty("role").GetString()!;
                var type=typeof(Plugin).Assembly.GetType("AmongUsDogsRoles."+name+"Role");
                GuessRpc.Send(d.GetProperty("target").GetByte(),type!=null?RoleId.Get(type):(ushort)Enum.Parse<RoleTypes>(name));
                return true;
            case "guess-picker": GuessPicker.Instance!.Open(d.GetProperty("target").GetByte()); return true;
            case "close-picker": GuessPicker.Instance!.Close(); return true;
            case "close-info": if(Minigame.Instance) Minigame.Instance.Close(); return true;
            case "open-admin":
                HudManager.Instance.InitMap(); MapBehaviour.Instance.ShowCountOverlay(false,true,true); return true;
            case "close-admin": MapBehaviour.Instance.Close(); return true;
            case "render-camera":
                HackerState.Tick();
                foreach(var camera in HackerState.FeedCameras())
                {
                    if(!camera.GetComponent<CameraIdentityProbe>()) camera.gameObject.AddComponent<CameraIdentityProbe>();
                    camera.Render();
                    var previous=RenderTexture.active; RenderTexture.active=camera.targetTexture;
                    var texture=new Texture2D(camera.targetTexture.width,camera.targetTexture.height,TextureFormat.RGB24,false);
                    texture.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0); texture.Apply();
                    File.WriteAllBytes(Path.Combine(BepInEx.Paths.GameRootPath,"qa-camera.png"),ImageConversion.EncodeToPNG(texture));
                    RenderTexture.active=previous; UnityEngine.Object.Destroy(texture);
                }
                return true;
            case "capture-picker":
                var canvas=UnityEngine.Object.FindObjectsOfType<Canvas>().Single(c=>c.name=="AmongUsDogsRolesGuessPicker");
                var mode=canvas.renderMode; var oldCamera=canvas.worldCamera; var distance=canvas.planeDistance;
                var renderObject=new GameObject("PickerTestCamera"); var renderCamera=renderObject.AddComponent<Camera>();
                renderCamera.clearFlags=CameraClearFlags.SolidColor; renderCamera.backgroundColor=Color.black;
                renderCamera.transform.position=new Vector3(1000,1000,-10); renderCamera.cullingMask=-1;
                var rt=new RenderTexture(Screen.width,Screen.height,24); renderCamera.targetTexture=rt;
                var prior=RenderTexture.active;
                try
                {
                    canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=renderCamera; canvas.planeDistance=1;
                    Canvas.ForceUpdateCanvases(); renderCamera.Render(); RenderTexture.active=rt;
                    var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
                    tex.ReadPixels(new Rect(0,0,tex.width,tex.height),0,0); tex.Apply();
                    File.WriteAllBytes(Path.Combine(BepInEx.Paths.GameRootPath,"qa-picker.png"),ImageConversion.EncodeToPNG(tex));
                    UnityEngine.Object.Destroy(tex);
                }
                finally
                { canvas.renderMode=mode; canvas.worldCamera=oldCamera; canvas.planeDistance=distance; RenderTexture.active=prior; rt.Release(); UnityEngine.Object.Destroy(rt); UnityEngine.Object.Destroy(renderObject); }
                return true;
            case "open-vitals":
                var vitals=UnityEngine.Object.Instantiate(RoleManager.Instance.GetRole(RoleTypes.Scientist).Cast<ScientistRole>().VitalsPrefab, HudManager.Instance.transform);
                vitals.transform.localPosition=new Vector3(0,0,-50); vitals.Begin(null); return true;
            case "scientist-vitals": PlayerControl.LocalPlayer.Data.Role.Cast<ScientistRole>().UseAbility(); return true;
            case "open-cameras":
                var console=UnityEngine.Object.FindObjectsOfType<SystemConsole>().First(c=>c.MinigamePrefab &&
                    (c.MinigamePrefab.TryCast<SurveillanceMinigame>()!=null || c.MinigamePrefab.TryCast<PlanetSurveillanceMinigame>()!=null || c.MinigamePrefab.TryCast<FungleSurveillanceMinigame>()!=null));
                var minigame=UnityEngine.Object.Instantiate(console.MinigamePrefab,HudManager.Instance.transform);
                minigame.transform.localPosition=new Vector3(0,0,-50); minigame.Begin(null); return true;
            default:return false;
        }
    }
}

[RegisterInIl2Cpp]
public sealed class CameraIdentityProbe(IntPtr pointer) : MonoBehaviour(pointer)
{
    public static object[] Last = [];
    public void OnPreRender() => Last = ReadPlayers();
    [HideFromIl2Cpp] public static object[] ReadPlayers() => PlayerControl.AllPlayerControls.ToArray()
        .Where(p=>p && p.Data != null && p.cosmetics && p.cosmetics.currentBodySprite != null &&
            p.Data.DefaultOutfit.ColorId >= 0 && p.Data.DefaultOutfit.ColorId < Palette.PlayerColors.Length)
        .Select(p=>(object)new { id=p.PlayerId, name=p.cosmetics.nameText.text,
            color=ColorUtility.ToHtmlStringRGBA(p.cosmetics.currentBodySprite.BodySprite.material.GetColor("_BodyColor")),
            realName=p.Data.PlayerName, realColor=ColorUtility.ToHtmlStringRGBA(Palette.PlayerColors[p.Data.DefaultOutfit.ColorId]) }).ToArray();
}
