using CounterStrikeSharp.API.Modules.Utils;

namespace WeaponPaints;

internal sealed record WeaponSaveEntry(CsTeam Team, int Defindex, WeaponInfo Info);

internal static class CosmeticSnapshot
{
    // DB workers must not read mutable slot caches after an await. A connection
    // can leave/rejoin, and even the same player's next selection can change them.
    internal static WeaponSaveEntry[] Capture(int slot, int? defindex = null)
    {
        if (!WeaponPaints.GPlayerWeaponsInfo.TryGetValue(slot, out var teams))
            return [];
        return teams.SelectMany(team => team.Value
            .Where(weapon => defindex is null || weapon.Key == defindex)
            .Select(weapon => new WeaponSaveEntry(team.Key, weapon.Key, Copy(weapon.Value))))
            .ToArray();
    }

    internal static WeaponInfo Copy(WeaponInfo info) => new()
    {
        Paint = info.Paint,
        Seed = info.Seed,
        Wear = float.IsFinite(info.Wear) ? Math.Clamp(info.Wear, 0f, 1f) : 0.01f,
        CustomWear = info.CustomWear,
        Nametag = info.Nametag,
        StatTrak = info.StatTrak,
        StatTrakCount = Math.Max(0, info.StatTrakCount),
        KeyChain = info.KeyChain is { } key ? new KeyChainInfo
        {
            Id = key.Id, OffsetX = key.OffsetX, OffsetY = key.OffsetY,
            OffsetZ = key.OffsetZ, Seed = key.Seed
        } : null,
        Stickers = info.Stickers.Select(sticker => new StickerInfo
        {
            Id = sticker.Id, Schema = sticker.Schema, OffsetX = sticker.OffsetX,
            OffsetY = sticker.OffsetY, Wear = sticker.Wear, Scale = sticker.Scale,
            Rotation = sticker.Rotation
        }).ToList()
    };
}
