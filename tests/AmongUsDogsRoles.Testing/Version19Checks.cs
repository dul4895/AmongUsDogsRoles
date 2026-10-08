using System.Text.Json;
using AmongUs.GameOptions;
using MiraAPI.Roles;
using UnityEngine;

namespace AmongUsDogsRoles.Testing;

// Exercises the real v19 ghost role (internally named SpiritGuide).
public static class Version19Checks
{
    public static object Snapshot()
    {
        // Singleton accessors can create empty managers during SplashIntro.
        // Wait for the game to instantiate its configured scene prefabs.
        if (!RoleManager.InstanceExists || !HudManager.InstanceExists)
            return new { ready = false };
        var me = PlayerControl.LocalPlayer;
        var role = me && me.Data?.Role != null ? me.Data.Role.TryCast<SpiritGuideRole>() : null;
        var feed = UnityEngine.Object.FindObjectOfType<SocialMediumFeedSystem>();
        return new
        {
            process64 = Environment.Is64BitProcess,
            influencerRegistered = RoleManager.Instance && RoleManager.Instance.AllRoles.ToArray().Any(r => r.Role == RoleTypes.SpiritGuide),
            influencerGhost = RoleManager.IsGhostRole(RoleTypes.SpiritGuide),
            influencerGuessable = RoleManager.Instance && GameOptionsManager.Instance?.CurrentGameOptions != null && MeetingGuesses.Choices().Any(r => r.Role == RoleTypes.SpiritGuide),
            localInfluencer = (bool)role,
            panelOpen = role && role!.spiritGuidePanel && role.spiritGuidePanel.activeInHierarchy,
            imageChoices = role?.imageButtons?.Count ?? 0,
            queuedMessages = feed ? feed.messageFeed.Count : 0,
            activeMessages = feed ? feed.activeMessages.Count : 0,
            abilityVisible = HudManager.Instance && HudManager.Instance.AbilityButton.isActiveAndEnabled,
            roleRates = GameOptionsManager.Instance?.CurrentGameOptions?.RoleOptions is { } options
                ? CustomRoleManager.CustomMiraRoles.Where(r => r.GetType().Assembly == typeof(Plugin).Assembly)
                    .Select(r => new { name = r.GetType().Name, id = (int)((RoleBehaviour)r).Role,
                        count = options.GetNumPerGame(((RoleBehaviour)r).Role), chance = options.GetChancePerGame(((RoleBehaviour)r).Role),
                        configuredCount = r.GetCount(), configuredChance = r.GetChance() }).ToArray() : null,
        };
    }

    public static bool Execute(JsonElement data)
    {
        switch (data.GetProperty("command").GetString())
        {
            case "v19-name-color-null":
                var color = Color.red;
                if (MiraAPI.Patches.Roles.NameTagPatch.GetPatch(null!, ref color) || color != Color.white)
                    throw new InvalidOperationException("Uninitialized role must use the safe white fallback");
                return true;
            case "v19-ghost-options":
                if (!AmongUsClient.Instance.AmHost || AmongUsClient.Instance.IsGameStarted)
                    throw new InvalidOperationException("Host lobby only");
                var options = GameOptionsManager.Instance.currentNormalGameOptions.RoleOptions;
                options.SetRoleRate(RoleTypes.GuardianAngel, 0, 0);
                options.SetRoleRate(RoleTypes.SpiritGuide, 15, 100);
                GameManager.Instance.LogicOptions.SyncOptions();
                return true;
            case "v19-influencer-open":
                var local = PlayerControl.LocalPlayer;
                var influencer = local.Data.Role.TryCast<SpiritGuideRole>();
                if (!local.Data.IsDead || influencer == null || !influencer)
                    throw new InvalidOperationException("A naturally assigned dead Influencer is required");
                influencer.cooldownSecondsRemaining = 0;
                influencer.SetPlayerTarget(RoundState.Find(data.GetProperty("target").GetByte()));
                influencer.UseAbility();
                return true;
            case "v19-influencer-send":
                var sender = PlayerControl.LocalPlayer.Data.Role.TryCast<SpiritGuideRole>();
                if (sender == null || !sender || !sender.spiritGuidePanel.activeInHierarchy || sender.imageButtons.Count == 0)
                    throw new InvalidOperationException("Open the Influencer's image panel first");
                sender.SelectImage(sender.imageButtons[0]);
                sender.SendMessageToSelectedPlayer();
                return true;
        }
        return false;
    }
}
