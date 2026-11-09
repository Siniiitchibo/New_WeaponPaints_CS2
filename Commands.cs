using System.Collections.Concurrent;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Newtonsoft.Json.Linq;

namespace WeaponPaints;

public partial class WeaponPaints
{
    private void OnCommandRefresh(
        CCSPlayerController? player,
        CommandInfo command)
    {
        if (!Config.Additional.CommandWpEnabled ||
            !Config.Additional.SkinEnabled ||
            !_gBCommandsAllowed ||
            player is null ||
            !Utility.IsPlayerValid(player) ||
            player.IsBot ||
            player.UserId is null)
        {
            return;
        }

        if (CommandsCooldown.TryGetValue(
                player.Slot, out var cooldownEndTime) &&
            DateTime.UtcNow < cooldownEndTime)
        {
            if (!string.IsNullOrEmpty(Localizer["wp_command_cooldown"]))
                player.Print(Localizer["wp_command_cooldown"]);

            return;
        }

        CommandsCooldown[player.Slot] = DateTime.UtcNow.AddSeconds(
            Config.CmdRefreshCooldownSeconds
        );

        LoadPlayerCosmetics(player);

        if (!string.IsNullOrEmpty(Localizer["wp_command_refresh_done"]))
            player.Print(Localizer["wp_command_refresh_done"]);
    }

    private void RegisterCommands()
	{
        RegisterWsCommands();
        _config.Additional.CommandStattrak.ForEach(c =>
		{
			AddCommand($"css_{c}", "Stattrak toggle", (player, info) =>
			{
				if (!Utility.IsPlayerValid(player)) return;

				OnCommandStattrak(player, info);
			});
		});
			
		_config.Additional.CommandRefresh.ForEach(c =>
		{
			AddCommand($"css_{c}", "Skins refresh", (player, info) =>
			{
				if (!Utility.IsPlayerValid(player)) return;
				OnCommandRefresh(player, info);
			});
		});

		if (Config.Additional.CommandKillEnabled)
		{
			_config.Additional.CommandKill.ForEach(c =>
			{
				AddCommand($"css_{c}", "kill yourself", (player, _) =>
				{
					if (player == null || !Utility.IsPlayerValid(player) || player.PlayerPawn.Value == null || !player.PlayerPawn.IsValid) return;

					player.PlayerPawn.Value.CommitSuicide(true, false);
				});
			});
		}

		AddCommand("wp_refresh", "Admin refresh player skins", (player, info) =>
		{
			OnCommandSkinRefresh(player, info);
		});
	}

	private void OnCommandSkinRefresh(CCSPlayerController? player, CommandInfo command)
	{
		if (!Config.Additional.CommandWpEnabled || !Config.Additional.SkinEnabled || !_gBCommandsAllowed) return;
		if (player != null)
		{
			return;
		}

		var args = command.GetArg(1);
			
		if (string.IsNullOrEmpty(args))
		{
			Console.WriteLine("[WeaponPaints] Usage: wp_refresh <steamid64|all>");
			Console.WriteLine("[WeaponPaints] Examples:");
			Console.WriteLine("[WeaponPaints]   wp_refresh all - Refresh skins for all players");
			Console.WriteLine("[WeaponPaints]   wp_refresh 76561198012345678 - Refresh skins by SteamID64");
			return;
		}

		var targetPlayers = new List<CCSPlayerController>();

		if (args.Equals("all", StringComparison.OrdinalIgnoreCase))
		{
            targetPlayers =
            [
                .. Utilities.GetPlayers().Where(p =>
                     p != null && p.IsValid && !p.IsBot && p.UserId != null)
            ];

            if (targetPlayers.Count == 0)
			{
				Console.WriteLine("[WeaponPaints] No players connected to refresh.");
				return;
			}
			
			Console.WriteLine($"[WeaponPaints] Refreshing skins for {targetPlayers.Count} players...");
		}
		else
		{
			var foundPlayer = Utilities.GetPlayers().FirstOrDefault(p => 
				p != null && p.IsValid && !p.IsBot && p.UserId != null && 
				 p.SteamID.ToString() == args);

			if (foundPlayer == null)
			{
				Console.WriteLine($"[WeaponPaints] Player with SteamID64 '{args}' not found.");
				return;
			}

			targetPlayers.Add(foundPlayer);
			Console.WriteLine($"[WeaponPaints] Refreshing skins for {foundPlayer.PlayerName}...");
		}

		foreach (var targetPlayer in targetPlayers)
		{
			try
			{
                LoadPlayerCosmetics(targetPlayer);

                if (!string.IsNullOrEmpty(Localizer["wp_command_refresh_done"]))
				{
					targetPlayer.Print(Localizer["wp_command_refresh_done"]);
				}

                Console.WriteLine($"[WeaponPaints] Loading skins for {targetPlayer.PlayerName}...");
            }
			catch (Exception ex)
			{
				Console.WriteLine($"[WeaponPaints] Error refreshing skins for {targetPlayer.PlayerName}: {ex.Message}");
			}
		}

		Console.WriteLine("[WeaponPaints] Refresh process completed.");
	}


	private void OnCommandStattrak(CCSPlayerController? player, CommandInfo commandInfo)
	{
		if (player == null || !player.IsValid) return;
		
		var weapon = player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;
		
		if (weapon == null || !weapon.IsValid)
			return;

		if (!HasChangedPaint(player, weapon.AttributeManager.Item.ItemDefinitionIndex, out var weaponInfo) || weaponInfo == null)
			return;
		
		weaponInfo.StatTrak = !weaponInfo.StatTrak;
		RefreshWeapons(player);

		if (!string.IsNullOrEmpty(Localizer["wp_stattrak_action"]))
		{
			player.Print(Localizer["wp_stattrak_action"]);
		}
	}

	private void SetupKnifeMenu()
	{
		if (!Config.Additional.KnifeEnabled || !_gBCommandsAllowed) return;

		var knivesOnly = WeaponList
			.Where(pair => pair.Key.StartsWith("weapon_knife") || pair.Key.StartsWith("weapon_bayonet"))
			.ToDictionary(pair => pair.Key, pair => pair.Value);

        var giveItemMenu = Utility.CreateMenu(Localizer["wp_knife_menu_title"], previousPlayer => OpenWsTeamSelection(previousPlayer, true));
        if (giveItemMenu == null)
            return;

        void HandleGive(CCSPlayerController player, string knifeName)
        {
			if (!Utility.IsPlayerValid(player)) return;

			var playerKnives = GPlayersKnife.GetOrAdd(player.Slot, new ConcurrentDictionary<CsTeam, string>());
            var teamsToCheck = GetWsCosmeticTeams(player, true);

			var knifeKey = knivesOnly.FirstOrDefault(x => x.Value == knifeName).Key;
			if (string.IsNullOrEmpty(knifeKey)) return;
			if (!string.IsNullOrEmpty(Localizer["wp_knife_menu_select"]))
			{
                string displayKnifeName =
				knifeKey is "weapon_knife" or "weapon_knife_t"
					? Localizer["wp_ws_default_knife"].Value
					: knifeName;

                player.Print(
                    Localizer["wp_knife_menu_select", displayKnifeName]);
            }

			if (!string.IsNullOrEmpty(Localizer["wp_knife_menu_kill"]) && Config.Additional.CommandKillEnabled)
			{
				player.Print(Localizer["wp_knife_menu_kill"]);
			}

            PlayerInfo playerInfo = new()
            {
				UserId = player.UserId,
				Slot = player.Slot,
				Index = (int)player.Index,
				SteamId = player.SteamID.ToString(),
				Name = player.PlayerName,
				IpAddress = player.IpAddress?.Split(":")[0]
			};
			
			foreach (var team in teamsToCheck)
			{
				// Attempt to get the existing knives
				playerKnives[team] = knifeKey;
			}
			
			if (_gBCommandsAllowed && (LifeState_t)player.LifeState == LifeState_t.LIFE_ALIVE)
                RefreshWeapons(player, knifeKey);

            if (WeaponSync != null)
                _ = QueuePlayerCosmeticWork(playerInfo.Slot, async () => await WeaponSync.SyncKnifeToDatabase(playerInfo, knifeKey, teamsToCheck));
            NavigateWs(player, target =>
            {
                if (knifeKey == "weapon_knife" || !Config.Additional.SkinEnabled)
                {
                    OpenWsMainMenu(target);
                    return;
                }

                _wsOpenWeaponSkins?.Invoke(target, knifeName);
            });
        }
        foreach (var knifePair in knivesOnly)
        {
            string originalKnifeName = knifePair.Value;

            string displayName =
                knifePair.Key is "weapon_knife" or "weapon_knife_t"
                    ? Localizer["wp_ws_default_knife"].Value
                    : originalKnifeName;

            var knifeOption = giveItemMenu.AddMenuOption(
                displayName,
                (player, _) => HandleGive(player, originalKnifeName));

            if (knifePair.Key is "weapon_knife" or "weapon_knife_t")
            {
                SetMenuOptionTranslation(
                    giveItemMenu,
                    knifeOption,
                    "wp_ws_default_knife"
                );
            }
        }
        giveItemMenu.PostSelectAction = PostSelectAction.Close;

        _wsKnifeMenu = giveItemMenu;
    }

    private void SetupSkinsMenu()
    {
        var classNamesByWeapon = WeaponList
            .Where(pair =>
                pair.Key != "weapon_knife" &&
                pair.Key != "weapon_knife_t")
            .ToDictionary(pair => pair.Value, pair => pair.Key);

        var weaponSelectionMenu = Utility.CreateMenu(
            Localizer["wp_skin_menu_weapon_title"],
            OpenWsMainMenu);

        if (weaponSelectionMenu == null)
            return;

        _wsOpenWeaponSkins = (player, selectedWeapon) =>
        {
                if (!CanUseWsMenu(player))
                    return;

                using var menuCulture = new MenuCulture(player);

                if (!classNamesByWeapon.TryGetValue(
                    selectedWeapon,
                    out var selectedWeaponClassname))
                {
                    return;
                }

                int weaponDefIndex = WeaponDefindex
                .FirstOrDefault(pair =>
                    pair.Value == selectedWeaponClassname)
                .Key;

                if (weaponDefIndex == 0)
                    return;

                bool isKnifeSelection =
                selectedWeaponClassname.StartsWith(
                    "weapon_knife",
                    StringComparison.OrdinalIgnoreCase) ||
                selectedWeaponClassname.Equals(
                    "weapon_bayonet",
                    StringComparison.OrdinalIgnoreCase);

                CsTeam[] targetTeams = isKnifeSelection
                ? GetWsCosmeticTeams(player, true)
                : player.TeamNum < 2
                    ? [CsTeam.Terrorist, CsTeam.CounterTerrorist]
                    : [player.Team];

                var skinSubMenu = Utility.CreateMenu(
                Localizer["wp_skin_menu_skin_title", selectedWeapon],
                target =>
                {
                    if (isKnifeSelection && _wsKnifeMenu != null)
                        _wsKnifeMenu.Open(target);
                    else
                        OpenWsMainMenu(target);
                });

                if (skinSubMenu == null)
                    return;

                skinSubMenu.PostSelectAction = PostSelectAction.Nothing;

                // StatTrak is always the first item.
                bool statTrakEnabled = AreAllTeamsStatTrakEnabled(player);

                skinSubMenu.AddMenuOption(
                Localizer[
                    statTrakEnabled
                        ? "wp_ws_stattrak_disable"
                        : "wp_ws_stattrak_enable"],
                (p, _) =>
                {
                    if (!CanUseWsMenu(p))
                        return;

                    // If the selected sides have different settings,
                    // enabling makes both sides enabled.
                    bool enable = !AreAllTeamsStatTrakEnabled(p);

                    foreach (var team in targetTeams)
                    {
                        var weaponInfo = GetWeaponInfo(p, team);

                        weaponInfo.StatTrak = enable;

                        // Keep the existing kill counter.
                    }

                    SaveWsWeaponSettings(p, selectedWeaponClassname);

                    p.Print(Localizer[
                        "wp_ws_stattrak_set",
                        selectedWeapon,
                        Localizer[
                            enable
                                ? "wp_ws_stattrak_on"
                                : "wp_ws_stattrak_off"].Value]);

                    // Reopen to update the toggle label.
                    NavigateWs(p, target =>
                        _wsOpenWeaponSkins?.Invoke(
                            target,
                            selectedWeapon));
                });

                // Skin choices follow the StatTrak toggle.
                var skinsForSelectedWeapon = SkinsList
                .Where(skin =>
                    skin["weapon_name"]?.ToString() ==
                    selectedWeaponClassname)
                .ToList();

                foreach (var skin in skinsForSelectedWeapon)
                {
                    string? paintName = skin["paint_name"]?.ToString();

                    if (string.IsNullOrWhiteSpace(paintName) ||
                    !int.TryParse(
                        skin["paint"]?.ToString(),
                        out int paintId))
                    {
                        continue;
                    }

                string skinLabel = Localizer["wp_skin_menu_entry", paintName, paintId].Value;
                int selectedPaintId = paintId;

                    skinSubMenu.AddMenuOption(skinLabel, (p, _) =>
                    {
                        if (!CanUseWsMenu(p))
                            return;

                        foreach (var team in targetTeams)
                        {
                            var weaponInfo = GetWeaponInfo(p, team);

                            weaponInfo.Paint = selectedPaintId;
                            weaponInfo.Wear = 0.01f;
                            weaponInfo.Seed = 0;

                            // Preserve StatTrak and its kill counter.
                        }

                        p.Print(
                        Localizer["wp_skin_menu_select", skinLabel]);

                        SaveWsWeaponSettings(p, selectedWeaponClassname);
                    });
                }

                skinSubMenu.Open(player);

                bool AreAllTeamsStatTrakEnabled(CCSPlayerController target)
                {
                    if (!GPlayerWeaponsInfo.TryGetValue(
                        target.Slot,
                        out var playerWeapons))
                    {
                        return false;
                    }

                    return targetTeams.All(team =>
                    playerWeapons.TryGetValue(team, out var teamWeapons) &&
                    teamWeapons.TryGetValue(
                        weaponDefIndex,
                        out var weaponInfo) &&
                    weaponInfo.StatTrak);
                }

                WeaponInfo GetWeaponInfo(
                CCSPlayerController target,
                CsTeam team)
                {
                    var playerWeapons = GPlayerWeaponsInfo.GetOrAdd(
                    target.Slot,
                    _ => new ConcurrentDictionary<
                        CsTeam,
                        ConcurrentDictionary<int, WeaponInfo>>());

                    var teamWeapons = playerWeapons.GetOrAdd(
                    team,
                    _ => new ConcurrentDictionary<int, WeaponInfo>());

                    return teamWeapons.GetOrAdd(
                    weaponDefIndex,
                    _ => new WeaponInfo
                    {
                        Wear = 0.01f
                    });
                }
            };

        foreach (var weaponName in WeaponList
                     .Where(pair =>
                         !pair.Key.StartsWith("weapon_knife") &&
                         pair.Key != "weapon_bayonet")
                     .Select(pair => pair.Value))
        {
            string selectedWeapon = weaponName;

            weaponSelectionMenu.AddMenuOption(
                selectedWeapon,
                (player, _) =>
                    NavigateWs(player, target =>
                        _wsOpenWeaponSkins?.Invoke(
                            target,
                            selectedWeapon)));
        }

        weaponSelectionMenu.PostSelectAction = PostSelectAction.Close;
        _wsWeaponsMenu = weaponSelectionMenu;
    }

    private void SaveWsWeaponSettings(
    CCSPlayerController player,
    string weaponClassname)
    {
        if (player.PawnIsAlive)
        {
            RefreshWeapons(player, weaponClassname);
        }

        var sync = WeaponSync;

        if (sync == null)
            return;

        PlayerInfo playerInfo = new()
        {
            UserId = player.UserId,
            Slot = player.Slot,
            Index = (int)player.Index,
            SteamId = player.SteamID.ToString(),
            Name = player.PlayerName,
            IpAddress = player.IpAddress?.Split(":")[0]
        };

        _ = QueuePlayerCosmeticWork(playerInfo.Slot, async () =>
        {
            try
            {
                // Create/update skin records first.
                await sync.SyncWeaponPaintsToDatabase(playerInfo);

                // Then persist StatTrak state and counters.
                await sync.SyncStatTrakToDatabase(playerInfo);
            }
            catch (Exception ex)
            {
                Utility.Log(
                    $"Error saving weapon settings: {ex.Message}");
            }
        });
    }

    private void SetupGlovesMenu()
    {
        var glovesSelectionMenu = Utility.CreateMenu(
            Localizer["wp_glove_menu_title"],
            previousPlayer => OpenWsTeamSelection(previousPlayer, false));

        if (glovesSelectionMenu == null)
            return;

        glovesSelectionMenu.PostSelectAction = PostSelectAction.Nothing;

        void HandleGloveSelection(CCSPlayerController? player, string selectedPaintName)
        {
                if (player == null || !CanUseWsMenu(player))
                    return;

                var selectedGlove = GlovesList.FirstOrDefault(glove =>
                glove["paint_name"]?.ToString() == selectedPaintName);

                if (selectedGlove == null ||
                !ushort.TryParse(
                    selectedGlove["weapon_defindex"]?.ToString(),
                    out ushort weaponDefindex) ||
                !int.TryParse(
                    selectedGlove["paint"]?.ToString(),
                    out int paint))
                {
                    return;
                }

                CsTeam[] teamsToCheck =
                GetWsCosmeticTeams(player, false);

                var playerGloves = GPlayersGlove.GetOrAdd(
                player.Slot,
                _ => new ConcurrentDictionary<CsTeam, ushort>());

                var playerWeapons = GPlayerWeaponsInfo.GetOrAdd(
                player.Slot,
                _ => new ConcurrentDictionary<
                    CsTeam,
                    ConcurrentDictionary<int, WeaponInfo>>());

                foreach (var team in teamsToCheck)
                {
                    // Change only the selected side or sides.
                    playerGloves[team] = paint == 0
                    ? (ushort)0
                    : weaponDefindex;

                    if (paint == 0)
                        continue;

                    var teamWeapons = playerWeapons.GetOrAdd(
                    team,
                    _ => new ConcurrentDictionary<int, WeaponInfo>());

                    var weaponInfo = teamWeapons.GetOrAdd(
                    weaponDefindex,
                    _ => new WeaponInfo());

                    weaponInfo.Paint = paint;
                    weaponInfo.Wear = 0.00f;
                    weaponInfo.Seed = 0;
                }

                if (!string.IsNullOrEmpty(
                    Localizer["wp_glove_menu_select"]))
                {
					string displayGloveName = paint == 0
						? Localizer["wp_ws_default_gloves"].Value
						: selectedPaintName;

            player.Print(
                Localizer["wp_glove_menu_select", displayGloveName]);
        }

                PlayerInfo playerInfo = new()
                {
                    UserId = player.UserId,
                    Slot = player.Slot,
                    Index = (int)player.Index,
                    SteamId = player.SteamID.ToString(),
                    Name = player.PlayerName,
                    IpAddress = player.IpAddress?.Split(":")[0]
                };

                var sync = WeaponSync;

                if (sync != null)
                {
                    ushort savedGloveDefindex = paint == 0
                    ? (ushort)0
                    : weaponDefindex;

                _ = QueuePlayerCosmeticWork(playerInfo.Slot, async () =>
                    {
                        await sync.SyncGloveToDatabase(
                        playerInfo,
                        savedGloveDefindex,
                        teamsToCheck);

                        if (paint != 0)
                        {
                            await sync.SyncWeaponPaintsToDatabase(playerInfo);
                        }
                    });
                }

                AddTimer(
                0.1f,
                () => GivePlayerGloves(player),
                TimerFlags.STOP_ON_MAPCHANGE);

                AddTimer(
                0.25f,
                () => GivePlayerGloves(player),
                TimerFlags.STOP_ON_MAPCHANGE);
        }

        AddWsGloveTypes(glovesSelectionMenu, HandleGloveSelection);

        _wsGlovesMenu = glovesSelectionMenu;
    }

    private void SetupAgentsMenu()
	{
        void HandleAgentSelection(CCSPlayerController? player, ChatMenuOption option)
        {
			if (!Utility.IsPlayerValid(player) || player is null) return;

			var selectedPaintName = option.Text;
			var selectedAgent = AgentsList.FirstOrDefault(g =>
				g.ContainsKey("agent_name") &&
				g["agent_name"] != null && g["agent_name"]!.ToString() == selectedPaintName &&
				g["team"] != null && (int)(g["team"]!) == player.TeamNum);

			if (selectedAgent == null) return;

			if (
				selectedAgent.ContainsKey("model")
			)
			{
                PlayerInfo playerInfo = new()
                {
					UserId = player.UserId,
					Slot = player.Slot,
					Index = (int)player.Index,
					SteamId = player.SteamID.ToString(),
					Name = player.PlayerName,
					IpAddress = player.IpAddress?.Split(":")[0]
				};

                if (Config.Additional.ShowSkinImage)
                {
                    string image = selectedAgent["image"]?.ToString() ?? "";

                    if (!string.IsNullOrWhiteSpace(image))
                    {
                        NavigateWs(player, target =>
                        {
                            int slot = target.Slot;
                            ulong steamId = target.SteamID;

                            _playerWeaponImage[slot] = image;

                            AddTimer(2.0f, () =>
                            {
                                if (!target.IsValid ||
                                    target.SteamID != steamId)
                                {
                                    return;
                                }

                                if (!_playerWeaponImage.TryGetValue(
                                        slot, out var activeImage) ||
                                    activeImage != image)
                                {
                                    return;
                                }

                                _playerWeaponImage.Remove(slot);

                            }, TimerFlags.STOP_ON_MAPCHANGE);
                        });
                    }
                }

                if (!string.IsNullOrEmpty(Localizer["wp_agent_menu_select"]))
				{
					player.Print(Localizer["wp_agent_menu_select", selectedPaintName]);
				}

				if (player.TeamNum == 3)
				{
					GPlayersAgent.AddOrUpdate(player.Slot,
						key => (selectedAgent["model"]!.ToString().Equals("null") ? null : selectedAgent["model"]!.ToString(), null),
						(key, oldValue) => (selectedAgent["model"]!.ToString().Equals("null") ? null : selectedAgent["model"]!.ToString(), oldValue.T));
				}
				else
				{
					GPlayersAgent.AddOrUpdate(player.Slot,
						key => (null, selectedAgent["model"]!.ToString().Equals("null") ? null : selectedAgent["model"]!.ToString()),
						(key, oldValue) => (oldValue.CT, selectedAgent["model"]!.ToString().Equals("null") ? null : selectedAgent["model"]!.ToString())
					);
				}

				if (WeaponSync != null)
				{
                    _ = QueuePlayerCosmeticWork(playerInfo.Slot, async () =>
					{
						await WeaponSync.SyncAgentToDatabase(playerInfo);
					});
				}
			};
        }

        // Command to open the weapon selection menu for players
        _wsOpenAgents = player =>
        {
            if (!CanUseWsMenu(player))
                return;

            using var menuCulture = new MenuCulture(player);

            var agentsSelectionMenu = Utility.CreateMenu(
                Localizer["wp_agent_menu_title"],
                OpenWsMainMenu);

            if (agentsSelectionMenu == null)
                return;

            agentsSelectionMenu.PostSelectAction = PostSelectAction.Nothing;

            foreach (var agentObject in AgentsList)
            {
                if (agentObject["team"]?.Value<int>() != player.TeamNum)
                    continue;

                string name = agentObject["agent_name"]?.ToString() ?? "";

                if (name.Length > 0)
                    agentsSelectionMenu.AddMenuOption(name, HandleAgentSelection);
            }

            agentsSelectionMenu.Open(player);
        };
    }
}
