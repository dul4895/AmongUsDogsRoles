using UnityEngine;

namespace AmongUsDogsRoles.Testing;

public static class BlastProbe
{
    public static AsyncOperation? Unload;
    public static object AssetsSnapshot()
    {
        var cached=(Sprite[]?)HarmonyLib.AccessTools.Field(typeof(ExplosionPresentation),"frames").GetValue(null);
        return new {cached=cached?.Length ?? 0,live=cached?.Count(s=>s && s.texture) ?? 0,unloading=Unload!=null && !Unload.isDone};
    }
    private static int visualSamples, audibleSamples;
    private static float maxVolume;
    private static bool victimSaw;
    private static readonly HashSet<string> Frames = new();
    public static void Reset() { visualSamples=audibleSamples=0;maxVolume=0;victimSaw=false;Frames.Clear(); }
    public static object Snapshot()
    {
        var player=PlayerControl.LocalPlayer;
        foreach(var sprite in UnityEngine.Object.FindObjectsOfType<SpriteRenderer>().Where(r=>r.name=="AmongUsDogsRolesExplosion" && r.enabled && r.gameObject.activeInHierarchy))
        {
            if(sprite.isVisible){visualSamples++;victimSaw |= player && player.Data!=null && player.Data.IsDead;}
            if(sprite.sprite) Frames.Add(sprite.sprite.name);
        }
        foreach(var source in UnityEngine.Object.FindObjectsOfType<AudioSource>().Where(s=>s.clip && s.clip.name=="AmongUsDogsRolesExplosion" && s.isPlaying))
        {audibleSamples++;maxVolume=Math.Max(maxVolume,source.volume);}
        var end=UnityEngine.Object.FindObjectOfType<EndGameManager>();
        return new {visualSamples,audibleSamples,maxVolume,victimSaw,frames=Frames.ToArray(),draw=BlastOutcome.IsDraw,
            endTitle=end?end.WinText.text:null,caption=end?end.GetComponentsInChildren<TMPro.TextMeshPro>().FirstOrDefault(t=>t.name=="AmongUsDogsRolesDrawCaption")?.text:null};
    }
}
