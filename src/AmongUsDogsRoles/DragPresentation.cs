using UnityEngine;
using Object = UnityEngine.Object;

namespace AmongUsDogsRoles;

// Derived from the replicated capture state on every client, including witnesses.
// Only the cosmetic transform is rotated; player physics and camera stay upright.
public static class DragPresentation
{
    private sealed class Pose(Transform transform)
    {
        public readonly Transform Transform = transform;
        private readonly Vector3 position = transform.localPosition;
        private readonly Quaternion rotation = transform.localRotation;
        public void Restore() { if (Transform) { Transform.localPosition = position; Transform.localRotation = rotation; } }
    }
    private sealed class Rig(PlayerControl victim, Vector2 direction)
    {
        public readonly PlayerControl Victim = victim;
        public readonly Transform Cosmetics = victim.cosmetics.transform;
        public readonly Vector3 Position = victim.cosmetics.transform.localPosition;
        public readonly Quaternion Rotation = victim.cosmetics.transform.localRotation;
        public readonly Vector3 NamePosition = victim.cosmetics.nameText.transform.localPosition;
        public readonly Quaternion NameRotation = victim.cosmetics.nameText.transform.localRotation;
        public readonly Pose[] Parts = PartsOf(victim);
        public readonly List<SpriteRenderer> Links = new();
        public Vector2 Trail = victim.GetTruePosition(), Direction = direction;
    }
    private static readonly Dictionary<byte, Rig> Rigs = new();
    private sealed class Settling(Rig rig)
    {
        public readonly Rig Rig = rig;
        public Vector2 Display = rig.Victim.GetTruePosition(), Velocity;
        public readonly float Until = Time.time + 1f;
    }
    private static readonly Dictionary<byte, Settling> Releases = new();

    private static void RestoreStanding(Rig rig)
    {
        foreach (var part in rig.Parts) part.Restore();
        if (rig.Victim && rig.Victim.cosmetics.nameText)
        {
            rig.Victim.cosmetics.nameText.transform.localPosition = rig.NamePosition;
            rig.Victim.cosmetics.nameText.transform.localRotation = rig.NameRotation;
        }
    }

    private static Pose[] PartsOf(PlayerControl victim)
    {
        var candidates = new[] { victim.cosmetics.transform, victim.cosmetics.currentBodySprite.BodySprite.transform,
            victim.cosmetics.hat.transform, victim.cosmetics.visor.transform, victim.cosmetics.skin.transform }.Distinct().ToArray();
        return candidates.Where(t => !candidates.Any(parent => parent != t && t.IsChildOf(parent))).Select(t => new Pose(t)).ToArray();
    }

    public static void Follow(PlayerControl actor, PlayerControl victim)
    {
        if (Releases.Remove(victim.PlayerId, out var settling)) RestoreStanding(settling.Rig);
        if (!Rigs.TryGetValue(actor.PlayerId, out var rig))
            Rigs[actor.PlayerId] = rig = new(victim, actor.MyPhysics.FlipX ? Vector2.left : Vector2.right);
        var origin = actor.GetTruePosition();
        var separation = origin - rig.Trail;
        var length = separation.magnitude;
        if (length > .02f) rig.Direction = separation / length;
        // Pull only when the chain becomes taut. Deriving the entire trail from
        // the latest movement delta flipped victims across the captor on tiny
        // remote interpolation corrections, especially when stopping/releasing.
        var distance = Mathf.Min(length, 1.05f);
        // Compress the trail near walls instead of putting the captive through them.
        while (distance > 0 && PhysicsHelpers.AnythingBetween(origin, origin - rig.Direction * distance, Constants.ShipAndObjectsMask, false))
            distance = Mathf.Max(0, distance - .15f);
        var position = rig.Trail = origin - rig.Direction * distance;
        var offset = (Vector2)victim.transform.position - victim.GetTruePosition();
        victim.transform.position = new Vector3(position.x + offset.x, position.y + offset.y, actor.transform.position.z + .001f);
    }

    public static void Update()
    {
        foreach (var (id, settling) in Releases.ToArray())
        {
            var rig = settling.Rig;
            RestoreStanding(rig);
            if (!RoundState.InRound || !RoundState.Alive(rig.Victim) || Time.time >= settling.Until)
            { Releases.Remove(id); continue; }
            var position = rig.Victim.GetTruePosition();
            // Each observer sees a different interpolated captor position. Blend
            // the rendered handoff to the captive owner's fresh network position;
            // physics, input and the captive's own camera resume immediately.
            settling.Display = Vector2.SmoothDamp(settling.Display, position, ref settling.Velocity, .12f);
            var offset = (Vector3)(settling.Display - position) * Mathf.Clamp01((settling.Until - Time.time) / .3f);
            foreach (var part in rig.Parts) if (part.Transform) part.Transform.position += offset;
            var name = rig.Victim.cosmetics.nameText.transform;
            if (!rig.Parts.Any(p => p.Transform && (p.Transform == name || name.IsChildOf(p.Transform))))
                name.position += offset;
        }
        foreach (var (actorId, rig) in Rigs.ToArray())
        {
            var actor = RoundState.Find(actorId);
            var victim = rig.Victim;
            if (!RoundState.InRound || !RoundState.Drags.ContainsKey(actorId) || !RoundState.Alive(actor) || !RoundState.Alive(victim))
            { Release(actorId); continue; }
            if (!rig.Cosmetics || !victim.cosmetics.currentBodySprite.BodySprite) continue;
            victim.MyPhysics.Animations.PlayIdleAnimation();
            foreach (var part in rig.Parts) part.Restore();
            var body = victim.cosmetics.currentBodySprite.BodySprite;
            var center = victim.GetTruePosition() + Vector2.up * .18f;
            var pivot = body.bounds.center;
            var translation = (Vector3)(center - (Vector2)pivot);
            foreach (var part in rig.Parts)
            {
                if (!part.Transform) continue;
                part.Transform.RotateAround(pivot, Vector3.forward, rig.Direction.x < 0 ? 90 : -90);
                part.Transform.position += translation;
            }
            var name = victim.cosmetics.nameText.transform;
            name.rotation = Quaternion.identity;
            name.position = new Vector3(center.x, center.y + .65f, name.position.z);

            var from = actor!.GetTruePosition() + Vector2.up * .28f - rig.Direction * .2f;
            var to = center + rig.Direction * .25f;
            var observer = PlayerControl.LocalPlayer;
            var visible = observer && body.enabled && body.color.a > .05f && actor.cosmetics.currentBodySprite.BodySprite.color.a > .05f;
            if (visible && !observer.Data.IsDead)
            {
                var eye = observer.GetTruePosition();
                var radius = ShipStatus.Instance.CalculateLightRadius(observer.Data);
                visible = Vector2.Distance(eye, from) < radius && Vector2.Distance(eye, to) < radius &&
                    !PhysicsHelpers.AnythingBetween(eye, from, Constants.ShipAndObjectsMask, false) &&
                    !PhysicsHelpers.AnythingBetween(eye, to, Constants.ShipAndObjectsMask, false);
            }
            var count = Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(from, to) / .12f), 1, 16);
            while (rig.Links.Count < count)
            {
                var go = new GameObject("AmongUsDogsRolesDragChain");
                go.layer = body.gameObject.layer;
                var link = go.AddComponent<SpriteRenderer>();
                link.sprite = Assets.Art("ChainLink", 512).LoadAsset();
                link.sortingLayerID = body.sortingLayerID;
                link.sortingOrder = body.sortingOrder;
                rig.Links.Add(link);
            }
            var angle = Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg;
            for (var i = 0; i < rig.Links.Count; i++)
            {
                var link = rig.Links[i];
                link.gameObject.SetActive(visible && i < count);
                if (i >= count) continue;
                var point = Vector2.Lerp(from, to, (i + .5f) / count);
                point.y -= .04f * Mathf.Sin((i + .5f) / count * Mathf.PI);
                link.transform.position = new Vector3(point.x, point.y, body.transform.position.z - .01f);
                link.transform.rotation = Quaternion.Euler(0, 0, angle);
                link.transform.localScale = new Vector3(1, i % 2 == 0 ? 1 : .5f, 1);
            }
        }
    }

    public static void Release(byte actor)
    {
        if (!Rigs.Remove(actor, out var rig)) return;
        RestoreStanding(rig);
        if (rig.Cosmetics)
        {
            rig.Cosmetics.localPosition = rig.Position;
            rig.Cosmetics.localRotation = rig.Rotation;
            if (rig.Victim && rig.Victim.cosmetics.nameText)
            {
                var name = rig.Victim.cosmetics.nameText.transform;
                name.localPosition = rig.NamePosition;
                name.localRotation = rig.NameRotation;
            }
        }
        foreach (var link in rig.Links) if (link) Object.Destroy(link.gameObject);
        if (RoundState.InRound && RoundState.Alive(rig.Victim) && !rig.Victim.AmOwner)
            Releases[rig.Victim.PlayerId] = new(rig);
    }
    public static void Clear()
    {
        foreach (var id in Rigs.Keys.ToArray()) Release(id);
        foreach (var settling in Releases.Values) RestoreStanding(settling.Rig);
        Releases.Clear();
    }
}
