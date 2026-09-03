using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Runs a race: the grid, the countdown, the standings, the laps, the finish and the payout.
/// </summary>
/// <remarks>
/// Everything here is bookkeeping over machinery that already exists. <see cref="RaceTrack"/>
/// answers where every car is, <see cref="TrafficDriver"/> drives seven of them, and
/// <see cref="RunScore.Award"/> exists precisely so a rule belonging to ONE mode does not have to
/// live inside RunScore. This component is the rules of the race and nothing else.
///
/// ⚠ IT READS EACH CAR'S EXISTING FOLLOWER AND NEVER MAKES A SECOND ONE. Two followers on one car
/// update at different moments in the frame, so they disagree across a lap boundary and a car
/// appears to gain and lose a lap in consecutive frames. The AI cars own theirs; the player has
/// none of their own, so the director owns exactly one — the player's.
///
/// COST. One follower advance and one sort per frame over eight racers, plus seven distance
/// checks for near misses. Against eight rigidbodies each doing four SphereCasts a physics step,
/// this does not register.
/// </remarks>
[DisallowMultipleComponent]
public class RaceDirector : MonoBehaviour
{
    /// <summary>The race in the current scene, or null if this map is not being raced.</summary>
    public static RaceDirector Instance { get; private set; }

    /// <summary>True while a race is running and cars should be driving.</summary>
    public static bool Green => Instance != null && Instance.state == State.Racing;

    public enum State { Forming, Countdown, Racing, Finished }

    [Header("Race")]
    [Tooltip("The track to race. Leave empty to use the RaceTrack in the scene.")]
    public RaceTrack track;

    [Tooltip("Laps to complete. Three is the design; the whole race is roughly this times the " +
             "lap length, so check the number against how long a lap actually takes.")]
    [Range(1, 20)] public int laps = 3;

    [Tooltip("Seconds of countdown before the green light. The cars are held on the grid for it — " +
             "handbrake on, driver ignored — so the field starts together instead of whenever " +
             "each car's AI happened to wake up.")]
    public float countdown = 3.5f;

    [Tooltip("Move the player's car onto the grid at the start, using the traffic spawner's own " +
             "layout. Off means the player starts wherever PlayerSpawn is, which is right for a " +
             "destruction map and wrong for a race.")]
    public bool playerOnGrid = true;

    [Header("Respawn")]
    [Tooltip("Key that puts the car back on the track. In a race this REPLACES the restart — " +
             "reloading the scene would throw away everyone else's race as well as your own.")]
    public Key respawnKey = Key.R;

    [Tooltip("Metres BEFORE the last safe point the car is put back. Non-zero so a respawn can " +
             "never be used to skip the corner that was being crashed at.")]
    public float respawnSetback = 25f;

    [Tooltip("Metres above the track the car is placed, so it drops onto its wheels.")]
    public float respawnLift = 1.5f;

    [Tooltip("Seconds before another respawn is allowed. Stops a held key looping.")]
    public float respawnCooldown = 1.5f;

    [Header("Payout")]
    [Tooltip("Gears for finishing 1st, 2nd, 3rd. Anything past the end of this list gets the " +
             "finish bonus only.")]
    public int[] placeBonus = { 1500, 900, 500 };

    [Tooltip("Gears for finishing at all, whatever the position. A race you completed should " +
             "always be worth more than one you abandoned.")]
    public int finishBonus = 200;

    [Tooltip("Gears per second spent above the speed below. Rewards committing on the straights " +
             "rather than crawling round a clean line.")]
    public float speedGearsPerSecond = 8f;

    [Tooltip("Metres per second above which the speed bonus pays. 25 m/s is 90 km/h.")]
    public float speedThreshold = 25f;

    [Tooltip("Gears for passing close to another car at speed WITHOUT touching it.")]
    public int nearMissGears = 45;

    [Tooltip("Metres that counts as a near miss. About two car widths.")]
    public float nearMissRadius = 4f;

    [Tooltip("Minimum speed for a near miss to pay, in m/s. Crawling past a parked car is not a " +
             "near miss.")]
    public float nearMissSpeed = 16f;

    [Tooltip("Seconds after taking damage that near misses stop paying. A pass that ends in a " +
             "collision was a collision.")]
    public float nearMissGrace = 0.6f;

    [Header("Read-only — watch these in play mode")]
    [SerializeField] State state = State.Forming;
    [SerializeField] int racerCount;
    [SerializeField] int playerPosition;
    [SerializeField] int playerLap;
    [SerializeField] float raceTime;

    [Tooltip("Grid slot the player started from, counting from 0 at the front. Random by " +
             "default — see TrafficSpawner.randomPlayerSlot.")]
    [SerializeField] int gridSlot;

    /// <summary>One car in the race.</summary>
    public class Racer
    {
        public Transform transform;
        public CarController car;
        public CarDamage damage;
        public RaceTrack.Follower line;
        public string name;
        public bool isPlayer;

        /// <summary>Follower distance at the green light, so grid position does not skew laps.</summary>
        public float startDistance;

        /// <summary>Metres of track covered since the green light.</summary>
        public float progress;

        /// <summary>1-based place in the current standings.</summary>
        public int position = 1;

        public bool finished;
        public float finishTime;

        /// <summary>
        /// Last ABSOLUTE follower distance at which this car was somewhere it could be put back.
        /// </summary>
        /// <remarks>
        /// Absolute — laps included — rather than a distance round the lap. A lap-relative value
        /// has to be un-wrapped against the current lap to respawn from, and the case where that
        /// goes wrong is a car crashing just after the start line: its safe point is on the
        /// previous lap, the wrap puts it back near the end of this one, and the respawn hands
        /// out a free lap.
        /// </remarks>
        public float safeDistance;

        /// <summary>True while this car is close to the player and has not touched them.</summary>
        public bool passing;
    }

    /// <summary>The field, sorted into standings order. Index 0 is leading.</summary>
    public IReadOnlyList<Racer> Standings => order;

    /// <summary>The player's racer, or null before the grid has formed.</summary>
    public Racer Player { get; private set; }

    /// <summary>Seconds left on the countdown, or 0 once racing.</summary>
    public float CountdownLeft { get; private set; }

    /// <summary>Laps set for this race.</summary>
    public int Laps => laps;

    /// <summary>Current state of the race.</summary>
    public State Phase => state;

    readonly List<Racer> racers = new List<Racer>();
    readonly List<Racer> order = new List<Racer>();

    float respawnAt;
    float lastPlayerDamage = -99f;
    int finishers;

    void Awake()
    {
        // ⚠ THE SAME SCENE IS BOTH MODES, so a director sitting in it is not permission to race.
        // It stands itself down in destruction mode and, crucially, does NOT claim Instance —
        // RunRestart stands ITSELF down when a director exists, so a director that merely
        // disabled itself would take R away from destruction mode and nothing would say why.
        if (!GameSelection.IsRace)
        {
            enabled = false;
            return;
        }

        Instance = this;
        if (track == null) track = RaceTrack.Instance;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (Player != null && Player.damage != null) Player.damage.Damaged -= OnPlayerDamaged;
    }

    void Update()
    {
        // The field is gathered on the first Update rather than in Start, because both spawners
        // create their cars in Awake or Start and component order across GameObjects is
        // undefined. By the first Update every Start has run, which makes this deterministic
        // instead of a coin flip that works on four cars and fails on eight.
        if (state == State.Forming)
        {
            Form();
            return;
        }

        if (track == null) return;

        if (state == State.Countdown)
        {
            CountdownLeft -= Time.deltaTime;
            if (CountdownLeft > 0f) return;

            GoGreen();
            return;
        }

        raceTime += Time.deltaTime;

        UpdateProgress();
        UpdateStandings();
        UpdateRespawn();
        UpdateSpeedBonus();
        UpdateNearMisses();
    }

    // ---- forming -------------------------------------------------------------------------

    void Form()
    {
        if (track == null)
        {
            Debug.LogError("RaceDirector has no RaceTrack, so there is nothing to race on. Add " +
                           "one to the scene, or drag it into the Track field.", this);
            enabled = false;
            return;
        }

        if (track.Count < 3)
        {
            Debug.LogError($"RaceDirector: the track has only {track.Count} waypoints. Lay one " +
                           "out with the Race Track inspector before racing on it.", this);
            enabled = false;
            return;
        }

        TrafficSpawner spawner = FindFirstObjectByType<TrafficSpawner>();

        // The player first, so they take the pole slot the spawner reserved.
        PlayerCar player = PlayerCar.Current;
        if (player != null)
        {
            if (playerOnGrid && spawner != null && spawner.ReservesPlayerSlot)
            {
                // The SPAWNER decides which slot is the player's, because the AI had to know
                // which one to skip long before this ran. Asking it rather than choosing here is
                // what stops two cars claiming the same square.
                spawner.GridPose(spawner.PlayerSlot, out Vector3 pose, out Quaternion facing);
                Place(player.transform, pose, facing);
                gridSlot = spawner.PlayerSlot;
            }

            Player = Add(player.transform, "YOU", true);
        }
        else
        {
            Debug.LogWarning("RaceDirector found no player car. The race will run without one.",
                             this);
        }

        foreach (TrafficDriver driver in TrafficDriver.All)
        {
            if (driver == null || !driver.Racing) continue;
            Add(driver.transform, driver.name, false, driver.Line);
        }

        racerCount = racers.Count;

        if (racers.Count == 0)
        {
            Debug.LogError("RaceDirector has no racers. Check that PlayerCarSpawner ran and that " +
                           "TrafficSpawner has its Race Track set.", this);
            enabled = false;
            return;
        }

        // Held on the grid so the field starts together, rather than whenever each car's AI
        // happened to take its first decision.
        foreach (Racer racer in racers)
        {
            if (racer.car != null) racer.car.Frozen = true;

            // Wheels stay on for the whole race. Set here rather than on the prefabs, because
            // the same prefabs are used by destruction mode, where losing a wheel is the point.
            if (racer.damage != null) racer.damage.protectWheels = true;
        }

        if (Player != null && Player.damage != null) Player.damage.Damaged += OnPlayerDamaged;

        CountdownLeft = Mathf.Max(0.1f, countdown);
        state = State.Countdown;
    }

    Racer Add(Transform car, string name, bool isPlayer, RaceTrack.Follower existing = null)
    {
        Racer racer = new Racer
        {
            transform = car,
            car = car.GetComponent<CarController>(),
            damage = car.GetComponent<CarDamage>(),

            // Reuses the AI's own follower. The player has none, so one is made here — and that
            // is the ONLY follower this component ever creates.
            line = existing ?? track.Follow(car.position),
            name = name,
            isPlayer = isPlayer,
        };

        racers.Add(racer);
        order.Add(racer);
        return racer;
    }

    static void Place(Transform car, Vector3 position, Quaternion rotation)
    {
        Rigidbody body = car.GetComponent<Rigidbody>();

        // Velocity cleared as well as the pose set. A car moved while carrying its old velocity
        // keeps travelling in whatever direction it was going, which on a grid means it drives
        // off the line before the countdown has finished.
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        car.SetPositionAndRotation(position, rotation);
    }

    /// <summary>The green light.</summary>
    /// <remarks>
    /// ⚠ NOT CALLED <c>Start</c>. It was, for about a minute, and Unity duly called it as the
    /// MonoBehaviour message — at scene load, over an empty field, setting the state to Racing
    /// before the grid had formed. The countdown never ran and every car was released the instant
    /// the scene opened. A lifecycle name on a method that is not a lifecycle method is a trap
    /// the compiler cannot see.
    /// </remarks>
    void GoGreen()
    {
        foreach (Racer racer in racers)
        {
            if (racer.car != null) racer.car.Frozen = false;

            // Re-acquired at the green light, not at spawn. A car that was nudged, or that
            // settled onto its springs, has moved since it was placed, and the start distance is
            // what every lap count is measured against.
            racer.line.Snap(racer.transform.position);
            racer.startDistance = racer.line.Distance;
            racer.safeDistance = racer.line.Distance;
        }

        CountdownLeft = 0f;
        state = State.Racing;
    }

    // ---- running -------------------------------------------------------------------------

    void UpdateProgress()
    {
        foreach (Racer racer in racers)
        {
            if (racer.transform == null) continue;

            // The AI advance their own followers in their own Update. Advancing them again here
            // would be harmless but wasteful; the player's is the only one that needs it.
            if (racer.isPlayer) racer.line.Advance(racer.transform.position);

            if (racer.finished) continue;

            racer.progress = racer.line.Distance - racer.startDistance;

            // A car is "safe" where it was last both on the track and the right way up. That is
            // the point a respawn goes back to — using the LIVE distance instead would put a car
            // that has driven off a dam back exactly where it drove off.
            bool upright = Vector3.Dot(racer.transform.up, Vector3.up) > 0.5f;
            if (!racer.line.OffTrack && upright && racer.car != null && racer.car.Grounded)
                racer.safeDistance = racer.line.Distance;

            if (racer.progress >= laps * track.Length) Finish(racer);
        }
    }

    void Finish(Racer racer)
    {
        racer.finished = true;
        racer.finishTime = raceTime;
        racer.position = ++finishers;

        // The AI stop driving. Left racing they carry on doing laps behind the player, which
        // reads as the race never having ended.
        if (!racer.isPlayer && racer.car != null) racer.car.Frozen = true;

        if (!racer.isPlayer) return;

        PayPlayer(racer.position);
        state = State.Finished;
    }

    void PayPlayer(int place)
    {
        if (RunScore.Instance == null) return;

        int bonus = finishBonus;
        if (placeBonus != null && place >= 1 && place <= placeBonus.Length)
            bonus += placeBonus[place - 1];

        string caption = place switch
        {
            1 => "WINNER",
            2 => "2ND PLACE",
            3 => "3RD PLACE",
            _ => $"FINISHED {place}TH",
        };

        Vector3 at = Player.transform != null ? Player.transform.position : transform.position;
        RunScore.Instance.Award(bonus, $"{caption}  +{bonus}", at, UiKit.Accent, true);
    }

    /// <summary>
    /// Sorts the field. Finishers first in the order they finished, then everyone else by track
    /// covered.
    /// </summary>
    /// <remarks>
    /// A plain distance sort would put a car that has just finished BEHIND one still racing the
    /// moment its progress stops climbing, so the winner would visibly slide down the standings
    /// while everyone else completed the lap.
    /// </remarks>
    void UpdateStandings()
    {
        order.Sort((a, b) =>
        {
            if (a.finished != b.finished) return a.finished ? -1 : 1;
            if (a.finished && b.finished) return a.finishTime.CompareTo(b.finishTime);
            return b.progress.CompareTo(a.progress);
        });

        for (int i = 0; i < order.Count; i++)
            if (!order[i].finished) order[i].position = i + 1;

        if (Player == null) return;

        playerPosition = Player.position;
        playerLap = LapOf(Player);
    }

    /// <summary>Which lap a racer is on, 1-based and capped at the race length.</summary>
    public int LapOf(Racer racer)
    {
        if (track == null || track.Length < 1f) return 1;
        return Mathf.Clamp(Mathf.FloorToInt(racer.progress / track.Length) + 1, 1, laps);
    }

    // ---- respawn -------------------------------------------------------------------------

    void UpdateRespawn()
    {
        if (Player == null || Player.finished) return;
        if (Time.unscaledTime < respawnAt) return;
        if (UiKit.Typing()) return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !keyboard[respawnKey].wasPressedThisFrame) return;

        Respawn(Player);
    }

    /// <summary>Put a car back on the track at the last place it was safely on it.</summary>
    /// <remarks>
    /// ⚠ THE FOLLOWER IS TOLD WHERE IT NOW IS, rather than being left to work it out. Moving the
    /// car and letting <c>Advance</c> discover it is a jump of tens of metres in one frame, which
    /// is exactly what the search window refuses to accept — so the follower would stay where the
    /// crash happened and the standings would freeze for that car.
    ///
    /// Progress is NOT restored to what it was. Respawning goes back <see cref="respawnSetback"/>
    /// metres, so it can never be used to skip the corner that was being crashed at.
    /// </remarks>
    public void Respawn(Racer racer)
    {
        if (racer == null || track == null || racer.transform == null) return;

        // Absolute distance throughout, so no wrap arithmetic is needed and none can be got
        // wrong. RespawnPose and SetDistance both wrap internally.
        float at = racer.safeDistance - Mathf.Max(0f, respawnSetback);

        track.RespawnPose(at, 0f, respawnLift, out Vector3 pose, out Quaternion facing);
        Place(racer.transform, pose, facing);

        // SetDistance, not Snap. Snap searches the whole track and RESETS the lap count, which
        // would hand a player on lap 3 a three-lap penalty for going off.
        racer.line.SetDistance(at);

        racer.progress = racer.line.Distance - racer.startDistance;
        racer.safeDistance = at;
        respawnAt = Time.unscaledTime + respawnCooldown;
    }

    // ---- scoring -------------------------------------------------------------------------

    void UpdateSpeedBonus()
    {
        if (Player == null || Player.finished || Player.car == null) return;
        if (RunScore.Instance == null) return;
        if (Player.car.Speed < speedThreshold) return;

        // Paid continuously and SILENTLY — no popup. A popup every frame would recycle the eight
        // popup slots constantly and starve everything else on screen, which is exactly the fault
        // that hid the WRECKER caption when traffic damage started paying out.
        //
        // Accumulated to whole gears before being awarded, because Award takes an int: paying
        // `8 * deltaTime` every frame rounds to zero every time and the bonus never arrives.
        speedCredit += speedGearsPerSecond * Time.deltaTime;

        int whole = Mathf.FloorToInt(speedCredit);
        if (whole <= 0) return;

        speedCredit -= whole;
        RunScore.Instance.Award(whole, null, Vector3.zero, Color.white, false);
    }

    float speedCredit;

    void OnPlayerDamaged(CarDamage source, float damage, Vector3 at, bool sustained, bool byPlayer)
    {
        lastPlayerDamage = Time.time;
    }

    /// <summary>
    /// Pays for going past another car closely and cleanly.
    /// </summary>
    /// <remarks>
    /// Measured as a STATE rather than an instant: the pass begins when a car comes within the
    /// radius at speed and pays when it leaves again, so a single frame of proximity does not
    /// pay and a long side-by-side fight pays once rather than every frame.
    ///
    /// A pass that ends in a collision was a collision. That is why the damage time is tracked —
    /// the PvP payout already covers hitting people, and paying both would make ramming the most
    /// profitable way to "pass".
    /// </remarks>
    void UpdateNearMisses()
    {
        if (Player == null || Player.finished || Player.transform == null) return;
        if (Player.car == null || RunScore.Instance == null) return;

        bool fast = Player.car.Speed >= nearMissSpeed;
        float exit = nearMissRadius * 1.4f;

        foreach (Racer other in racers)
        {
            if (other.isPlayer || other.transform == null) continue;

            float gap = Vector3.Distance(Player.transform.position, other.transform.position);

            if (!other.passing)
            {
                if (fast && gap < nearMissRadius) other.passing = true;
                continue;
            }

            if (gap < exit) continue;

            other.passing = false;

            if (Time.time - lastPlayerDamage < nearMissGrace) continue;

            RunScore.Instance.Award(nearMissGears, $"NEAR MISS  +{nearMissGears}",
                                    Player.transform.position, new Color(0.35f, 0.85f, 1f), false);
        }
    }
}
