using UnityEngine;

/// <summary>
/// What the player picked in the menu, carried into the run.
/// </summary>
/// <remarks>
/// A plain static would be enough to survive the scene load, since statics live as long as the
/// process. PlayerPrefs is used anyway so the choice survives a page refresh too — on a school
/// Chromebook the tab gets killed often enough that re-picking a map every time would grate,
/// and it costs two string reads at menu load.
///
/// Stored as string IDs rather than array indices on purpose. An index silently points at a
/// different map the moment the list is reordered, and the failure is invisible: you press play
/// and get the wrong track.
/// </remarks>
public static class GameSelection
{
    const string MapKey = "carcrash.map";
    const string CarKey = "carcrash.car";
    const string ModeKey = "carcrash.mode";

    /// <summary>Wreck the car for gears. What the game was until race mode.</summary>
    public const string Destruction = "destruction";

    /// <summary>Laps against a field of AI, on the same maps.</summary>
    public const string Race = "race";

    /// <summary>
    /// Which game mode the next run is. One of <see cref="Destruction"/> or <see cref="Race"/>.
    /// </summary>
    /// <remarks>
    /// A mode belongs here rather than in the scene, because the SAME scene serves both — The
    /// Dam is a destruction map and a race track, and the only difference is which rules are
    /// switched on when it loads. Defaults to destruction, so a save from before race mode
    /// existed, or a cleared browser store, starts in the mode the game has always had.
    ///
    /// Stored as a string id for the reason the map and car are: an index quietly means
    /// something else the moment a mode is inserted in the list.
    /// </remarks>
    public static string ModeId
    {
        get => PlayerPrefs.GetString(ModeKey, Destruction);
        set { PlayerPrefs.SetString(ModeKey, string.IsNullOrEmpty(value) ? Destruction : value);
              PlayerPrefs.Save(); }
    }

    /// <summary>True when the next run is a race.</summary>
    public static bool IsRace => ModeId == Race;

    /// <summary>Chosen map id, or empty if the player has not picked one yet.</summary>
    public static string MapId
    {
        get => PlayerPrefs.GetString(MapKey, "");
        set { PlayerPrefs.SetString(MapKey, value ?? ""); PlayerPrefs.Save(); }
    }

    /// <summary>Chosen car id, or empty if the player has not picked one yet.</summary>
    public static string CarId
    {
        get => PlayerPrefs.GetString(CarKey, "");
        set { PlayerPrefs.SetString(CarKey, value ?? ""); PlayerPrefs.Save(); }
    }

    public static void Clear()
    {
        PlayerPrefs.DeleteKey(MapKey);
        PlayerPrefs.DeleteKey(CarKey);
        PlayerPrefs.DeleteKey(ModeKey);
        PlayerPrefs.Save();
    }
}
