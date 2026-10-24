namespace D4BuildFilter.Core;

/// <summary>
/// Companion affix rows leave MagicType at 0 for every normal affix (Legendary = 1 is the aspect
/// magic type, not a Greater Affix flag) and do not ship a GreaterAffixPossible bit.
/// A Greater Affix can roll on a normal affix. It cannot roll on a unique-only, uber, or
/// tempered-only id. This does not change MagicType and it is not written into a filter.
/// </summary>
public static class AffixGreater
{
    public static bool GreaterAffixPossible(string? idName)
    {
        if (string.IsNullOrWhiteSpace(idName)) return false;
        if (idName.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)) return false;
        if (idName.Contains("UBER", StringComparison.OrdinalIgnoreCase)) return false;
        if (idName.Contains("Tempered_", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }
}
