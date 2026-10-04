using System.Collections;
using BepInEx;
using BepInEx.Unity.IL2CPP.Utils;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace AmongUsDogsRoles.Testing;

// Captures actual rendered game frames. Test tooling only; never shipped as a plugin.
public static class TestMedia
{
    public static bool Recording;
    public static bool Clean;
    [HideFromIl2Cpp]
    public static void Capture(MonoBehaviour runner, string name, float seconds)
    {
        if (Recording) throw new InvalidOperationException("Already recording");
        if (name.Length == 0 || name.Any(c => !char.IsLetterOrDigit(c) && c != '-'))
            throw new InvalidOperationException("Invalid capture name");
        Clean = true;
        // Test role reassignment does not run the game's normal intro/header setup.
        // Rebuild it before recording so old ghost/impostor instructions cannot linger.
        var player = PlayerControl.LocalPlayer;
        if (player && player.Data?.Role != null && HudManager.Instance)
        {
            foreach (var task in player.myTasks.ToArray())
                if (task.TryCast<ImportantTextTask>() != null)
                {
                    player.myTasks.Remove(task);
                    UnityEngine.Object.Destroy(task.gameObject);
                }
            player.Data.Role.SpawnTaskHeader(player);
            foreach (var panel in HudManager.Instance.GetComponentsInChildren<TaskPanelBehaviour>(true))
                if (panel.name == "RolePanel") panel.open = true;
        }
        runner.StartCoroutine(Frames(name, Mathf.Clamp(seconds, 0, 45)));
    }
    [HideFromIl2Cpp]
    private static IEnumerator Frames(string name, float seconds)
    {
        Recording = true;
        yield return new WaitForSeconds(.5f);
        var directory = Path.Combine(Paths.GameRootPath, "media", name);
        Directory.CreateDirectory(directory);
        var times = new List<float>();
        var motion = new List<object>();
        var texture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        var start = Time.realtimeSinceStartup;
        var next = start;
        do
        {
            yield return new WaitForEndOfFrame();
            if (Time.realtimeSinceStartup < next) continue;
            texture.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            times.Add(Time.realtimeSinceStartup - start);
            if (seconds > 0) motion.Add(new {
                time = times[^1], frameTime = Time.unscaledDeltaTime,
                captureTimer = CaptureTimerProbe.Snapshot(),
                players = PlayerControl.AllPlayerControls.ToArray().Where(p => p && p.Data?.Role != null).Select(p => new {
                    id = p.PlayerId, x = p.GetTruePosition().x, y = p.GetTruePosition().y,
                    vx = p.MyPhysics.body.velocity.x, vy = p.MyPhysics.body.velocity.y,
                    sprite = p.cosmetics.currentBodySprite.BodySprite.sprite.name,
                    bodyX = p.cosmetics.currentBodySprite.BodySprite.bounds.center.x,
                    bodyY = p.cosmetics.currentBodySprite.BodySprite.bounds.center.y,
                    rotation = p.cosmetics.currentBodySprite.BodySprite.transform.eulerAngles.z,
                    dragged = RoundState.Dragged(p.PlayerId)
                }).ToArray()
            });
            // PNG encoding stalls the game thread and made the old movement demos
            // look choppy. Reuse the readback texture and use fast JPEG for video.
            var extension = seconds == 0 ? "png" : "jpg";
            var bytes = seconds == 0 ? ImageConversion.EncodeToPNG(texture) : ImageConversion.EncodeToJPG(texture, 92);
            File.WriteAllBytes(Path.Combine(directory, $"{times.Count - 1:D5}.{extension}"), bytes);
            next = start + times.Count / 30f;
        } while (Time.realtimeSinceStartup - start < seconds);
        UnityEngine.Object.Destroy(texture);
        File.WriteAllText(Path.Combine(directory, "timing.json"), System.Text.Json.JsonSerializer.Serialize(times));
        if (seconds > 0) File.WriteAllText(Path.Combine(directory, "motion.json"), System.Text.Json.JsonSerializer.Serialize(motion));
        Recording = false;
    }
}
