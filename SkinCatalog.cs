using Newtonsoft.Json.Linq;

namespace WeaponPaints;

internal sealed class SkinCatalog
{
    private readonly Dictionary<(int Defindex, int Paint), bool> _legacy = [];
    private readonly Dictionary<int, int[]> _paints;
    private readonly Dictionary<int, int[]> _knifeModels;

    internal SkinCatalog(IEnumerable<JObject> skins)
    {
        Dictionary<int, HashSet<int>> paints = [];
        Dictionary<int, HashSet<int>> knifeModels = [];
        foreach (var skin in skins)
        {
            int defindex = skin["weapon_defindex"]?.ToObject<int>() ?? 0;
            int paint = skin["paint"]?.ToObject<int>() ?? 0;
            _legacy.TryAdd((defindex, paint), skin.Value<bool?>("legacy_model") ?? false);
            if (!paints.TryGetValue(defindex, out var values))
                paints[defindex] = values = [];
            values.Add(paint);
            if (defindex >= 500 && paint is 409 or 413)
            {
                if (!knifeModels.TryGetValue(paint, out var models))
                    knifeModels[paint] = models = [];
                models.Add(defindex);
            }
        }
        _paints = paints.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        _knifeModels = knifeModels.ToDictionary(pair => pair.Key, pair => pair.Value.Order().ToArray());
    }

    internal bool IsLegacy(int defindex, int paint) =>
        !_legacy.TryGetValue((defindex, paint), out bool legacy) || legacy;

    internal int RandomPaint(int defindex) =>
        _paints.TryGetValue(defindex, out var paints) && paints.Length > 0
            ? paints[Random.Shared.Next(paints.Length)] : 0;

    internal (int Slot, int Count) KnifeModel(int defindex, int paint)
    {
        if (!_knifeModels.TryGetValue(paint, out var models))
            return (0, 1);
        int index = Array.BinarySearch(models, defindex);
        int slot = index >= 0 ? index : ~index;
        return (slot, Math.Max(models.Length, slot + 1));
    }
}
