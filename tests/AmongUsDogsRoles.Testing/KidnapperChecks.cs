using System.Text.Json;
using AmongUs.GameOptions;
using MiraAPI.Hud;
using MiraAPI.Roles;
using UnityEngine;

namespace AmongUsDogsRoles.Testing;

public static class KidnapperChecks
{
    public static bool Execute(JsonElement d)
    {
        switch (d.GetProperty("command").GetString())
        {
            case "raw-role":
                if (!AmongUsClient.Instance.AmHost) throw new InvalidOperationException("Host only");
                var name = d.GetProperty("role").GetString()!;
                var type = typeof(Plugin).Assembly.GetType("AmongUsDogsRoles." + name + "Role");
                TestRpc.Send(new(TestCommand.RawRole, d.GetProperty("player").GetByte(),
                    type != null ? RoleId.Get(type) : (ushort)Enum.Parse<RoleTypes>(name)));
                return true;
            case "click-drag":
                // Actual mouse button callback, including Mira's click events.
                CustomButtonManager.Buttons.OfType<DragButton>().Single().Button!
                    .GetComponent<PassiveButton>().OnClick.Invoke();
                return true;
            case "hud":
                var me = PlayerControl.LocalPlayer;
                HudManager.Instance.SetHudActive(me, me.Data.Role, d.GetProperty("visible").GetBoolean());
                return true;
            case "button-role-disabled":
                // Framework's FixedUpdate role-mismatch path, independent of HUD visibility.
                CustomButtonManager.Buttons.OfType<DragButton>().Single().SetActive(false,
                    RoleManager.Instance.GetRole(RoleTypes.Crewmate));
                return true;
            case "capture-cooldown":
                if (!AmongUsClient.Instance.AmHost) throw new InvalidOperationException("Host only");
                RoundState.Consume(RoundState.Find(d.GetProperty("player").GetByte())!,
                    Enum.Parse<Ability>(d.GetProperty("ability").GetString()!), d.GetProperty("seconds").GetSingle());
                return true;
            case "kill-distance":
                GameOptionsManager.Instance.currentNormalGameOptions.KillDistance = d.GetProperty("value").GetInt32();
                GameManager.Instance.LogicOptions.SyncOptions();
                return true;
            default: return false;
        }
    }
}
