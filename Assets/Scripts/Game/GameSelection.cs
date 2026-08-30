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
        PlayerPrefs.Save();
    }
}
