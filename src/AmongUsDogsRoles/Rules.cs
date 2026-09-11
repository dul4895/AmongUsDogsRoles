namespace AmongUsDogsRoles;

// Pure rules shared by the game adapter and executable regression checks.
public enum Faction { Crew, Impostor, Neutral }
public enum ShotResult { TargetDies, SheriffDies, AttackerDies }
public static class Rules
{
    public static ShotResult SheriffShot(Faction target, bool alert) =>
        alert ? ShotResult.AttackerDies : target == Faction.Crew ? ShotResult.SheriffDies : ShotResult.TargetDies;

    public static bool InBlast(float squaredDistance, float radius, bool alive, bool disconnected) =>
        alive && !disconnected && squaredDistance >= 0 && squaredDistance <= radius * radius;

    public static bool CanAct(bool alive, bool connected, bool inRound, bool mobile, bool dragged) =>
        alive && connected && inRound && mobile && !dragged;

    public static bool Ready(float now, float readyAt) => now >= readyAt;
    public static bool JesterWins(bool isJester, bool tied, bool skipped, bool aliveBeforeVote) =>
        isJester && !tied && !skipped && aliveBeforeVote;
}
