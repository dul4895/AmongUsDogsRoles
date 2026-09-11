using MiraAPI.GameEnd;
using MiraAPI.Utilities;
using UnityEngine;

namespace AmongUsDogsRoles;

public static class BlastOutcome
{
    private static float showUntil;
    private static bool pending, sent;
    public static bool IsDraw { get; set; }
    public static bool NobodyAlive => GameData.Instance && GameData.Instance.AllPlayers.ToArray().Any(p => p != null && !p.Disconnected) &&
        !GameData.Instance.AllPlayers.ToArray().Any(p => p != null && !p.Disconnected && !p.IsDead);
    public static bool Drawing => pending && NobodyAlive;
    public static bool Holding => pending && Time.time < showUntil;
    public static void Begin() { pending = true; showUntil = Time.time + ExplosionPresentation.Duration; }
    public static void Tick()
    {
        // Peers do not run the host's game-ending checker. Expire their ordinary
        // blast state too, so an unrelated later death cannot become a draw candidate.
        if (pending && !Holding && !NobodyAlive && !sent) pending = false;
    }
    public static bool CheckEnd()
    {
        if (!pending) return true;
        if (Holding || RoundState.ResolvingBlast || sent) return false;
        if (NobodyAlive)
        {
            sent = true;
            CustomGameOver.Trigger<DrawGameOver>(Array.Empty<NetworkedPlayerInfo>());
            return false;
        }
        pending = false;
        return true;
    }
    public static void Reset() { pending = sent = IsDraw = false; showUntil = 0; }
}

public sealed class DrawGameOver : CustomGameOver
{
    public override bool VerifyCondition(PlayerControl sender, NetworkedPlayerInfo[] winners)
    {
        if (!sender.IsHost() || winners.Length != 0 || !BlastOutcome.Drawing) return false;
        BlastOutcome.IsDraw = true;
        return true;
    }
    public override bool BeforeEndGameSetup(EndGameManager manager)
    {
        EndGameResult.CachedWinners.Clear();
        // A draw should not sound like a crew or impostor victory.
        manager.CrewStinger = manager.ImpostorStinger = manager.DisconnectStinger;
        return true;
    }
    public override void AfterEndGameSetup(EndGameManager manager)
    {
        manager.WinText.text = "Draw";
        manager.WinText.color = new Color32(190, 209, 220, 255);
        manager.BackgroundBar.material.color = new Color32(92, 112, 130, 255);
        var caption = UnityEngine.Object.Instantiate(manager.WinText, manager.WinText.transform.parent);
        caption.name = "AmongUsDogsRolesDrawCaption";
        caption.text = "The kamikaze killed everyone, including himself";
        caption.enableAutoSizing = false;
        caption.fontSize = manager.WinText.fontSize * .28f;
        caption.transform.localPosition = manager.WinText.transform.localPosition + new Vector3(0, -.55f, 0);
    }
}
