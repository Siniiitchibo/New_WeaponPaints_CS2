using System.Net;
using System.Text;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using System.Globalization;
using CounterStrikeSharp.API.Core.Translations;
using CS2UIKit;
using UiPanel = CS2UIKit.Panel;

namespace WeaponPaints;

public partial class WeaponPaints
{
    private UiPanel? _panoramaMain;
    private UiPanel? _panoramaSub;

    private readonly Dictionary<int, PanoramaSession> _panoramaSessions = [];

    private sealed class PanoramaSession
    {
        public required ulong SteamId { get; init; }

        public required PanoramaMenu Menu { get; init; }

        public required bool IsMain { get; init; }

        public int Page { get; set; }

        public Dictionary<string, ChatMenuOption> Bindings { get; } = [];
    }

    private sealed class PanoramaMenu(
        WeaponPaints owner,
        string title,
        Action<CCSPlayerController>? back,
        string? titleKey) : BaseMenu(title)
    {
        public Action<CCSPlayerController>? Back { get; } = back;

        public string? TitleKey { get; } = titleKey;

        public Dictionary<ChatMenuOption, string> TextKeys { get; } = [];

        public string DisplayTitle =>
            TitleKey is null
                ? Title
                : owner.Localizer[TitleKey].Value;

        public string DisplayOption(ChatMenuOption option) =>
            TextKeys.TryGetValue(option, out var key)
                ? owner.Localizer[key].Value
                : option.Text;

        public override void Open(CCSPlayerController player)
        {
            owner.OpenPanoramaMenu(player, this);
        }
    }

    internal IMenu CreatePanoramaMenu(
        string title,
        Action<CCSPlayerController>? back,
        string? titleKey = null)
    {
        return new PanoramaMenu(this, title, back, titleKey);
    }

    private sealed class MenuCulture : IDisposable
    {
        private readonly CultureInfo _culture =
            CultureInfo.CurrentCulture;

        private readonly CultureInfo _uiCulture =
            CultureInfo.CurrentUICulture;

        public MenuCulture(CCSPlayerController player)
        {
            CultureInfo.CurrentCulture =
                CultureInfo.CurrentUICulture =
                    player.GetLanguage();
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _uiCulture;
        }
    }

    private static void SetMenuOptionTranslation(
        IMenu menu,
        ChatMenuOption option,
        string key)
    {
        if (menu is PanoramaMenu panorama)
            panorama.TextKeys[option] = key;
    }

    private void InitializePanoramaMenus(bool hotReload)
    {
        UIKit.Init(this, hotReload);

        _panoramaMain = new UiPanel(
            "panorama/layout/custom_game/poplight_menu.xml",
            new PanelOptions
            {
                Root = "poplight_window",
                CaptureInput = true
            }
        );

        _panoramaSub = new UiPanel(
            "panorama/layout/custom_game/poplight_submenu.xml",
            new PanelOptions
            {
                Root = "submenu_window",
                CaptureInput = true
            }
        );

        _panoramaMain.Clicked += HandlePanoramaClick;
        _panoramaSub.Clicked += HandlePanoramaClick;

        RegisterEventHandler<EventPlayerDeath>((@event, info) =>
        {
            if (@event.Userid is { IsValid: true } player)
                CloseWsMenus(player);

            return HookResult.Continue;
        });

        RegisterEventHandler<EventPlayerDisconnect>((@event, info) =>
        {
            if (@event.Userid is { } player)
            {
                _panoramaSessions.Remove(player.Slot);
            }

            return HookResult.Continue;
        }, HookMode.Pre);

        RegisterListener<Listeners.OnMapEnd>(() =>
        {
            _panoramaSessions.Clear();
        });
    }

    private void TogglePanoramaMenu(CCSPlayerController? player)
    {
        if (player is null || !player.IsValid || player.IsBot)
            return;

        using var menuCulture = new MenuCulture(player);

        if (_panoramaSessions.ContainsKey(player.Slot))
        {
            CloseWsMenus(player);
            return;
        }

        if (!CanUseWsMenu(player))
            return;

        if (_panoramaMain?.Entity is null ||
            _panoramaSub?.Entity is null)
        {
            player.PrintToChat(
                Localizer["wp_panorama_not_ready"].Value
            );
            return;
        }

        OpenWsMainMenu(player);
    }

    private void CloseWsMenus(CCSPlayerController player)
    {
        _panoramaMain?.Hide(player);
        _panoramaSub?.Hide(player);

        _panoramaSessions.Remove(player.Slot);
    }

    private void OpenPanoramaMenu(
        CCSPlayerController player,
        PanoramaMenu menu)
    {
        if (!CanUseWsMenu(player) ||
            _panoramaMain?.Entity is null ||
            _panoramaSub?.Entity is null)
        {
            return;
        }

        using var menuCulture = new MenuCulture(player);

        bool isMain = menu.TitleKey == "wp_ws_title";

        _panoramaMain.Hide(player);
        _panoramaSub.Hide(player);

        var session = new PanoramaSession
        {
            SteamId = player.SteamID,
            Menu = menu,
            IsMain = isMain,
            Page = 0
        };

        _panoramaSessions[player.Slot] = session;

        DrawPanoramaLabels(
            player,
            isMain ? _panoramaMain : _panoramaSub,
            isMain
        );

        DrawPanoramaMenu(player, session);

        if (isMain)
            _panoramaMain.Show(player);
        else
            _panoramaSub.Show(player);
    }

    private void DrawPanoramaMenu(
        CCSPlayerController player,
        PanoramaSession session)
    {
        using var menuCulture = new MenuCulture(player);
        session.Bindings.Clear();

        if (session.IsMain)
        {
            DrawPanoramaMain(player, session);
            return;
        }

        var panel = _panoramaSub;
        if (panel is null)
            return;

        const int perPage = 6;

        int count = session.Menu.MenuOptions.Count;
        int pages = Math.Max(1, (count + perPage - 1) / perPage);

        session.Page = Math.Clamp(session.Page, 0, pages - 1);

        panel.SetText(
            player,
            "submenu_title",
            PanoramaPlainText(session.Menu.DisplayTitle)
        );

        panel.SetText(
            player,
            "submenu_page",
            Localizer["wp_panorama_page", session.Page + 1, pages].Value
        );

        panel.SetText(player, "submenu_status", "");

        panel.SetClass(
            player,
            "submenu_back",
            "submenu-hidden",
            session.Menu.Back is null
        );

        panel.SetClass(
            player,
            "submenu_previous",
            "submenu-unavailable",
            session.Page == 0
        );

        panel.SetClass(
            player,
            "submenu_next",
            "submenu-unavailable",
            session.Page >= pages - 1
        );

        for (int row = 0; row < perPage; row++)
        {
            string buttonId = $"submenu_row_{row}";
            int index = session.Page * perPage + row;
            bool exists = index < count;

            panel.SetClass(
                player, buttonId, "submenu-hidden", !exists
            );

            panel.SetClass(
                player,
                buttonId,
                "panorama-disabled",
                exists && session.Menu.MenuOptions[index].Disabled
            );

            panel.SetText(
                player,
                $"submenu_number_{row}",
                exists
                    ? Localizer["wp_panorama_number", index + 1].Value
                    : ""
            );

            panel.SetText(
                player,
                $"submenu_text_{row}",
                exists
                    ? PanoramaPlainText(session.Menu.DisplayOption(session.Menu.MenuOptions[index]))
                    : ""
            );

            if (exists)
                session.Bindings[buttonId] =
                    session.Menu.MenuOptions[index];
        }
    }

    private void DrawPanoramaMain(
        CCSPlayerController player,
        PanoramaSession session)
    {
        var panel = _panoramaMain;
        if (panel is null)
            return;

        Bind("poplight_all", Localizer["wp_ws_all_weapons"].Value);
        Bind("poplight_knife", Localizer["wp_ws_knife"].Value);
        Bind("poplight_gloves", Localizer["wp_ws_gloves"].Value);
        Bind("poplight_agents", Localizer["wp_ws_agents"].Value);

        var weapons = player.PlayerPawn.Value?.WeaponServices?.MyWeapons;

        if (weapons is not null)
        {
            foreach (var handle in weapons)
            {
                var weapon = handle.Value;

                if (weapon is null || !weapon.IsValid ||
                    !WeaponDefindex.TryGetValue(
                        weapon.AttributeManager.Item.ItemDefinitionIndex,
                        out var weaponClass) ||
                    !WeaponList.TryGetValue(weaponClass, out var name))
                {
                    continue;
                }

                var data = weapon.As<CCSWeaponBase>().VData;

                if (data?.GearSlot == gear_slot_t.GEAR_SLOT_PISTOL)
                    Bind("poplight_pistol", name);

                if (data?.GearSlot == gear_slot_t.GEAR_SLOT_RIFLE)
                    Bind("poplight_primary", name);
            }
        }

        string[] ids =
        [
            "poplight_all",
            "poplight_knife",
            "poplight_pistol",
            "poplight_primary",
            "poplight_gloves",
            "poplight_agents"
        ];

        foreach (string id in ids)
        {
            bool exists = session.Bindings.TryGetValue(id, out var option);

            panel.SetClass(
                player, id, "panorama-hidden", !exists
            );

            panel.SetClass(
                player, id, "panorama-disabled",
                exists && option!.Disabled
            );
        }

        panel.SetText(
            player,
            "poplight_pistol_name",
            session.Bindings.TryGetValue("poplight_pistol", out var pistol)
                ? PanoramaPlainText(pistol.Text)
                : ""
        );

        panel.SetText(
            player,
            "poplight_primary_name",
            session.Bindings.TryGetValue("poplight_primary", out var primary)
                ? PanoramaPlainText(primary.Text)
                : ""
        );

        panel.SetText(player, "poplight_status", "");

        void Bind(string id, string text)
        {
            var option = session.Menu.MenuOptions
                .FirstOrDefault(item => item.Text == text);

            if (option is not null)
                session.Bindings[id] = option;
        }
    }

    private void DrawPanoramaLabels(
    CCSPlayerController player,
    UiPanel panel,
    bool main)
    {
        if (main)
        {
            Set("poplight_brand", "wp_panorama_brand");
            Set("poplight_title", "wp_ws_title");
            Set("poplight_description", "wp_panorama_description");

            Set("poplight_all_text", "wp_ws_all_weapons");
            Set("poplight_all_description", "wp_panorama_all_description");

            Set("poplight_loadout_section", "wp_panorama_loadout_section");

            Set("poplight_knife_text", "wp_ws_knife");
            Set("poplight_knife_description", "wp_panorama_knife_description");

            Set("poplight_pistol_description", "wp_panorama_pistol_description");
            Set("poplight_primary_description", "wp_panorama_primary_description");

            Set("poplight_appearance_section", "wp_panorama_appearance_section");

            Set("poplight_gloves_text", "wp_ws_gloves");
            Set("poplight_gloves_description", "wp_panorama_gloves_description");

            Set("poplight_agents_text", "wp_ws_agents");
            Set("poplight_agents_description", "wp_panorama_agents_description");

            Set("poplight_close_text", "wp_panorama_close");

            for (int row = 0; row < 6; row++)
            {
                panel.SetText(
                    player,
                    $"poplight_menu_number_{row}",
                    Localizer["wp_panorama_number", row + 1].Value
                );

                Set(
                    $"poplight_menu_arrow_{row}",
                    "wp_panorama_row_arrow"
                );
            }
        }
        else
        {
            Set("submenu_brand", "wp_panorama_brand");
            Set("submenu_description", "wp_panorama_submenu_description");

            Set("submenu_back_text", "wp_panorama_back");
            Set("submenu_close_text", "wp_panorama_close");

            Set("submenu_previous_text", "wp_panorama_previous");
            Set("submenu_next_text", "wp_panorama_next");

            for (int row = 0; row < 6; row++)
            {
                Set(
                    $"poplight_submenu_arrow_{row}",
                    "wp_panorama_row_arrow"
                );
            }
        }

        void Set(string id, string key)
        {
            panel.SetText(
                player,
                id,
                PanoramaPlainText(Localizer[key].Value)
            );
        }
    }

    private void HandlePanoramaClick(PanelClick click)
    {
        var player = click.Player;

        if (!player.IsValid ||
            player.IsBot ||
            !_panoramaSessions.TryGetValue(player.Slot, out var session) ||
            session.SteamId != player.SteamID)
        {
            return;
        }
        using var menuCulture = new MenuCulture(player);
        // Closing must work even while cosmetics are loading.
        if (click.ButtonId is "poplight_close" or "submenu_close")
        {
            CloseWsMenus(player);
            return;
        }

        if (!CanUseWsMenu(player))
            return;

        switch (click.ButtonId)
        {
            case "submenu_back":
                if (!session.IsMain && session.Menu.Back is { } back)
                {
                    CloseWsMenus(player);
                    back(player);
                }
                return;

            case "submenu_previous":
                if (!session.IsMain && session.Page > 0)
                {
                    session.Page--;
                    DrawPanoramaMenu(player, session);
                }
                return;

            case "submenu_next":
                if (!session.IsMain &&
                    (session.Page + 1) * 6 < session.Menu.MenuOptions.Count)
                {
                    session.Page++;
                    DrawPanoramaMenu(player, session);
                }
                return;
        }

        if (!session.Bindings.TryGetValue(click.ButtonId, out var option) ||
            option.Disabled)
        {
            return;
        }

        var action = session.Menu.PostSelectAction;

        if (action == PostSelectAction.Close)
            CloseWsMenus(player);

        option.OnSelect(player, option);

        if (action != PostSelectAction.Close &&
            player.IsValid &&
            _panoramaSessions.TryGetValue(player.Slot, out var current) &&
            ReferenceEquals(current, session))
        {
            if (action == PostSelectAction.Reset)
                session.Page = 0;

            DrawPanoramaMenu(player, session);
        }
    }

    private static string PanoramaPlainText(string text)
    {
        StringBuilder result = new();
        bool insideTag = false;

        foreach (char character in text)
        {
            if (character == '<')
                insideTag = true;
            else if (character == '>')
                insideTag = false;
            else if (!insideTag && !char.IsControl(character))
                result.Append(character);
        }

        return WebUtility.HtmlDecode(result.ToString()).Trim();
    }
}