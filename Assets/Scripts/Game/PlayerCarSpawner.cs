using UnityEngine;

/// <summary>
/// Spawns whichever car the player chose in the garage, at the start of the run.
/// </summary>
/// <remarks>
/// The player's car stops being a scene object here, and that is a real change rather than a
/// tidy-up: a car placed in the scene is one car forever, and the whole point of a garage is
/// that it is not.
///
/// Everything downstream was already built to cope, which is why this is small. `PlayerCar`
/// announces itself on enable, so scoring finds it; `ChaseCamera` and `PerfReadout` fall back
/// to `PlayerCar.Current` when their target is empty. The one thing that would NOT cope is a
/// hard scene reference to the old car, so make sure nothing still points at one.
///
/// Spawns in Awake so the car exists before anything's Start runs — `RunScore.Start` reads
/// `PlayerCar.Current`, and a car spawned in Start would be a coin flip.
/// </remarks>
[DisallowMultipleComponent]
public class PlayerCarSpawner : MonoBehaviour
{
    [Tooltip("The shared roster asset. Same one the menu uses — that is the point of it being " +
             "an asset rather than a list on a component.")]
    public CarRoster roster;

    [Tooltip("Where the car appears. Leave empty to use this GameObject's own transform. Its " +
             "blue arrow must point DOWN the course or the run starts backwards.")]
    public Transform spawnPoint;

    [Tooltip("Metres above the spawn point, so the car settles onto the ground instead of " +
             "starting inside it.")]
    public float dropHeight = 0.6f;

    [Header("Read-only")]
    [SerializeField] string spawnedCar;

    void Awake()
    {
        if (roster == null)
        {
            Debug.LogError("PlayerCarSpawner has no CarRoster, so no player car will exist.", this);
            return;
        }

        CarRoster.Entry entry = roster.Selected;
        if (entry == null || entry.prefab == null)
        {
            Debug.LogError(
                "PlayerCarSpawner: the roster has no usable car. Check that at least one entry " +
                "has a prefab and is ticked Owned From The Start.", this);
            return;
        }

        Transform at = spawnPoint != null ? spawnPoint : transform;
        GameObject car = Instantiate(entry.prefab,
                                     at.position + Vector3.up * dropHeight,
                                     at.rotation);
        car.name = entry.displayName;
        spawnedCar = entry.id;
    }
}
