namespace AmongUsDogsRoles;

// Pure rules shared by the game adapter and executable regression checks.
public enum Faction { Crew, Impostor, Neutral }
public enum ShotResult { TargetDies, SheriffDies, AttackerDies }
public static class Rules
{
    public static bool KeepTrail(float now, float expires, float distance, float screenHeight) =>
        now < expires && distance > screenHeight * 1.5f;

    public static bool CanGuess(bool enabled, bool voting, bool alive, bool impostor, bool faking,
        bool targetAlive, bool targetCrew, bool self) =>
        enabled && voting && alive && impostor && !faking && targetAlive && targetCrew && !self;

    // Each configured copy rolls once. Guaranteed roles take priority; winning
    // tickets compete uniformly for the limited slots, independent of type order.
    public static int[] SelectRoles((int Id, int Count, int Chance)[] roles, int slots, Func<int, int> next)
    {
        var chosen = new List<int>();
        foreach (var guaranteed in new[] { true, false })
        {
            var tickets = new List<int>();
            foreach (var role in roles)
                for (var i = 0; i < role.Count; i++)
                    if (guaranteed ? role.Chance == 100 : role.Chance > 0 && role.Chance < 100 && next(100) < role.Chance)
                        tickets.Add(role.Id);
            while (chosen.Count < slots && tickets.Count > 0)
            {
                var i = next(tickets.Count);
                chosen.Add(tickets[i]); tickets.RemoveAt(i);
            }
        }
        return chosen.ToArray();
    }

    public static Dictionary<byte, byte> CameraPairs(byte[] players, Random random)
    {
        var shuffled = players.OrderBy(_ => random.Next()).ToArray();
        var pairs = new Dictionary<byte, byte>();
        // Round half the lobby down to complete pairs: 9 players -> 4 swapped.
        for (var i = 0; i < players.Length / 4 * 2; i += 2)
        { pairs[shuffled[i]] = shuffled[i + 1]; pairs[shuffled[i + 1]] = shuffled[i]; }
        return pairs;
    }

    public static int[] CorruptCounts(int[] counts, int seed)
    {
        var result = (int[])counts.Clone();
        var random = new Random(seed);
        var occupied = Enumerable.Range(0, counts.Length).Where(i => counts[i] > 0).OrderBy(_ => random.Next()).ToArray();
        // Prefer occupied rooms, at most two transfers, never invent a crowd or
        // remove a room's last occupant. Retain the true total and most rooms.
        var used = new HashSet<int>();
        foreach (var donor in occupied.Where(i => counts[i] >= 2))
        {
            var receiver = occupied.FirstOrDefault(i => i != donor && !used.Contains(i), -1);
            if (receiver < 0 || used.Contains(donor)) continue;
            result[donor]--; result[receiver]++;
            used.Add(donor); used.Add(receiver);
            if (used.Count >= 4) break;
        }
        return result;
    }
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
