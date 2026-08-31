using UnityEngine;

/// <summary>
/// Developer mode: unlocked with a code in the menu's Options screen.
/// </summary>
/// <remarks>
/// This is a CONVENIENCE, not a security boundary, and it is worth being clear about that. The
/// code sits in the shipped build and anyone who opens devtools can set the PlayerPrefs key
/// directly. That is fine — there is no leaderboard and nothing to cheat anyone out of. It
/// exists so Ethan can tune the car and skip the grind on a school Chromebook, where there is
/// no Inspector and no console.
///
/// Turning it on grants a large gear balance rather than infinite gears, so the garage, the
/// wallet and the purchase path all still run their real code. An "everything is free" flag
/// would mean the buying path is never exercised until a player without dev mode hits it.
/// </remarks>
public static class DevMode
{
    const string Key = "carcrash.dev";
    const string Code = "doc1157";

    /// <summary>Gears granted on unlocking. Enough to buy anything, still a real number.</summary>
    public const int GearGrant = 1000000;

    public static bool Enabled
    {
        get => PlayerPrefs.GetInt(Key, 0) == 1;
        private set { PlayerPrefs.SetInt(Key, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    /// <summary>
    /// Try a code. Returns true if it unlocked dev mode, which also tops the wallet up.
    /// </summary>
    public static bool TryUnlock(string entered)
    {
        if (string.IsNullOrWhiteSpace(entered)) return false;
        if (entered.Trim() != Code) return false;

        Enabled = true;

        // Top up TO the grant rather than adding it, so repeatedly submitting the code does
        // not stack into a meaningless number.
        int missing = GearGrant - PlayerWallet.Gears;
        if (missing > 0) PlayerWallet.Deposit(missing);

        return true;
    }

    public static void Disable()
    {
        Enabled = false;
    }
}
