using CounterStrikeSharp.API.Core;

namespace WeaponPaints;

public partial class WeaponPaints
{
    private int _mapGeneration;

    private sealed record PlayerActionGuard(
        ulong SteamId, int? UserId, nint PawnHandle, int Team, int MapGeneration);

    private PlayerActionGuard CapturePlayerAction(CCSPlayerController player) => new(
        player.SteamID, player.UserId, player.PlayerPawn.Value?.Handle ?? 0,
        player.TeamNum, _mapGeneration);

    private bool IsCurrentPlayerAction(CCSPlayerController player, PlayerActionGuard guard) =>
        _cosmeticWorkActive && player.IsValid &&
        guard.MapGeneration == _mapGeneration &&
        player.SteamID == guard.SteamId && player.UserId == guard.UserId &&
        player.TeamNum == guard.Team && player.PawnIsAlive &&
        player.PlayerPawn.Value is { IsValid: true } pawn && pawn.Handle == guard.PawnHandle;

    private sealed class WeaponRefreshState(PlayerActionGuard guard, string? preferred)
    {
        public PlayerActionGuard Guard { get; } = guard;
        public string? Preferred { get; set; } = preferred;
        public bool Pending { get; set; }
        public HashSet<string?> PendingClasses { get; } = [];
    }

    private readonly Dictionary<int, WeaponRefreshState> _weaponRefreshes = [];
}
