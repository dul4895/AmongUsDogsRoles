using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MiraAPI;

namespace AmongUsDogsRoles;

// MiraAPI 0.5 indexes vanilla bindings using the managed wrapper's GetType().
// IL2CPP can return an ActionButton wrapper for a concrete native button after
// repeated scene loads. Resolve the native type instead of losing HUD setup.
[HarmonyPatch]
public static class MiraHudCompatibility
{
    public static MethodBase TargetMethod() => AccessTools.Method(
        typeof(MiraApiPlugin).Assembly.GetType("MiraAPI.Patches.HudManagerPatches", true), "StartPostfix");

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var getType = AccessTools.Method(typeof(object), nameof(GetType));
        var nativeType = AccessTools.Method(typeof(MiraHudCompatibility), nameof(ButtonType));
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(getType))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = nativeType;
            }
            yield return instruction;
        }
    }

    public static Type ButtonType(object value)
    {
        if (value is ActionButton button)
        {
            if (button.TryCast<global::KillButton>() != null) return typeof(global::KillButton);
            if (button.TryCast<UseButton>() != null) return typeof(UseButton);
            if (button.TryCast<ReportButton>() != null) return typeof(ReportButton);
            if (button.TryCast<VentButton>() != null) return typeof(VentButton);
            if (button.TryCast<SabotageButton>() != null) return typeof(SabotageButton);
            if (button.TryCast<AbilityButton>() != null) return typeof(AbilityButton);
        }
        return value.GetType();
    }
}
