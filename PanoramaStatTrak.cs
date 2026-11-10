using System.Collections.Concurrent;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace WeaponPaints;

public partial class WeaponPaints
{
    private static WeaponInfo GetOrCreateMenuWeaponInfo(
        CCSPlayerController player, WearMenuContext context, CsTeam team)
    {
        var playerWeapons = GPlayerWeaponsInfo.GetOrAdd(player.Slot,
            _ => new ConcurrentDictionary<CsTeam, ConcurrentDictionary<int, WeaponInfo>>());
        var teamWeapons = playerWeapons.GetOrAdd(team, _ => new ConcurrentDictionary<int, WeaponInfo>());
        return teamWeapons.GetOrAdd(context.Defindex, _ => new WeaponInfo { Wear = 0.01f });
    }

    private static bool IsMenuStatTrakEnabled(
        CCSPlayerController player, WearMenuContext context, CsTeam team) =>
        GPlayerWeaponsInfo.TryGetValue(player.Slot, out var weapons) &&
        weapons.TryGetValue(team, out var teamWeapons) &&
        teamWeapons.TryGetValue(context.Defindex, out var info) && info.StatTrak;

    private void DrawPanoramaStatTrak(CCSPlayerController player, PanoramaSession session)
    {
        var panel = _panoramaSub;
        if (panel is null)
            return;
        var context = session.Menu.Wear;
        bool visible = context is { Gloves: false };
        panel.SetClass(player, "submenu_stattrak", "submenu-hidden", !visible);
        if (!visible)
            return;

        int enabledTeams = context!.Teams.Count(team => IsMenuStatTrakEnabled(player, context, team));
        bool all = enabledTeams == context.Teams.Length && enabledTeams > 0;
        bool mixed = enabledTeams > 0 && !all;
        panel.SetClass(player, "submenu_stattrak", "stattrak-checked", all);
        panel.SetClass(player, "submenu_stattrak", "stattrak-mixed", mixed);
        panel.SetText(player, "submenu_stattrak_text",
            Localizer[mixed ? "wp_stattrak_checkbox_mixed" : "wp_stattrak_checkbox"].Value);
    }

    private void HandlePanoramaStatTrakClick(CCSPlayerController player, PanoramaSession session)
    {
        if (session.IsMain || session.Menu.Wear is not { Gloves: false } context ||
            DateTime.UtcNow < session.NextWearChangeUtc ||
            !WeaponDefindex.TryGetValue(context.Defindex, out string? classname))
            return;

        session.NextWearChangeUtc = DateTime.UtcNow.AddSeconds(0.5);
        // Mixed sides become enabled on both sides on the first click.
        bool enable = !context.Teams.All(team => IsMenuStatTrakEnabled(player, context, team));
        foreach (var team in context.Teams)
            GetOrCreateMenuWeaponInfo(player, context, team).StatTrak = enable;

        // Existing counters and the list's current page remain unchanged.
        SaveWsWeaponSettings(player, classname);
        DrawPanoramaStatTrak(player, session);
    }
}
