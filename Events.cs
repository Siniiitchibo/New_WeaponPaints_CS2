using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using Microsoft.Extensions.Logging;

namespace WeaponPaints
{
	public partial class WeaponPaints
	{
		private bool _giveNamedItemHooked;

        [GameEventHandler]
        public HookResult OnClientFullConnect(
            EventPlayerConnectFull @event,
            GameEventInfo info)
        {
            var player = @event.Userid;

            if (player is null ||
                !player.IsValid ||
                player.IsBot ||
                WeaponSync is null ||
                Database is null)
            {
                return HookResult.Continue;
            }

            int slot = player.Slot;

            Players.RemoveAll(existing =>
                !existing.IsValid || existing.Slot == slot);

            Players.Add(player);

            LoadPlayerCosmetics(player);

            return HookResult.Continue;
        }

        [GameEventHandler]
        public HookResult OnPlayerDisconnect(
            EventPlayerDisconnect @event,
            GameEventInfo info)
        {
            var player = @event.Userid;

            if (player is null || !player.IsValid || player.IsBot)
                return HookResult.Continue;

            PlayerInfo playerInfo = CaptureCosmeticPlayerInfo(player);
            int slot = playerInfo.Slot;

            // Immediately invalidate callbacks belonging to this connection.
            _cosmeticSessions.TryRemove(slot, out _);

            _temporaryPlayerWeaponWear.TryRemove(slot, out _);
            CommandsCooldown.Remove(slot);
            _wsKnifeTeams.TryRemove(slot, out _);
            _wsGloveTeams.TryRemove(slot, out _);
            _playerWeaponImage.Remove(slot);

            Players.RemoveAll(existing =>
                !existing.IsValid || existing.Slot == slot);

            var sync = WeaponSync;

            // A subsequent connection's load will wait for this operation.
            _ = QueuePlayerCosmeticWork(slot, async () =>
            {
                try
                {
                    if (sync is not null)
                    {
                        await sync.SyncStatTrakToDatabase(playerInfo)
                            .ConfigureAwait(false);
                    }
                }
                finally
                {
                    ClearPlayerCosmeticCache(slot);
                }
            });

            return HookResult.Continue;
        }

        private void OnMapStart(string mapName)
        {
            _fadeSeed = 0;
            _nextItemId = MinimumCustomItemId;
        }

        private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
		{
			CCSPlayerController? player = @event.Userid;

			if (player is null || !player.IsValid || Config.Additional is { KnifeEnabled: false, GloveEnabled: false })
				return HookResult.Continue;

			CCSPlayerPawn? pawn = player.PlayerPawn.Value;

			if (pawn == null || !pawn.IsValid)
				return HookResult.Continue;

			GivePlayerAgent(player);
			Server.NextFrame(() =>
			{
				GivePlayerGloves(player);
			});

			return HookResult.Continue;
		}

		private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
		{
			_gBCommandsAllowed = false;
			return HookResult.Continue;
		}

		private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
		{
			_gBCommandsAllowed = true;
			return HookResult.Continue;
		}

		private HookResult OnGiveNamedItemPost(DynamicHook hook)
		{
			try
			{
				var itemServices = hook.GetParam<CCSPlayer_ItemServices>(0);
				var weapon = hook.GetReturn<CBasePlayerWeapon>();
				if (itemServices == null || weapon == null || !weapon.IsValid ||
				    weapon.DesignerName?.Contains("weapon") != true)
					return HookResult.Continue;

				var player = GetPlayerFromItemServices(itemServices);
				if (player != null)
				{
					GivePlayerWeaponSkin(player, weapon);
				}
			}
			catch { }

			return HookResult.Continue;
		}

		private void OnEntityCreated(CEntityInstance entity)
		{
			var designerName = entity.DesignerName;

			if (designerName != null && designerName.Contains("weapon"))
			{
				Server.NextWorldUpdate(() =>
				{
					var weapon = new CBasePlayerWeapon(entity.Handle);
					if (!weapon.IsValid) return;

					try
					{
						SteamID? steamid = null;

						if (weapon.OriginalOwnerXuidLow > 0)
							steamid = new SteamID(weapon.OriginalOwnerXuidLow);

						CCSPlayerController? player;

						if (steamid != null && steamid.IsValid())
						{
							player = Players.FirstOrDefault(p => p.IsValid && p.SteamID == steamid.SteamId64);

                            player ??= Utilities.GetPlayerFromSteamId(weapon.OriginalOwnerXuidLow);
                        }
						else
						{
							var ownerPawn = weapon.OwnerEntity.Value?.As<CCSPlayerPawn>();
							player = ownerPawn is { IsValid: true }
								? ownerPawn.Controller.Value?.As<CCSPlayerController>()
								: null;
						}

						if (string.IsNullOrEmpty(player?.PlayerName)) return;
						if (!Utility.IsPlayerValid(player)) return;
						
						GivePlayerWeaponSkin(player, weapon);
					}
					catch (Exception)
					{
					}
				});
			}
		}

		private void OnTick()
		{
			if (!Config.Additional.ShowSkinImage) return;

			foreach (var player in Players)
			{
				if (_playerWeaponImage.TryGetValue(player.Slot, out var value) && !string.IsNullOrEmpty(value))
				{
                    string imageUrl = System.Net.WebUtility.HtmlEncode(value);

                    player.PrintToCenterHtml(
                        $"<img src=\"{imageUrl}\" />");
                }
			}
		}
		
		[GameEventHandler]
		public HookResult OnItemPickup(EventItemPickup @event, GameEventInfo _)
		{
			// if (!IsWindows) return HookResult.Continue;
			var player = @event.Userid;
			if (player == null || !player.IsValid || player.IsBot) return HookResult.Continue;
			if (!@event.Item.Contains("knife")) return HookResult.Continue;
		
			var weaponDefIndex = (int)@event.Defindex;
				
			if (!HasChangedKnife(player, out var _) || !HasChangedPaint(player, weaponDefIndex, out var _))
				return HookResult.Continue;
			
			if (player is { Connected: PlayerConnectedState.Connected, PawnIsAlive: true, PlayerPawn.IsValid: true })
			{
				GiveOnItemPickup(player);
			}
		
			return HookResult.Continue;
		}

		private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
		{
			CCSPlayerController? player = @event.Attacker;
			CCSPlayerController? victim = @event.Userid;

			if (player is null || !player.IsValid)
				return HookResult.Continue;
			
			if (victim == null || !victim.IsValid || victim == player)
				return HookResult.Continue;
			
			CBasePlayerWeapon? weapon = player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;

			if (weapon == null) return HookResult.Continue;

			int weaponDefIndex = weapon.AttributeManager.Item.ItemDefinitionIndex;

			if (!HasChangedPaint(player, weaponDefIndex, out var weaponInfo) || weaponInfo == null)
				return HookResult.Continue;
				
			if (!weaponInfo.StatTrak) return HookResult.Continue;
			
			weaponInfo.StatTrakCount += 1;
				
			CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, "kill eater", ViewAsFloat((uint)weaponInfo.StatTrakCount));
			CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, "kill eater score type", 0);
				
			CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.AttributeList.Handle, "kill eater", ViewAsFloat((uint)weaponInfo.StatTrakCount));
			CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.AttributeList.Handle, "kill eater score type", 0);

			return HookResult.Continue;
		}

		private void RegisterListeners()
		{
			RegisterListener<Listeners.OnMapStart>(OnMapStart);

			RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
			RegisterEventHandler<EventRoundStart>(OnRoundStart);
			RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
			RegisterListener<Listeners.OnEntitySpawned>(OnEntityCreated);
			RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);

			if (Config.Additional.ShowSkinImage)
				RegisterListener<Listeners.OnTick>(OnTick);

			VirtualFunctions.GiveNamedItemFunc.Hook(OnGiveNamedItemPost, HookMode.Post);
			_giveNamedItemHooked = true;
		}
	}
}