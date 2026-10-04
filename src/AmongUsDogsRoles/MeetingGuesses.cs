using AmongUs.GameOptions;
using HarmonyLib;
using Hazel;
using Il2CppInterop.Runtime.Attributes;
using MiraAPI.MeetingAbilities;
using MiraAPI.Roles;
using MiraAPI.Utilities;
using MiraAPI.Utilities.Assets;
using MiraAPI.Voting;
using Reactor.Networking.Attributes;
using Reactor.Networking.Rpc;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AmongUsDogsRoles;

public readonly record struct GuessRequest(uint Meeting, byte Target, ushort Role);

public static class MeetingGuesses
{
    public static bool Voting => MeetingHud.Instance && MeetingHud.Instance.state == MeetingHud.MeetingStates.NotVoted ||
        MeetingHud.Instance && MeetingHud.Instance.state == MeetingHud.MeetingStates.Voted;
    public static bool CanUse(PlayerControl? actor) => RoleTuning.MeetingGuesses && Voting &&
        RoundState.Alive(actor) && actor!.Data.Role.IsImpostor && !FakerState.IsFaking(actor);
    public static bool CanTarget(PlayerControl actor, PlayerControl? target) => target && Rules.CanGuess(
        RoleTuning.MeetingGuesses, Voting, RoundState.Alive(actor), actor.Data.Role.IsImpostor,
        FakerState.IsFaking(actor), RoundState.Alive(target), !target!.Data.Role.IsImpostor, actor.PlayerId == target.PlayerId);
    public static RoleBehaviour[] Choices() => RoleManager.Instance.AllRoles.ToArray()
        .Where(r => !r.IsImpostor && !RoleManager.IsGhostRole(r.Role) &&
            (r.Role == RoleTypes.Crewmate || GameOptionsManager.Instance.CurrentGameOptions.RoleOptions.GetNumPerGame(r.Role) > 0 &&
             GameOptionsManager.Instance.CurrentGameOptions.RoleOptions.GetChancePerGame(r.Role) > 0))
        .GroupBy(r => r.Role).Select(g => g.First()).OrderBy(Name).ToArray();
    public static string Name(RoleBehaviour role) => role is ICustomRole custom ? custom.RoleName :
        TranslationController.Instance.GetString(role.StringName);
    public static void Handle(PlayerControl actor, GuessRequest request)
    {
        if (!CanUse(actor) || MeetingHud.Instance.NetId != request.Meeting) return;
        var target = RoundState.Find(request.Target);
        if (!CanTarget(actor, target) || !Choices().Any(r => (ushort)r.Role == request.Role)) return;
        var victim = (ushort)target!.Data.Role.Role == request.Role ? target : actor;
        StateRpc.Broadcast(new(StateKind.MeetingDeath, actor.PlayerId, victim.PlayerId));
        MeetingHud.Instance.CheckForEndVoting();
    }
    public static void ApplyDeath(byte source, byte victim)
    {
        var target = RoundState.Find(victim);
        if (!MeetingHud.Instance || !RoundState.Alive(target)) return;
        target!.Die(DeathReason.Kill, true);
        RoundState.Killers[victim] = source;
        target.gameObject.layer = LayerMask.NameToLayer("Ghost");
        var meeting = MeetingHud.Instance;
        foreach (var player in PlayerControl.AllPlayerControls)
        {
            var data = player.GetVoteData();
            var row = meeting.playerStates.ToArray().FirstOrDefault(r => (byte)r.PlayerId == player.PlayerId);
            if (player.PlayerId == victim)
            {
                data.Votes.Clear(); data.SetRemainingVotes(0);
                if (row) { row!.SetDead(true); row.UnsetVote(); }
            }
            else if (data.VotedFor(victim))
            {
                data.Votes.Clear(); data.SetRemainingVotes(RoundState.Alive(player) ? 1 : 0);
                if (row) row!.UnsetVote();
                if (player.AmOwner) meeting.ClearVote(player.PlayerId, false);
            }
        }
        if (AmongUsClient.Instance.AmHost) { target.Data.MarkDirty(); meeting.SetDirtyBit(1U); }
        if (target.AmOwner) { meeting.SetForegroundForDead(); GuessPicker.Instance?.Close(); }
    }
}

[RegisterCustomRpc(3)]
public sealed class GuessRpc(Plugin plugin, uint id) : PlayerCustomRpc<Plugin, GuessRequest>(plugin, id)
{
    public override RpcLocalHandling LocalHandling => RpcLocalHandling.Before;
    public override void Write(MessageWriter w, GuessRequest d) { w.Write(d.Meeting); w.Write(d.Target); w.Write(d.Role); }
    public override GuessRequest Read(MessageReader r) => new(r.ReadUInt32(), r.ReadByte(), r.ReadUInt16());
    public override void Handle(PlayerControl sender, GuessRequest d)
    { if (AmongUsClient.Instance.AmHost) MeetingGuesses.Handle(sender, d); }
    public static void Send(byte target, ushort role) => Rpc<GuessRpc>.Instance.SendTo(AmongUsClient.Instance.HostId,
        new(MeetingHud.Instance.NetId, target, role));
}

public sealed class GuessButton : TargetedMeetingButton
{
    public override string Name => "Guess role";
    public override int MaxUses => 0;
    public override float Cooldown => 0;
    public override LoadableAsset<Sprite> Sprite => Assets.Art("Shoot");
    public override Color OutlineColor => Palette.ImpostorRed;
    public override bool Enabled(RoleBehaviour r) => MeetingGuesses.CanUse(PlayerControl.LocalPlayer);
    public override bool IsTargetValid(PlayerVoteArea area) =>
        MeetingGuesses.CanTarget(PlayerControl.LocalPlayer, RoundState.Find((byte)area.PlayerId));
    protected override void OnClick(PlayerVoteArea area)
    { if (IsTargetValid(area)) GuessPicker.Instance?.Open((byte)area.PlayerId); }
}

// A local role picker contains configured role names only, never target roles.
// Select a role, then explicitly confirm the lethal guess.
public sealed class GuessPicker(IntPtr pointer) : MonoBehaviour(pointer)
{
    public static GuessPicker? Instance;
    private Canvas? canvas;
    private GameObject? root;
    private RectTransform? panel;
    private RoleBehaviour[] choices = [];
    private readonly List<TextMeshProUGUI> labels = [];
    private TextMeshProUGUI? heading, confirm;
    private byte target;
    private int selected = -1, openedFrame, closedFrame = -1;
    private uint meeting;
    private float scale;
    public static bool Blocks => Instance && (Instance!.root || Instance.closedFrame == Time.frameCount);
    public void Awake() => Instance = this;
    public void Open(byte player)
    {
        Close(); target = player; selected = -1; choices = MeetingGuesses.Choices();
        meeting = MeetingHud.Instance.NetId; openedFrame = Time.frameCount;
        root = new GameObject("AmongUsDogsRolesGuessPicker"); root.transform.SetParent(transform, false);
        canvas = root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32010;
        panel = new GameObject("Panel").AddComponent<RectTransform>(); panel.SetParent(root.transform, false);
        panel.anchorMin = panel.anchorMax = new Vector2(.5f, .5f); panel.pivot = new Vector2(0, 1);
        panel.anchoredPosition = new Vector2(-380, 260); panel.sizeDelta = new Vector2(760, 520);
        Box(new(0, 0, 760, 520), new Color(.045f, .065f, .10f, .995f));
        heading = Text(new(25, 20, 710, 50), "Guess " + RoundState.Find(player)!.Data.PlayerName + "'s role", 26);
        heading.richText = false;
        Text(new(25, 70, 710, 45), "Correct: they die. Wrong: you die. Choose carefully.", 19);
        labels.Clear();
        for (var i = 0; i < choices.Length; i++)
        {
            var rect = ChoiceRect(i); Box(rect, new Color(.13f, .18f, .24f));
            labels.Add(Text(rect, MeetingGuesses.Name(choices[i]), 20));
        }
        Box(new(25, 450, 165, 45), new Color(.16f, .21f, .27f)); Text(new(25, 450, 165, 45), "Cancel", 21);
        Box(new(210, 450, 525, 45), new Color(.43f, .10f, .13f)); confirm = Text(new(210, 450, 525, 45), "Select a role", 21);
    }
    [HideFromIl2Cpp] private static Rect ChoiceRect(int i) => new(25 + i % 3 * 240, 130 + i / 3 * 55, 230, 46);
    public void Update()
    {
        if (!root) return;
        if (!MeetingGuesses.CanTarget(PlayerControl.LocalPlayer, RoundState.Find(target)) || MeetingHud.Instance.NetId != meeting ||
            Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
        scale = Mathf.Min(Screen.width / 800f, Screen.height / 560f); canvas!.scaleFactor = scale;
        if (Time.frameCount == openedFrame || !Input.GetMouseButtonUp(0)) return;
        var point = new Vector2((Input.mousePosition.x - (Screen.width - 760 * scale) / 2) / scale,
            ((Screen.height + 520 * scale) / 2 - Input.mousePosition.y) / scale);
        if (new Rect(25, 450, 165, 45).Contains(point)) { Close(); return; }
        for (var i = 0; i < choices.Length; i++) if (ChoiceRect(i).Contains(point))
        {
            selected = i; confirm!.text = "Guess " + MeetingGuesses.Name(choices[i]);
            for (var j = 0; j < labels.Count; j++) labels[j].color = j == i ? Color.yellow : Color.white;
        }
        if (selected >= 0 && new Rect(210, 450, 525, 45).Contains(point))
        { var role = (ushort)choices[selected].Role; Close(); GuessRpc.Send(target, role); }
    }
    public void Close()
    { if (root) { Destroy(root); closedFrame = Time.frameCount; } root = null; labels.Clear(); }
    [HideFromIl2Cpp] private RectTransform Node(Rect rect)
    {
        var go = new GameObject("GuessItem"); var node = go.AddComponent<RectTransform>(); node.SetParent(panel!, false);
        node.anchorMin = node.anchorMax = node.pivot = new Vector2(0, 1);
        node.anchoredPosition = new(rect.x, -rect.y); node.sizeDelta = rect.size; return node;
    }
    [HideFromIl2Cpp] private void Box(Rect rect, Color color)
    { var image = Node(rect).gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false; }
    [HideFromIl2Cpp] private TextMeshProUGUI Text(Rect rect, string text, float size)
    {
        var label = Node(rect).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = HudManager.Instance.TaskPanel.taskText.font; label.fontSize = size; label.text = text;
        label.color = Color.white; label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false; return label;
    }
}
