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

    [Header("Arcade handling — applied to every racer at the start")]
    [Tooltip("Switch the arcade assists on for a race. Off leaves every car exactly as a " +
             "destruction run drives it, which is the right comparison when something feels " +
             "wrong and you want to know whether the assists are the cause.")]
    public bool arcadeHandling = true;

    [Tooltip("Righting torque, in m/s². Past its dead zone this is what makes a race car almost " +
             "never end up on its roof. Cornering lean is untouched — see CarController.")]
    public float uprightTorque = 11f;

    [Tooltip("How hard rotation the steering did not ask for is cancelled, 0-1. This is the " +
             "anti-spin. It only ever subtracts, so drifting still works.")]
    [Range(0f, 1f)] public float antiSpin = 0.65f;

    [Tooltip("Multiplies air control torque and the spin it may build. Above 1 gives the loose, " +
             "steerable air a stunt racer wants.")]
    public float airControlScale = 2.2f;

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

    [Header("Drifting")]
    [Tooltip("Gears per second of drift, at the best angle. Scaled by how sideways the car is " +
             "and by speed, so a lazy slide pays little and a committed one pays properly.")]
    public float driftGearsPerSecond = 55f;

    [Tooltip("Sideslip in degrees before it counts as a drift at all. Below this it is just " +
             "cornering, and paying for that would pay for driving normally.")]
    public float driftMinAngle = 14f;

    [Tooltip("Sideslip at which the drift is worth full rate. Past it the payout stops rising, " +
             "so there is nothing to gain from spinning.")]
    public float driftBestAngle = 42f;

    [Tooltip("Sideslip past which the car is spinning rather than drifting and the drift ends.")]
    public float driftMaxAngle = 78f;

    [Tooltip("Minimum speed for a drift to count, in m/s. Sliding at walking pace is not a drift.")]
    public float driftMinSpeed = 11f;

    [Tooltip("Seconds the car may straighten up or leave the ground without the drift ending. " +
             "This is what CHAINS corners together — a flick from one drift into the next stays " +
             "one combo rather than banking twice.")]
    public float driftGrace = 0.65f;

    [Tooltip("Gears below which a finished drift raises no popup, so a twitch does not litter " +
             "the screen.")]
    public int driftMinPayout = 15;

    [Header("Read-only — watch these in play mode")]
    [SerializeField] State state = State.Forming;
    [SerializeField] int racerCount;
    [SerializeField] int playerPosition;
    [SerializeField] int playerLap;
    [SerializeField] float raceTime;

    [Tooltip("Grid slot the player started from, counting from 0 at the front. Random by " +
             "default — see TrafficSpawner.randomPlayerSlot.")]
    [SerializeField] int gridSlot;

    [Tooltip("Metres of track the player has covered since the green light.")]
    [SerializeField] float playerProgress;

    [Tooltip("The whole field and how far each has got. If every AI reads 0 m while the player " +
             "climbs, their followers are not being advanced — that is a follower problem, not " +
             "a sorting one.")]
#if UNITY_EDITOR
    [SerializeField] string standings;
#endif

    /// <summary>One car in the race.</summary>
    public class Racer
    {
        public Transform transform;
        public CarController car;
        public CarDamage damage;
        public string name;
        public bool isPlayer;

        /// <summary>The AI driving this car, or null for the player.</summary>
        public TrafficDriver driver;

        /// <summary>The player's follower. AI cars use their driver's — see <see cref="Line"/>.</summary>
        public RaceTrack.Follower ownLine;

        /// <summary>
        /// Where this car is on the track, asked of its driver FRESH every time.
        /// </summary>
        /// <remarks>
        /// ⚠ NOT A CACHED REFERENCE, and that is the fix for a bug that made every AI read as
        /// having made no progress. Holding a reference to `driver.Line` means holding whichever
        /// follower existed at the moment the field was gathered — and if anything replaces that
        /// object afterwards, this component is left watching a follower nobody advances. Its
        /// distance sits at the start line forever and the player is permanently first.
        ///
        /// Asking the driver each time costs a null check and cannot go stale. The rule is still
        /// one follower per car; this just stops there being a second way to get it wrong.
        /// </remarks>
        public RaceTrack.Follower Line => driver != null && driver.Line != null
            ? driver.Line
            : ownLine;

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
        UpdateDrift();
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
            Add(driver.transform, driver.name, false, driver);
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

            // Arcade handling, applied to the WHOLE FIELD rather than to the player. An AI that
            // rolls over on a kerb the player cannot roll on is not seven opponents, it is seven
            // obstacles — and the standings would be decided by which AI survived rather than by
            // which drove well.
            if (!arcadeHandling || racer.car == null) continue;

            racer.car.uprightTorque = uprightTorque;
            racer.car.antiSpin = antiSpin;
            racer.car.airControlScale = airControlScale;
        }

        if (Player != null && Player.damage != null) Player.damage.Damaged += OnPlayerDamaged;

        CountdownLeft = Mathf.Max(0.1f, countdown);
        state = State.Countdown;
    }

    Racer Add(Transform car, string name, bool isPlayer, TrafficDriver driver = null)
    {
        Racer racer = new Racer
        {
            transform = car,
            car = car.GetComponent<CarController>(),
            damage = car.GetComponent<CarDamage>(),

            // The DRIVER is stored, not its follower. An AI's follower is read through it on
            // every access, so replacing that object cannot leave this component watching a
            // follower nobody advances. Only the player, who has no driver, gets one made here.
            driver = driver,
            ownLine = driver != null ? null : track.Follow(car.position),
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

            // Re-acquired at the green light, not at spawn: a car has settled on its springs and
            // may have been nudged since it was placed.
            racer.Line.Snap(racer.transform.position);

            // ⚠ THE GRID IS BEHIND THE START LINE, WHICH PROJECTS ONTO THE LAST SEGMENT — so a
            // car on the grid reads as being nearly a whole lap AHEAD rather than a few metres
            // behind. Put those cars on lap -1, so the whole field shares one increasing
            // coordinate with the start line at zero and the grid at small negative numbers.
            //
            // This is what makes position a comparison at all. See UpdateProgress.
            if (racer.Line.LapFraction > 0.5f)
                racer.Line.SetDistance(racer.Line.LapDistance - track.Length);

            racer.safeDistance = racer.Line.Distance;
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
            if (racer.isPlayer) racer.Line.Advance(racer.transform.position);

            if (racer.finished) continue;

            // ⚠ ABSOLUTE TRACK DISTANCE, NOT DISTANCE TRAVELLED. This used to be measured from
            // each car's OWN grid slot, which is a different quantity and ranks the field wrongly:
            // a car that started on the back row and has drawn level with one from the front row
            // has travelled further, so it read as being AHEAD of a car it is sitting beside.
            //
            // Reported as the position "changing at the wrong time, sometimes too early" — which
            // is exactly what a ranking that is right about the wrong thing looks like. Position
            // is who is furthest round the track, full stop, and every racer now measures it
            // against the same origin: the start line.
            racer.progress = racer.Line.Distance;

            // A car is "safe" where it was last both on the track and the right way up. That is
            // the point a respawn goes back to — using the LIVE distance instead would put a car
            // that has driven off a dam back exactly where it drove off.
            bool upright = Vector3.Dot(racer.transform.up, Vector3.up) > 0.5f;
            if (!racer.Line.OffTrack && upright && racer.car != null && racer.car.Grounded)
                racer.safeDistance = racer.Line.Distance;

            if (LapsDone(racer) >= laps) Finish(racer);
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

        // The standings as text, because a position stuck at 1 looks identical whether the sort
        // is wrong, the progress is wrong or the player is simply winning — and this project has
        // already lost a session to a frozen readout that could not tell those apart.
        //
        // Every racer's metres are here: if the AI all sit at zero while the player climbs, the
        // fault is their followers, not the sort.
        // ⚠ EDITOR ONLY. Built with string concatenation over eight racers, which is eight
        // allocations a frame — on a 512 MB WASM heap with one thread, a debug readout nobody can
        // see in a build is not worth any of that. The field it fills is an Inspector readout and
        // there is no Inspector on a Chromebook.
#if UNITY_EDITOR
        standings = "";
        for (int i = 0; i < order.Count && i < 8; i++)
            standings += $"{i + 1}. {order[i].name} {order[i].progress:0} m   ";
#endif

        if (Player == null) return;

        playerPosition = Player.position;
        playerLap = LapOf(Player);
        playerProgress = Player.progress;
    }

    /// <summary>
    /// Whole laps a racer has completed since the start line.
    /// </summary>
    /// <remarks>
    /// Falls straight out of the common coordinate: the grid sits at a small NEGATIVE distance,
    /// so the floor is -1 on the grid, 0 from the first crossing of the line, 1 from the second,
    /// and the race ends when it reaches the lap count. Nothing needs to know where a car
    /// started, which is the property the old per-car origin lacked.
    /// </remarks>
    public int LapsDone(Racer racer)
    {
        if (track == null || track.Length < 1f) return 0;
        return Mathf.Max(0, Mathf.FloorToInt(racer.progress / track.Length));
    }

    /// <summary>Which lap a racer is ON, 1-based and capped at the race length.</summary>
    public int LapOf(Racer racer) => Mathf.Clamp(LapsDone(racer) + 1, 1, laps);

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
        racer.Line.SetDistance(at);

        racer.progress = racer.Line.Distance;
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

        // A drift that ends in the wall was not a drift. Asphalt cancels the bank on contact and
        // it is the right call: without it the cheapest way to earn is to slide into scenery,
        // which is the opposite of the skill the mechanic is there to reward.
        DropDrift();
    }

    // ---- drifting ---------------------------------------------------------------------------

    /// <summary>
    /// Sideways, on purpose, pays — banked when the slide ends rather than while it lasts.
    /// </summary>
    /// <remarks>
    /// Modelled on Asphalt 8, which is the reference Ethan asked for. Three properties are what
    /// make it feel like that game rather than like a slip-angle readout:
    ///
    ///   * IT CHAINS. <see cref="driftGrace"/> lets the car straighten, or leave the ground,
    ///     without ending the drift — so flicking out of one corner and into the next is ONE
    ///     combo that keeps climbing, which is where the drama is.
    ///   * IT IS BANKED ON EXIT, not paid continuously. The counter climbing while the outcome is
    ///     still in doubt is the tension; the same argument as airtime, and the same reason a
    ///     jump that ends in a ravine pays nothing.
    ///   * IT IS LOST ON CONTACT. See OnPlayerDamaged.
    ///
    /// The angle term is the honest part of the scoring: a lazy 15-degree slide earns almost
    /// nothing and a committed 40-degree one earns full rate, but past <see cref="driftBestAngle"/>
    /// there is no more to gain — so there is never a reason to spin.
    /// </remarks>
    void UpdateDrift()
    {
        if (Player == null || Player.finished || Player.car == null) return;
        if (RunScore.Instance == null) return;

        float slip = Mathf.Abs(Player.car.Sideslip);

        bool sliding = Player.car.Grounded
                       && Player.car.Speed >= driftMinSpeed
                       && slip >= driftMinAngle
                       && slip <= driftMaxAngle;

        if (sliding)
        {
            driftStraightFor = 0f;

            // Angle and speed both matter. Speed is capped at the car's own top speed so a
            // faster car does not simply earn more for the same manoeuvre.
            float quality = Mathf.InverseLerp(driftMinAngle, driftBestAngle, slip);
            float pace = Mathf.Clamp01(Player.car.Speed / Mathf.Max(1f, Player.car.topSpeed));

            driftCredit += driftGearsPerSecond * quality * (0.4f + 0.6f * pace) * Time.deltaTime;
            driftTime += Time.deltaTime;
            return;
        }

        if (driftCredit <= 0f) return;

        // Straightened, slowed, spun or landed. The grace is what chains corners together, so
        // this only banks once the car has genuinely stopped drifting.
        driftStraightFor += Time.deltaTime;
        if (driftStraightFor < driftGrace) return;

        BankDrift();
    }

    void BankDrift()
    {
        int gears = Mathf.FloorToInt(driftCredit);
        driftCredit = 0f;
        driftTime = 0f;
        driftStraightFor = 0f;

        if (gears < driftMinPayout || Player == null || Player.transform == null) return;

        RunScore.Instance.Award(gears, $"DRIFT  +{gears}", Player.transform.position,
                                new Color(1f, 0.55f, 0.25f), gears >= driftMinPayout * 6);
    }

    /// <summary>Throws away an unbanked drift. Called when the player hits something.</summary>
    void DropDrift()
    {
        driftCredit = 0f;
        driftTime = 0f;
        driftStraightFor = 0f;
    }

    /// <summary>Gears the current drift is worth so far. For a live HUD counter.</summary>
    public int DriftGears => Mathf.FloorToInt(driftCredit);

    /// <summary>True while a drift is worth showing on screen.</summary>
    public bool Drifting => driftCredit > 0f && driftStraightFor < driftGrace;

    float driftCredit;
    float driftTime;
    float driftStraightFor;

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
