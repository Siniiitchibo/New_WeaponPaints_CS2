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

        using var menuCulture = new MenuCulture(player);
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
                using var menuCulture = new MenuCulture(targetPlayer);
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
		if (!Config.Additional.SkinEnabled || player == null || !player.IsValid) return;
        using var menuCulture = new MenuCulture(player);
		var weapon = player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;
		
		if (weapon == null || !weapon.IsValid)
			return;

		if (!HasChangedPaint(player, weapon.AttributeManager.Item.ItemDefinitionIndex, out var weaponInfo) || weaponInfo == null)
			return;
		
        if (!CanUseWsMenu(player) ||
            !WeaponDefindex.TryGetValue(weapon.AttributeManager.Item.ItemDefinitionIndex, out var classname))
            return;
        weaponInfo.StatTrak = !weaponInfo.StatTrak;
        SaveWsWeaponSettings(player, classname);

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

            var sync = WeaponSync;
            if (sync is not null)
                _ = QueuePlayerCosmeticWork(playerInfo.Slot,
                    () => sync.SyncKnifeToDatabase(playerInfo, knifeKey, teamsToCheck));
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

            SetMenuOptionSelection(giveItemMenu, knifeOption, target => GetKnifeSelectionMarker(target, knifePair.Key, GetWsCosmeticTeams(target, true)));

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

                SetWearMenuContext(skinSubMenu, weaponDefIndex, false, targetTeams);

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

                var skinOption = skinSubMenu.AddMenuOption(skinLabel, (p, _) =>
                {
                        if (!CanUseWsMenu(p))
                            return;

                        foreach (var team in targetTeams)
                        {
                            var weaponInfo = GetWeaponInfo(p, team);

                            weaponInfo.Paint = selectedPaintId;
                            weaponInfo.Seed = 0;

                            // Preserve StatTrak and its kill counter.
                        }

                        p.Print(
                        Localizer["wp_skin_menu_select", skinLabel]);

                        SaveWsWeaponSettings(p, selectedWeaponClassname);
                    });
                        SetMenuOptionSelection(skinSubMenu, skinOption, target => GetSkinSelectionMarker(target, weaponDefIndex, selectedPaintId, false, targetTeams));
            }

                skinSubMenu.Open(player);

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

        int? defindex = WeaponDefindex.FirstOrDefault(pair => pair.Value == weaponClassname).Key;
        var snapshot = CosmeticSnapshot.Capture(playerInfo.Slot, defindex);
        _ = QueuePlayerCosmeticWork(playerInfo.Slot,
            () => sync.SyncWeaponPaintsToDatabase(playerInfo, snapshot));
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
                    // Carry the active gloves' wear over to another glove model.
                    float savedWear = GetActiveGloveWear(player, team);

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
                    weaponInfo.Wear = savedWear;
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
                    var snapshot = CosmeticSnapshot.Capture(playerInfo.Slot, weaponDefindex);
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
                            await sync.SyncWeaponPaintsToDatabase(playerInfo, snapshot);
                        }
                    });
                }

                var gloveGuard = CapturePlayerAction(player);
                AddTimer(
                0.1f,
                () => { if (IsCurrentPlayerAction(player, gloveGuard)) GivePlayerGloves(player); },
                TimerFlags.STOP_ON_MAPCHANGE);

                AddTimer(
                0.25f,
                () => { if (IsCurrentPlayerAction(player, gloveGuard)) GivePlayerGloves(player); },
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
                            object previewToken = new();
                            _playerPreviewTokens[slot] = previewToken;
                            _playerWeaponImage[slot] = image;

                            AddTimer(2.0f, () =>
                            {
                                // Managed cleanup must also happen after death;
                                // the unique token already protects newer previews/connections.
                                if (!_playerPreviewTokens.TryGetValue(slot, out var currentToken) ||
                                    !ReferenceEquals(currentToken, previewToken))
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
                                _playerPreviewTokens.Remove(slot);

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

                var sync = WeaponSync;
                if (sync is not null && GPlayersAgent.TryGetValue(playerInfo.Slot, out var agents))
                    _ = QueuePlayerCosmeticWork(playerInfo.Slot,
                        () => sync.SyncAgentToDatabase(playerInfo, agents));
                GivePlayerAgent(player);
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

                string name =
                    agentObject["agent_name"]?.ToString() ?? "";

                if (name.Length == 0)
                    continue;

                string? agentModel =
                    agentObject["model"]?.ToString();

                var agentOption = agentsSelectionMenu.AddMenuOption(
                    name,
                    HandleAgentSelection);

                SetMenuOptionSelection(
                    agentsSelectionMenu,
                    agentOption,
                    target => GetAgentSelectionMarker(
                        target,
                        agentModel)
                );
            }

            agentsSelectionMenu.Open(player);
        };
    }
}
