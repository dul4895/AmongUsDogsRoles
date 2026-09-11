using MiraAPI.GameOptions;
using MiraAPI.Hud;
using MiraAPI.Keybinds;
using MiraAPI.PluginLoading;
using MiraAPI.Utilities;
using MiraAPI.Utilities.Assets;
using UnityEngine;

namespace AmongUsDogsRoles;

// Same typed CustomActionButton pattern used by TOU Mira, with one host request path.
[MiraIgnore]
public abstract class RoleButton : CustomActionButton
{
    private bool hudVisible = true;
    public abstract Ability Action { get; }
    public override bool CanClick() => !RoleGuide.BlocksControls && Button && Button!.isActiveAndEnabled && base.CanClick();
    public override ButtonLocation Location { get; set; } = ButtonLocation.BottomRight;
    public override BaseKeybind Keybind => MiraGlobalKeybinds.PrimaryAbility;
    public override void SetActive(bool visible, RoleBehaviour role)
    { hudVisible = visible; Button?.ToggleVisible(visible && Enabled(role) && ButtonPresentation.Relevant(Action)); }
    public override void FixedUpdateHandler(PlayerControl player)
    {
        ButtonPresentation.Sync(this, Action, player);
        base.FixedUpdateHandler(player);
        ButtonPresentation.Refresh(this, Action, player, hudVisible);
    }
    public override float Cooldown => RoundState.Cooldown(Action);
    public override float InitialCooldown => 10;
    public override bool PauseTimerInVent => true;
    public override LoadableAsset<Sprite> Sprite => Assets.For(Action);
    public override bool CanUse() => base.CanUse() && RoundState.CanAct(PlayerControl.LocalPlayer) && RoundState.Ready(PlayerControl.LocalPlayer.PlayerId, Action);
    protected override void OnClick() => AbilityRpc.Request(Action);
}
[MiraIgnore]
public abstract class TargetButton : CustomActionButton<PlayerControl>
{
    private bool hudVisible = true;
    public abstract Ability Action { get; }
    public override bool CanClick() => !RoleGuide.BlocksControls && Button && Button!.isActiveAndEnabled && base.CanClick();
    public override ButtonLocation Location { get; set; } = ButtonLocation.BottomRight;
    public override BaseKeybind Keybind => MiraGlobalKeybinds.PrimaryAbility;
    public override void SetActive(bool visible, RoleBehaviour role)
    {
        hudVisible = visible;
        var show = visible && Enabled(role) && ButtonPresentation.Relevant(Action);
        if (!show) { SetOutline(false); Target = null; }
        Button?.ToggleVisible(show);
    }
    public override void FixedUpdateHandler(PlayerControl player)
    {
        ButtonPresentation.Sync(this, Action, player);
        base.FixedUpdateHandler(player);
        ButtonPresentation.Refresh(this, Action, player, hudVisible);
    }
    public override float Cooldown => RoundState.Cooldown(Action);
    public override float InitialCooldown => 10;
    public override bool PauseTimerInVent => true;
    public override LoadableAsset<Sprite> Sprite => Assets.For(Action);
    public override bool CanUse() => base.CanUse() && RoundState.CanAct(PlayerControl.LocalPlayer) && RoundState.Ready(PlayerControl.LocalPlayer.PlayerId, Action);
    public override PlayerControl? GetTarget() => !RoundState.CanAct(PlayerControl.LocalPlayer) ? null : PlayerControl.AllPlayerControls.ToArray()
        .Where(p => AbilityService.InReach(PlayerControl.LocalPlayer, p, Action is Ability.Shoot or Ability.Investigate))
        .OrderBy(p => Vector2.Distance(PlayerControl.LocalPlayer.GetTruePosition(), p.GetTruePosition())).FirstOrDefault();
    public override void SetOutline(bool active)
    {
        // Unity objects can retain a managed wrapper after their renderer is
        // destroyed during meetings/round transitions. Test native lifetimes.
        var target = Target;
        if (target == null || !target || !target.cosmetics || target.cosmetics.currentBodySprite == null) return;
        var renderer = target.cosmetics.currentBodySprite.BodySprite;
        if (!renderer) return;
        renderer.UpdateOutline(active ? Color.yellow : null);
    }
    protected override void OnClick() { if (Target != null) AbilityRpc.Request(Action, Target.PlayerId); }
}
public sealed class KillButton : TargetButton
{
    public override BaseKeybind Keybind => VanillaKeybinding<global::KillButton>.Instance;
    public override string Name => "Kill";
    public override Ability Action => Ability.Kill;
    public override bool Enabled(RoleBehaviour? role) => role is ILightRole && role.IsImpostor;
    public override bool CanUse() => base.CanUse() && !RoundState.Drags.ContainsKey(PlayerControl.LocalPlayer.PlayerId);
}
public sealed class DragButton : TargetButton
{
    public override string Name => "Drag";
    public override Ability Action => Ability.Drag;
    public override bool Enabled(RoleBehaviour? role) => role is PenguinRole;
    public override bool CanUse() => base.CanUse() && !RoundState.Drags.ContainsKey(PlayerControl.LocalPlayer.PlayerId) && RoundState.Ready(PlayerControl.LocalPlayer.PlayerId, Ability.Kill);
}
public sealed class ExecuteButton : RoleButton
{
    public override BaseKeybind Keybind => VanillaKeybinding<global::KillButton>.Instance;
    public override string Name => "Execute";
    public override Ability Action => Ability.Execute;
    public override float InitialCooldown => 0;
    public override float Cooldown => 0;
    public override bool Enabled(RoleBehaviour? role) => role is PenguinRole;
    public override bool CanUse() => RoundState.CanAct(PlayerControl.LocalPlayer) && RoundState.Drags.ContainsKey(PlayerControl.LocalPlayer.PlayerId);
}
public sealed class ReleaseButton : RoleButton
{
    public override string Name => "Release";
    public override Ability Action => Ability.Release;
    public override float InitialCooldown => 0;
    public override float Cooldown => 0;
    public override bool Enabled(RoleBehaviour? role) => role is PenguinRole;
    public override bool CanUse() => base.CanUse() && RoundState.Drags.ContainsKey(PlayerControl.LocalPlayer.PlayerId);
    public override void FixedUpdateHandler(PlayerControl player)
    {
        base.FixedUpdateHandler(player);
        if (!Button) return;
        var button = Button!;
        var holding = button.isActiveAndEnabled && RoundState.InRound && RoundState.Alive(player) &&
            player.Data.Role is PenguinRole && RoundState.Drags.ContainsKey(player.PlayerId);
        if (!holding)
        {
            button.graphic.transform.localPosition = button.position;
            button.cooldownTimerText.gameObject.SetActive(false);
            button.SetCooldownFill(0);
            button.isCoolingDown = false;
            return;
        }

        var captive = RoundState.Drags[player.PlayerId];
        var remaining = Mathf.Max(0, captive.Expires - Time.time);
        // Use the same fill and icon jitter as vanilla vent/unshift warnings.
        // Vanilla starts jitter below 3s; keep the icon still until our final 2s.
        button.SetFillUp(remaining, Mathf.Max(.01f, captive.Duration));
        if (remaining >= 2 || remaining <= 0)
            button.graphic.transform.localPosition = button.position;
        button.cooldownTimerText.text = Mathf.CeilToInt(remaining).ToString(System.Globalization.CultureInfo.InvariantCulture);
        button.cooldownTimerText.gameObject.SetActive(remaining > 0);
        // This is a duration display, not an ability cooldown. Release must
        // remain clickable while the host's capture timer is running.
        button.isCoolingDown = false;
    }
}
public sealed class DetonateButton : RoleButton
{
    public override float InitialCooldown => RoundState.InitialCooldown(Ability.Detonate);
    public override BaseKeybind Keybind => MiraGlobalKeybinds.PrimaryAbility;
    public override string Name => "Explode";
    public override Ability Action => Ability.Detonate;
    public override bool Enabled(RoleBehaviour? role) => role is BomberRole;
}
public sealed class InvestigateButton : TargetButton
{
    public override string Name => "Investigate";
    public override Ability Action => Ability.Investigate;
    public override bool Enabled(RoleBehaviour? role) => role is ConsigliereRole;
}
public sealed class MarkButton : RoleButton
{
    public override string Name => "Mark";
    public override Ability Action => Ability.Mark;
    public override float Cooldown => RoleTuning.RecallCooldown;
    public override bool Enabled(RoleBehaviour? role) => role is EscapistRole;
    public override bool CanUse() => base.CanUse() && !RoundState.Marks.ContainsKey(PlayerControl.LocalPlayer.PlayerId);
}
public sealed class RecallButton : RoleButton
{
    public override string Name => "Recall";
    public override Ability Action => Ability.Recall;
    public override float InitialCooldown => 0;
    public override bool Enabled(RoleBehaviour? role) => role is EscapistRole;
    public override bool CanUse() => base.CanUse() && RoundState.Marks.ContainsKey(PlayerControl.LocalPlayer.PlayerId);
}
public sealed class FakeButton : RoleButton
{
    public override string Name => "Fake";
    public override Ability Action => Ability.Fake;
    public override int MaxUses => 1;
    public override bool Enabled(RoleBehaviour? role) => role is FakerRole;
    public override bool CanUse() => base.CanUse() && !FakerState.Used.Contains(PlayerControl.LocalPlayer.PlayerId);
}
public sealed class UnfakeButton : RoleButton
{
    // Fake refreshes the host HUD synchronously. Both actions share one key,
    // so the newly visible Unfake must not consume that same key dispatch.
    public override bool CanClick() => Time.frameCount != FakerState.LocalFakeFrame && base.CanClick();
    public override string Name => "Unfake";
    public override Ability Action => Ability.Unfake;
    public override float InitialCooldown => 0;
    public override float Cooldown => 0;
    public override bool Enabled(RoleBehaviour? role) => role is FakerRole;
    public override bool CanUse() => FakerState.CanUnfake(PlayerControl.LocalPlayer);
}
public sealed class AlertButton : RoleButton
{
    public override string Name => "Alert";
    public override Ability Action => Ability.Alert;
    public override float EffectDuration => RoleTuning.AlertDuration;
    public override int MaxUses => RoleTuning.AlertUses;
    public override bool Enabled(RoleBehaviour? role) => role is VeteranRole;
}
public sealed class ShootButton : TargetButton
{
    public override BaseKeybind Keybind => VanillaKeybinding<global::KillButton>.Instance;
    public override string Name => "Shoot";
    public override Ability Action => Ability.Shoot;
    public override bool Enabled(RoleBehaviour? role) => role is SheriffRole;
}
public sealed class ExamineButton : CustomActionButton<DeadBody>
{
    public override bool CanClick() => !RoleGuide.BlocksControls && Button && Button!.isActiveAndEnabled && base.CanClick();
    private bool hudVisible = true;
    public override ButtonLocation Location { get; set; } = ButtonLocation.BottomRight;
    public override BaseKeybind Keybind => MiraGlobalKeybinds.PrimaryAbility;
    public override void SetActive(bool visible, RoleBehaviour role)
    { hudVisible = visible; base.SetActive(visible, role); }
    public override void FixedUpdateHandler(PlayerControl player)
    {
        ButtonPresentation.Sync(this, Ability.Examine, player);
        base.FixedUpdateHandler(player);
        ButtonPresentation.Refresh(this, Ability.Examine, player, hudVisible);
    }
    public override string Name => "Sniff";
    public override float Cooldown => RoundState.Cooldown(Ability.Examine);
    public override float InitialCooldown => 10;
    public override LoadableAsset<Sprite> Sprite => Assets.For(Ability.Examine);
    public override bool Enabled(RoleBehaviour? role) => role is CoronerRole;
    public override bool CanUse() => base.CanUse() && RoundState.CanAct(PlayerControl.LocalPlayer) && RoundState.Ready(PlayerControl.LocalPlayer.PlayerId, Ability.Examine);
    public override DeadBody? GetTarget() => !RoundState.CanAct(PlayerControl.LocalPlayer) ? null : UnityEngine.Object.FindObjectsOfType<DeadBody>().ToArray()
        .Where(b => !b.Reported && Vector2.Distance(PlayerControl.LocalPlayer.GetTruePosition(), b.TruePosition) <= PlayerControl.LocalPlayer.MaxReportDistance &&
            !PhysicsHelpers.AnythingBetween(PlayerControl.LocalPlayer.GetTruePosition(), b.TruePosition, Constants.ShipAndObjectsMask, false))
        .OrderBy(b => Vector2.Distance(PlayerControl.LocalPlayer.GetTruePosition(), b.TruePosition)).FirstOrDefault();
    public override void SetOutline(bool active) { }
    protected override void OnClick() { if (Target != null) AbilityRpc.Request(Ability.Examine, Target.ParentId); }
}

public static class Assets
{
    private static readonly Dictionary<string, LoadableAsset<Sprite>> Cache = new();
    private sealed class VanillaKillAsset : LoadableAsset<Sprite>
    { public override Sprite LoadAsset() => HudManager.Instance.KillButton.graphic.sprite; }
    private static readonly LoadableAsset<Sprite> VanillaKill = new VanillaKillAsset();
    public static LoadableAsset<Sprite> Art(string name, float pixelsPerUnit = 100)
    {
        if (!Cache.TryGetValue(name, out var sprite)) Cache[name] = sprite = new LoadableResourceAsset($"AmongUsDogsRoles.Resources.Abilities.{name}.png", pixelsPerUnit);
        return sprite;
    }
    public static LoadableAsset<Sprite> For(Ability a)
    {
        if (a == Ability.Kill) return VanillaKill;
        var name = a switch
        {
            Ability.Shoot => "Shoot",
            Ability.Detonate => "Explode",
            Ability.Execute => "Execute",
            Ability.Alert => "Alert",
            Ability.Mark => "Mark",
            Ability.Recall => "Recall",
            Ability.Release => "Release",
            Ability.Drag => "Capture",
            Ability.Examine => "Sniff",
            Ability.Fake => "Fake",
            Ability.Unfake => "Unfake",
            _ => "Investigate",
        };
        return Art(name);
    }
}

public static class ButtonPresentation
{
    public static bool Relevant(Ability ability)
    {
        var player = PlayerControl.LocalPlayer;
        if (ability == Ability.Unfake) return FakerState.IsFaking(player);
        if (!RoundState.Alive(player)) return false;
        var dragging = RoundState.Drags.ContainsKey(player.PlayerId);
        return ability switch
        {
            Ability.Kill or Ability.Drag => !dragging,
            Ability.Execute or Ability.Release => dragging,
            Ability.Mark => !RoundState.Marks.ContainsKey(player.PlayerId),
            Ability.Recall => RoundState.Marks.ContainsKey(player.PlayerId),
            Ability.Fake => !FakerState.Used.Contains(player.PlayerId),
            _ => true,
        };
    }

    public static void Sync(CustomActionButton button, Ability ability, PlayerControl player)
    {
        // Display the accepted host state, including changes initiated remotely.
        var alerting = ability == Ability.Alert && RoundState.Alerting(player);
        button.EffectActive = alerting;
        button.Timer = alerting ? Mathf.Max(0, RoundState.Alerts[player.PlayerId] - Time.time) :
            ability is Ability.Execute or Ability.Release or Ability.Unfake ? 0 :
            Mathf.Max(0, RoundState.ReadyAt.GetValueOrDefault((player.PlayerId, RoundState.Slot(ability))) - Time.time);
        if (ability == Ability.Drag)
            button.Timer = Mathf.Max(button.Timer, RoundState.ReadyAt.GetValueOrDefault((player.PlayerId, Ability.Kill)) - Time.time);
        if (ability == Ability.Alert) button.UsesLeft = Math.Max(0, RoleTuning.AlertUses - RoundState.AlertsUsed.GetValueOrDefault(player.PlayerId));
        if (ability == Ability.Fake) button.UsesLeft = FakerState.Used.Contains(player.PlayerId) ? 0 : 1;
    }

    public static void Refresh(CustomActionButton button, Ability ability, PlayerControl player, bool hudVisible)
    {
        if (!button.Button) return;
        button.Button!.ToggleVisible((hudVisible || ability == Ability.Unfake && player.Data.Role is FakerRole) && button.Enabled(player.Data.Role) && RoundState.InRound && Relevant(ability));
        if (button.MaxUses <= 0)
        {
            button.Button.usesRemainingText.gameObject.SetActive(false);
            button.Button.usesRemainingSprite.gameObject.SetActive(false);
        }
        else button.Button.SetUsesRemaining(button.UsesLeft);
        if (ability == Ability.Alert) button.OverrideName(RoundState.Alerting(player) ? "Alerting" : "Alert");
    }
}
