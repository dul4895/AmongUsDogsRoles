using MiraAPI.Roles;

namespace AmongUsDogsRoles;

// Native IL2CPP getters can return a base RoleBehaviour wrapper. Network role
// IDs survive wrapper changes, scene loads and repeated role assignment.
public static class RoleFacts
{
    public static bool Is<T>(RoleBehaviour? role) where T : RoleBehaviour, ICustomRole =>
        role && (ushort)role!.Role == RoleId.Get<T>();
    public static bool IsImpostor(RoleBehaviour? role) => role && role!.IsImpostor &&
        (Is<PenguinRole>(role) || Is<BomberRole>(role) || Is<ConsigliereRole>(role) ||
         Is<EscapistRole>(role) || Is<FakerRole>(role) || Is<HackerRole>(role));
}
