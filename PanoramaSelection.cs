using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace WeaponPaints;

public partial class WeaponPaints
{
    private static string? GetMenuSelectionMarker(
        CsTeam[] teams,
        Func<CsTeam, bool> isSelected)
    {
        bool checksCT =
            Array.IndexOf(teams, CsTeam.CounterTerrorist) >= 0;

        bool checksT =
            Array.IndexOf(teams, CsTeam.Terrorist) >= 0;

        bool selectedCT =
            checksCT && isSelected(CsTeam.CounterTerrorist);

        bool selectedT =
            checksT && isSelected(CsTeam.Terrorist);

        if (!selectedCT && !selectedT)
            return null;

        if ((!checksCT || selectedCT) &&
            (!checksT || selectedT))
        {
            return "✓";
        }

        return selectedCT ? "CT" : "T";
    }

    private static string? GetSkinSelectionMarker(
        CCSPlayerController player,
        int defindex,
        int paint,
        bool gloves,
        CsTeam[] teams)
    {
        return GetMenuSelectionMarker(teams, team =>
        {
            if (gloves)
            {
                ushort activeGlove = 0;

                if (GPlayersGlove.TryGetValue(
                        player.Slot, out var playerGloves))
                {
                    playerGloves.TryGetValue(team, out activeGlove);
                }

                if (paint == 0)
                    return activeGlove == 0;

                if (activeGlove != defindex)
                    return false;
            }

            int selectedPaint = 0;

            if (GPlayerWeaponsInfo.TryGetValue(
                    player.Slot, out var playerWeapons) &&
                playerWeapons.TryGetValue(
                    team, out var teamWeapons) &&
                teamWeapons.TryGetValue(
                    defindex, out var weaponInfo))
            {
                selectedPaint = weaponInfo.Paint;
            }

            return selectedPaint == paint;
        });
    }

    private static string? GetKnifeSelectionMarker(
    CCSPlayerController player,
    string knifeClassname,
    CsTeam[] teams)
    {
        return GetMenuSelectionMarker(teams, team =>
        {
            string selectedKnife = "weapon_knife";

            if (GPlayersKnife.TryGetValue(
                    player.Slot, out var playerKnives) &&
                playerKnives.TryGetValue(team, out var savedKnife))
            {
                selectedKnife = savedKnife;
            }

            bool defaultOption =
                knifeClassname is "weapon_knife" or "weapon_knife_t";

            bool defaultSelected =
                selectedKnife is "weapon_knife" or "weapon_knife_t";

            return defaultOption
                ? defaultSelected
                : string.Equals(
                    selectedKnife,
                    knifeClassname,
                    StringComparison.Ordinal);
        });
    }

    private static string? GetGloveModelSelectionMarker(
        CCSPlayerController player,
        ushort defindex,
        CsTeam[] teams)
    {
        return GetMenuSelectionMarker(teams, team =>
        {
            ushort selectedGlove = 0;

            if (GPlayersGlove.TryGetValue(
                    player.Slot, out var playerGloves))
            {
                playerGloves.TryGetValue(team, out selectedGlove);
            }

            return selectedGlove == defindex;
        });
    }

    private static string? GetAgentSelectionMarker(
    CCSPlayerController player,
    string? model)
    {
        if (player.Team is not CsTeam.CounterTerrorist
            and not CsTeam.Terrorist)
        {
            return null;
        }

        string? selectedModel = null;

        if (GPlayersAgent.TryGetValue(
                player.Slot, out var agents))
        {
            selectedModel = player.Team == CsTeam.CounterTerrorist
                ? agents.CT
                : agents.T;
        }

        if (string.IsNullOrEmpty(model) || model == "null")
            model = null;

        if (string.IsNullOrEmpty(selectedModel) ||
            selectedModel == "null")
        {
            selectedModel = null;
        }

        return string.Equals(
            selectedModel,
            model,
            StringComparison.Ordinal)
                ? "✓"
                : null;
    }
}