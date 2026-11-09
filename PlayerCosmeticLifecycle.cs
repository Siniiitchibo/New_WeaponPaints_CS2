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

                if (!_cosmeticWorkActive)
                    return;

                try
                {
                    await work().ConfigureAwait(false);
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

                Logger.LogInformation(
                    "Cosmetics loaded for {SteamId}, slot {Slot}.",
                    playerInfo.SteamId,
                    playerInfo.Slot
                );

                if (!currentPlayer.PawnIsAlive)
                    return;

                GivePlayerGloves(currentPlayer);
                RefreshWeapons(currentPlayer);
                GivePlayerAgent(currentPlayer);
            });
        });
    }
}