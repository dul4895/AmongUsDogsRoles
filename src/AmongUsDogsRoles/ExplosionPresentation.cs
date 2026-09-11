using UnityEngine;
using Object = UnityEngine.Object;

namespace AmongUsDogsRoles;

public static class ExplosionPresentation
{
    public const float Duration = 1.2f;
    public const int FrameCount = 36;
    private sealed record Burst(SpriteRenderer Renderer, float Started);
    private static readonly List<Burst> Bursts = new();
    private static Sprite[]? frames;
    private static AudioClip? sound;

    public static void Preload()
    {
        // Unity can unload native sprites between scenes even while the managed
        // array remains allocated. Re-enter Mira's loader to recover dead assets.
        frames ??= new Sprite[FrameCount];
        for (var i = 0; i < frames.Length; i++)
        {
            if (frames[i] && frames[i].texture) continue;
            var asset = Assets.Art("Blast" + i);
            asset.UnloadAsset();
            frames[i] = asset.LoadAsset();
        }
        if (sound) return;
        // The source generator writes mono, 24 kHz, signed 16-bit PCM with a 44-byte header.
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream("AmongUsDogsRoles.Resources.Abilities.Explosion.wav")!;
        using var reader = new BinaryReader(stream);
        reader.ReadBytes(44);
        var samples = new float[(int)(stream.Length - 44) / 2];
        for (var i = 0; i < samples.Length; i++) samples[i] = reader.ReadInt16() / 32768f;
        sound = AudioClip.Create("AmongUsDogsRolesExplosion", samples.Length, 1, 24000, false);
        sound.SetData(samples, 0);
    }

    public static void Play(Vector2 center, float radius)
    {
        if (!RoundState.InRound) return;
        Preload();
        var renderer = new GameObject("AmongUsDogsRolesExplosion").AddComponent<SpriteRenderer>();
        renderer.sprite = frames![0];
        renderer.transform.position = new Vector3(center.x, center.y + .45f, -1f);
        renderer.transform.localScale = Vector3.one * (radius * 2 / 5f);
        Bursts.Add(new(renderer, Time.time));
        // Each recipient plays the effect locally at the shared world position.
        // Use the normal SFX mixer so the game's sound volume/mute still applies.
        var player = PlayerControl.LocalPlayer;
        if (!player || !SoundManager.Instance || !Constants.ShouldPlaySfx()) return;
        var distance = Vector2.Distance(player.GetTruePosition(), center);
        var volume = .9f * Mathf.Clamp01((radius + 4 - distance) / 4);
        if (volume > 0) SoundManager.Instance.PlaySound(sound, false, volume, SoundManager.Instance.sfxMixer);
    }

    public static void Tick()
    {
        BlastOutcome.Tick();
        for (var i = Bursts.Count - 1; i >= 0; i--)
        {
            var burst = Bursts[i];
            var t = (Time.time - burst.Started) / Duration;
            if (!burst.Renderer || !RoundState.InRound || t >= 1)
            {
                if (burst.Renderer) Object.Destroy(burst.Renderer.gameObject);
                Bursts.RemoveAt(i);
                continue;
            }
            // Also animate for killed players and witnesses; never depend on the owner's role/alive flag.
            burst.Renderer.sprite = frames![Mathf.Clamp((int)(t * frames.Length), 0, frames.Length - 1)];
        }
    }

    public static void Clear()
    {
        foreach (var burst in Bursts) if (burst.Renderer) Object.Destroy(burst.Renderer.gameObject);
        Bursts.Clear();
    }
}
