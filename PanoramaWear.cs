using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Utils;

namespace WeaponPaints;

public partial class WeaponPaints
{
    private sealed record WearMenuContext(int Defindex, bool Gloves, CsTeam[] Teams);

    private static readonly string[] WearConditionKeys =
    [
        "wp_wear_factory_new", "wp_wear_minimal_wear", "wp_wear_field_tested",
        "wp_wear_well_worn", "wp_wear_battle_scarred"
    ];

    private static void SetWearMenuContext(
        IMenu menu, int defindex, bool gloves, CsTeam[] teams)
    {
        if (menu is PanoramaMenu panorama)
            panorama.Wear = new WearMenuContext(defindex, gloves, [.. teams]);
    }

    private static bool TryGetWearInfo(
        CCSPlayerController player, WearMenuContext context, CsTeam team,
        out WeaponInfo info)
    {
        info = null!;
        if (context.Gloves &&
            (!GPlayersGlove.TryGetValue(player.Slot, out var gloves) ||
             !gloves.TryGetValue(team, out ushort activeGlove) ||
             activeGlove != context.Defindex))
            return false;

        return GPlayerWeaponsInfo.TryGetValue(player.Slot, out var playerWeapons) &&
               playerWeapons.TryGetValue(team, out var teamWeapons) &&
               teamWeapons.TryGetValue(context.Defindex, out info!) && info.Paint > 0;
    }

    private static float GetActiveGloveWear(CCSPlayerController player, CsTeam team)
    {
        if (GPlayersGlove.TryGetValue(player.Slot, out var gloves) &&
            gloves.TryGetValue(team, out ushort defindex) && defindex != 0 &&
            GPlayerWeaponsInfo.TryGetValue(player.Slot, out var weapons) &&
            weapons.TryGetValue(team, out var teamWeapons) &&
            teamWeapons.TryGetValue(defindex, out var info) && float.IsFinite(info.Wear))
            return Math.Clamp(info.Wear, 0f, 1f);

        return 0f;
    }

    private static bool UsesKnifeWearFix(WearMenuContext context, WeaponInfo info) =>
        !context.Gloves && context.Defindex is >= 500 and < 5000 && info.Paint is 409 or 413;

    private static (int Slot, int Count) GetKnifeWearModelSlot(int defindex, int paint) =>
        _skinCatalog.KnifeModel(defindex, paint);

    private void DrawPanoramaWear(CCSPlayerController player, PanoramaSession session)
    {
        var panel = _panoramaSub;
        if (panel is null)
            return;

        var context = session.Menu.Wear;
        panel.SetClass(player, "submenu_wear", "submenu-hidden", context is null);
        if (context is null)
            return;

        panel.SetText(player, "submenu_wear_title", Localizer["wp_wear_title"].Value);
        for (int index = 0; index < WearConditionKeys.Length; index++)
            panel.SetText(player, $"submenu_wear_legend_{index}",
                Localizer[WearConditionKeys[index] + "_short"].Value);

        WeaponInfo? displayedInfo = null;
        bool mixed = false;
        // Prefer the player's current side when applying settings to both sides.
        foreach (var team in context.Teams.OrderByDescending(team => team == player.Team))
        {
            if (!TryGetWearInfo(player, context, team, out var info))
                continue;

            if (displayedInfo is null)
                displayedInfo = info;
            else
                mixed |= Math.Abs(displayedInfo.Wear - info.Wear) > 0.00001f ||
                    (displayedInfo.CustomWear != info.CustomWear &&
                     (UsesKnifeWearFix(context, displayedInfo) || UsesKnifeWearFix(context, info)));
        }

        bool enabled = displayedInfo is not null;
        panel.SetClass(player, "submenu_wear_track", "wear-disabled", !enabled);
        panel.SetClass(player, "submenu_wear_marker", "submenu-hidden", !enabled);

        if (!enabled)
        {
            panel.SetText(player, "submenu_wear_value", Localizer["wp_wear_choose_skin"].Value);
            return;
        }

        float wear = float.IsFinite(displayedInfo!.Wear)
            ? Math.Clamp(displayedInfo.Wear, 0f, 1f) : 0f;
        bool automatic = UsesKnifeWearFix(context, displayedInfo) && !displayedInfo.CustomWear;
        if (automatic)
        {
            var model = GetKnifeWearModelSlot(context.Defindex, displayedInfo.Paint);
            wear = KnifeWearPolicy.AutomaticWear(model.Slot);
        }
        string condition = Localizer[WearConditionKeys[WearBar.Condition(wear)]].Value;
        // Four decimals prevent the end of FN (0.0699) from displaying as 0.070.
        string value = wear.ToString("0.0000", System.Globalization.CultureInfo.CurrentCulture);
        panel.SetText(player, "submenu_wear_value",
            Localizer[mixed ? "wp_wear_value_mixed" : automatic ? "wp_wear_value_auto" : "wp_wear_value", condition, value].Value);
        panel.SetVariant(player, "submenu_wear_marker", "wear-position-",
            WearBar.NearestStep(wear).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private void HandlePanoramaWearClick(
        CCSPlayerController player, PanoramaSession session, string buttonId)
    {
        if (session.IsMain || session.Menu.Wear is not { } context ||
            !WearBar.TryParseButton(buttonId, out int step) ||
            DateTime.UtcNow < session.NextWearChangeUtc)
            return;

        List<WeaponInfo> targets = [];
        foreach (var team in context.Teams)
        {
            if (!TryGetWearInfo(player, context, team, out var info))
                continue;
            targets.Add(info);
        }
        if (targets.Count == 0)
            return;

        float wear = WearBar.ValueAt(step);
        if (targets.All(info => info.CustomWear && Math.Abs(info.Wear - wear) < 0.000001f))
            return;

        // RefreshWeapons recreates items after 0.23 s; prevent overlapping refreshes.
        session.NextWearChangeUtc = DateTime.UtcNow.AddSeconds(0.5);
        foreach (var info in targets)
        {
            info.Wear = wear;
            info.CustomWear = true;
        }

        // Sticker refreshes cache their own small wear increment. Start from the
        // newly selected wear rather than an older cached value.
        if (_temporaryPlayerWeaponWear.TryGetValue(player.Slot, out var temporaryWear))
            temporaryWear.TryRemove(context.Defindex, out _);

        if (context.Gloves)
        {
            SavePanoramaGloveWear(player, context.Defindex);
        }
        else if (WeaponDefindex.TryGetValue(context.Defindex, out string? classname))
        {
            SaveWsWeaponSettings(player, classname);
        }

        // Keep the skin list and page open; only the footer needs updating.
        DrawPanoramaWear(player, session);
    }

    private void SavePanoramaGloveWear(CCSPlayerController player, int defindex)
    {
        if (player.PawnIsAlive)
            GivePlayerGloves(player);

        var sync = WeaponSync;
        if (sync is null)
            return;

        var playerInfo = CaptureCosmeticPlayerInfo(player);
        var snapshot = CosmeticSnapshot.Capture(playerInfo.Slot, defindex);
        _ = QueuePlayerCosmeticWork(playerInfo.Slot,
            () => sync.SyncWeaponPaintsToDatabase(playerInfo, snapshot));
    }
}
