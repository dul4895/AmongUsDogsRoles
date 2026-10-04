using AmongUsDogsRoles;

var passed = 0;
void Check(string name, bool condition)
{
    if (!condition) throw new Exception($"FAIL: {name}");
    System.Console.WriteLine($"PASS: {name}");
    passed++;
}
Check("Sheriff kills an impostor", Rules.SheriffShot(Faction.Impostor, false) == ShotResult.TargetDies);
Check("Sheriff can kill a Jester", Rules.SheriffShot(Faction.Neutral, false) == ShotResult.TargetDies);
Check("Crew misfire kills only Sheriff", Rules.SheriffShot(Faction.Crew, false) == ShotResult.SheriffDies);
Check("Alert retaliation precedes crew misfire", Rules.SheriffShot(Faction.Crew, true) == ShotResult.AttackerDies);
foreach (var faction in Enum.GetValues<Faction>())
    Check($"Blast includes {faction} at its edge", Rules.InBlast(6.25f, 2.5f, true, false));
Check("Blast excludes outside radius", !Rules.InBlast(6.26f, 2.5f, true, false));
Check("Blast ignores disconnected players", !Rules.InBlast(0, 2.5f, true, true));
Check("Blast ignores corpses", !Rules.InBlast(0, 2.5f, false, false));
Check("Blast includes center", Rules.InBlast(0, 2.5f, true, false));
Check("Blast rejects NaN position", !Rules.InBlast(float.NaN, 2.5f, true, false));
Check("Alive mobile player can act in round", Rules.CanAct(true, true, true, true, false));
Check("Captive cannot act", !Rules.CanAct(true, true, true, true, true));
Check("Dead player cannot act", !Rules.CanAct(false, true, true, true, false));
Check("Disconnected player cannot act", !Rules.CanAct(true, false, true, true, false));
Check("Meeting blocks actions", !Rules.CanAct(true, true, false, true, false));
Check("Ladder and vent transitions block actions", !Rules.CanAct(true, true, true, false, false));
Check("Cooldown rejects early replay", !Rules.Ready(9.99f, 10));
Check("Cooldown allows exact boundary", Rules.Ready(10, 10));
Check("Cooldown permits later action", Rules.Ready(11, 10));
Check("Jester wins on actual living-player ejection", Rules.JesterWins(true, false, false, true));
Check("Jester does not win a tie", !Rules.JesterWins(true, true, false, true));
Check("Jester does not win a skip", !Rules.JesterWins(true, false, true, true));
Check("Killed Jester does not win", !Rules.JesterWins(true, false, false, false));
Check("Crew ejection is not a Jester win", !Rules.JesterWins(false, false, false, true));
Check("Trail expires at exactly ten seconds", !Rules.KeepTrail(10, 10, 20, 6));
Check("Trail expires within 1.5 screens", !Rules.KeepTrail(5, 10, 9, 6));
Check("Distant fresh trail remains", Rules.KeepTrail(5, 10, 9.01f, 6));
Check("Living impostor can guess living crew", Rules.CanGuess(true, true, true, true, false, true, true, false));
Check("Fake death cannot guess", !Rules.CanGuess(true, true, true, true, true, true, true, false));
Check("Dead impostor cannot guess", !Rules.CanGuess(true, true, false, true, false, true, true, false));
Check("Disabled guesses blocked", !Rules.CanGuess(false, true, true, true, false, true, true, false));
Check("Results cannot accept guesses", !Rules.CanGuess(true, false, true, true, false, true, true, false));
Check("Cannot guess self", !Rules.CanGuess(true, true, true, true, false, true, true, true));
Check("Cannot guess dead target", !Rules.CanGuess(true, true, true, true, false, false, true, false));
var rng = new Random(71491);
int solo = 0, competing = 0;
for (var game = 0; game < 100000; game++)
{
    solo += Rules.SelectRoles([(1, 1, 90)], 2, rng.Next).Contains(1) ? 1 : 0;
    var selection = Rules.SelectRoles([(1, 1, 90), (2, 1, 50), (3, 1, 50), (4, 1, 50), (5, 1, 50), (6, 1, 50)], 2, rng.Next);
    competing += selection.Contains(1) ? 1 : 0;
    if (selection.Length > 2 || selection.Distinct().Count() != selection.Length) throw new Exception("Role capacity/count exceeded");
}
Check("Kamikaze 90% alone has correct measured probability", solo is > 89000 and < 91000);
Check("Kamikaze remains eligible among all six impostor roles", competing is > 45000 and < 65000);
System.Console.WriteLine($"Allocation samples: solo={solo}/100000; competing={competing}/100000.");
Check("Disabled role never selected", Rules.SelectRoles([(1, 3, 0)], 3, rng.Next).Length == 0);
Check("Zero count never selected", Rules.SelectRoles([(1, 0, 100)], 3, rng.Next).Length == 0);
Check("Guaranteed role has priority", Rules.SelectRoles([(1, 1, 90), (2, 2, 100)], 2, rng.Next).SequenceEqual(new[] {2, 2}));
Check("90% includes roll 89", Rules.SelectRoles([(1, 1, 90)], 1, n => n == 100 ? 89 : 0).Length == 1);
Check("90% excludes roll 90", Rules.SelectRoles([(1, 1, 90)], 1, n => n == 100 ? 90 : 0).Length == 0);
for (byte count = 4; count <= 15; count++)
{
    var pairs = Rules.CameraPairs(Enumerable.Range(0, count).Select(i => (byte)i).ToArray(), rng);
    Check($"{count}-player camera pairs cover half rounded to pairs", pairs.Count == count / 4 * 2);
    Check($"{count}-player camera swaps are symmetric and have no self-pairs", pairs.All(p => p.Key != p.Value && pairs[p.Value] == p.Key));
}
for (var i = 0; i < 1000; i++)
{
    var counts = Enumerable.Range(0, 12).Select(_ => rng.Next(4)).ToArray();
    var changed = Rules.CorruptCounts(counts, i);
    if (changed.Sum() != counts.Sum() || changed.Any(x => x < 0) || changed.Where((x,j) => Math.Abs(x-counts[j]) > 1).Any())
        throw new Exception("Admin count invariant failed");
}
Check("Admin 2+2 becomes plausible 1+3", Rules.CorruptCounts([2,2], 5).OrderBy(x=>x).SequenceEqual(new[]{1,3}));
Check("Admin empty map stays empty", Rules.CorruptCounts([0,0,0], 5).Sum() == 0);
System.Console.WriteLine($"{passed} gameplay rule checks passed (plus 201,000 allocation/count invariant trials).");
