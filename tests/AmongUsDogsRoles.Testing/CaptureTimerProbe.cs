using MiraAPI.Hud;
using UnityEngine;

namespace AmongUsDogsRoles.Testing;

public static class CaptureTimerProbe
{
    public static object? Snapshot()
    {
        var release = CustomButtonManager.Buttons.OfType<ReleaseButton>().FirstOrDefault();
        if (release == null || !release.Button) return null;
        var button = release.Button!;
        var player = PlayerControl.LocalPlayer;
        var holding = player && RoundState.Drags.TryGetValue(player.PlayerId, out _);
        var captive = holding ? RoundState.Drags[player!.PlayerId] : default;
        var offset = button.graphic.transform.localPosition - button.position;
        return new {
            holding, remaining = holding ? Mathf.Max(0, captive.Expires - Time.time) : 0,
            duration = holding ? captive.Duration : 0, visible = button.isActiveAndEnabled,
            textVisible = button.cooldownTimerText.gameObject.activeInHierarchy,
            text = button.cooldownTimerText.text, offsetX = offset.x, offsetY = offset.y,
            canClick = release.CanClick(), isCoolingDown = button.isCoolingDown,
            fill = button.graphic.material.GetFloat("_Percent")
        };
    }
}
