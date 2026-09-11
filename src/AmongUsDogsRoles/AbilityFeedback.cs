using MiraAPI.Utilities;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AmongUsDogsRoles;

// Cosmetic feedback only. Gameplay applies immediately; blast results allow the impact to finish.
public static class AbilityFeedback
{
    private static SpriteRenderer? beacon;
    private static SpriteRenderer? arrow;
    private static SpriteRenderer? flash;
    private static TMPro.TextMeshPro? captiveLabel;
    private static float flashUntil;

    private static SpriteRenderer Sprite(string name, Transform? parent = null)
    {
        var go = new GameObject(name);
        if (parent)
        {
            go.transform.SetParent(parent, false);
            go.layer = LayerMask.NameToLayer("UI");
        }
        return go.AddComponent<SpriteRenderer>();
    }

    public static void Recall(byte actor)
    {
        if (PlayerControl.LocalPlayer && PlayerControl.LocalPlayer.PlayerId == actor)
            flashUntil = Time.time + .22f;
    }

    public static void Detonate(Vector2 center, float radius)
    {
        ExplosionPresentation.Play(center, radius);
    }

    public static void Update(HudManager hud)
    {
        var player = PlayerControl.LocalPlayer;
        if (!player || player.Data?.Role == null) return;
        var active = RoundState.InRound && RoundState.Alive(player);
        var marked = active && player.Data.Role is EscapistRole && RoundState.Marks.ContainsKey(player.PlayerId);
        if (marked)
        {
            if (!beacon) beacon = Sprite("AmongUsDogsRolesRecallMark");
            beacon!.sprite = Assets.Art("Beacon" + ((int)(Time.time * 12) % 10), 400).LoadAsset();
            var mark = RoundState.Marks[player.PlayerId];
            beacon.transform.position = new Vector3(mark.x, mark.y + .3f, -.1f);
            beacon.color = new Color(1, 1, 1, .8f);
        }
        if (beacon) beacon!.gameObject.SetActive(marked);

        var tracking = active && player.Data.Role is CoronerRole && RoundState.Tracks.TryGetValue(player.PlayerId, out _);
        var killer = tracking ? RoundState.Find(RoundState.Tracks[player.PlayerId]) : null;
        var bodyTrack = FakerState.BodyTracks.Contains(player.PlayerId);
        tracking &= bodyTrack ? killer && FakerState.Active.ContainsKey(killer!.PlayerId) && !killer.Data.Disconnected : RoundState.Alive(killer);
        if (tracking)
        {
            if (!arrow)
            {
                arrow = Sprite("AmongUsDogsRolesKillerArrow", hud.transform);
                arrow.sprite = Assets.Art("Arrow", 320).LoadAsset();
                arrow.color = new Color32(110, 215, 215, 255);
            }
            var destination = bodyTrack ? FakerState.BodyPosition(killer!.PlayerId) : killer!.GetTruePosition();
            // The world camera and HUD have different origins and scales. Aim in
            // HUD space, including camera follow lag, rather than treating the
            // centre of the HUD as the player's feet.
            var worldCamera = hud.PlayerCam.GetComponent<Camera>();
            var uiDepth = hud.UICamera.WorldToViewportPoint(hud.transform.TransformPoint(new Vector3(0, 0, -10))).z;
            Vector2 Project(Vector2 position)
            {
                var viewport = worldCamera.WorldToViewportPoint(new Vector3(position.x, position.y, 0));
                viewport.z = uiDepth;
                return hud.transform.InverseTransformPoint(hud.UICamera.ViewportToWorldPoint(viewport));
            }
            var origin = Project(player.GetTruePosition());
            var offset = Project(destination) - origin;
            var distance = offset.magnitude;
            var direction = distance > .001f ? offset / distance : Vector2.right;
            // Keep the arrow tip before the target, even at very close range.
            // A negative distance places it behind the observer when overlapping.
            var tipClearance = arrow!.sprite.bounds.extents.x + .12f;
            var position = origin + direction * Mathf.Min(1.3f, distance - tipClearance);
            arrow.transform.localPosition = new Vector3(position.x, position.y, -10);
            arrow.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
        }
        if (arrow) arrow!.gameObject.SetActive(tracking);

        var captured = active && RoundState.Dragged(player.PlayerId);
        var holding = active && RoundState.Drags.ContainsKey(player.PlayerId);
        if (captured || holding)
        {
            if (!captiveLabel)
            {
                captiveLabel = Object.Instantiate(hud.TaskPanel.taskText, hud.transform);
                captiveLabel.name = "AmongUsDogsRolesCaptureStatus";
                captiveLabel.alignment = TMPro.TextAlignmentOptions.Center;
                captiveLabel.fontSize = 2.1f;
                captiveLabel.transform.localPosition = new Vector3(0, -1.8f, -10);
                captiveLabel.color = Color.white;
            }
            var captive = holding ? RoundState.Drags[player.PlayerId] : RoundState.Drags.Values.First(d => d.Target == player.PlayerId);
            captiveLabel!.text = $"{(holding ? "Holding player" : "Captured")} · {Mathf.CeilToInt(Mathf.Max(0, captive.Expires - Time.time))}s";
        }
        if (captiveLabel) captiveLabel!.gameObject.SetActive(captured || holding);

        var recalling = active && Time.time < flashUntil;
        if (recalling)
        {
            if (!flash)
            {
                flash = Object.Instantiate(hud.FullScreen, hud.transform);
                flash.name = "AmongUsDogsRolesRecallFade";
            }
            flash!.color = new Color(.6f, .85f, 1, .16f * (flashUntil - Time.time) / .22f);
        }
        if (flash) flash!.gameObject.SetActive(recalling);

        ExplosionPresentation.Tick();
    }

    public static void Clear()
    {
        ExplosionPresentation.Clear();
        foreach (var renderer in new Renderer?[] { beacon, arrow, flash })
            if (renderer) Object.Destroy(renderer!.gameObject);
        if (captiveLabel) Object.Destroy(captiveLabel!.gameObject);
        beacon = arrow = flash = null; captiveLabel = null;
        flashUntil = 0;
    }
}
