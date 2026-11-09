# PopLight WeaponPaints CS2

A customized version of CS2 WeaponPaints with a clickable Panorama menu inspired by the familiar CS:GO skin-selection workflow.

Open the menu using `.ws` or `!ws`.

## Features

- One menu for weapons, knives, gloves and agents.
- Quick access to the player's current pistol and primary weapon.
- Knife model selection followed by skin selection.
- Glove categories and skin selection.
- Knife and glove changes for both teams or the current team.
- StatTrak controls for weapons and knives.
- MySQL persistence.
- Workshop-delivered Panorama menu.
- English and Slovak interface translations, with per-player language selection.
- Menu text customization through server-side translation files.

## Server requirements

- Metamod:Source.
- CounterStrikeSharp with runtime, compatible with the API version referenced in `WeaponPaints.csproj`.
- MultiAddonManager for Workshop menu delivery.
- MySQL or MariaDB.

CS2UIKit is included in `3rd_party` and copied alongside the plugin during compilation.

## Installation

1. Install Metamod, CounterStrikeSharp and MultiAddonManager.
2. Download the latest release and extract it into the server's `game/csgo` directory, merging the included folders.
3. Set `"FollowCS2ServerGuidelines": false` in CounterStrikeSharp's `configs/core.json`.
4. Start the server once, then enter your MySQL connection details in the generated `WeaponPaints.json` configuration.
5. Configure the Workshop menu as described below and restart the server.

Database tables are created automatically. A missing database is also created if the MySQL account has sufficient permissions; otherwise, create it through your hosting panel first.

## Workshop menu

Add the [PopLight Skin Menu](https://steamcommunity.com/sharedfiles/filedetails/?id=3836442690) to your MultiAddonManager configuration:

```cfg
mm_client_extra_addons "3836442690"
```

Keep any existing addon IDs, separated by commas. Players download the menu when connecting and may need to confirm the download.

## Language and text customization

English (`en`) and Slovak (`sk`) are included.

Players can change their language in the game console:

```text
css_lang en
css_lang sk
```

Close and reopen the menu after switching languages.

The server's default language is configured through `ServerLanguage` in CounterStrikeSharp's `configs/core.json`.

To customize interface text, edit the plugin's `lang/en.json` or `lang/sk.json` on the server, then restart the server. Preserve formatting placeholders such as `{0}`, `{1}` and `{0:00}`.

Menu labels use plain text; their visual appearance is controlled by CSS. Chat messages support CounterStrikeSharp color tags.

`SkinsLanguage` is a separate plugin setting for cosmetic names loaded from `data`. Use a language available in that folder, such as `en` or `cs`. The Slovak interface does not require `SkinsLanguage` to be set to `sk`.

## Editing the menu

The main menu uses:

```text
hud/layout/poplight_menu.xml
hud/styles/poplight_menu.css
```

Selection submenus use:

```text
hud/layout/poplight_submenu.xml
hud/styles/poplight_submenu.css
```

The submenu also includes the main stylesheet.

All menu interface text is supplied by the server plugin. Customize titles, descriptions, buttons and messages in `lang/en.json` and `lang/sk.json`; text changes do not require a Workshop update.

XML files define the layout, while CSS files control its appearance. Layout and styling changes require recompiling and updating the Workshop addon.

Keep existing panel and button IDs unless you also update their references in `PanoramaMenus.cs`.

## Compiling the menu

Install Counter-Strike 2 Workshop Tools before compiling.

### Running the script from Windows Explorer

1. Open this repository's `hud` folder in Windows Explorer.
2. Click the Explorer address bar.
3. Type `powershell` and press Enter.
4. Run:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File ".\BuildPoplightMenu.ps1" -Cs2 "C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive"
   ```

Replace the example installation path with your own CS2 installation.

Use the installation directory containing both `game` and `content`, not the dedicated server directory or the `game/csgo` subfolder.

The script compiles both layouts and both stylesheets. Check that all four resources finish successfully.

Compiled files are written to:

```text
<CS2 installation>/game/csgo_addons/poplight_menu/panorama/layout/custom_game/poplight_menu.vxml_c
<CS2 installation>/game/csgo_addons/poplight_menu/panorama/layout/custom_game/poplight_submenu.vxml_c
<CS2 installation>/game/csgo_addons/poplight_menu/panorama/styles/custom_game/poplight_menu.vcss_c
<CS2 installation>/game/csgo_addons/poplight_menu/panorama/styles/custom_game/poplight_submenu.vcss_c
```

Compiled resources are generated locally and are excluded from Git.

### Prepare Workshop packaging

Before editing, back up the client installation's:

```text
<CS2 installation>/game/csgo/gameinfo.gi
```

Inside `AddonConfig` → `VpkDirectories`, ensure these entries are present:

```text
"include"    "panorama/layout/custom_game"
"include"    "panorama/styles/custom_game"
```

Preserve all other existing entries. Close and reopen Workshop Manager after changing this configuration.

### Publish your own Workshop item

1. Launch **Counter-Strike 2 Workshop Tools** from Steam.
2. Select your addon and click **Launch Tools**.
3. In Asset Browser, open **Tools → Workshop Manager**.
4. Click **New** and select your compiled addon folder.
5. Enter a title and description, choose a preview image and set suitable visibility.
6. Open **Contents** and verify that the package contains:
   - Two `vxml_c` files.
   - Two `vcss_c` files.
7. Submit the item.
8. Replace the default Workshop ID in your server's `mm_client_extra_addons` setting with your own item ID.
9. Restart the server and test with a client that has no manually installed copies of the menu.

Workshop game-mode tags categorize the item; they do not change the server's game mode.

Publishing a new Workshop item does not require changing the C# code if the layout paths and IDs remain unchanged.

## Updating a published menu

Translation-only changes do not require a Workshop update. Edit the server's plugin translation files and restart the server.

After changing XML or CSS:

1. Run `BuildPoplightMenu.ps1` again.
2. Open **Tools → Workshop Manager**.
3. Select the existing item and click **Re-Upload** to upload the updated addon files. **Edit** changes the item's information.
4. Keep the same Workshop item ID.
5. Let Steam download the update, then restart CS2 before testing.

When updating from a menu with fixed XML text to the localized layout, update both the Workshop addon and the server plugin, including its `lang` folder.

Publishing the server plugin alone does not update Workshop resources.

## Versioning

Keep `VERSION` synchronized with `ModuleVersion` in `WeaponPaints.cs`.

## Credits and licenses

PopLight customization and maintenance by [Siniiitchibo](https://github.com/Siniiitchibo).

Based on [Nereziel/cs2-WeaponPaints](https://github.com/Nereziel/cs2-WeaponPaints).

Panorama integration uses [CS2UIKit](https://github.com/nvmxre/cs2-ui-kit) by PROJECT ZERO.

Workshop delivery uses [MultiAddonManager](https://github.com/Source2ZE/MultiAddonManager).

The original project license is retained in `LICENSE`.
The CS2UIKit license is included in `3rd_party/CS2UIKit.LICENSE`.