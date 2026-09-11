using MiraAPI.GameOptions;
using MiraAPI.GameOptions.Attributes;
using MiraAPI.Utilities;
using MiraAPI.Roles;
using MiraAPI.PluginLoading;

namespace AmongUsDogsRoles;

public sealed class RoleSetupOptions : AbstractOptionGroup
{
    public override string GroupName => "Extra roles";
    public override uint GroupPriority => 0;
    [ModdedToggleOption("Customize abilities")]
    public bool Customize { get; set; } = false;
}

[MiraIgnore]
public abstract class AdvancedRoleOptions<T> : AbstractRoleOptionGroup<T> where T : ICustomRole
{
    public override Func<bool> GroupVisible => () => RoleTuning.Custom;
}

// Basic setup uses known defaults; switching it back on cannot leave hidden
// custom values affecting the game. Advanced values remain saved for later.
public static class RoleTuning
{
    public static bool Custom => OptionGroupSingleton<RoleSetupOptions>.Instance.Customize;
    public static float DragCooldown => Custom ? OptionGroupSingleton<PenguinOptions>.Instance.Cooldown : 25;
    public static float DragDuration => Custom ? OptionGroupSingleton<PenguinOptions>.Instance.Duration : 10;
    public static float BombCooldown => Custom ? OptionGroupSingleton<BomberOptions>.Instance.Cooldown : 25;
    public static float BombRadius => Custom ? OptionGroupSingleton<BomberOptions>.Instance.Radius : 2.5f;
    public static float InvestigateCooldown => Custom ? OptionGroupSingleton<ConsigliereOptions>.Instance.Cooldown : 20;
    public static float RecallCooldown => Custom ? OptionGroupSingleton<EscapistOptions>.Instance.Cooldown : 25;
    public static float AlertCooldown => Custom ? OptionGroupSingleton<VeteranOptions>.Instance.Cooldown : 25;
    public static float AlertDuration => Custom ? OptionGroupSingleton<VeteranOptions>.Instance.Duration : 5;
    public static int AlertUses => Custom ? (int)OptionGroupSingleton<VeteranOptions>.Instance.Uses : 3;
    public static float ShootCooldown => Custom ? OptionGroupSingleton<SheriffOptions>.Instance.Cooldown : 25;
    public static float ExamineCooldown => Custom ? OptionGroupSingleton<CoronerOptions>.Instance.Cooldown : 5;
}

public sealed class PenguinOptions : AdvancedRoleOptions<PenguinRole>
{
    public override string GroupName => RoleNames.Kidnapper;
    [ModdedNumberOption("Drag cooldown", 5, 60, 2.5f, MiraNumberSuffixes.Seconds)]
    public float Cooldown { get; set; } = 25;
    [ModdedNumberOption("Maximum drag duration", 5, 30, 1, MiraNumberSuffixes.Seconds)]
    public float Duration { get; set; } = 10;
}
public sealed class BomberOptions : AdvancedRoleOptions<BomberRole>
{
    public override string GroupName => RoleNames.Kamikaze;
    [ModdedNumberOption("Explode cooldown", 5, 60, 2.5f, MiraNumberSuffixes.Seconds)]
    public float Cooldown { get; set; } = 25;
    [ModdedNumberOption("Blast radius", 0.5f, 5, 0.25f)]
    public float Radius { get; set; } = 2.5f;
}
public sealed class ConsigliereOptions : AdvancedRoleOptions<ConsigliereRole>
{
    public override string GroupName => "Consigliere";
    [ModdedNumberOption("Investigate cooldown", 5, 60, 2.5f, MiraNumberSuffixes.Seconds)]
    public float Cooldown { get; set; } = 20;
}
public sealed class EscapistOptions : AdvancedRoleOptions<EscapistRole>
{
    public override string GroupName => "Escapist";
    [ModdedNumberOption("Recall cooldown", 5, 60, 2.5f, MiraNumberSuffixes.Seconds)]
    public float Cooldown { get; set; } = 25;
}
public sealed class VeteranOptions : AdvancedRoleOptions<VeteranRole>
{
    public override string GroupName => "Veteran";
    [ModdedNumberOption("Alert cooldown", 5, 60, 2.5f, MiraNumberSuffixes.Seconds)]
    public float Cooldown { get; set; } = 25;
    [ModdedNumberOption("Alert duration", 2, 15, 1, MiraNumberSuffixes.Seconds)]
    public float Duration { get; set; } = 5;
    [ModdedNumberOption("Alerts per game", 1, 10, 1)]
    public float Uses { get; set; } = 3;
}
public sealed class SheriffOptions : AdvancedRoleOptions<SheriffRole>
{
    public override string GroupName => "Sheriff";
    [ModdedNumberOption("Shoot cooldown", 5, 60, 2.5f, MiraNumberSuffixes.Seconds)]
    public float Cooldown { get; set; } = 25;
}
public sealed class CoronerOptions : AdvancedRoleOptions<CoronerRole>
{
    public override string GroupName => "Coroner";
    [ModdedNumberOption("Sniff cooldown", 2.5f, 30, 2.5f, MiraNumberSuffixes.Seconds)]
    public float Cooldown { get; set; } = 5;
}
