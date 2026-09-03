using UnityEngine;

/// <summary>
/// Puts the traffic cars on the grid at the start of a run.
/// </summary>
/// <remarks>
/// A spawner rather than three cars placed by hand, so the count and the grid are one number
/// each and a fourth car costs nothing to add.
///
/// Prefabs are picked at RANDOM per car, so a grid might be two E30s and a P72, or three P72s.
/// Kept separate from CarRoster on purpose: the roster is what the player can BUY and its
/// prefabs carry PlayerCar, so spawning those as traffic would hand PlayerCar.Current to an AI.
///
/// A traffic prefab must NOT carry <see cref="PlayerCar"/>. That component claims
/// <c>PlayerCar.Current</c> in its OnEnable, so a traffic car built from the player's prefab
/// would take over as "the player's car" the moment it spawned — scoring, and later the camera,
/// would follow the wrong vehicle. Stripping it after the fact is worse, not better: destroying
/// it fires OnDisable, which clears Current and takes the real player's registration with it.
/// The spawner checks and refuses rather than papering over it.
/// </remarks>
[DisallowMultipleComponent]
public class TrafficSpawner : MonoBehaviour
{
    [Header("What to spawn")]
    [Tooltip("Traffic car prefabs, picked at random per car — so a grid might be two E30s and a " +
             "P72, or three P72s, or anything else. Each must have CarController, CarDamage and " +
             "TrafficDriver, and must NOT have PlayerCar or CarInput.\n\n" +
             "Separate from the CarRoster on purpose: the roster is what the PLAYER can buy, and " +
             "its prefabs carry PlayerCar. Spawning those as traffic would hand PlayerCar.Current " +
             "to an AI car.")]
    public GameObject[] carPrefabs = new GameObject[0];

    [Tooltip("0 gives a different mix every run. Any other value makes the mix reproducible, " +
             "which is what you want while tuning — otherwise a change to the AI and a change " +
             "in the field happen at once and neither can be judged.")]
    public int mixSeed = 0;

    [Tooltip("How many. Three is what the valley is sized for; more is fine, but every car is " +
             "a rigidbody plus four sphere casts a physics step.")]
    [Range(0, 12)] public int count = 3;

    [Header("Racing")]
    [Tooltip("Set this to the map's RaceTrack to make the field RACE rather than seek the " +
             "descent. Leave empty on a destruction map.\n\n" +
             "With a track set, the grid is laid out ON the track behind the start line and " +
             "aimed along it, so the Grid transform below is ignored — a hand-placed grid that " +
             "disagrees with the racing line by a few degrees is eight cars starting sideways.")]
    public RaceTrack raceTrack;

    [Tooltip("Metres behind the start/finish line the FRONT row sits, when racing.")]
    public float gridSetback = 18f;

    [Tooltip("Draw the player's grid slot at random instead of always giving them pole. Starting " +
             "at the front means the race is won by driving away from everyone, which is the " +
             "least interesting version of it — from the middle of the pack there is something " +
             "to do on the first lap.")]
    public bool randomPlayerSlot = true;

    /// <summary>
    /// The grid slot held for the player. The race director reads this to place them.
    /// </summary>
    /// <remarks>
    /// Decided HERE rather than by the director, because the AI have to know which slot to skip
    /// and they are spawned in this component's Start — long before the director gathers the
    /// field on its first Update. Two places choosing it means two cars in one slot, and the
    /// failure is a physics explosion at the green light that reads as a bug in the cars.
    /// </remarks>
    public int PlayerSlot { get; private set; }

    [Header("Grid")]
    [Tooltip("Where the grid is laid out. Leave empty to use this GameObject's transform. " +
             "Put it in the start bay, behind and beside where the player spawns.\n\n" +
             "IGNORED when Race Track is set — the track lays the grid out itself.")]
    public Transform grid;

    [Tooltip("Metres between cars across the grid.")]
    public float lateralSpacing = 7f;

    [Tooltip("Metres each row sits behind the one in front.")]
    public float rowSpacing = 8f;

    [Tooltip("Cars per row before starting a new one.")]
    [Range(1, 6)] public int perRow = 3;

    [Tooltip("Metres above the grid point to drop them from, so they settle onto the ground " +
             "rather than starting inside it.")]
    public float dropHeight = 0.8f;

    [Header("Paint")]
    [Tooltip("Cycled through in order. Cars are tinted so the field is not four white E30s.\n\n" +
             "Deliberately muted. These multiply a near-white body texture, so a saturated " +
             "primary comes out as flat poster colour and the car reads as a moulded toy — real " +
             "car paint is darker and greyer than people expect.")]
    public Color[] palette =
    {
        new Color(0.48f, 0.09f, 0.11f),   // deep red
        new Color(0.12f, 0.20f, 0.40f),   // navy
        new Color(0.10f, 0.26f, 0.17f),   // racing green
        new Color(0.62f, 0.55f, 0.38f),   // champagne
        new Color(0.24f, 0.25f, 0.27f),   // graphite
    };

    [Header("Driving")]
    [Tooltip("How much the AI steers around obstacles ON THIS MAP. 1 is normal. 0 switches it " +
             "off entirely, which is what EVEREST wants: the whole face is jagged rock, so the " +
             "hazard sweep sees an obstacle everywhere and the cars pick their way down a " +
             "mountain that is supposed to be bombed straight off. Quarry needs it ON — that " +
             "course has real boulders on an otherwise clear floor, which is the case the sweep " +
             "was built for.")]
    [Range(0f, 1f)] public float obstacleAvoidance = 1f;

    [Header("Scoring")]
    [Tooltip("Whether damage to TRAFFIC counts toward the run score. Off by default because " +
             "the design says the score is damage to YOUR car, and with this on a traffic car " +
             "wrecking itself on a wall would pay the player for doing nothing. Turn it on when " +
             "you decide hitting traffic should pay.")]
    public bool scoreTrafficDamage = false;

    [Header("Read-only")]
    [SerializeField] int spawned;

    System.Random mix;

    void Start()
    {
        usable.Clear();

        foreach (GameObject prefab in carPrefabs)
        {
            if (prefab == null) continue;

            if (prefab.GetComponent<PlayerCar>() != null)
            {
                Debug.LogError(
                    $"TrafficSpawner: traffic prefab '{prefab.name}' has a PlayerCar component. " +
                    "It would claim PlayerCar.Current and scoring, and later the camera, would " +
                    "follow a traffic car instead of the player. Remove PlayerCar from the " +
                    "prefab — do not strip it at runtime, because destroying it clears Current " +
                    "and unregisters the real player's car too. Skipping this one.", this);
                continue;
            }

            usable.Add(prefab);
        }

        if (usable.Count == 0)
        {
            Debug.LogWarning("TrafficSpawner has no usable car prefabs, so no traffic will " +
                             "appear.", this);
            return;
        }

        // Its own generator rather than UnityEngine.Random, so seeding the mix cannot disturb
        // anything else that happens to draw from the shared one that frame.
        mix = mixSeed == 0 ? new System.Random() : new System.Random(mixSeed);

        Transform origin = grid != null ? grid : transform;

        // The grid is count + 1 slots when the player is on it. Their slot is drawn from the
        // same seeded generator as the car mix, so a seeded run reproduces the whole grid rather
        // than half of it.
        PlayerSlot = ReservesPlayerSlot && randomPlayerSlot ? mix.Next(count + 1) : 0;

        int slot = 0;
        for (int i = 0; i < count; i++)
        {
            if (ReservesPlayerSlot && slot == PlayerSlot) slot++;
            Spawn(origin, slot);
            slot++;
        }

        spawned = count;
    }

    readonly System.Collections.Generic.List<GameObject> usable =
        new System.Collections.Generic.List<GameObject>();

    void Spawn(Transform origin, int index)
    {
        GridSlot(origin, index, out Vector3 position, out Quaternion rotation);

        GameObject prefab = usable[mix.Next(usable.Count)];
        GameObject car = Instantiate(prefab, position, rotation);
        car.name = $"Traffic{index:00}_{prefab.name}";

        CarPaint paint = car.GetComponent<CarPaint>();
        if (paint != null && palette != null && palette.Length > 0)
            paint.Apply(palette[index % palette.Length]);

        // Obstacle avoidance is a per-MAP decision, so it is set here rather than on the prefab.
        // The same traffic prefab is used on every course, and Everest wants it off while Quarry
        // very much wants it on.
        TrafficDriver driver = car.GetComponent<TrafficDriver>();
        if (driver != null)
        {
            driver.hazardWeight *= obstacleAvoidance;

            // Handed over rather than wired on the prefab, for the same reason obstacle
            // avoidance is: the same traffic prefabs are used on every map, and only the scene
            // knows whether this one is being raced.
            if (Racing) driver.Race(raceTrack);
        }

        if (!scoreTrafficDamage) return;

        CarDamage damage = car.GetComponent<CarDamage>();
        if (damage != null && RunScore.Instance != null) RunScore.Instance.Register(damage);
    }

    /// <summary>
    /// The grid slot at <paramref name="index"/>, for whoever else needs to stand on this grid.
    /// </summary>
    /// <remarks>
    /// The race director puts the PLAYER on the grid through this, rather than working the
    /// position out for itself from the track. Two pieces of code laying out one grid is how you
    /// get a player car spawned inside an AI car — and the failure is a physics explosion at the
    /// green light, which reads as a bug in the cars rather than in the arithmetic.
    ///
    /// Slot 0 is reserved for the player whenever <see cref="raceTrack"/> is set, which is why
    /// the AI spawn loop offsets its index.
    /// </remarks>
    public void GridPose(int index, out Vector3 position, out Quaternion rotation)
    {
        if (UseTrackGrid) raceTrack.Rebuild();
        GridSlot(grid != null ? grid : transform, index, out position, out rotation);
    }

    /// <summary>True when this run is actually a race — a track is wired AND the mode is race.</summary>
    /// <remarks>
    /// THE SAME SCENE SERVES BOTH MODES, which is the whole reason a mode id exists. The Dam is a
    /// race track and a destruction map, and the only difference is which rules are switched on
    /// when it loads — so a wired Race Track means "this map CAN be raced", not "this run is a
    /// race".
    /// </remarks>
    public bool Racing => raceTrack != null && GameSelection.IsRace;

    /// <summary>
    /// Whether to lay the grid out along the track rather than on the Grid transform.
    /// </summary>
    /// <remarks>
    /// Deliberately true in the EDITOR regardless of mode, so the grid gizmo shows the racing
    /// layout whenever a track is wired. Otherwise the gizmo would silently depend on whichever
    /// mode was last played, and a scene would look wrong for a reason that is not in the scene.
    /// </remarks>
    bool UseTrackGrid => raceTrack != null && (!Application.isPlaying || GameSelection.IsRace);

    /// <summary>True when slot 0 is being held for the player.</summary>
    public bool ReservesPlayerSlot => UseTrackGrid;

    /// <summary>Where car <paramref name="index"/> starts, and which way it faces.</summary>
    /// <remarks>
    /// One function for the spawn and the gizmo, so what is drawn in the scene view is what
    /// actually happens. Two copies of a layout is how a grid ends up looking right and
    /// spawning wrong — this project has already paid for that lesson with the car roster.
    ///
    /// ON A TRACK the grid is measured back along the RACING LINE rather than laid out on a
    /// transform's axes. A hand-placed grid transform is only correct if its blue arrow agrees
    /// with the track to within a couple of degrees, and nothing checks that: eight cars start
    /// slightly sideways, all of them correct their line in the first second, and the field is
    /// scattered before the flag drops.
    /// </remarks>
    void GridSlot(Transform origin, int index, out Vector3 position, out Quaternion rotation)
    {
        int row = index / perRow;
        int slot = index % perRow;

        // Centre each row on the grid line rather than growing off to one side.
        float across = (slot - (perRow - 1) * 0.5f) * lateralSpacing;

        if (UseTrackGrid && raceTrack.Count >= 2)
        {
            // Measured backwards from the start line along the lap, so a row is a row of TRACK
            // and stays on the road through a corner instead of running off into the scenery.
            float back = -(gridSetback + row * rowSpacing);
            Vector3 centre = raceTrack.PointAtDistance(back);
            Vector3 forward = raceTrack.ForwardAtDistance(back);
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

            position = centre + right * across + Vector3.up * dropHeight;
            rotation = Quaternion.LookRotation(forward, Vector3.up);
            return;
        }

        position = origin.position
                   + origin.right * across
                   - origin.forward * (row * rowSpacing)
                   + Vector3.up * dropHeight;
        rotation = origin.rotation;
    }

    void OnDrawGizmosSelected()
    {
        Transform origin = grid != null ? grid : transform;
        if (raceTrack != null) raceTrack.Rebuild();

        // count + 1 when the player is on the grid, or the scene view shows a grid one car
        // shorter than the one the race actually forms — and the missing square is the one most
        // worth looking at, since it is the only one not laid out by this component.
        int slots = count + (ReservesPlayerSlot ? 1 : 0);

        for (int i = 0; i < slots; i++)
        {
            GridSlot(origin, i, out Vector3 at, out Quaternion facing);

            Gizmos.color = new Color(1f, 0.78f, 0.15f, 0.9f);
            Gizmos.DrawWireCube(at + Vector3.up * 0.6f, new Vector3(1.7f, 1.2f, 4.2f));
            Gizmos.DrawLine(at, at + facing * Vector3.forward * 3f);
        }
    }
}
