using UnityEngine;

namespace AmongUsDogsRoles.Testing;

// Measure the rendered arrow independently in screen pixels, not HUD coordinates.
public static class CoronerArrowProbe
{
    public static object? Snapshot()
    {
        var player = PlayerControl.LocalPlayer;
        if (!player || player.Data?.Role is not CoronerRole || !RoundState.InRound ||
            !RoundState.Tracks.TryGetValue(player.PlayerId, out var id)) return null;
        var target = RoundState.Find(id);
        var arrow = UnityEngine.Object.FindObjectsOfType<SpriteRenderer>()
            .FirstOrDefault(r => r.name == "AmongUsDogsRolesKillerArrow" && r.gameObject.activeInHierarchy);
        if (!target || !arrow) return null;
        var body = FakerState.BodyTracks.Contains(player.PlayerId);
        var world = body ? FakerState.BodyPosition(id) : target!.GetTruePosition();
        var destination = (Vector2)Camera.main.WorldToScreenPoint(new Vector3(world.x, world.y, 0));
        var camera = HudManager.Instance.UICamera;
        var centre = (Vector2)camera.WorldToScreenPoint(arrow!.transform.position);
        var tip = (Vector2)camera.WorldToScreenPoint(arrow.transform.TransformPoint(new Vector3(arrow.sprite.bounds.max.x, 0, 0)));
        var direction = (tip - centre).normalized;
        var offset = destination - tip;
        return new {
            body, target = id, x = centre.x, y = centre.y,
            tipX = tip.x, tipY = tip.y, targetX = destination.x, targetY = destination.y,
            missPixels = Mathf.Abs(direction.x * offset.y - direction.y * offset.x),
            clearancePixels = Vector2.Dot(direction, offset),
            onScreen = destination.x >= 0 && destination.x <= Screen.width && destination.y >= 0 && destination.y <= Screen.height,
            arrowOnScreen = centre.x >= 0 && centre.x <= Screen.width && centre.y >= 0 && centre.y <= Screen.height
        };
    }
}
