using System.Text.Json;
using AmongUs.GameOptions;
using HarmonyLib;
using MiraAPI.Roles;
using UnityEngine;

namespace AmongUsDogsRoles.Testing;

// Private-session test controls. Assignment tests change lobby settings and let
// the real role allocator run; they do not replace roles after the intro.
public static class PreflightControls
{
    private static Dictionary<string,(int Count,int Chance)>? saved;
    private static Dictionary<RoleTypes,(int Count,int Chance)>? nativeSaved;
    public static bool Execute(JsonElement d)
    {
        var cmd=d.GetProperty("command").GetString();
        var me=PlayerControl.LocalPlayer;
        switch(cmd)
        {
            case "stale-native-snap":
            case "stale-recall":
                if(!AmongUsClient.Instance.AmHost) throw new InvalidOperationException("Host only");
                var actor=RoundState.Find(d.GetProperty("player").GetByte())!;
                if(actor.AmOwner || !RoundState.Marks.TryGetValue(actor.PlayerId,out var mark)) throw new InvalidOperationException("Marked remote actor required");
                // Reproduce a host movement view behind the owner's sequence.
                actor.NetTransform.lastSequenceId=unchecked((ushort)(actor.NetTransform.lastSequenceId-200));
                if(cmd=="stale-native-snap") actor.NetTransform.RpcSnapTo(mark+(Vector2)actor.transform.position-actor.GetTruePosition());
                else AbilityService.Handle(actor,new Request(Ability.Recall,255));
                return true;
            case "sabotage-ready":
                if(!AmongUsClient.Instance.AmHost) throw new InvalidOperationException("Host only");
                var sabotage=ShipStatus.Instance.Systems[SystemTypes.Sabotage].Cast<SabotageSystemType>();
                sabotage.Timer=0; sabotage.initialCooldown=false;
                return true;
            case "chat":
                var sent=me.RpcSendChat(d.GetProperty("text").GetString());
                File.WriteAllText(Path.Combine(BepInEx.Paths.GameRootPath,"qa-chat.json"),JsonSerializer.Serialize(new {sent}));
                return true;
            case "sabotage": ShipStatus.Instance.RpcUpdateSystem(SystemTypes.Sabotage,(byte)(GameOptionsManager.Instance.currentNormalGameOptions.MapId == 2 ? SystemTypes.Laboratory : SystemTypes.Reactor)); return true;
            case "repair": ShipStatus.Instance.RepairCriticalSabotages(); return true;
            case "sabotage-countdown":
                if(!AmongUsClient.Instance.AmHost) throw new InvalidOperationException("Host only");
                var critical=ShipStatus.Instance.Systems[GameOptionsManager.Instance.currentNormalGameOptions.MapId == 2 ? SystemTypes.Laboratory : SystemTypes.Reactor].Cast<ReactorSystemType>();
                if(!critical.IsActive) throw new InvalidOperationException("Start the real sabotage first");
                critical.Countdown=d.GetProperty("seconds").GetSingle();
                return true;
            case "role-settings":
            case "role-settings-restore":
                if(!AmongUsClient.Instance.AmHost || AmongUsClient.Instance.IsGameStarted) throw new InvalidOperationException("Host lobby only");
                var roles=CustomRoleManager.CustomMiraRoles.Where(r=>r.GetType().Assembly==typeof(Plugin).Assembly).ToArray();
                saved ??= roles.ToDictionary(r=>r.GetType().Name,r=>(r.GetCount()??0,r.GetChance()??0));
                var nativeOptions=GameOptionsManager.Instance.currentNormalGameOptions.RoleOptions;
                nativeSaved ??= Enum.GetValues<RoleTypes>().ToDictionary(r=>r,r=>(nativeOptions.GetNumPerGame(r),nativeOptions.GetChancePerGame(r)));
                var enabled=cmd=="role-settings" ? d.GetProperty("roles").EnumerateArray().Select(r=>r.GetString()+"Role").ToHashSet() : [];
                foreach(var role in roles)
                {
                    var previous=saved[role.GetType().Name];
                    role.SetCount(cmd=="role-settings-restore" ? previous.Count : enabled.Contains(role.GetType().Name)?1:0);
                    role.SetChance(cmd=="role-settings-restore" ? previous.Chance : 100);
                }
                AccessTools.Method(typeof(CustomRoleManager),"SyncAllRoleSettings").Invoke(null,[-1]);
                // Disable native special roles for isolated custom-role assignment.
                foreach(var role in Enum.GetValues<RoleTypes>())
                {
                    var previous=nativeSaved[role];
                    nativeOptions.SetRoleRate(role,cmd=="role-settings-restore"?previous.Count:0,cmd=="role-settings-restore"?previous.Chance:0);
                }
                GameManager.Instance.LogicOptions.SyncOptions();
                return true;
            case "vent-site":
                var vent=ShipStatus.Instance.AllVents.OrderBy(v=>Vector2.Distance(v.transform.position,me.GetTruePosition())).First();
                me.NetTransform.RpcSnapTo((Vector2)vent.transform.position+Vector2.down*.1f);
                return true;
            case "vent":
                ShipStatus.Instance.AllVents.OrderBy(v=>Vector2.Distance(v.transform.position,me.GetTruePosition())).First().Use();
                return true;
            case "vent-exit":
                var near=ShipStatus.Instance.AllVents.OrderBy(v=>Vector2.Distance(v.transform.position,me.GetTruePosition())).First();
                me.MyPhysics.RpcExitVent(near.Id);
                return true;
            case "spawn":
                var spawn=UnityEngine.Object.FindObjectOfType<SpawnInMinigame>();
                if(spawn) spawn.SpawnAt(spawn.Locations[0]);
                return true;
            default:return false;
        }
    }
}

[HarmonyPatch(typeof(ChatController),nameof(ChatController.AddChat))]
public static class PreflightChatRecord
{
    public static readonly List<object> Messages = new();
    public static void Postfix(PlayerControl sourcePlayer,string chatText,bool __runOriginal)
    {
        if(!TestState.AllowedSession || !chatText.StartsWith("FAKER-QA-")) return;
        Messages.Add(new {source=sourcePlayer.PlayerId,text=chatText,nativeRan=__runOriginal});
        if(Messages.Count>20) Messages.RemoveAt(0);
    }
}
