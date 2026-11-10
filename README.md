# PopLight WeaponPaints CS2

A fully working and optimized version of CS2 WeaponPaints with a clickable Panorama menu for weapons, knives, gloves and agents.

Open the menu using `.ws` or `!ws` in chat, or `css_ws` in the console.

![PopLight Skin Menu](hud/menu.jpg)

## Features

- One menu for weapons, knives, gloves and agents, with quick access to your current pistol and primary weapon.
- Knife model selection, glove categories and individual skin selection.
- Knife and glove settings for your current team or both CT and T.
- A clickable, color-coded wear bar for weapons, knives and gloves, with Factory New, Minimal Wear, Field-Tested, Well-Worn and Battle-Scarred ranges.
- A StatTrak checkbox above the wear bar for weapons and knives, preserving existing kill counters when changing skins.
- Highlighted current selections for skins, knife models, glove models and agents. Skin matching uses the exact paint kit ID to distinguish variants such as Doppler phases.
- CT/T markers when a selection applies to only one side while editing both teams.
- Immediate agent changes with previews that keep the selection menu open.
- MySQL persistence for cosmetic settings, wear and StatTrak counters.
- Workshop-delivered Panorama resources, with editable layout and style sources included in the repository.
- English and Slovak interface translations, per-player language selection and server-side text customization.

## Recent improvements — 0.3.0

The latest updates add wear controls, the StatTrak checkbox and current-selection highlighting, together with fixes to cosmetic updates and persistence:

- Delayed operations are guarded against reconnects, team changes, pawn replacements and map changes.
- Selecting a weapon or knife skin refreshes only the relevant weapon instead of rebuilding the entire inventory. Overlapping refresh requests are combined during rapid switching, including pending changes to different weapon types.
- Database writes use independent snapshots of cosmetic settings, reducing SQL queries and keeping T and CT data separate.
- StatTrak counts the weapon responsible for the kill. Grenade kills no longer increase the counter of the weapon currently being held, and counter changes are saved as they occur.
- Fixed loading of incomplete data, combinations of disabled cosmetic features and overlapping agent previews.
- Faster skin lookup and a corrected plugin update check.
- Fixed live application of Tiger Tooth and Marble Fade on knives, while retaining per-knife material separation when players customize wear.

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

Database tables are created and updated automatically. A missing database is also created if the MySQL account has sufficient permissions; otherwise, create it through your hosting panel first.

When upgrading to the wear controls and selection highlighting, update both the server plugin and the Workshop menu resources.

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

Close and reopen the menu after switching languages. The server's default language is configured through `ServerLanguage` in CounterStrikeSharp's `configs/core.json`.

To customize interface text, edit the plugin's `lang/en.json` or `lang/sk.json` on the server, then restart the server. Preserve formatting placeholders such as `{0}`, `{1}` and `{0:00}`. Translation changes do not require recompiling or updating the Workshop addon.

Menu labels use plain text; their appearance is controlled by CSS. Chat messages support CounterStrikeSharp color tags.

`SkinsLanguage` is a separate plugin setting for cosmetic names loaded from `data`. Use a language available in that folder, such as `en` or `cs`. The Slovak interface does not require `SkinsLanguage` to be set to `sk`.

## Building the plugin

Install the .NET 10 SDK, then build using Visual Studio Publish or `dotnet publish`.

Example PowerShell command:

```powershell
dotnet publish "E:\GitHub\New_WeaponPaints_CS2\WeaponPaints.csproj" -c Release -o "E:\GitHub\New_WeaponPaints_CS2\publish"
```

Replace the example repository path with your own. Keep the published dependencies and the `data` and `lang` folders together. The release workflow packages the server files into the ready-to-extract folder structure.

## Editing the menu

The active menu sources are:

```text
hud/layout/poplight_menu.xml
hud/styles/poplight_menu.css
hud/layout/poplight_submenu_wear.xml
hud/styles/poplight_submenu_wear.css
```

The selection submenu also includes the main stylesheet. Its layout contains the skin list, pagination, StatTrak checkbox, wear bar and navigation buttons.

XML defines the layout, while CSS controls the appearance. Layout and styling changes require recompiling and updating the Workshop addon. Server-provided text is customized through the translation files.

Keep existing panel and button IDs unless you also update their references in the plugin's Panorama code. The active submenu uses the `_wear` filename; editing an older `poplight_submenu.xml` or `.css` file will not change it.

## Compiling and testing the menu

Install Counter-Strike 2 Workshop Tools and close CS2 before compiling. Run the commands below in PowerShell, replacing the repository and game paths with your own.

Use the CS2 installation directory containing both `game` and `content`, rather than the dedicated server directory or the `game/csgo` subfolder.

To compile the Workshop addon:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "E:\GitHub\New_WeaponPaints_CS2\hud\BuildPoplightMenu.ps1" -Cs2 "C:\Hry\Steam\steamapps\common\Counter-Strike Global Offensive"
```

For local testing of the selection submenu before publishing:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "E:\GitHub\New_WeaponPaints_CS2\hud\BuildPoplightMenu.ps1" -Cs2 "C:\Hry\Steam\steamapps\common\Counter-Strike Global Offensive" -InstallForLocalTest
```

The script compiles two layouts and two stylesheets. Check that all four resources finish successfully. Restart CS2 after each build because Panorama caches layouts.

Compiled resources are written to:

```text
<CS2 installation>/game/csgo_addons/poplight_menu/panorama/layout/custom_game/poplight_menu.vxml_c
<CS2 installation>/game/csgo_addons/poplight_menu/panorama/layout/custom_game/poplight_submenu_wear.vxml_c
<CS2 installation>/game/csgo_addons/poplight_menu/panorama/styles/custom_game/poplight_menu.vcss_c
<CS2 installation>/game/csgo_addons/poplight_menu/panorama/styles/custom_game/poplight_submenu_wear.vcss_c
```

Compiled resources are generated locally and excluded from Git.

`-InstallForLocalTest` additionally copies the two compiled `_wear` submenu files into your client's `game/csgo/panorama` folders. It does not publish a Workshop update or install the menu for other players.

Before testing the published Workshop version, remove these two local test copies and restart CS2:

```text
<CS2 installation>/game/csgo/panorama/layout/custom_game/poplight_submenu_wear.vxml_c
<CS2 installation>/game/csgo/panorama/styles/custom_game/poplight_submenu_wear.vcss_c
```

## Publishing your own Workshop menu

### Prepare packaging

Back up the client installation's `game/csgo/gameinfo.gi`. Inside `AddonConfig` → `VpkDirectories`, ensure these entries are present:

```text
"include"    "panorama/layout/custom_game"
"include"    "panorama/styles/custom_game"
```

Preserve all other entries. Close and reopen Workshop Manager after changing this configuration.

### Publish

1. Compile the menu using the script above.
2. Launch **Counter-Strike 2 Workshop Tools** from Steam.
3. Select `poplight_menu` and click **Launch Tools**.
4. In Asset Browser, open **Tools → Workshop Manager**.
5. Click **New** and select your compiled addon folder.
6. Enter a title and description, choose a preview image and set suitable visibility.
7. Open **Contents** and verify that the package includes the two `.vxml_c` and two `.vcss_c` resources listed above.
8. Submit the item and replace the default Workshop ID in `mm_client_extra_addons` with your own item ID.
9. Restart the server and test with a client that has no manually installed menu copies.

Workshop game-mode tags categorize the item; they do not change the server's game mode. Publishing your own item does not require changing the C# code if resource paths and IDs remain unchanged.

## Updating a published menu

Translation-only changes require updating the server's translation files and restarting the server.

After changing XML or CSS:

1. Run `BuildPoplightMenu.ps1` again using the full command above.
2. Open **Tools → Workshop Manager**.
3. Select the existing item and click **Re-Upload** to upload the updated addon files. **Edit** changes the item's information.
4. Keep the same Workshop item ID.
5. Remove any local test copies, let Steam download the update and restart CS2 before testing.

Publishing the server plugin alone does not update Workshop resources. Changes that introduce new controls or styling may require updating both the plugin and the addon.

## Versioning

Keep `VERSION` synchronized with `ModuleVersion` in `WeaponPaints.cs`. Use a new version for each release so previous builds remain easy to identify and restore.

## Credits and licenses

PopLight customization and maintenance by [Siniiitchibo](https://github.com/Siniiitchibo).

Based on [Nereziel/cs2-WeaponPaints](https://github.com/Nereziel/cs2-WeaponPaints).

Panorama integration uses [CS2UIKit](https://github.com/nvmxre/cs2-ui-kit) by PROJECT ZERO.

Workshop delivery uses [MultiAddonManager](https://github.com/Source2ZE/MultiAddonManager).

The original project license is retained in `LICENSE`.
The CS2UIKit license is included in `3rd_party/CS2UIKit.LICENSE`.
