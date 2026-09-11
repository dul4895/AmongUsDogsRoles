using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AmongUsDogsRoles;

public sealed record RoleGuideEntry(string Name, string Team, string Color, string Icon, string Summary, string Details);

// Public information only: the guide never reads anyone's assigned role.
public static class RoleGuideCatalog
{
    public static readonly RoleGuideEntry[] Entries =
    [
        new(RoleNames.Kidnapper, "IMPOSTOR", "64AFE1", "Capture", "Take someone with you.",
            "<b>Drag</b> captures a nearby non-impostor and pulls them behind you. Everyone nearby can see the captive and chain.\n\n<b>Execute</b> kills your captive. <b>Release</b> lets them go; they also escape when the capture timer runs out.\n\nYou cannot vent while dragging. When you aren't holding someone, you can kill normally."),
        new(RoleNames.Kamikaze, "IMPOSTOR", "F06E23", "Explode", "Go out with a bang.",
            "<b>Explode</b> kills you and every living player within the blast radius, including fellow impostors. Nearby players see and hear the explosion.\n\nAn alert Veteran is not protected from the blast. If the explosion leaves nobody alive, the match ends in a <b>draw</b>.\n\nYou can also kill normally."),
        new("Consigliere", "IMPOSTOR", "E0808E", "Investigate", "Find out who you're dealing with.",
            "<b>Investigate</b> a nearby player to learn their exact role. Only you see the result.\n\nInvestigating an alert Veteran kills you and blocks the investigation.\n\nYou can also kill normally."),
        new("Escapist", "IMPOSTOR", "64BE82", "Mark", "Plan your escape.",
            "<b>Mark</b> saves your current position. <b>Recall</b> teleports you back to that spot, even after making a kill.\n\nRecalling uses up the mark. Meetings clear unused marks.\n\nYou can also kill normally."),
        new("Faker", "IMPOSTOR", "B69ADB", "Fake", "Play dead. Choose your moment.",
            "<b>Fake</b> your death once per game. Your body can be reported. You count as dead, cannot vote, move, kill or sabotage, and see only the spot where you faked.\n\n<b>Unfake</b> outside meetings to return alive at that spot. You can kill, sabotage and vote again.\n\nA Coroner sniff points to your fake body.\n<b>CAUTION:</b> if the faker is the last imposter left and uses his fake and a meeting starts while he is faked, imposters lose immediately"),
        new("Veteran", "CREWMATE", "CBA568", "Alert", "Make attackers think twice.",
            "<b>Alert</b> briefly protects you from direct abilities. A player who tries to kill, shoot, capture or investigate you dies, and their action is blocked.\n\nAlerts have limited uses and a cooldown. They do not protect you from a Kamikaze's explosion.\n\nComplete your tasks and help vote out the impostors."),
        new("Sheriff", "CREWMATE", "F5D246", "Shoot", "Choose your shot carefully.",
            "<b>Shoot</b> a nearby player. Hitting an impostor or the Jester kills them.\n\nShooting a crewmate kills only you. An alert Veteran also retaliates against your shot.\n\nComplete your tasks and help vote out the impostors."),
        new("Coroner", "CREWMATE", "6ED7D7", "Sniff", "Follow the evidence.",
            "<b>Sniff</b> a nearby body to get a private arrow pointing to its killer. The trail lasts until the next meeting or the killer dies.\n\nA Faker's fake body points back to that body; the trail clears when they unfake.\n\nYou <b>cannot report bodies</b>, but you can call emergency meetings. Complete your tasks and share what you discover."),
        new("Jester", "NEUTRAL", "F087C8", "", "Make them vote you out.",
            "Get <b>voted out</b> to win the match alone. Being killed, a tied vote or a skip does not give you a win.\n\nYour task list is fake: you cannot complete tasks and they do not count toward task progress.\n\nYou count with the crew for the impostor win threshold, but do not share a normal crew or impostor victory.")
    ];
}

// A screen-space canvas survives HUD replacements, meetings and ghost mode.
// Pointer handling is kept here because Among Us uses its own PassiveButton
// system, rather than Unity UI's EventSystem.
public sealed class RoleGuide(IntPtr pointer) : MonoBehaviour(pointer)
{
    public static RoleGuide? Instance;
    public bool IsOpen { get; private set; }
    public bool Visible { get; private set; }
    public int Selected { get; private set; }
    private Canvas? canvas;
    private RectTransform? layout, panel, toggle;
    private TextMeshProUGUI? toggleText, title, team, summary, details, footer;
    private Image? icon;
    private TMP_FontAsset? font;
    private Sprite? rounded;
    private readonly List<Image> rows = [];
    private float scale = 1, width = 1000, height = 625;
    private string phase = "";
    private int closedFrame = -1;
    private Rect ToggleBounds => new(width - 420, 12, 132, 36);
    private Rect PanelBounds => new((width - 840) / 2, (height - 510) / 2 + 18, 840, 510);
    public static bool Reading => Instance && Instance!.Visible && Instance.IsOpen;
    public static bool BlocksControls => Reading || (Instance && Instance!.closedFrame == Time.frameCount);
    public static bool BlocksPointer => BlocksControls || (Instance && Instance!.Visible && Instance.ToggleBounds.Contains(Instance.MousePosition));
    private Vector2 MousePosition => new(Input.mousePosition.x / scale, (Screen.height - Input.mousePosition.y) / scale);

    public void Awake() => Instance = this;

    public void Update()
    {
        var client = AmongUsClient.Instance;
        var resultsScreen = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "EndGame";
        Visible = client && (client.AmConnected || client.NetworkMode == NetworkModes.FreePlay ||
            resultsScreen) && (PlayerControl.LocalPlayer || resultsScreen);
        if (!Visible) { Close(); if (canvas) canvas!.gameObject.SetActive(false); return; }
        if (!canvas)
        {
            // Connection precedes player/HUD initialization. Never access the
            // HUD's lazily created singleton before the local player exists.
            var hud = PlayerControl.LocalPlayer ? HudManager.Instance : null;
            font = hud && hud!.TaskPanel && hud.TaskPanel.taskText ? hud.TaskPanel.taskText.font :
                Object.FindObjectsOfType<TextMeshPro>().FirstOrDefault(t => t.font)?.font;
            if (!font) return;
            Build();
        }
        canvas!.gameObject.SetActive(true);
        scale = Mathf.Min(Screen.width / 1000f, Screen.height / 625f);
        width = Screen.width / scale; height = Screen.height / scale;
        canvas.scaleFactor = scale;
        layout!.sizeDelta = new(width, height);
        Place(toggle!, ToggleBounds); Place(panel!, PanelBounds);
        var nextPhase = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + ":" + client.IsGameStarted + ":" + (bool)MeetingHud.Instance + ":" + (bool)ExileController.Instance;
        if (phase != nextPhase) { Close(); phase = nextPhase; }
        if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Close();
        // Process on release, so the press and release are both intercepted and
        // cannot click a vote, report or other control underneath the guide.
        if (Input.GetMouseButtonUp(0)) Click(MousePosition.x, MousePosition.y);
        for (var i = 0; i < rows.Count; i++)
            rows[i].color = i == Selected ? new Color(.19f, .29f, .35f) :
                RowBounds(i).Contains(MousePosition) ? new Color(.15f, .2f, .25f) : new Color(.08f, .115f, .16f);
    }

    [HideFromIl2Cpp] public void Click(float x, float y)
    {
        if (!Visible || !canvas) return;
        var point = new Vector2(x, y);
        if (ToggleBounds.Contains(point)) { if (IsOpen) Close(); else Open(); return; }
        if (!IsOpen) return;
        var p = PanelBounds;
        if (new Rect(p.x + 758, p.y + 18, 60, 34).Contains(point)) { Close(); return; }
        for (var i = 0; i < rows.Count; i++) if (RowBounds(i).Contains(point)) { Select(i); return; }
    }

    [HideFromIl2Cpp] private Rect RowBounds(int i) => new(PanelBounds.x + 22, PanelBounds.y + 112 + i * 37, 202, 33);

    public void Open()
    {
        if (!Visible || !canvas) return;
        IsOpen = true; panel!.gameObject.SetActive(true); toggleText!.text = "Close roles";
        // Stop existing movement without changing moveable or networking state.
        if (PlayerControl.LocalPlayer && PlayerControl.LocalPlayer.MyPhysics)
            PlayerControl.LocalPlayer.MyPhysics.body.velocity = Vector2.zero;
    }

    public void Close()
    {
        if (IsOpen) closedFrame = Time.frameCount;
        IsOpen = false;
        if (panel) panel!.gameObject.SetActive(false);
        if (toggleText) toggleText!.text = "New roles";
    }

    public void Select(int index)
    {
        if (index < 0 || index >= RoleGuideCatalog.Entries.Length || !title) return;
        Selected = index;
        var entry = RoleGuideCatalog.Entries[index];
        title!.text = entry.Name; title.color = Parse(entry.Color);
        team!.text = entry.Team; team.color = entry.Team == "IMPOSTOR" ? Parse("FF8686") : entry.Team == "CREWMATE" ? Parse("80C8FF") : Parse("F087C8");
        summary!.text = entry.Summary; details!.text = entry.Details;
        footer!.text = entry.Team == "IMPOSTOR" ? "Impostor team  ·  Normal kill, vent and sabotage rules apply unless noted above." :
            entry.Team == "CREWMATE" ? "Crewmate team  ·  Win through tasks or by eliminating the impostors." : "Neutral  ·  Your own objective, your own victory.";
        icon!.gameObject.SetActive(entry.Icon.Length > 0);
        if (entry.Icon.Length > 0) icon.sprite = Assets.Art(entry.Icon).LoadAsset();
    }

    [HideFromIl2Cpp] private void Build()
    {
        var root = new GameObject("AmongUsDogsRolesRoleGuide");
        root.transform.SetParent(transform, false);
        canvas = root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32000;
        layout = Node("Layout", root.transform, new(0, 0, width, height));
        toggle = Box("Toggle", layout, ToggleBounds, new Color(.08f, .115f, .16f), true);
        toggleText = Text("ToggleText", toggle, new(0, 0, 132, 36), "New roles", 21, TextAlignmentOptions.Center);
        panel = Box("Panel", layout, PanelBounds, new Color(.055f, .075f, .11f, .985f), true);
        Text("Heading", panel, new(22, 17, 500, 38), "New roles", 30);
        Text("Subtitle", panel, new(24, 57, 690, 28), "Same game. A few new tricks. Select a role to learn how it works.", 18).color = Parse("AFBECE");
        var close = Box("Close", panel, new(758, 18, 60, 34), new Color(.17f, .21f, .27f));
        Text("CloseText", close, new(0, 0, 60, 34), "Close", 18, TextAlignmentOptions.Center);
        Box("Divider", panel, new(243, 111, 1, 333), new Color(.25f, .31f, .38f));
        for (var i = 0; i < RoleGuideCatalog.Entries.Length; i++)
        {
            var entry = RoleGuideCatalog.Entries[i];
            var row = Box("Role-" + entry.Name, panel, new(22, 112 + i * 37, 202, 33), Color.white);
            rows.Add(row.GetComponent<Image>());
            Box("Accent", row, new(0, 6, 3, 21), Parse(entry.Color));
            Text("Name", row, new(14, 0, 181, 33), entry.Name, 21);
        }
        team = Text("Team", panel, new(266, 108, 460, 24), "", 17);
        title = Text("RoleTitle", panel, new(264, 132, 454, 46), "", 34);
        summary = Text("Summary", panel, new(266, 181, 548, 30), "", 20); summary.color = Parse("AFBECE");
        details = Text("Details", panel, new(266, 223, 548, 227), "", 20);
        details.alignment = TextAlignmentOptions.TopLeft;
        details.enableAutoSizing = true; details.fontSizeMin = 18; details.fontSizeMax = 20;
        icon = Node("AbilityIcon", panel, new(744, 107, 65, 65)).gameObject.AddComponent<Image>(); icon.preserveAspect = true;
        footer = Text("TeamRules", panel, new(24, 461, 791, 18), "", 15); footer.color = Parse("AFBECE");
        Text("ReadingHint", panel, new(24, 484, 791, 18), "The match continues while you read.  ·  Esc to close", 15).color = Parse("AFBECE");
        Select(Selected); Close();
    }

    [HideFromIl2Cpp] private static RectTransform Node(string name, Transform parent, Rect rect)
    {
        var node = new GameObject(name).AddComponent<RectTransform>(); node.SetParent(parent, false);
        node.anchorMin = node.anchorMax = node.pivot = new Vector2(0, 1); Place(node, rect); return node;
    }
    [HideFromIl2Cpp] private static void Place(RectTransform node, Rect rect) { node.anchoredPosition = new(rect.x, -rect.y); node.sizeDelta = rect.size; }
    [HideFromIl2Cpp] private RectTransform Box(string name, Transform parent, Rect rect, Color color, bool border = false)
    {
        var node = Node(name, parent, rect);
        var image = node.gameObject.AddComponent<Image>(); image.sprite = Rounded(); image.type = Image.Type.Sliced; image.raycastTarget = false;
        image.color = border ? Parse("A4B4C2") : color;
        if (border) Box("Fill", node, new(2, 2, rect.width - 4, rect.height - 4), color);
        return node;
    }
    [HideFromIl2Cpp] private TextMeshProUGUI Text(string name, Transform parent, Rect rect, string value, float size, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
    {
        var label = Node(name, parent, rect).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font; label.fontSize = size; label.text = value; label.color = Color.white;
        label.alignment = alignment; label.enableWordWrapping = true; label.raycastTarget = false;
        return label;
    }
    [HideFromIl2Cpp] private Sprite Rounded()
    {
        if (rounded) return rounded!;
        var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++)
        {
            var dx = Mathf.Max(7.5f - x, x - 23.5f, 0); var dy = Mathf.Max(7.5f - y, y - 23.5f, 0);
            texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(8 - Mathf.Sqrt(dx * dx + dy * dy))));
        }
        texture.Apply();
        rounded = Sprite.Create(texture, new(0, 0, 32, 32), new(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new(9, 9, 9, 9));
        rounded.hideFlags = HideFlags.HideAndDontSave; return rounded;
    }
    [HideFromIl2Cpp] private static Color Parse(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var color); return color; }
}

[HarmonyPatch(typeof(PassiveButtonManager), nameof(PassiveButtonManager.Update))]
public static class GuidePointerPatch { public static bool Prefix() => !RoleGuide.BlocksPointer; }

[HarmonyPatch(typeof(KeyboardJoystick), nameof(KeyboardJoystick.HandleHud))]
public static class GuideHotkeyPatch { public static bool Prefix() => !RoleGuide.BlocksControls; }

[HarmonyPatch(typeof(ControllerManager), nameof(ControllerManager.Update))]
public static class GuideControllerPatch { public static bool Prefix() => !RoleGuide.BlocksControls; }

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CanMove), MethodType.Getter)]
public static class GuideMovementPatch
{
    public static void Postfix(PlayerControl __instance, ref bool __result)
    { if (__instance.AmOwner && RoleGuide.BlocksControls) __result = false; }
}
