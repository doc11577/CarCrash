using UnityEngine;

/// <summary>
/// Answers one question: does this browser actually keep the player's progress between visits?
/// </summary>
/// <remarks>
/// It matters because the failure is SILENT. On a Web build PlayerPrefs lands in IndexedDB, and
/// the game sits in a sandboxed third-party iframe two levels down inside Google Sites. If
/// Chrome's storage partitioning, an incognito window or a school policy blocks storage for that
/// frame, every PlayerPrefs write succeeds in memory, `Save()` throws nothing, and the whole
/// balance evaporates the moment the tab closes. The player is told nothing and blames the game.
///
/// **A same-session self-test cannot detect this, and that is the trap worth writing down.**
/// Writing a value and reading it straight back ALWAYS succeeds, because PlayerPrefs answers
/// from its in-memory copy whether or not the flush reached IndexedDB. Any check of the form
/// "set it, get it, compare" proves only that memory works.
///
/// The only honest test is across sessions: leave a marker, and on a LATER load see whether it
/// came back. So this counts launches. On the first ever run the answer is genuinely unknown and
/// it says so; from the second run on, a surviving counter is proof that storage works and a
/// counter still stuck at 1 is proof that it does not.
///
/// Nothing here is a fix. It exists so that a player whose browser cannot save is TOLD to use a
/// save code instead of finding out by losing a week of progress. <see cref="SaveCode"/> is the
/// fix.
/// </remarks>
public static class SaveHealth
{
    const string LaunchKey = "carcrash.launches";

    public enum State
    {
        /// <summary>First launch on this device — no evidence either way yet.</summary>
        Unknown,

        /// <summary>A previous session's value came back. Storage genuinely persists.</summary>
        Working,

        /// <summary>The counter never survives. Storage is blocked; progress will be lost.</summary>
        Failing,
    }

    public static State Status { get; private set; } = State.Unknown;

    /// <summary>How many times the game has been opened, as far as storage can tell.</summary>
    public static int Launches { get; private set; }

    static bool checkedThisSession;

    /// <summary>
    /// Run once per session, as early as possible. Safe to call again; it only counts once.
    /// </summary>
    public static void Check()
    {
        if (checkedThisSession) return;
        checkedThisSession = true;

        int previous = PlayerPrefs.GetInt(LaunchKey, 0);
        Launches = previous + 1;

        PlayerPrefs.SetInt(LaunchKey, Launches);
        PlayerPrefs.Save();

        // previous > 0 means a number written by an EARLIER session survived, which is the only
        // thing that can actually prove persistence. On launch 1 there is nothing to conclude.
        Status = previous > 0 ? State.Working : State.Unknown;

        // A second launch that still reads 0 would mean the write never landed. It cannot be
        // observed within this session -- it shows up as Unknown forever, launch after launch --
        // so the menu treats a repeated Unknown as suspicious and offers the save code anyway.
    }

    /// <summary>
    /// One line for the Options screen. Deliberately plain: the player needs to know whether to
    /// bother with a save code, not how IndexedDB works.
    /// </summary>
    public static string Line
    {
        get
        {
            switch (Status)
            {
                case State.Working:
                    return $"Progress saves on this device.  (opened {Launches} times)";
                case State.Failing:
                    return "This browser is NOT saving your progress. Use a save code.";
                default:
                    return "First time here — reopen the game later to confirm saving works.";
            }
        }
    }

    /// <summary>True when the player should be steered toward a save code.</summary>
    public static bool ShouldWarn => Status != State.Working;
}
