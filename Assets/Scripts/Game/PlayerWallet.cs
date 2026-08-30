using UnityEngine;

/// <summary>
/// The player's gear balance, persisted between runs.
///
/// Gears are the game's only currency: damage earns them, the garage spends them. A run's
/// score is banked here when the run ends, so the number survives the scene reload that
/// <see cref="RunRestart"/> does on every restart.
/// </summary>
/// <remarks>
/// PlayerPrefs is the right store for this despite its reputation. It is a handful of
/// integers, not save data, and on a Web build it lands in IndexedDB — which is the only
/// persistence a browser tab gets without a server. There is no filesystem to write to.
///
/// <see cref="PlayerPrefs.Save"/> is what flushes Unity's in-memory copy to IndexedDB on the
/// Web target. Without it the balance survives a scene reload but not a page refresh, which
/// is the case that actually matters on a school Chromebook where tabs get killed. It is
/// called on every change; that is a few times a run, not a few times a frame.
///
/// Not encrypted, not validated. A player who opens devtools can give themselves gears. That
/// is fine — there is no leaderboard and nothing to cheat anyone out of.
/// </remarks>
public static class PlayerWallet
{
    const string GearsKey = "carcrash.gears";
    const string BestRunKey = "carcrash.bestRun";

    /// <summary>Gears banked and available to spend.</summary>
    public static int Gears => PlayerPrefs.GetInt(GearsKey, 0);

    /// <summary>Best single run, in gears. Shown on the run summary and the menu.</summary>
    public static int BestRun => PlayerPrefs.GetInt(BestRunKey, 0);

    /// <summary>Bank a finished run. Ignores zero and negative amounts.</summary>
    public static void Deposit(int gears)
    {
        if (gears <= 0) return;

        PlayerPrefs.SetInt(GearsKey, Gears + gears);
        if (gears > BestRun) PlayerPrefs.SetInt(BestRunKey, gears);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Spend gears. Returns false and changes nothing if the balance will not cover it,
    /// so the garage can use this as both the test and the purchase.
    /// </summary>
    public static bool Spend(int gears)
    {
        if (gears <= 0 || Gears < gears) return false;

        PlayerPrefs.SetInt(GearsKey, Gears - gears);
        PlayerPrefs.Save();
        return true;
    }

    /// <summary>Wipe the balance and the best run. For a settings screen, or for testing.</summary>
    public static void ResetAll()
    {
        PlayerPrefs.DeleteKey(GearsKey);
        PlayerPrefs.DeleteKey(BestRunKey);
        PlayerPrefs.Save();
    }
}
