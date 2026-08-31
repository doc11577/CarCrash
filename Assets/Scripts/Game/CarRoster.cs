using System;
using UnityEngine;

/// <summary>
/// Every car in the game: what it is called, what it costs, and which prefab to spawn.
/// </summary>
/// <remarks>
/// A ScriptableObject asset rather than a list on a component, because TWO scenes need it —
/// the menu shows the roster, the run spawns from it — and they live in different scenes. Two
/// separate lists would drift, and the failure is silent: you buy one car in the garage and a
/// different one appears on the grid.
///
/// Create with Assets > Create > CarCrash > Car Roster.
/// </remarks>
[CreateAssetMenu(menuName = "CarCrash/Car Roster", fileName = "CarRoster")]
public class CarRoster : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        [Tooltip("Stable id, saved in PlayerPrefs. NEVER renamed once players own the car — " +
                 "changing it takes their purchase away.")]
        public string id = "e30";

        public string displayName = "BMW E30";

        [Tooltip("Prefab spawned at the start of a run. Needs PlayerCar; must NOT be the same " +
                 "prefab as the traffic one, which deliberately has PlayerCar removed.")]
        public GameObject prefab;

        [Tooltip("Cost in gears. Ignored when Owned From The Start is ticked.")]
        public int price = 0;

        [Tooltip("The car the player begins with. Exactly one entry should have this, or the " +
                 "game opens with nothing drivable.")]
        public bool ownedFromTheStart = false;

        public string blurb = "1,200 kg  ·  rear wheel drive";

        [Tooltip("Attribution. REQUIRED for anything CC-BY — both current cars are ROH3D's.")]
        public string credit = "";

        /// <summary>Bought, or free from the start.</summary>
        public bool Owned => ownedFromTheStart || PlayerWallet.Owns(id);
    }

    public Entry[] cars = new Entry[0];

    public Entry Find(string id)
    {
        if (cars == null || string.IsNullOrWhiteSpace(id)) return null;

        foreach (Entry entry in cars)
            if (entry != null && entry.id == id) return entry;

        return null;
    }

    /// <summary>
    /// What to fall back to when nothing is selected, or the selection names a car that has
    /// been removed from the roster. Prefers the starter car so a fresh player gets the one
    /// they actually own.
    /// </summary>
    public Entry Default
    {
        get
        {
            if (cars == null || cars.Length == 0) return null;

            foreach (Entry entry in cars)
                if (entry != null && entry.ownedFromTheStart) return entry;

            return cars[0];
        }
    }

    /// <summary>The selected car, falling back to the default if it is missing or unowned.</summary>
    public Entry Selected
    {
        get
        {
            Entry chosen = Find(GameSelection.CarId);

            // An unowned selection is possible: the wallet can be reset while a choice stands.
            // Silently dropping back beats spawning a car the player does not own.
            return chosen != null && chosen.Owned ? chosen : Default;
        }
    }
}
