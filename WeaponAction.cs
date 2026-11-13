using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace WeaponPaints
{
	public partial class WeaponPaints
	{
        private readonly Dictionary<(int Paint, int Seed, int WearStep), int>
            _knifeWearModels = new();

        private float ReserveKnifeWear(
            int definitionIndex, int paint, int seed, float wear, bool manual = false)
        {
            if (paint <= 0 || !float.IsFinite(wear) || wear < 0f || wear > 1f)
                return wear;

            int requestedStep = (int)Math.Round(wear * 1000d);
            var requestedKey = (paint, seed, requestedStep);

            if (_knifeWearModels.TryAdd(requestedKey, definitionIndex) ||
                _knifeWearModels[requestedKey] == definitionIndex ||
                manual)
            {
                return wear;
            }

            int condition = WearBar.Condition(wear);
            int firstPosition = condition * WearBar.StepsPerCondition;
            int lastPosition = firstPosition + WearBar.StepsPerCondition - 1;
            int firstStep = (int)Math.Floor(WearBar.ValueAt(firstPosition) * 1000d);
            int lastStep = (int)Math.Ceiling(WearBar.ValueAt(lastPosition) * 1000d);

            int maxDistance = Math.Max(requestedStep - firstStep, lastStep - requestedStep);
            for (int distance = 1; distance <= maxDistance; distance++)
            {
                for (int direction = -1; direction <= 1; direction += 2)
                {
                    int step = requestedStep + distance * direction;
                    if (step < firstStep || step > lastStep)
                        continue;

                    float candidate = Math.Max(0.0001f, step / 1000f);
                    if (WearBar.Condition(candidate) != condition)
                        continue;

                    var key = (paint, seed, step);
                    if (_knifeWearModels.TryAdd(key, definitionIndex) ||
                        _knifeWearModels[key] == definitionIndex)
                    {
                        return candidate;
                    }
                }
            }

            Logger.LogWarning(
                "[WPCheck] No free knife wear step for def={Def} paint={Paint} seed={Seed} wear={Wear:R}",
                definitionIndex, paint, seed, wear);

            return wear;
        }

        private static float GetRenderedWear(CCSPlayerController player, int defindex, int paint, float selectedWear, uint attempt = 0)
        {
            const int scale = 10_000;
            const int maxOffset = 300;   // 0.0300
            const int minSideGap = 80;  // 0.0080

            float wear = float.IsFinite(selectedWear)
                ? Math.Clamp(selectedWear, 0f, 1f)
                : 0.01f;

            int selected = (int)MathF.Round(wear * scale);
            int firstStep = WearBar.Condition(wear) * WearBar.StepsPerCondition;
            int lastStep = firstStep + WearBar.StepsPerCondition - 1;

            int conditionMin = (int)MathF.Round(WearBar.ValueAt(firstStep) * scale);
            int conditionMax = (int)MathF.Round(WearBar.ValueAt(lastStep) * scale);

            int min = Math.Max(1, Math.Max(conditionMin, selected - maxOffset));
            int max = Math.Min(scale - 1, Math.Min(conditionMax, selected + maxOffset));
            int split = min + (max - min - minSideGap) / 2;

            unchecked
            {
                ulong steamId = player.SteamID;
                uint sharedSeed = Mix(
                    (uint)steamId ^ (uint)(steamId >> 32) ^
                    ((uint)paint * 0x9E3779B9u) ^ (uint)selected);

                bool ctGetsLowerRange = (sharedSeed & 1u) == 0;
                bool useLowerRange = (player.Team == CsTeam.CounterTerrorist) == ctGetsLowerRange;

                int lower = useLowerRange ? min : split + minSideGap;
                int upper = useLowerRange ? split : max;

                uint itemSeed = Mix(sharedSeed ^ ((uint)defindex * 0x85EBCA6Bu));
                int candidateCount = upper - lower + 1;
                int stride = (candidateCount - 1) / 2;
                if (candidateCount % 4 == 2)
                    stride--;

                ulong offset = (ulong)itemSeed + (ulong)attempt * (uint)stride;
                int rendered = lower + (int)(offset % (uint)candidateCount);

                return rendered / (float)scale;
            }

            static uint Mix(uint value)
            {
                unchecked
                {
                    value ^= value >> 16;
                    value *= 0x7FEB352Du;
                    value ^= value >> 15;
                    value *= 0x846CA68Bu;
                    return value ^ (value >> 16);
                }
            }
        }
        private void GivePlayerWeaponSkin(
            CCSPlayerController player,
            CBasePlayerWeapon weapon,
            bool fromGiveNamedItem = false)
        {
            if (!ArePlayerCosmeticsReady(player))
                return;

            if (!weapon.IsValid)
                return;

            var designerName = weapon.DesignerName ?? "";

            bool isKnife =
                designerName.Contains("knife") ||
                designerName.Contains("bayonet");

            if (isKnife && Config.Additional.KnifeEnabled && Config.Additional.GiveRandomKnife)
            {
                var knives = GPlayersKnife.GetOrAdd(player.Slot,
                    _ => new ConcurrentDictionary<CsTeam, string>());
                if (!knives.ContainsKey(player.Team))
                {
                    var choices = WeaponDefindex.Where(pair => pair.Key >= 500).Select(pair => pair.Value).ToArray();
                    if (choices.Length > 0)
                        knives[player.Team] = choices[Random.Shared.Next(choices.Length)];
                }
            }

            switch (isKnife)
            {
                case true when !Config.Additional.KnifeEnabled || !HasChangedKnife(player, out _):
                    return;

                case true:
                    {
                        var newDefIndex = WeaponDefindex.FirstOrDefault(
                            x => x.Value == GPlayersKnife[player.Slot][player.Team]);

                        if (newDefIndex.Key == 0)
                            return;

                        if (weapon.AttributeManager.Item.ItemDefinitionIndex != newDefIndex.Key)
                        {
                            SubclassChange(weapon, (ushort)newDefIndex.Key);
                        }

                        weapon.AttributeManager.Item.ItemDefinitionIndex =
                            (ushort)newDefIndex.Key;

                        weapon.AttributeManager.Item.EntityQuality = 3;

                        weapon.AttributeManager.Item.AttributeList.Attributes.RemoveAll();
                        weapon.AttributeManager.Item.NetworkedDynamicAttributes.Attributes.RemoveAll();

                        break;
                    }

                default:
                    weapon.AttributeManager.Item.EntityQuality = 0;
                    break;
            }

            if (!Config.Additional.SkinEnabled)
                return;

            UpdatePlayerEconItemId(weapon.AttributeManager.Item);

            int weaponDefIndex = weapon.AttributeManager.Item.ItemDefinitionIndex;
			int fallbackPaintKit;
			
			weapon.AttributeManager.Item.AccountID = (uint)player.SteamID;
			
			bool isLegacyModel;

			if (_config.Additional.GiveRandomSkin &&
			    !HasChangedPaint(player, weaponDefIndex, out _))
			{
				// Random skins
				weapon.FallbackPaintKit = GetRandomPaint(weaponDefIndex);
				weapon.FallbackSeed = 0;
                weapon.FallbackWear = isKnife
                    ? GetRenderedWear(player, weaponDefIndex, weapon.FallbackPaintKit, 0f)
                    : 0.01f;

                if (isKnife)
                {
                    weapon.FallbackWear = ReserveKnifeWear(
                        weaponDefIndex,
                        weapon.FallbackPaintKit,
                        weapon.FallbackSeed,
                        weapon.FallbackWear);
                }

                weapon.AttributeManager.Item.NetworkedDynamicAttributes.Attributes.RemoveAll();
				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, "set item texture prefab", weapon.FallbackPaintKit);
				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, "set item texture seed", 0);
				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, "set item texture wear", weapon.FallbackWear);
			
				weapon.AttributeManager.Item.AttributeList.Attributes.RemoveAll();
				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.AttributeList.Handle, "set item texture prefab", weapon.FallbackPaintKit);
				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.AttributeList.Handle, "set item texture seed", 0);
				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.AttributeList.Handle, "set item texture wear", weapon.FallbackWear);
			
				fallbackPaintKit = weapon.FallbackPaintKit;
			
				if (fallbackPaintKit == 0)
					return;
            isLegacyModel = _skinCatalog.IsLegacy(weaponDefIndex, fallbackPaintKit);
				UpdatePlayerWeaponMeshGroupMask(player, weapon, isLegacyModel);
				return;
			}

			if (!HasChangedPaint(player, weaponDefIndex, out var weaponInfo) || weaponInfo == null)
				return;

			//Log($"Apply on {weapon.DesignerName}({weapon.AttributeManager.Item.ItemDefinitionIndex}) paint {gPlayerWeaponPaints[steamId.SteamId64][weapon.AttributeManager.Item.ItemDefinitionIndex]} seed {gPlayerWeaponSeed[steamId.SteamId64][weapon.AttributeManager.Item.ItemDefinitionIndex]} wear {gPlayerWeaponWear[steamId.SteamId64][weapon.AttributeManager.Item.ItemDefinitionIndex]}");

			weapon.AttributeManager.Item.AttributeList.Attributes.RemoveAll();
			weapon.AttributeManager.Item.NetworkedDynamicAttributes.Attributes.RemoveAll();
			
			UpdatePlayerEconItemId(weapon.AttributeManager.Item);

			weapon.AttributeManager.Item.CustomName = weaponInfo.Nametag;
			weapon.FallbackPaintKit = weaponInfo.Paint;
			
			weapon.FallbackSeed = weaponInfo is { Paint: 38, Seed: 0 } ? _fadeSeed++ : weaponInfo.Seed;

            float selectedWear = isKnife && !weaponInfo.CustomWear
                ? 0f
                : weaponInfo.Wear;

            weapon.FallbackWear = weaponInfo.ManualRenderWear
                ?? (isKnife || weaponInfo.CustomWear
                    ? GetRenderedWear(
                        player, weaponDefIndex, weaponInfo.Paint, selectedWear,
                        weaponInfo.RenderWearAttempt)
                    : selectedWear);

            if (isKnife)
            {
                weapon.FallbackWear = ReserveKnifeWear(
                    weaponDefIndex,
                    weapon.FallbackPaintKit,
                    weapon.FallbackSeed,
                    weapon.FallbackWear,
                    manual: weaponInfo.ManualRenderWear.HasValue);
            }

            nint networkAttributes =
			weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle;

            nint itemAttributes =
                weapon.AttributeManager.Item.AttributeList.Handle;

            CAttributeListSetOrAddAttributeValueByName.Invoke(
                networkAttributes,
                "set item texture prefab",
                weapon.FallbackPaintKit
            );

            CAttributeListSetOrAddAttributeValueByName.Invoke(
                networkAttributes,
                "set item texture seed",
                weapon.FallbackSeed
            );

            CAttributeListSetOrAddAttributeValueByName.Invoke(
                networkAttributes,
                "set item texture wear",
                weapon.FallbackWear
            );

            CAttributeListSetOrAddAttributeValueByName.Invoke(
                itemAttributes,
                "set item texture prefab",
                weapon.FallbackPaintKit
            );

            CAttributeListSetOrAddAttributeValueByName.Invoke(
                itemAttributes,
                "set item texture seed",
                weapon.FallbackSeed
            );

            CAttributeListSetOrAddAttributeValueByName.Invoke(
                itemAttributes,
                "set item texture wear",
                weapon.FallbackWear
            );

            if (weaponInfo.StatTrak)
			{			
				weapon.AttributeManager.Item.EntityQuality = 9;

				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, "kill eater", ViewAsFloat((uint)weaponInfo.StatTrakCount));
				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, "kill eater score type", 0);
				
				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.AttributeList.Handle, "kill eater", ViewAsFloat((uint)weaponInfo.StatTrakCount));
				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.AttributeList.Handle, "kill eater score type", 0);
			}

			fallbackPaintKit = weapon.FallbackPaintKit;

			if (fallbackPaintKit == 0)
				return;

            isLegacyModel = _skinCatalog.IsLegacy(weaponDefIndex, fallbackPaintKit);
			UpdatePlayerWeaponMeshGroupMask(player, weapon, isLegacyModel);

            if (fromGiveNamedItem && isKnife &&
                _pendingKnifeSpawnPaint.ContainsKey(weapon.EntityHandle.Raw))
            {
                _pendingKnifeSpawnPaint[weapon.EntityHandle.Raw] = CapturePlayerAction(player);
            }
		}
		
		private void IncrementWearForWeaponWithStickers(CCSPlayerController player, CBasePlayerWeapon weapon)
		{
			int weaponDefIndex = weapon.AttributeManager.Item.ItemDefinitionIndex;
			if (!HasChangedPaint(player, weaponDefIndex, out var weaponInfo) || weaponInfo == null ||
			    weaponInfo.Stickers.Count <= 0) return;
			
			float wearIncrement = 0.001f;
			float currentWear = weaponInfo.Wear;

			var playerWear = _temporaryPlayerWeaponWear.GetOrAdd(player.Slot, _ => new ConcurrentDictionary<int, float>());

			float incrementedWear = playerWear.AddOrUpdate(
				weaponDefIndex,
                Math.Clamp(currentWear + wearIncrement, 0f, 1f),
				(_, oldWear) => Math.Min(oldWear + wearIncrement, 1.0f)
			);

			weapon.FallbackWear = incrementedWear;
		}

		private void SetStickers(CCSPlayerController? player, CBasePlayerWeapon weapon)
		{
			if (player == null || !player.IsValid) return;

			int weaponDefIndex = weapon.AttributeManager.Item.ItemDefinitionIndex;

			if (!HasChangedPaint(player ,weaponDefIndex, out var weaponInfo) || weaponInfo == null)
				return;

			foreach (var sticker in weaponInfo.Stickers)
			{
				int stickerSlot = weaponInfo.Stickers.IndexOf(sticker);

				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
					$"sticker slot {stickerSlot} id", ViewAsFloat(sticker.Id));
				if (sticker.OffsetX != 0 || sticker.OffsetY != 0)
					CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
						$"sticker slot {stickerSlot} schema", 0);
				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
					$"sticker slot {stickerSlot} offset x", sticker.OffsetX);
				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
					$"sticker slot {stickerSlot} offset y", sticker.OffsetY);
				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
					$"sticker slot {stickerSlot} wear", sticker.Wear);
				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
					$"sticker slot {stickerSlot} scale", sticker.Scale);
				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
					$"sticker slot {stickerSlot} rotation", sticker.Rotation);
			}

			if (_temporaryPlayerWeaponWear.TryGetValue(player.Slot, out var playerWear) &&
				playerWear.TryGetValue(weaponDefIndex, out float storedWear))
			{
				weapon.FallbackWear = storedWear;
			}
		}

		private void SetKeychain(CCSPlayerController? player, CBasePlayerWeapon weapon)
		{
			if (player == null || !player.IsValid) return;

			int weaponDefIndex = weapon.AttributeManager.Item.ItemDefinitionIndex;

			if (!HasChangedPaint(player, weaponDefIndex, out var value) || value?.KeyChain == null)
				return;
			
			var keyChain = value.KeyChain;

			CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
				"keychain slot 0 id", ViewAsFloat(keyChain.Id));
			CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
				"keychain slot 0 offset x", keyChain.OffsetX);
			CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
				"keychain slot 0 offset y", keyChain.OffsetY);
			CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
				"keychain slot 0 offset z", keyChain.OffsetZ);
			CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
				"keychain slot 0 seed", ViewAsFloat(keyChain.Seed));
		}

		private static void GiveKnifeToPlayer(CCSPlayerController? player)
		{
			if (!_config.Additional.KnifeEnabled || player == null || !player.IsValid) return;

			if (PlayerHasKnife(player)) return;

			//string knifeToGive = (CsTeam)player.TeamNum == CsTeam.Terrorist ? "weapon_knife_t" : "weapon_knife";
			player.GiveNamedItem(CsItem.Knife);
			Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInventoryServices");
		}

		private static bool PlayerHasKnife(CCSPlayerController? player)
		{

			if (player == null || !player.IsValid || !player.PlayerPawn.IsValid)
			{
				return false;
			}

			if (player.PlayerPawn.Value == null || player.PlayerPawn.Value.WeaponServices == null || player.PlayerPawn.Value.ItemServices == null)
				return false;

			var weapons = player.PlayerPawn.Value.WeaponServices?.MyWeapons;
			if (weapons == null) return false;
			foreach (var weapon in weapons)
			{
				if (!weapon.IsValid || weapon.Value == null || !weapon.Value.IsValid) continue;
				if (weapon.Value.DesignerName.Contains("knife") || weapon.Value.DesignerName.Contains("bayonet"))
				{
					return true;
				}
			}
			return false;
		}

        private void RefreshWeapons(CCSPlayerController? player, string? preferredWeaponClassname = null)
        {
			if (!_gBCommandsAllowed) return;
			if (player == null || !player.IsValid || player.PlayerPawn.Value == null || (LifeState_t)player.LifeState != LifeState_t.LIFE_ALIVE)
				return;
			if (player.PlayerPawn.Value.WeaponServices == null || player.PlayerPawn.Value.ItemServices == null)
				return;

			var weapons = player.PlayerPawn.Value.WeaponServices.MyWeapons;

			if (weapons.Count == 0)
				return;
			if (player.Team is CsTeam.None or CsTeam.Spectator)
				return;

            int refreshSlot = player.Slot;
            var actionGuard = CapturePlayerAction(player);
            if (_weaponRefreshes.TryGetValue(refreshSlot, out var pending) &&
                IsCurrentPlayerAction(player, pending.Guard))
            {
                pending.Pending = true;
                pending.Preferred = preferredWeaponClassname ?? pending.Preferred;
                pending.PendingClasses.Add(preferredWeaponClassname);
                return;
            }
            var refreshState = new WeaponRefreshState(actionGuard, preferredWeaponClassname);
            _weaponRefreshes[refreshSlot] = refreshState;
            AddTimer(0.5f, () =>
            {
                if (!_weaponRefreshes.TryGetValue(refreshSlot, out var current) ||
                    !ReferenceEquals(current, refreshState))
                    return;
                _weaponRefreshes.Remove(refreshSlot);
                if (current.Pending && IsCurrentPlayerAction(player, actionGuard))
                {
                    if (current.PendingClasses.Contains(null))
                        RefreshWeapons(player);
                    else
                        foreach (var classname in current.PendingClasses)
                            RefreshWeapons(player, classname);
                }
            }, TimerFlags.STOP_ON_MAPCHANGE);

			var hasKnife = false;
            var refreshPawnHandle = player.PlayerPawn.Value.Handle;

            Dictionary<string, List<(int, int)>> weaponsWithAmmo = [];

			foreach (var weapon in weapons)
			{
				if (!weapon.IsValid || weapon.Value == null ||
					!weapon.Value.IsValid || !weapon.Value.DesignerName.Contains("weapon_"))
					continue;
				
				CCSWeaponBaseGun gun = weapon.Value.As<CCSWeaponBaseGun>();

				if (weapon.Value.Entity == null) continue;
				if (!weapon.Value.OwnerEntity.IsValid) continue;
				if (gun.Entity == null) continue;
				if (!gun.IsValid) continue;

				try
				{
					CCSWeaponBaseVData? weaponData = weapon.Value.As<CCSWeaponBase>().VData;

					if (weaponData == null) continue;
                    // Cosmetic changes rebuild only the requested weapon. A
                    // knife-model change must match the old knife's gear slot.
                    // Explicit profile/!wp refreshes pass null and cover all guns.
                    if (preferredWeaponClassname is not null)
                    {
                        bool knifeRequest = preferredWeaponClassname.StartsWith(
                            "weapon_knife", StringComparison.OrdinalIgnoreCase) ||
                            preferredWeaponClassname.Equals("weapon_bayonet", StringComparison.OrdinalIgnoreCase);
                        bool matches = knifeRequest
                            ? weaponData.GearSlot == gear_slot_t.GEAR_SLOT_KNIFE
                            : WeaponDefindex.TryGetValue(weapon.Value.AttributeManager.Item.ItemDefinitionIndex,
                                out var currentClass) && string.Equals(currentClass, preferredWeaponClassname,
                                StringComparison.OrdinalIgnoreCase);
                        if (!matches) continue;
                    }

                    if (weaponData.GearSlot is gear_slot_t.GEAR_SLOT_RIFLE or gear_slot_t.GEAR_SLOT_PISTOL)
                    {
                        if (weapon.Value is not { IsValid: true } originalWeapon ||
                            !WeaponDefindex.TryGetValue(
                                originalWeapon.AttributeManager.Item.ItemDefinitionIndex,
                                out var weaponClassname))
                        {
                            continue;
                        }

                        int clip1 = originalWeapon.Clip1;
                        int reservedAmmo = originalWeapon.ReserveAmmo[0];
                        var weaponHandle = new CHandle<CBasePlayerWeapon>(weapon.Raw);

                        AddTimer(0.1f, () =>
                        {
                            if (!IsCurrentPlayerAction(player, actionGuard))
                                return;

                            var services = player.PlayerPawn.Value?.WeaponServices;
                            var ownedWeapon = weaponHandle.Value;

                            if (services == null ||
                                ownedWeapon is not { IsValid: true } ||
                                !services.MyWeapons.Any(handle => handle.Raw == weaponHandle.Raw))
                            {
                                return;
                            }

                            try
                            {
                                RemoveOwnedKnife.Value.Invoke(services.Handle, ownedWeapon.Handle);

                                if (services.MyWeapons.Any(handle => handle.Raw == weaponHandle.Raw))
                                    return;

                                if (!weaponsWithAmmo.TryGetValue(weaponClassname, out var ammoEntries))
                                {
                                    ammoEntries = [];
                                    weaponsWithAmmo.Add(weaponClassname, ammoEntries);
                                }

                                ammoEntries.Add((clip1, reservedAmmo));
                            }
                            catch (Exception ex)
                            {
                                Logger.LogWarning(ex, "Unable to remove weapon {WeaponClassname}.", weaponClassname);
                            }
                        }, TimerFlags.STOP_ON_MAPCHANGE);
                    }

                    if (weaponData.GearSlot == gear_slot_t.GEAR_SLOT_KNIFE)
                    {
                        hasKnife = true;
                        var knifeHandle = new CHandle<CBasePlayerWeapon>(weapon.Raw);

                        AddTimer(0.1f, () =>
                        {
                            if (!IsCurrentPlayerAction(player, actionGuard))
                                return;

                            var services = player.PlayerPawn.Value?.WeaponServices;
                            var knife = knifeHandle.Value;

                            if (services == null ||
                                knife is not { IsValid: true } ||
                                !services.MyWeapons.Any(handle => handle.Raw == knifeHandle.Raw))
                            {
                                return;
                            }

                            try
                            {
                                RemoveOwnedKnife.Value.Invoke(services.Handle, knife.Handle);
                            }
                            catch (Exception ex)
                            {
                                Logger.LogWarning(ex, "Unable to remove the original knife.");
                            }
                        }, TimerFlags.STOP_ON_MAPCHANGE);
                    }
                }
				catch (Exception ex)
				{
                    Logger.LogWarning(ex, "Unable to refresh a weapon.");
                }
			}

			AddTimer(0.23f, () =>
					{
                        // Once items were removed, restore them even if the round
                        // ended in the meantime, but never on another pawn/session.
                        if (!IsCurrentPlayerAction(player, actionGuard)) return;

						if (!PlayerHasKnife(player) && hasKnife)
						{        
							var newKnife = new CBasePlayerWeapon(player.GiveNamedItem(CsItem.Knife));
							var newWeapon = new CBasePlayerWeapon(player.GiveNamedItem(CsItem.USP));
							player.GiveNamedItem(CsItem.Knife);
							player.ExecuteClientCommand("slot3");

							Server.NextFrame(() =>
							{
								try
								{
                                    if (!IsCurrentPlayerAction(player, actionGuard)) return;
                                    var services = player.PlayerPawn.Value?.WeaponServices;

                                    foreach (var temporaryWeapon in new[] { newKnife, newWeapon })
                                    {
                                        if (temporaryWeapon is not { IsValid: true })
                                            continue;

                                        var raw = temporaryWeapon.EntityHandle.Raw;

                                        if (services != null &&
                                            services.MyWeapons.Any(handle => handle.Raw == raw))
                                        {
                                            RemoveOwnedKnife.Value.Invoke(services.Handle, temporaryWeapon.Handle);
                                        }
                                        else
                                        {
                                            temporaryWeapon.AddEntityIOEvent("Kill", temporaryWeapon, null, "", 0.01f);
                                        }
                                    }
                                }
								catch (Exception ex)
								{
                                    Logger.LogWarning(ex, "Error executing AddEntityIOEvent.");
                                }
							});
						}


						foreach (var entry in weaponsWithAmmo)
						{
							foreach (var ammo in entry.Value)
							{
								var newWeapon = new CBasePlayerWeapon(player.GiveNamedItem(entry.Key));
								Server.NextFrame(() =>
						{
							try
							{
								if (!IsCurrentPlayerAction(player, actionGuard) || !newWeapon.IsValid) return;

								newWeapon.Clip1 = ammo.Item1;
								newWeapon.ReserveAmmo[0] = ammo.Item2;

								IncrementWearForWeaponWithStickers(player, newWeapon);
							}
							catch (Exception ex)
							{
                                Logger.LogWarning(ex, "Error setting weapon properties.");
                            }
						});
							}
						}
					}, TimerFlags.STOP_ON_MAPCHANGE);

            if (!string.IsNullOrWhiteSpace(preferredWeaponClassname))
            {
                string requestedWeapon = preferredWeaponClassname;

                AddTimer(0.4f, () =>
                {
                    if (!_gBCommandsAllowed || !IsCurrentPlayerAction(player, actionGuard))
                    {
                        return;
                    }

                    var currentPawn = player.PlayerPawn.Value;

                    if (currentPawn == null ||
                        !currentPawn.IsValid ||
                        currentPawn.Handle != refreshPawnHandle ||
                        currentPawn.WeaponServices == null)
                    {
                        return;
                    }

                    bool wantsKnife =
                        requestedWeapon.StartsWith(
                            "weapon_knife",
                            StringComparison.OrdinalIgnoreCase) ||
                        requestedWeapon.Equals(
                            "weapon_bayonet",
                            StringComparison.OrdinalIgnoreCase);

                    foreach (var weaponHandle in currentPawn.WeaponServices.MyWeapons)
                    {
                        var weapon = weaponHandle.Value;

                        if (weapon == null || !weapon.IsValid)
                            continue;

                        var weaponData = weapon.As<CCSWeaponBase>().VData;

                        if (weaponData == null)
                            continue;

                        if (wantsKnife)
                        {
                            if (weaponData.GearSlot != gear_slot_t.GEAR_SLOT_KNIFE)
                                continue;

                            player.ExecuteClientCommand("slot3");
                            return;
                        }

                        if (!WeaponDefindex.TryGetValue(
                                weapon.AttributeManager.Item.ItemDefinitionIndex,
                                out var classname) ||
                            !string.Equals(
                                classname,
                                requestedWeapon,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (weaponData.GearSlot == gear_slot_t.GEAR_SLOT_PISTOL)
                        {
                            player.ExecuteClientCommand("slot2");
                            return;
                        }

                        if (weaponData.GearSlot == gear_slot_t.GEAR_SLOT_RIFLE)
                        {
                            player.ExecuteClientCommand("slot1");
                            return;
                        }
                    }
                }, TimerFlags.STOP_ON_MAPCHANGE);
            }
        }

		private void GivePlayerGloves(CCSPlayerController player)
		{
            if (!Config.Additional.GloveEnabled || !ArePlayerCosmeticsReady(player))
                return;
            if (!Utility.IsPlayerValid(player) || (LifeState_t)player.LifeState != LifeState_t.LIFE_ALIVE) return;

			CCSPlayerPawn? pawn = player.PlayerPawn.Value;
			if (pawn == null || !pawn.IsValid)
				return;

            var gloveGuard = CapturePlayerAction(player);
			CEconItemView item = pawn.EconGloves;

			item.NetworkedDynamicAttributes.Attributes.RemoveAll();
			item.AttributeList.Attributes.RemoveAll();

			//force gloves model refresh to prevent model overlap
			player.ExecuteClientCommand("lastinv");
			Instance.AddTimer(0.08f, () =>
			{	
				try
				{
                    if (!IsCurrentPlayerAction(player, gloveGuard) || !pawn.IsValid)
						return;

					if (!GPlayersGlove.TryGetValue(player.Slot, out var gloveInfo) ||
                        !gloveInfo.TryGetValue(player.Team, out var gloveId))
                        return;
                    if (gloveId == 0)
                    {
                        item.ItemDefinitionIndex = 0;
                        item.Initialized = false;
                    }
                    else
                    {
                        if (!HasChangedPaint(player, gloveId, out var weaponInfo) || weaponInfo is null)
                        return;

					item.ItemDefinitionIndex = gloveId;
					
					UpdatePlayerEconItemId(item);

                    float renderedWear = weaponInfo.CustomWear
                        ? GetRenderedWear(player, gloveId, weaponInfo.Paint, weaponInfo.Wear)
                        : weaponInfo.Wear;

                    item.NetworkedDynamicAttributes.Attributes.RemoveAll();
					CAttributeListSetOrAddAttributeValueByName.Invoke(item.NetworkedDynamicAttributes.Handle, "set item texture prefab", weaponInfo.Paint);
					CAttributeListSetOrAddAttributeValueByName.Invoke(item.NetworkedDynamicAttributes.Handle, "set item texture seed", weaponInfo.Seed);
					CAttributeListSetOrAddAttributeValueByName.Invoke(item.NetworkedDynamicAttributes.Handle, "set item texture wear", weaponInfo.Wear);

					item.AttributeList.Attributes.RemoveAll();
					CAttributeListSetOrAddAttributeValueByName.Invoke(item.AttributeList.Handle, "set item texture prefab", weaponInfo.Paint);
					CAttributeListSetOrAddAttributeValueByName.Invoke(item.AttributeList.Handle, "set item texture seed", weaponInfo.Seed);
					CAttributeListSetOrAddAttributeValueByName.Invoke(item.AttributeList.Handle, "set item texture wear", weaponInfo.Wear);

					item.Initialized = true;
                    }

                    //force gloves model refresh to prevent model overlap
                    player.ExecuteClientCommand("lastinv");
					SetBodygroup(pawn, "first_or_third_person", 0);
					AddTimer(0.2f, () =>
					{
						if (IsCurrentPlayerAction(player, gloveGuard) && pawn.IsValid) SetBodygroup(pawn, "first_or_third_person", 1);
					}, TimerFlags.STOP_ON_MAPCHANGE);
				}
				catch (Exception) { }
			}, TimerFlags.STOP_ON_MAPCHANGE);
		}

        private static int GetRandomPaint(int defindex) => _skinCatalog.RandomPaint(defindex);

		//xstage idea on css discord
		public static void SubclassChange(CBasePlayerWeapon weapon, ushort itemD)
		{
			weapon.AcceptInput("ChangeSubclass", value: itemD.ToString());
		}

		public static void SetBodygroup(CCSPlayerPawn pawn, string group, int value)
		{
			pawn.AcceptInput("SetBodygroup", value:$"{group},{value}");
		}

        private void UpdateWeaponMeshGroupMask(
            CBaseEntity weapon,
            bool isLegacy = false)
        {
            if (!weapon.IsValid)
                return;

            var sceneNode = weapon.CBodyComponent?.SceneNode;

            if (sceneNode is null)
                return;

            weapon.AcceptInput(
                "SetBodygroup",
                value: $"body,{(isLegacy ? 1 : 0)}");
        }

        private void UpdatePlayerWeaponMeshGroupMask(CCSPlayerController player, CBasePlayerWeapon weapon, bool isLegacy)
		{
			UpdateWeaponMeshGroupMask(weapon, isLegacy);
		}

		private static void GivePlayerAgent(CCSPlayerController player)
		{
            if (!Instance.Config.Additional.AgentEnabled || !Instance.ArePlayerCosmeticsReady(player))
                return;
            GPlayersAgent.TryGetValue(player.Slot, out var value);
            var model = player.TeamNum == 3 ? value.CT : value.T;

			if (player.PlayerPawn.Value == null)
				return;

            var agentGuard = Instance.CapturePlayerAction(player);
			try
			{
				Server.NextFrame(() =>
				{
                    if (!Instance.IsCurrentPlayerAction(player, agentGuard)) return;

					var pawn = player.PlayerPawn.Value;
					if (pawn == null || !pawn.IsValid) return;

                    int slot = player.Slot;
                    if (string.IsNullOrEmpty(model))
                    {
                        if (Instance._originalAgentModels.TryGetValue(slot, out var original) &&
                            original.Pawn == pawn.Handle)
                        {
                            pawn.SetModel(original.Model);
                            Instance._originalAgentModels.Remove(slot);
                        }
                        return;
                    }
                    if (!Instance._originalAgentModels.TryGetValue(slot, out var saved) || saved.Pawn != pawn.Handle)
                    {
                        string originalModel = pawn.CBodyComponent?.SceneNode?.GetSkeletonInstance()
                            .ModelState.ModelName ?? "";
                        if (!string.IsNullOrEmpty(originalModel))
                            Instance._originalAgentModels[slot] = (pawn.Handle, originalModel);
                    }
                    pawn.SetModel($"agents/models/{model}.vmdl");
				});
			}
			catch (Exception)
			{
			}
		}
		
		private void GiveOnItemPickup(CCSPlayerController player)
		{
			var pawn = player.PlayerPawn.Value;
			if (pawn == null) return;
			
			var myWeapons = pawn.WeaponServices?.MyWeapons;
			if (myWeapons == null) return;
			
			foreach (var handle in myWeapons)
			{
				var weapon = handle.Value;
			
				if (weapon == null || !weapon.IsValid) continue;
				if (myWeapons.Count == 1)
				{
					var newWeapon = new CBasePlayerWeapon(player.GiveNamedItem(CsItem.USP));
					weapon.AddEntityIOEvent("Kill", weapon, null, "", 0.01f);
					player.GiveNamedItem(CsItem.Knife);
					player.ExecuteClientCommand("slot3");
					newWeapon.AddEntityIOEvent("Kill", newWeapon, null, "", 0.01f);
				}
					
				GivePlayerWeaponSkin(player, weapon);
			}
		}

        private void UpdatePlayerEconItemId(CEconItemView econItemView)
        {
            ulong itemId = _nextItemId++;

            econItemView.ItemID = itemId;
            econItemView.ItemIDLow = unchecked((uint)itemId);
            econItemView.ItemIDHigh = (uint)(itemId >> 32);
        }

        private static CCSPlayerController? GetPlayerFromItemServices(CCSPlayer_ItemServices itemServices)
		{
			var pawn = itemServices.Pawn.Value;
			if (pawn == null || !pawn.IsValid || !pawn.Controller.IsValid || pawn.Controller.Value == null) return null;
			var player = new CCSPlayerController(pawn.Controller.Value.Handle);
			return !Utility.IsPlayerValid(player) ? null : player;
		}

		private static bool HasChangedKnife(CCSPlayerController player, out string? knifeValue)
		{
			knifeValue = null;

            if (!Instance.ArePlayerCosmeticsReady(player))
                return false;

            // Check if player has knife info for their slot and team
            if (!GPlayersKnife.TryGetValue(player.Slot, out var knife) ||
			    !knife.TryGetValue(player.Team, out var value) ||
			    value == "weapon_knife") return false;
			knifeValue = value; // Assign the knife value to the out parameter
			return true;
		}
		
		private static bool HasChangedPaint(CCSPlayerController player, int weaponDefIndex, out WeaponInfo? weaponInfo)
		{
			weaponInfo = null;

            if (!Instance.ArePlayerCosmeticsReady(player))
                return false;

            // Check if player has weapons info for their slot and team
            if (!GPlayerWeaponsInfo.TryGetValue(player.Slot, out var teamInfo) || 
			    !teamInfo.TryGetValue(player.Team, out var teamWeapons))
			{
				return false;
			}

			// Check if the specified weapon has a paint/skin change
			if (!teamWeapons.TryGetValue(weaponDefIndex, out var value) || value.Paint <= 0) return false;
			
			weaponInfo = value; // Assign the out variable when it exists
			return true;
		}

		private static float ViewAsFloat(uint value)
		{
			return BitConverter.Int32BitsToSingle((int)value);
		}
	}
}
