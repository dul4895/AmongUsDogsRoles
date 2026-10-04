using AmongUs.GameOptions;
using HarmonyLib;
using MiraAPI.Patches.Roles;
using MiraAPI.Roles;

namespace AmongUsDogsRoles;

[HarmonyPatch(typeof(SelectRolesPatch), nameof(SelectRolesPatch.AssignRolesForTeam))]
public static class RoleAllocation
{
    public static bool Prefix(List<NetworkedPlayerInfo> players, IGameOptions opts, RoleTeamTypes team,
        int teamMax, RoleTypes defaultRole)
    {
        var slots = Math.Min(players.Count, teamMax);
        var options = opts.RoleOptions;
        var roles = RoleManager.Instance.AllRoles.ToArray()
            .Where(r => r.TeamType == team && !RoleManager.IsGhostRole(r.Role) && CustomRoleUtils.CanSpawnOnCurrentMode(r))
            .Select(r => ((int)r.Role, options.GetNumPerGame(r.Role), options.GetChancePerGame(r.Role))).ToArray();
        var selected = Rules.SelectRoles(roles, slots, HashRandom.Next);
        for (var i = 0; i < slots; i++)
        {
            var index = HashRandom.Next(players.Count);
            players[index].Object.RpcSetRole(i < selected.Length ? (RoleTypes)selected[i] : defaultRole, false);
            players.RemoveAt(index);
        }
        return false;
    }
}
