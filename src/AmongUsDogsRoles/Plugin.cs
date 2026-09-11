using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using MiraAPI;
using MiraAPI.PluginLoading;
using Reactor;
using Reactor.Networking;
using Reactor.Networking.Attributes;

namespace AmongUsDogsRoles;

[BepInAutoPlugin("dogs.amongusdogsroles", "AmongUsDogsRoles")]
[BepInProcess("Among Us.exe")]
[BepInDependency(ReactorPlugin.Id)]
[BepInDependency(MiraApiPlugin.Id)]
[BepInIncompatibility("auavengers.tou.mira")]
[ReactorModFlags(ModFlags.RequireOnAllClients)]
public partial class Plugin : BasePlugin, IMiraPlugin
{
    public string OptionsTitleText => "AmongUsDogsRoles";
    public ConfigFile GetConfigFile() => Config;
    public Harmony Harmony { get; } = new(Id);
    public override void Load()
    {
        Harmony.PatchAll();
        AddComponent<RoleGuide>();
        Log.LogInfo("AmongUsDogsRoles 0.1.21 — nine roles, Steam 2026.8.18 / 18.0.x");
    }
}
