using HarmonyLib;
using UnityEngine;

namespace AmongUsDogsRoles.Testing;

// Feed the same input used by the vanilla movement loop. Never move transforms,
// set velocity, select animations, or send movement RPCs from this test helper.
// TestingPlugin only installs these patches for explicit --hardening launches.
[HarmonyPatch(typeof(KeyboardJoystick), nameof(KeyboardJoystick.Update))]
public static class TestWalking
{
    private static Vector2 direction;
    private static float until;
    public static void Hold(Vector2 input, float seconds)
    {
        if (!TestState.AllowedSession || !RoundState.InRound) throw new InvalidOperationException("Private test round required");
        direction = Vector2.ClampMagnitude(input, 1);
        until = Time.realtimeSinceStartup + Mathf.Clamp(seconds, 0, 8);
    }
    public static void Postfix(KeyboardJoystick __instance)
    {
        if (Time.realtimeSinceStartup < until && TestState.AllowedSession && RoundState.InRound)
            __instance.del = direction;
    }
}
