using System.Text.Json;
using AmongUs.GameOptions;
using BepInEx;
using HarmonyLib;
using MiraAPI.GameOptions;
using MiraAPI.GameOptions.OptionTypes;
using MiraAPI.Roles;

namespace AmongUsDogsRoles.Testing;

public static class SettingsControls
{
    private static Dictionary<string, AbstractOptionGroup> Groups => new()
    {
        ["Penguin"]=OptionGroupSingleton<PenguinOptions>.Instance,
        ["Bomber"]=OptionGroupSingleton<BomberOptions>.Instance,
        ["Consigliere"]=OptionGroupSingleton<ConsigliereOptions>.Instance,
        ["Escapist"]=OptionGroupSingleton<EscapistOptions>.Instance,
        ["Veteran"]=OptionGroupSingleton<VeteranOptions>.Instance,
        ["Sheriff"]=OptionGroupSingleton<SheriffOptions>.Instance,
        ["Coroner"]=OptionGroupSingleton<CoronerOptions>.Instance,
    };
    private static ModdedToggleOption Toggle => OptionGroupSingleton<RoleSetupOptions>.Instance.Children.OfType<ModdedToggleOption>().Single(o=>o.Title=="Customize abilities");
    public static object Snapshot() => new
    {
        custom=RoleTuning.Custom,
        effective=typeof(RoleTuning).GetProperties(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static)
            .Where(p=>p.Name!="Custom").ToDictionary(p=>p.Name,p=>Convert.ToSingle(p.GetValue(null))),
        groups=Groups.Select(g=>new {name=g.Key,displayName=g.Value.GroupName,visible=g.Value.GroupVisible(),options=g.Value.Children.OfType<ModdedNumberOption>().Select(o=>new {title=o.Title,value=o.Value,min=o.Min,max=o.Max,step=o.Increment}).ToArray()}).ToArray(),
        native=new {killCooldown=GameOptionsManager.Instance.currentNormalGameOptions.KillCooldown,killDistance=GameOptionsManager.Instance.currentNormalGameOptions.KillDistance}
    };
    public static bool Execute(JsonElement d)
    {
        var cmd=d.GetProperty("command").GetString();
        if(cmd is not ("settings-save" or "settings-restore" or "settings-custom" or "settings-number" or "settings-edge" or "settings-rate" or "settings-native")) return false;
        if(!AmongUsClient.Instance.AmHost) throw new InvalidOperationException("Host only");
        var roles=CustomRoleManager.CustomMiraRoles.Where(r=>r.GetType().Assembly==typeof(Plugin).Assembly).ToArray();
        var opt=GameOptionsManager.Instance.currentNormalGameOptions;
        var file=Path.Combine(Paths.GameRootPath,"qa-saved-settings.json");
        if(cmd=="settings-save")
        {
            File.WriteAllText(file,JsonSerializer.Serialize(new {tuning=Snapshot(),
                roles=roles.Select(r=>new {name=r.GetType().Name,count=r.GetCount(),chance=r.GetChance()}),
                vanilla=Enum.GetValues<RoleTypes>().Select(r=>new {id=(int)r,count=opt.RoleOptions.GetNumPerGame(r),chance=opt.RoleOptions.GetChancePerGame(r)}),
                normal=new {opt.NumImpostors,opt.MapId,opt.DiscussionTime,opt.VotingTime,opt.NumCommonTasks,opt.NumLongTasks,opt.NumShortTasks,opt.KillCooldown,opt.KillDistance}
            },new JsonSerializerOptions {WriteIndented=true}));
            return true;
        }
        if(cmd=="settings-restore")
        {
            using var doc=JsonDocument.Parse(File.ReadAllText(file));var saved=doc.RootElement;
            foreach(var g in saved.GetProperty("tuning").GetProperty("groups").EnumerateArray())
                foreach(var o in g.GetProperty("options").EnumerateArray())
                    Groups[g.GetProperty("name").GetString()!].Children.OfType<ModdedNumberOption>().Single(n=>n.Title==o.GetProperty("title").GetString()).SetValue(o.GetProperty("value").GetSingle());
            Toggle.SetValue(saved.GetProperty("tuning").GetProperty("custom").GetBoolean());
            foreach(var r in saved.GetProperty("roles").EnumerateArray())
            {var role=roles.Single(x=>x.GetType().Name==r.GetProperty("name").GetString());role.SetCount(r.GetProperty("count").GetInt32());role.SetChance(r.GetProperty("chance").GetInt32());}
            foreach(var r in saved.GetProperty("vanilla").EnumerateArray())opt.RoleOptions.SetRoleRate((RoleTypes)r.GetProperty("id").GetInt32(),r.GetProperty("count").GetInt32(),r.GetProperty("chance").GetInt32());
            var normal=saved.GetProperty("normal");
            opt.NumImpostors=normal.GetProperty("NumImpostors").GetInt32();opt.MapId=normal.GetProperty("MapId").GetByte();
            opt.DiscussionTime=normal.GetProperty("DiscussionTime").GetInt32();opt.VotingTime=normal.GetProperty("VotingTime").GetInt32();
            opt.NumCommonTasks=normal.GetProperty("NumCommonTasks").GetInt32();opt.NumLongTasks=normal.GetProperty("NumLongTasks").GetInt32();opt.NumShortTasks=normal.GetProperty("NumShortTasks").GetInt32();
            opt.KillCooldown=normal.GetProperty("KillCooldown").GetSingle();opt.KillDistance=normal.GetProperty("KillDistance").GetInt32();
        }
        else if(cmd=="settings-custom")Toggle.SetValue(d.GetProperty("value").GetBoolean());
        else if(cmd=="settings-number")
        {
            var option=Groups[d.GetProperty("group").GetString()!].Children.OfType<ModdedNumberOption>().Single(o=>o.Title==d.GetProperty("title").GetString());
            var value=d.GetProperty("value").GetSingle();
            if(value<option.Min || value>option.Max || !float.IsFinite(value))throw new InvalidOperationException("Outside UI range");
            option.SetValue(value);
        }
        else if(cmd=="settings-edge")
        {
            var max=d.GetProperty("max").GetBoolean();
            Toggle.SetValue(true);
            foreach(var option in Groups.Values.SelectMany(g=>g.Children).OfType<ModdedNumberOption>())option.SetValue(max?option.Max:option.Min);
        }
        else if(cmd=="settings-native")
        {
            if(d.TryGetProperty("killCooldown",out var cd))opt.KillCooldown=cd.GetSingle();
            if(d.TryGetProperty("killDistance",out var dist))opt.KillDistance=dist.GetInt32();
        }
        else if(cmd=="settings-rate")
        {
            if(AmongUsClient.Instance.IsGameStarted)throw new InvalidOperationException("Lobby only");
            var r=roles.Single(r=>r.GetType().Name==d.GetProperty("role").GetString()+"Role");
            r.SetCount(d.GetProperty("count").GetInt32());r.SetChance(d.GetProperty("chance").GetInt32());
        }
        AccessTools.Method(typeof(CustomRoleManager),"SyncAllRoleSettings").Invoke(null,[-1]);
        GameManager.Instance.LogicOptions.SyncOptions();
        return true;
    }
}
