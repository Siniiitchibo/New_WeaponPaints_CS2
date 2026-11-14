using CounterStrikeSharp.API;
using Microsoft.Extensions.Logging;

namespace WeaponPaints;

public partial class WeaponPaints
{
    private void LoadGameCosmetics()
    {
        try
        {
            string installDirectory = Server.GameDirectory;
            string csgoDirectory = Directory.Exists(Path.Combine(installDirectory, "csgo"))
                ? Path.Combine(installDirectory, "csgo")
                : installDirectory;
            var catalog = GameCosmeticCatalog.Read(
                csgoDirectory, _config.SkinsLanguage, WeaponDefindex, WeaponList,
                SkinsList, GlovesList, AgentsList);

            Logger.LogInformation(
                "CS2 cosmetic catalog added {Skins} skins, {Gloves} glove finishes, {Agents} agents, {Knives} knife models and {Firearms} firearm models.",
                catalog.Skins.Count - SkinsList.Count, catalog.Gloves.Count - GlovesList.Count,
                catalog.Agents.Count - AgentsList.Count, catalog.KnifeModels.Count, catalog.FirearmModels.Count);

            foreach (var (id, className, name) in catalog.KnifeModels.Concat(catalog.FirearmModels))
            {
                WeaponDefindex.Add(id, className);
                WeaponList.Add(className, name);
            }

            SkinsList = catalog.Skins;
            GlovesList = catalog.Gloves;
            AgentsList = catalog.Agents;
        }
        catch (Exception error)
        {
            Logger.LogWarning(error, "Unable to read the CS2 cosmetic catalog. Using the bundled JSON data.");
        }
    }
}
