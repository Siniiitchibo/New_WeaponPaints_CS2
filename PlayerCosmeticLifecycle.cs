using System.Collections.Concurrent;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace WeaponPaints;

public partial class WeaponPaints
{
    private sealed class CosmeticSession
    {
        public required ulong SteamId { get; init; }
        public required int? UserId { get; init; }
        public bool Ready { get; set; }
    }

    private readonly ConcurrentDictionary<int, CosmeticSession>
        _cosmeticSessions = new();

    private readonly object _cosmeticQueueLock = new();

    private readonly Dictionary<int, Task> _cosmeticQueueTails = new();

    private volatile bool _cosmeticWorkActive = true;

    private Task QueuePlayerCosmeticWork(
        int slot,
        Func<Task> work)
    {
        lock (_cosmeticQueueLock)
        {
            Task previous = _cosmeticQueueTails.TryGetValue(
                slot, out var pending)
                ? pending
                : Task.CompletedTask;

            Task next = Task.Run(async () =>
            {
                await previous.ConfigureAwait(false);

                try
                {
                    for (int attempt = 0; ; attempt++)
                    {
                        try
                        {
                            await work().ConfigureAwait(false);
                            break;
                        }
                        catch (MySqlConnector.MySqlException ex) when (ex.IsTransient && attempt < 2)
                        {
                            await Task.Delay(150 * (attempt + 1)).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(
                        ex,
                        "Cosmetic operation failed for slot {Slot}.",
                        slot
                    );
                }
            });

            _cosmeticQueueTails[slot] = next;

            return next;
        }
    }

    private static PlayerInfo CaptureCosmeticPlayerInfo(
        CCSPlayerController player)
    {
        return new PlayerInfo
        {
            UserId = player.UserId,
            Slot = player.Slot,
            Index = (int)player.Index,
            SteamId = player.SteamID.ToString(),
            Name = player.PlayerName,
            IpAddress = player.IpAddress?.Split(':')[0]
        };
    }

    private bool IsCurrentCosmeticSession(
        int slot,
        CosmeticSession session)
    {
        return _cosmeticWorkActive &&
               _cosmeticSessions.TryGetValue(slot, out var current) &&
               ReferenceEquals(current, session);
    }

    private bool ArePlayerCosmeticsReady(CCSPlayerController player)
    {
        return player.IsValid &&
               _cosmeticSessions.TryGetValue(
                   player.Slot, out var session) &&
               session.Ready &&
               session.SteamId == player.SteamID &&
               session.UserId == player.UserId;
    }

    private static void ClearPlayerCosmeticCache(int slot)
    {
        GPlayerWeaponsInfo.TryRemove(slot, out _);
        GPlayersKnife.TryRemove(slot, out _);
        GPlayersGlove.TryRemove(slot, out _);
        GPlayersAgent.TryRemove(slot, out _);
    }

    private void LoadPlayerCosmetics(CCSPlayerController player)
    {
        var sync = WeaponSync;

        if (!_cosmeticWorkActive ||
            sync is null ||
            !player.IsValid ||
            player.IsBot ||
            player.UserId is null)
        {
            return;
        }

        // Capture native player properties on the game thread.
        PlayerInfo playerInfo = CaptureCosmeticPlayerInfo(player);

        var session = new CosmeticSession
        {
            SteamId = player.SteamID,
            UserId = player.UserId,
            Ready = false
        };

        _cosmeticSessions[playerInfo.Slot] = session;

        _ = QueuePlayerCosmeticWork(playerInfo.Slot, async () =>
        {
            // Ignore a queued load belonging to an old connection.
            if (!IsCurrentCosmeticSession(playerInfo.Slot, session))
                return;

            ClearPlayerCosmeticCache(playerInfo.Slot);

            await sync.GetPlayerData(playerInfo).ConfigureAwait(false);

            if (!IsCurrentCosmeticSession(playerInfo.Slot, session))
                return;

            Server.NextFrame(() =>
            {
                if (!IsCurrentCosmeticSession(playerInfo.Slot, session))
                    return;

                var currentPlayer = Utilities.GetPlayerFromSlot(
                    playerInfo.Slot
                );

                if (currentPlayer is null ||
                    !currentPlayer.IsValid ||
                    currentPlayer.IsBot ||
                    currentPlayer.SteamID != session.SteamId ||
                    currentPlayer.UserId != session.UserId)
                {
                    return;
                }

                session.Ready = true;

                if (Logger.IsEnabled(LogLevel.Information))
                    Logger.LogInformation("Cosmetics loaded for {SteamId}, slot {Slot}.",
                        playerInfo.SteamId, playerInfo.Slot);

                if (!currentPlayer.PawnIsAlive)
                    return;

                GivePlayerGloves(currentPlayer);
                if (Config.Additional.SkinEnabled || Config.Additional.KnifeEnabled)
                    RefreshWeapons(currentPlayer);
                GivePlayerAgent(currentPlayer);
            });
        });
    }

    private void SaveConnectedPlayerCosmetics()
    {
        var sync = WeaponSync;
        if (sync is null)
            return;
        foreach (var player in Utilities.GetPlayers())
        {
            if (!Utility.IsPlayerValid(player) || !ArePlayerCosmeticsReady(player))
                continue;
            var info = CaptureCosmeticPlayerInfo(player);
            var snapshot = CosmeticSnapshot.Capture(info.Slot);
            _ = QueuePlayerCosmeticWork(info.Slot,
                () => sync.SyncWeaponPaintsToDatabase(info, snapshot));
        }
    }

    private void DrainCosmeticWrites()
    {
        Task[] pending;
        lock (_cosmeticQueueLock)
            pending = _cosmeticQueueTails.Values.ToArray();
        try
        {
            if (!Task.WhenAll(pending).Wait(TimeSpan.FromSeconds(5)))
                Logger.LogWarning("Cosmetic DB writes are still finishing after the unload timeout.");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to finish cosmetic DB writes during unload.");
        }
    }

}