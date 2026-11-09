using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace WeaponPaints;

[MinimumApiVersion(338)]
public partial class WeaponPaints : BasePlugin, IPluginConfig<WeaponPaintsConfig>
{
	internal static WeaponPaints Instance { get; private set; } = new();

	public WeaponPaintsConfig Config { get; set; } = new();
    private static WeaponPaintsConfig _config = new();
    public override string ModuleAuthor => "Nereziel, daffyy, Siniii_tchibo";
	public override string ModuleDescription => "Skin, gloves, agents and knife selector.";
	public override string ModuleName => "New_WeaponPaints_CS2";
	public override string ModuleVersion => "0.2.9";

	public override void Load(bool hotReload)
	{
		Instance = this;
        InitializePanoramaMenus(hotReload);

        if (hotReload)
		{
			OnMapStart(string.Empty);
			
			GPlayerWeaponsInfo.Clear();
			GPlayersKnife.Clear();
			GPlayersGlove.Clear();
			GPlayersAgent.Clear();

			foreach (var player in Enumerable
				         .OfType<CCSPlayerController>(Utilities.GetPlayers().TakeWhile(_ => WeaponSync != null))
				         .Where(player => player.IsValid &&
					         !string.IsNullOrEmpty(player.IpAddress) && player is
						         { IsBot: false, Connected: PlayerConnectedState.Connected }))
			{
                LoadPlayerCosmetics(player);
			}
		}

		Utility.LoadSkinsFromFile(ModuleDirectory + $"/data/skins_{_config.SkinsLanguage}.json", Logger);
		Utility.LoadGlovesFromFile(ModuleDirectory + $"/data/gloves_{_config.SkinsLanguage}.json", Logger);
		Utility.LoadAgentsFromFile(ModuleDirectory + $"/data/agents_{_config.SkinsLanguage}.json", Logger);

		RegisterListeners();
	}

	public override void Unload(bool hotReload)
	{
        _cosmeticWorkActive = false;
        _cosmeticSessions.Clear();
        CS2UIKit.UIKit.Shutdown();

        _panoramaSessions.Clear();

        _panoramaMain = null;
        _panoramaSub = null;
        // Native hook outlives the plugin's AssemblyLoadContext; without this, css_plugins reload
        // leaves the old OnGiveNamedItemPost delegate registered and the next GiveNamedItem crashes the server.
        if (!_giveNamedItemHooked) return;

		VirtualFunctions.GiveNamedItemFunc.Unhook(OnGiveNamedItemPost, HookMode.Post);
		_giveNamedItemHooked = false;
	}

	public void OnConfigParsed(WeaponPaintsConfig config)
	{
		Config = config;
		_config = config;

		if (config.DatabaseHost.Length < 1 || config.DatabaseName.Length < 1 || config.DatabaseUser.Length < 1)
		{
			throw new InvalidOperationException("You need to setup Database credentials in \"configs/plugins/WeaponPaints/WeaponPaints.json\"!");
		}

		if (!File.Exists(Path.GetDirectoryName(Path.GetDirectoryName(ModuleDirectory)) + "/gamedata/weaponpaints.json"))
		{
			throw new InvalidOperationException("You need to upload \"weaponpaints.json\" to \"gamedata directory\"!");
		}
		
		var builder = new MySqlConnectionStringBuilder
		{
			Server = config.DatabaseHost,
			UserID = config.DatabaseUser,
			Password = config.DatabasePassword,
			Database = config.DatabaseName,
			Port = (uint)config.DatabasePort,
			Pooling = true,
			MaximumPoolSize = 640,
		};

        var database = new Database(builder.ConnectionString);
        Database = database;

        try
        {
            Task.Run(async () =>
            {
                await database.EnsureDatabaseExistsAsync();
                await Utility.CheckDatabaseTables();
            }).GetAwaiter().GetResult();

            WeaponSync = new WeaponSynchronization(database, config);

            if (Logger.IsEnabled(LogLevel.Information))
            {
                Logger.LogInformation(
                    "Database {DatabaseName} and required tables are ready.",
                    config.DatabaseName);
            }
        }
        catch (Exception ex)
        {
            WeaponSync = null;

            Logger.LogError(
                ex,
                "Unable to initialize database {DatabaseName}.",
                config.DatabaseName);

            throw;
        }
        _localizer = Localizer;

		Utility.Config = config;
		Utility.ShowAd(ModuleVersion);
		Task.Run(async () => await Utility.CheckVersion(ModuleVersion, Logger));
	}

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        if (Config.Additional.KnifeEnabled)
            SetupKnifeMenu();

        if (Config.Additional.SkinEnabled)
            SetupSkinsMenu();

        if (Config.Additional.GloveEnabled)
            SetupGlovesMenu();

        if (Config.Additional.AgentEnabled)
            SetupAgentsMenu();

        RegisterCommands();
    }
}
