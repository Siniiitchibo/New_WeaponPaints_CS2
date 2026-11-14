using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using ValveKeyValue;
using ValvePak;

namespace WeaponPaints;

internal sealed class GameCosmeticCatalog
{
    private const string KnifePrefab = "melee_unusual";
    private const string AgentPrefab = "customplayertradable";
    private const string GlovePrefab = "hands_paintable";
    private const string AgentModelPrefix = "agents/models/";
    private const string TextureDirectory = "panorama/images/econ/default_generated";
    private const string PaintIconSuffix = "_medium_png";

    // Localization includes engine escape sequences (e.g. \x00A2) outside the KV1 parser's escape set.
    private static readonly Regex TranslationLine = new(
        """^\s*"([^"\\]+)"\s+"((?:[^"\\]|\\.)*)"\s*(?://.*)?$""",
        RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, string> GameLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bg"] = "bulgarian", ["cs"] = "czech", ["da"] = "danish", ["de"] = "german",
        ["el"] = "greek", ["es-ES"] = "spanish", ["es-MX"] = "latam", ["fi"] = "finnish",
        ["fr"] = "french", ["hu"] = "hungarian", ["it"] = "italian", ["ja"] = "japanese",
        ["ko"] = "koreana", ["nl"] = "dutch", ["no"] = "norwegian", ["pl"] = "polish",
        ["pt-BR"] = "brazilian", ["pt-PT"] = "portuguese", ["ro"] = "romanian", ["ru"] = "russian",
        ["sv"] = "swedish", ["th"] = "thai", ["tr"] = "turkish", ["uk"] = "ukrainian",
        ["vi"] = "vietnamese", ["zh-CN"] = "schinese", ["zh-TW"] = "tchinese"
    };

    internal List<JObject> Skins { get; }
    internal List<JObject> Gloves { get; }
    internal List<JObject> Agents { get; }
    internal List<(int Id, string ClassName, string Name)> KnifeModels { get; } = [];
    internal List<(int Id, string ClassName, string Name)> FirearmModels { get; } = [];

    private GameCosmeticCatalog(List<JObject> skins, List<JObject> gloves, List<JObject> agents)
    {
        Skins = new(skins);
        Gloves = new(gloves);
        Agents = agents.Select(agent => (JObject)agent.DeepClone()).ToList();
    }

    internal static GameCosmeticCatalog Read(
        string gameDirectory, string language, IReadOnlyDictionary<int, string> weaponClasses,
        IReadOnlyDictionary<string, string> weaponNames,
        List<JObject> skins, List<JObject> gloves, List<JObject> agents)
    {
        string path = Path.Combine(gameDirectory, "pak01_dir.vpk");
        if (!File.Exists(path))
            path = Path.Combine(gameDirectory, "pak01.vpk");

        using var package = new Package();
        package.Read(path);
        var serializer = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
        KVObject schema = ReadDocument("scripts/items/items_game.txt")
            ?? throw new InvalidDataException("items_game.txt is missing from the CS2 VPK.");

        var translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddTranslations("english");
        if (GameLanguages.TryGetValue(language, out var gameLanguage))
            AddTranslations(gameLanguage);

        var result = new GameCosmeticCatalog(skins, gloves, agents);
        var paintKits = new Dictionary<string, PaintKit>(StringComparer.Ordinal);
        foreach (var (key, kit) in ReadSection(schema, "paint_kits"))
        {
            if (int.TryParse(key, out int id) && id > 0 &&
                ReadString(kit, "name") is { } name && ReadString(kit, "description_tag") is { } label)
            {
                paintKits[name] = new PaintKit(id, Translate(label, name), ReadString(kit, "use_legacy_model") == "1");
            }
        }

        var prefabs = new Dictionary<string, KVObject>(StringComparer.Ordinal);
        foreach (var (name, prefab) in ReadSection(schema, "prefabs"))
            prefabs[name] = prefab;

        var targets = new Dictionary<string, PaintTarget>(StringComparer.Ordinal);
        var knownClasses = new HashSet<string>(weaponNames.Keys, StringComparer.Ordinal);
        var knownNames = new HashSet<string>(weaponNames.Values, StringComparer.Ordinal);
        var knownIds = new HashSet<int>(weaponClasses.Keys);
        var agentsByModel = result.Agents.ToLookup(agent => (agent.Value<int>("team"), agent.Value<string>("model")));
        var agentNames = result.Agents.Select(agent => (agent.Value<int>("team"), agent.Value<string>("agent_name"))).ToHashSet();
        var agentModels = result.Agents.Select(agent => (agent.Value<int>("team"), agent.Value<string>("model"))).ToHashSet();

        foreach (var (key, item) in ReadSection(schema, "items"))
        {
            if (!int.TryParse(key, out int id) || ReadString(item, "name") is not { } className)
                continue;

            string? prefab = ReadString(item, "prefab");
            string itemName = Translate(ReadString(item, "item_name"), className);

            if (weaponClasses.TryGetValue(id, out var knownClass) && knownClass == className &&
                weaponNames.TryGetValue(className, out var knownName))
            {
                targets[className] = new PaintTarget(id, knownName, false);
            }
            else if (prefab == KnifePrefab && id is >= 500 and < 5000 &&
                (className.StartsWith("weapon_knife_", StringComparison.Ordinal) || className == "weapon_bayonet") &&
                !knownIds.Contains(id) && !knownClasses.Contains(className) && !knownNames.Contains(itemName) &&
                HasModel(item))
            {
                knownIds.Add(id);
                knownClasses.Add(className);
                knownNames.Add(itemName);
                targets[className] = new PaintTarget(id, itemName, false);
                result.KnifeModels.Add((id, className, itemName));
            }
            else if (id is > 0 and < 500 &&
                className.StartsWith("weapon_", StringComparison.Ordinal) &&
                ReadString(item, "baseitem") == "1" &&
                !knownIds.Contains(id) && !knownClasses.Contains(className) &&
                ReadInheritedString(item, "item_gear_slot", prefabs) is "primary" or "secondary" &&
                ReadInheritedString(item, "model_player", prefabs) is { } firearmModel &&
                firearmModel.StartsWith("weapons/models/", StringComparison.Ordinal) &&
                HasModelPath(firearmModel))
            {
                string name = Translate(ReadInheritedString(item, "item_name", prefabs), className);
                if (!knownNames.Add(name))
                {
                    name = $"{name} ({className})";
                    if (!knownNames.Add(name))
                        continue;
                }

                knownIds.Add(id);
                knownClasses.Add(className);
                targets[className] = new PaintTarget(id, name, false);
                result.FirearmModels.Add((id, className, name));
            }
            else if (prefab == GlovePrefab && id is > 0 and <= ushort.MaxValue)
            {
                targets[className] = new PaintTarget(id, itemName, true);
            }
            else if (prefab == AgentPrefab &&
                ReadString(item, "model_player") is { } model &&
                model.StartsWith(AgentModelPrefix, StringComparison.Ordinal) && HasModel(item))
            {
                string modelKey = model[AgentModelPrefix.Length..^".vmdl".Length];
                string portrait = ReadString(item, "image_inventory") is { } inventoryImage &&
                    inventoryImage.StartsWith("econ/characters/", StringComparison.Ordinal) &&
                    package.FindEntry($"panorama/images/{inventoryImage}_png.vtex_c") is not null
                    ? $"s2r://panorama/images/{inventoryImage}_png.vtex"
                    : "";
                if (item.TryGetValue("used_by_classes", out var teams))
                {
                    AddAgent(2, "terrorists");
                    AddAgent(3, "counter-terrorists");
                }

                void AddAgent(int team, string classKey)
                {
                    if (ReadString(teams, classKey) != "1")
                        return;

                    var existing = agentsByModel[(team, modelKey)];
                    foreach (var agent in existing)
                        agent["image"] = portrait;

                    if (!agentModels.Contains((team, modelKey)) && agentNames.Add((team, itemName)))
                    {
                        agentModels.Add((team, modelKey));
                        result.Agents.Add(new JObject
                        {
                            ["team"] = team, ["model"] = modelKey,
                            ["agent_name"] = itemName, ["image"] = portrait
                        });
                    }
                }
            }
        }

        if (paintKits.Count == 0 || targets.Count == 0 ||
            package.Entries is null || !package.Entries.TryGetValue("vtex_c", out var textures))
        {
            throw new InvalidDataException("The CS2 VPK has no usable cosmetic catalog.");
        }

        // Add the vanilla option before the paint kits, unless JSON already provides it.
        var skinKeys = ReadPaintKeys(result.Skins);
        foreach (var (id, className, name) in result.KnifeModels.Concat(result.FirearmModels))
        {
            if (!skinKeys.Add((id, 0)))
                continue;

            result.Skins.Add(new JObject
            {
                ["weapon_defindex"] = id, ["weapon_name"] = className,
                ["paint"] = 0, ["paint_name"] = $"{name} | Default", ["legacy_model"] = false
            });
        }

        // The VPK retains icons for test and unused rifle/pistol finishes.
        // Require a pairing in the economy schema for guns. Knife and glove
        // pairings are also shipped via icon entries for rare special items.
        var releasedGunPaints = ReadSection(schema, "client_loot_lists")
            .SelectMany(list => list.Value)
            .Concat(ReadSection(schema, "item_sets")
                .Where(set => set.Value.ContainsKey("items"))
                .SelectMany(set => set.Value["items"]))
            .Select(item => item.Key)
            .Where(key => key.StartsWith('['))
            .ToHashSet(StringComparer.Ordinal);

        var gloveKeys = ReadPaintKeys(result.Gloves);
        foreach (var icon in textures)
        {
            if (icon.DirectoryName != TextureDirectory ||
                !icon.FileName.EndsWith(PaintIconSuffix, StringComparison.Ordinal))
                continue;

            // Try each underscore boundary so similar class names (e.g. M4A4 and M4A1-S)
            // get the matching paint kit instead of splitting at the first shared prefix.
            string pair = icon.FileName[..^PaintIconSuffix.Length];
            for (int split = pair.LastIndexOf('_'); split > 0; split = pair.LastIndexOf('_', split - 1))
            {
                string className = pair[..split];
                if (!targets.TryGetValue(className, out var target))
                    continue;

                string kitName = pair[(split + 1)..];
                if (!paintKits.TryGetValue(kitName, out var kit))
                    continue;

                if (!target.Glove && target.Id < 500 &&
                    !releasedGunPaints.Contains($"[{kitName}]{className}"))
                    break;

                var keys = target.Glove ? gloveKeys : skinKeys;
                if (keys.Add((target.Id, kit.Id)))
                {
                    var row = new JObject
                    {
                        ["weapon_defindex"] = target.Id, ["weapon_name"] = className,
                        ["paint"] = kit.Id, ["paint_name"] = $"{target.Name} | {kit.Name}",
                        ["legacy_model"] = kit.LegacyModel,
                        ["image"] = $"s2r://{TextureDirectory}/{icon.FileName}.vtex"
                    };
                    (target.Glove ? result.Gloves : result.Skins).Add(row);
                }

                break;
            }
        }

        return result;

        MemoryStream? OpenEntry(string name)
        {
            var entry = package.FindEntry(name);
            if (entry is null)
                return null;

            package.ReadEntry(entry, out byte[] bytes);
            return new MemoryStream(bytes, writable: false);
        }

        KVObject? ReadDocument(string name)
        {
            using var stream = OpenEntry(name);
            return stream is null ? null : serializer.Deserialize(stream).Root;
        }

        void AddTranslations(string gameLanguage)
        {
            using var stream = OpenEntry($"resource/csgo_{gameLanguage}.txt");
            if (stream is null)
                return;

            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                var match = TranslationLine.Match(line);
                if (match.Success)
                    translations[match.Groups[1].Value] = match.Groups[2].Value.Replace("\\\"", "\"");
            }
        }

        string Translate(string? token, string fallback)
        {
            if (string.IsNullOrWhiteSpace(token))
                return fallback;

            return token.StartsWith('#')
                ? translations.GetValueOrDefault(token[1..], fallback)
                : token;
        }

        bool HasModel(KVObject item) =>
            ReadString(item, "model_player") is { } model && HasModelPath(model);

        bool HasModelPath(string model) =>
            model.EndsWith(".vmdl", StringComparison.Ordinal) &&
            package.FindEntry(model + "_c") is not null;
    }

    private static IEnumerable<KeyValuePair<string, KVObject>> ReadSection(KVObject root, string key) =>
        root.Where(section => section.Key == key).SelectMany(section => section.Value);

    private static string? ReadString(KVObject node, string key) =>
        node.TryGetValue(key, out var value) ? value.ToString() : null;

    private static string? ReadInheritedString(
        KVObject item, string key, IReadOnlyDictionary<string, KVObject> prefabs)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        return Find(item);

        string? Find(KVObject node)
        {
            if (ReadString(node, key) is { } value)
                return value;

            if (ReadString(node, "prefab") is not { } parents)
                return null;

            // Later prefabs override earlier ones. Item values override inherited values.
            foreach (string parent in parents.Split(' ', StringSplitOptions.RemoveEmptyEntries).Reverse())
            {
                if (prefabs.TryGetValue(parent, out var prefab) && visited.Add(parent) &&
                    Find(prefab) is { } inherited)
                    return inherited;
            }

            return null;
        }
    }

    private static HashSet<(int, int)> ReadPaintKeys(List<JObject> items) =>
        items.Select(item => (item.Value<int>("weapon_defindex"), item.Value<int>("paint"))).ToHashSet();

    private sealed record PaintTarget(int Id, string Name, bool Glove);
    private sealed record PaintKit(int Id, string Name, bool LegacyModel);
}
