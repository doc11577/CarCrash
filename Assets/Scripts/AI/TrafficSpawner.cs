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

    [Header("Grid")]
    [Tooltip("Where the grid is laid out. Leave empty to use this GameObject's transform. " +
             "Put it in the start bay, behind and beside where the player spawns.")]
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

        for (int i = 0; i < count; i++)
            Spawn(origin, i);

        spawned = count;
    }

    readonly System.Collections.Generic.List<GameObject> usable =
        new System.Collections.Generic.List<GameObject>();

    void Spawn(Transform origin, int index)
    {
        int row = index / perRow;
        int slot = index % perRow;

        // Centre each row on the grid line rather than growing off to one side.
        float across = (slot - (perRow - 1) * 0.5f) * lateralSpacing;
        Vector3 position = origin.position
                           + origin.right * across
                           - origin.forward * (row * rowSpacing)
                           + Vector3.up * dropHeight;

        GameObject prefab = usable[mix.Next(usable.Count)];
        GameObject car = Instantiate(prefab, position, origin.rotation);
        car.name = $"Traffic{index:00}_{prefab.name}";

        CarPaint paint = car.GetComponent<CarPaint>();
        if (paint != null && palette != null && palette.Length > 0)
            paint.Apply(palette[index % palette.Length]);

        // Obstacle avoidance is a per-MAP decision, so it is set here rather than on the prefab.
        // The same traffic prefab is used on every course, and Everest wants it off while Quarry
        // very much wants it on.
        TrafficDriver driver = car.GetComponent<TrafficDriver>();
        if (driver != null) driver.hazardWeight *= obstacleAvoidance;

        if (!scoreTrafficDamage) return;

        CarDamage damage = car.GetComponent<CarDamage>();
        if (damage != null && RunScore.Instance != null) RunScore.Instance.Register(damage);
    }

    void OnDrawGizmosSelected()
    {
        Transform origin = grid != null ? grid : transform;

        Gizmos.color = new Color(1f, 0.78f, 0.15f, 0.9f);
        for (int i = 0; i < count; i++)
        {
            int row = i / perRow;
            int slot = i % perRow;
            float across = (slot - (perRow - 1) * 0.5f) * lateralSpacing;

            Vector3 at = origin.position
                         + origin.right * across
                         - origin.forward * (row * rowSpacing)
                         + Vector3.up * dropHeight;

            Gizmos.DrawWireCube(at + Vector3.up * 0.6f, new Vector3(1.7f, 1.2f, 4.2f));
            Gizmos.DrawLine(at, at + origin.forward * 3f);
        }
    }
}
