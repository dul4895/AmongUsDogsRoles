using System.Text.Json;
using TMPro;
using UnityEngine;

namespace AmongUsDogsRoles.Testing;

public static class RoleGuideChecks
{
    public static object Snapshot()
    {
        var guide = RoleGuide.Instance;
        return new {
            visible = guide && guide!.Visible, open = guide && guide!.IsOpen,
            selected = guide ? guide!.Selected : -1,
            blocksControls = RoleGuide.BlocksControls,
            width = Screen.width, height = Screen.height,
            roots = UnityEngine.Object.FindObjectsOfType<Canvas>().Count(c => c.name == "AmongUsDogsRolesRoleGuide"),
            labels = guide ? guide!.GetComponentsInChildren<TextMeshProUGUI>(true).Select(t => new {
                name = t.name, text = t.text, active = t.gameObject.activeInHierarchy,
                overflow = t.isTextOverflowing, fontSize = t.fontSize,
                x = t.transform.position.x, y = t.transform.position.y,
                w = t.rectTransform.rect.width, h = t.rectTransform.rect.height
            }).ToArray() : null
        };
    }

    public static bool Execute(JsonElement command)
    {
        var op = command.GetProperty("command").GetString();
        if (op == "resolution") {
            Screen.SetResolution(command.GetProperty("width").GetInt32(), command.GetProperty("height").GetInt32(), false);
            return true;
        }
        if (op != "guide") return false;
        var guide = RoleGuide.Instance;
        if (!guide || !guide!.Visible) throw new InvalidOperationException("Role guide is unavailable");
        var target = command.GetProperty("target").GetString();
        var element = guide.GetComponentsInChildren<RectTransform>(true).First(t => t.name == target);
        if (!element.gameObject.activeInHierarchy) throw new InvalidOperationException("Guide target is hidden");
        var screen = RectTransformUtility.WorldToScreenPoint(null, element.TransformPoint(element.rect.center));
        var scale = Mathf.Min(Screen.width / 1000f, Screen.height / 625f);
        guide.Click(screen.x / scale, (Screen.height - screen.y) / scale);
        return true;
    }
}
