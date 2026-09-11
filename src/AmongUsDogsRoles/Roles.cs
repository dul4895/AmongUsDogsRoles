using MiraAPI.Roles;
using UnityEngine;

namespace AmongUsDogsRoles;

// Keep the registered type names stable so saved role counts, chances and
// network role identifiers survive a display-name change.
public static class RoleNames
{
    public const string Kidnapper = "Kidnapper";
    public const string Kamikaze = "Kamikaze";
    public static string For(Type type) => type == typeof(PenguinRole) ? Kidnapper :
        type == typeof(BomberRole) ? Kamikaze : type.Name.Replace("Role", "");
}

public interface ILightRole : ICustomRole
{
    string Help { get; }
    string ICustomRole.RoleNameLocale => RoleNames.For(GetType());
    string ICustomRole.RoleDescriptionLocale => this switch
    {
        PenguinRole => "Capture and execute your target.",
        BomberRole => "Explode to take nearby players with you.",
        ConsigliereRole => "Discover their roles in secret.",
        EscapistRole => "Mark a spot and return to it.",
        FakerRole => "Play dead. Return when the time is right.",
        VeteranRole => "Go on alert to punish attackers.",
        SheriffRole => "Shoot enemies. Avoid crewmates.",
        CoronerRole => "Sniff bodies to track killers.",
        JesterRole => "Get voted out to win alone.",
        _ => Help,
    };
    string ICustomRole.RoleMedDescriptionLocale => Help;
    string ICustomRole.RoleLongDescriptionLocale => Help;
    CustomRoleConfiguration ICustomRole.Configuration => new(this)
    {
        MaxRoleCount = this is JesterRole ? 1 : 3,
        DefaultRoleCount = 1,
        DefaultChance = 50,
        UseVanillaKillButton = false,
        CanUseVent = Team == ModdedRoleTeams.Impostor,
        CanUseSabotage = Team == ModdedRoleTeams.Impostor,
        TasksCountForProgress = Team == ModdedRoleTeams.Crewmate,
    };
}

public sealed class PenguinRole(IntPtr ptr) : ImpostorRole(ptr), ILightRole
{
    public string Help => "Drag a nearby player. Execute your captive or kill instantly. You cannot vent while dragging.";
    public Color RoleColor => new Color32(100, 175, 225, 255);
    public ModdedRoleTeams Team => ModdedRoleTeams.Impostor;
}
public sealed class BomberRole(IntPtr ptr) : ImpostorRole(ptr), ILightRole
{
    public string Help => "Explode to kill everyone nearby, including yourself and fellow impostors. You can also kill normally.";
    public Color RoleColor => new Color32(240, 110, 35, 255);
    public ModdedRoleTeams Team => ModdedRoleTeams.Impostor;
}
public sealed class ConsigliereRole(IntPtr ptr) : ImpostorRole(ptr), ILightRole
{
    public string Help => "Investigate nearby players to privately learn their exact roles. You can also kill.";
    public Color RoleColor => new Color32(180, 65, 80, 255);
    public ModdedRoleTeams Team => ModdedRoleTeams.Impostor;
}
public sealed class EscapistRole(IntPtr ptr) : ImpostorRole(ptr), ILightRole
{
    public string Help => "Mark your position, then recall to it. Marks are consumed on recall and cleared by meetings.";
    public Color RoleColor => new Color32(100, 190, 130, 255);
    public ModdedRoleTeams Team => ModdedRoleTeams.Impostor;
}
public sealed class FakerRole(IntPtr ptr) : ImpostorRole(ptr), ILightRole
{
    public string Help => "Fake your death once per game. You count as dead and stay at your body. Unfake outside meetings to return there. You cannot sabotage while faking.";
    public Color RoleColor => new Color32(165, 130, 200, 255);
    public ModdedRoleTeams Team => ModdedRoleTeams.Impostor;
    public override void SpawnTaskHeader(PlayerControl playerControl)
    {
        if (!playerControl.AmOwner) return;
        var header = PlayerTask.GetOrCreateTask<ImportantTextTask>(playerControl, 0);
        header.Text = FakerState.IsFaking(playerControl)
            ? "<color=#A582C8>Faking death. Unfake to return.</color>"
            : "<color=#FF1919>Sabotage and kill everyone.</color>\nFake Tasks:";
    }
}
public sealed class VeteranRole(IntPtr ptr) : CrewmateRole(ptr), ILightRole
{
    public string Help => "Alert for a short time. Anyone who targets you with a direct ability dies, and their action is blocked.";
    public Color RoleColor => new Color32(170, 135, 75, 255);
    public ModdedRoleTeams Team => ModdedRoleTeams.Crewmate;
}
public sealed class SheriffRole(IntPtr ptr) : CrewmateRole(ptr), ILightRole
{
    public string Help => "Shoot impostors or neutrals. Shooting a crewmate kills only you. An alert Veteran retaliates.";
    public Color RoleColor => new Color32(245, 210, 70, 255);
    public ModdedRoleTeams Team => ModdedRoleTeams.Crewmate;
}
public sealed class CoronerRole(IntPtr ptr) : CrewmateRole(ptr), ILightRole
{
    public string Help => "Sniff a nearby body to track its killer with an arrow until the next meeting. You cannot report bodies.";
    public Color RoleColor => new Color32(110, 215, 215, 255);
    public ModdedRoleTeams Team => ModdedRoleTeams.Crewmate;
}
public sealed class JesterRole(IntPtr ptr) : CrewmateRole(ptr), ILightRole
{
    public string Help => "Convince the crew to vote you out. Ejection wins the game for you alone. Your tasks are fake and cannot be completed.";
    public Color RoleColor => new Color32(240, 135, 200, 255);
    public ModdedRoleTeams Team => ModdedRoleTeams.Custom;
    public override bool DidWin(GameOverReason reason) => RoundState.JesterWinner == Player.PlayerId;
    public override void SpawnTaskHeader(PlayerControl playerControl)
    {
        if (!playerControl.AmOwner) return;
        var header = PlayerTask.GetOrCreateTask<ImportantTextTask>(playerControl, 0);
        header.Text = "<color=#F087C8>Get voted out to win.</color>\nFake Tasks:";
    }
    public override bool CanUse(IUsable usable)
    {
        var taskConsole = usable.TryCast<Console>();
        return taskConsole == null || taskConsole.AllowImpostor;
    }
}
