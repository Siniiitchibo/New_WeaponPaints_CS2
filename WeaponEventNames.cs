namespace WeaponPaints;

internal static class WeaponEventNames
{
    internal static bool MatchesKill(string eventWeapon, string candidateClass)
    {
        string name = eventWeapon.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase)
            ? eventWeapon[7..] : eventWeapon;
        if (name.EndsWith("_off", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        if (name.Contains("knife", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("bayonet", StringComparison.OrdinalIgnoreCase))
            return candidateClass.StartsWith("weapon_knife", StringComparison.OrdinalIgnoreCase) ||
                   candidateClass.Equals("weapon_bayonet", StringComparison.OrdinalIgnoreCase);
        return candidateClass.Equals("weapon_" + name, StringComparison.OrdinalIgnoreCase);
    }
}
