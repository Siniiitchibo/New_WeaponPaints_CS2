using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using System.Collections.Concurrent;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.Linq;

namespace WeaponPaints;

public partial class WeaponPaints
{
    private IMenu? _wsWeaponsMenu;
    private IMenu? _wsKnifeMenu;
    private IMenu? _wsGlovesMenu;
    private readonly ConcurrentDictionary<int, CsTeam[]> _wsKnifeTeams = new();
    private readonly ConcurrentDictionary<int, CsTeam[]> _wsGloveTeams = new();

    private Action<CCSPlayerController>? _wsOpenAgents;
    private Action<CCSPlayerController, string>? _wsOpenWeaponSkins;

    private bool CanUseWsMenu(CCSPlayerController? player)
    {
        return player is not null &&
               Utility.IsPlayerValid(player) &&
               _gBCommandsAllowed &&
               ArePlayerCosmeticsReady(player);
    }

    private void NavigateWs(
        CCSPlayerController player,
        Action<CCSPlayerController> openMenu)
    {
        // Open the next menu after the previous selection has finished.
        Server.NextFrame(() =>
        {
            if (CanUseWsMenu(player))
                openMenu(player);
        });
    }

    private CsTeam[] GetWsCosmeticTeams(
    CCSPlayerController player,
    bool knife)
    {
        var selections = knife ? _wsKnifeTeams : _wsGloveTeams;

        if (selections.TryGetValue(player.Slot, out var teams))
            return teams;

        return player.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist
            ? [player.Team]
            : [CsTeam.CounterTerrorist, CsTeam.Terrorist];
    }

    private void OpenWsTeamSelection(
        CCSPlayerController player,
        bool knife)
    {
        if (!CanUseWsMenu(player))
            return;

        var menu = Utility.CreateMenu(
        Localizer[
            knife
                ? "wp_ws_knife_team_title"
                : "wp_ws_glove_team_title"],
        OpenWsMainMenu);

        if (menu == null)
            return;

        menu.PostSelectAction = PostSelectAction.Close;

        menu.AddMenuOption(Localizer["wp_ws_apply_both"], (p, _) =>
        {
            SelectTeams(
                p,
                [CsTeam.CounterTerrorist, CsTeam.Terrorist]);
        });

        CsTeam currentTeam = player.Team;

        if (currentTeam is CsTeam.Terrorist or CsTeam.CounterTerrorist)
        {
            string teamName =
                currentTeam == CsTeam.CounterTerrorist ? "CT" : "T";

            menu.AddMenuOption(Localizer["wp_ws_apply_current", teamName], (p, _) =>
            {
                SelectTeams(p, [currentTeam]);
            });
        }
        else
        {
            menu.AddMenuOption(
                Localizer["wp_ws_join_team_first"],
                (p, _) => { },
                true);
        }

        menu.Open(player);

        void SelectTeams(CCSPlayerController target, CsTeam[] teams)
        {
            if (!CanUseWsMenu(target))
                return;

            var selections = knife ? _wsKnifeTeams : _wsGloveTeams;
            selections[target.Slot] = teams;

            NavigateWs(target, nextPlayer =>
            {
                if (knife)
                    _wsKnifeMenu?.Open(nextPlayer);
                else
                    _wsGlovesMenu?.Open(nextPlayer);
            });
        }
    }

    private void OpenWsMainMenu(CCSPlayerController player)
    {
        if (!CanUseWsMenu(player))
            return;

        var menu = Utility.CreateMenu(Localizer["wp_ws_title"]);
        if (menu == null)
            return;

        menu.PostSelectAction = PostSelectAction.Close;

        if (Config.Additional.SkinEnabled && _wsWeaponsMenu != null)
        {
            menu.AddMenuOption(Localizer["wp_ws_all_weapons"], (p, _) =>
                NavigateWs(p, target => _wsWeaponsMenu?.Open(target)));
        }

        if (Config.Additional.KnifeEnabled && _wsKnifeMenu != null)
        {
            menu.AddMenuOption(Localizer["wp_ws_knife"], (p, _) =>
                NavigateWs(p, target => OpenWsTeamSelection(target, true)));
        }

        if (Config.Additional.SkinEnabled)
        {
            var weapons = player.PlayerPawn.Value?
                .WeaponServices?.MyWeapons;

            string? pistol = null;
            string? primary = null;

            if (weapons != null)
            {
                foreach (var handle in weapons)
                {
                    var weapon = handle.Value;
                    if (weapon == null || !weapon.IsValid)
                        continue;

                    if (!WeaponDefindex.TryGetValue(
                            weapon.AttributeManager.Item.ItemDefinitionIndex,
                            out var weaponClass) ||
                        !WeaponList.TryGetValue(weaponClass, out var weaponName))
                        continue;

                    var data = weapon.As<CCSWeaponBase>().VData;
                    if (data == null)
                        continue;

                    if (data.GearSlot == gear_slot_t.GEAR_SLOT_PISTOL)
                        pistol = weaponName;

                    if (data.GearSlot == gear_slot_t.GEAR_SLOT_RIFLE)
                        primary = weaponName;
                }
            }

            AddWeaponShortcut(pistol);
            AddWeaponShortcut(primary);
        }

        if (Config.Additional.GloveEnabled && _wsGlovesMenu != null)
            menu.AddMenuOption(Localizer["wp_ws_gloves"], (p, _) =>
                NavigateWs(p, target => OpenWsTeamSelection(target, false)));

        if (Config.Additional.AgentEnabled && _wsOpenAgents != null)
            menu.AddMenuOption(Localizer["wp_ws_agents"], (p, _) =>
                NavigateWs(p, target => _wsOpenAgents?.Invoke(target)));

        menu.Open(player);

        void AddWeaponShortcut(string? name)
        {
            if (string.IsNullOrEmpty(name))
                return;

            string selectedWeapon = name;

            menu.AddMenuOption(selectedWeapon, (p, _) =>
                NavigateWs(p, target =>
                    _wsOpenWeaponSkins?.Invoke(target, selectedWeapon)));
        }
    }

    private void AddWsGloveTypes(
    IMenu typesMenu,
    Action<CCSPlayerController?, string> handleSelection)
    {
        var defaultGlove = GlovesList.FirstOrDefault(glove =>
            glove["paint"]?.ToString() == "0");

        string? defaultPaintName =
            defaultGlove?["paint_name"]?.ToString();

        if (!string.IsNullOrWhiteSpace(defaultPaintName))
        {
            string originalDefaultName = defaultPaintName;

            typesMenu.AddMenuOption(
                Localizer["wp_ws_default_gloves"],
                (player, _) =>
                    handleSelection(player, originalDefaultName));
        }

        var gloveTypes = GlovesList
            .Where(glove =>
                glove["paint"]?.ToString() != "0" &&
                !string.IsNullOrWhiteSpace(
                    glove["weapon_defindex"]?.ToString()) &&
                !string.IsNullOrWhiteSpace(
                    glove["paint_name"]?.ToString()))
            .GroupBy(glove => glove["weapon_defindex"]!.ToString())
            .Select(group => new
            {
                Defindex = group.Key,
                Name = group.First()["paint_name"]!
                    .ToString()
                    .Split('|')[0]
                    .Replace("★", "")
                    .Trim()
            })
            .OrderBy(type => type.Name)
            .ToList();

        foreach (var gloveType in gloveTypes)
        {
            string selectedDefindex = gloveType.Defindex;
            string selectedTypeName = gloveType.Name;

            typesMenu.AddMenuOption(selectedTypeName, (player, _) =>
            {
                NavigateWs(player, target =>
                {
                    OpenWsGloveSkins(
                        target,
                        selectedDefindex,
                        selectedTypeName,
                        handleSelection);
                });
            });
        }
    }

    private void OpenWsGloveSkins(
    CCSPlayerController player,
    string gloveDefindex,
    string gloveTypeName,
    Action<CCSPlayerController?, string> handleSelection)
    {
        if (!CanUseWsMenu(player))
            return;

        var menu = Utility.CreateMenu(
            Localizer["wp_ws_glove_skin_title", gloveTypeName],
            previousPlayer => _wsGlovesMenu?.Open(previousPlayer));

        if (menu == null)
            return;

        menu.PostSelectAction = PostSelectAction.Nothing;

        var matchingGloves = GlovesList
            .Where(glove =>
                glove["weapon_defindex"]?.ToString() == gloveDefindex &&
                !string.IsNullOrWhiteSpace(
                    glove["paint_name"]?.ToString()))
            .OrderBy(glove => glove["paint_name"]!.ToString())
            .ToList();

        foreach (var glove in matchingGloves)
        {
            string paintName = glove["paint_name"]!.ToString();

            menu.AddMenuOption(
                paintName,
                (target, _) => handleSelection(target, paintName));
        }

        menu.Open(player);
    }

    private void RegisterWsCommands()
    {
        AddCommand(
            "css_ws",
            Localizer["wp_ws_command_description"],
            (player, _) => TogglePanoramaMenu(player)
        );

        AddCommandListener("say", HandleWsChat, HookMode.Pre);
        AddCommandListener("say_team", HandleWsChat, HookMode.Pre);
    }

    private HookResult HandleWsChat(
        CCSPlayerController? player,
        CommandInfo command)
    {
        string message = command.GetArg(1).Trim().Trim('"').Trim();

        if (!message.Equals(".ws", StringComparison.OrdinalIgnoreCase) &&
            !message.Equals("!ws", StringComparison.OrdinalIgnoreCase))
        {
            return HookResult.Continue;
        }

        TogglePanoramaMenu(player);

        return HookResult.Handled;
    }
}