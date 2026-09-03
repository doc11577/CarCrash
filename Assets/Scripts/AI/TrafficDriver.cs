using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives a car down a hill by looking for the way down, or round a track by following its line.
/// </summary>
/// <remarks>
/// TWO STEERING RULES IN ONE COMPONENT, chosen by whether a <see cref="RaceTrack"/> has been
/// handed over. They share everything below the choice of direction — the hazard sweep, the
/// sideslip catch, the speed-scaled steering slew, the wrong-way turnaround, the stuck recovery —
/// and that sharing is the point: a race car and a traffic car that handled differently would be
/// two things to fix every time something went wrong with either.
///
///   DESTRUCTION — "go where the ground drops most", described below.
///   RACE — aim at a point ON the racing line, offset into a chosen lane. See `RaceDecide`, and
///   note that it does NOT score directions: it cannot choose to leave the line, which is what
///   three rounds of penalty tuning failed to achieve.
///
/// THE DESTRUCTION RULE IS "GO WHERE THE GROUND DROPS MOST". A fan of short downward probes
/// is thrown ahead of the car, and it steers at whichever one finds the biggest drop.
///
/// That one rule covers everything this map needs, which is why there is no path and no
/// waypoint graph:
///
///   * The valley floor descends, so following the drop follows the course.
///   * A wall goes UP, so it scores badly and is steered away from for free.
///   * A kicker, a hump or a boulder also goes up, so obstacles are avoided by the same test
///     rather than by a second system that has to agree with the first.
///
/// It also degrades sensibly. On a map with no clear descent the cars simply slow and stop
/// instead of driving into scenery with confidence.
///
/// COST. Probes are raycasts, taken at a fixed rate rather than every physics step: the 7-probe
/// fan (one downward ray plus one sphere sweep each) at 14 Hz plus a 12-ray full-circle scan at
/// 3 Hz is about 4 casts per physics step per car, against the 4 the car itself already does.
/// Measured on a school Chromebook 2026-08-31: the whole game holds 60 FPS.
///
/// Traffic here is NOT the "kinematic until struck" scheme in the architecture notes. That was
/// sized for ~20 background cars; these three are racing the player and have to drive over the
/// same rollers and kickers, so they get real physics. If the roster ever grows to twenty, the
/// kinematic scheme comes back for the extras.
/// </remarks>
[RequireComponent(typeof(CarController))]
[DisallowMultipleComponent]
public class TrafficDriver : MonoBehaviour, ICarDriver
{
    /// <summary>Every live traffic car, for separation without a physics query.</summary>
    static readonly List<TrafficDriver> Live = new List<TrafficDriver>();

    /// <summary>
    /// AI cars alive right now. Published for the perf readout, not for gameplay.
    /// </summary>
    /// <remarks>
    /// Every one of these is a rigidbody doing four SphereCasts a physics step plus about four
    /// of its own, and rigidbody count is named in the project's budget as the first thing that
    /// will blow the frame time. There is no Inspector on a Chromebook, so the only way to know
    /// whether a measurement was taken with the field intact is to put the number on screen.
    /// </remarks>
    public static int LiveCount => Live.Count;

    /// <summary>
    /// Every AI car alive, in spawn order. Read-only — this is the register, not a scratch list.
    /// </summary>
    /// <remarks>
    /// Exists so the race director can build the standings without a scene search, and more
    /// importantly so it can reuse each car's EXISTING follower. Giving the director its own
    /// follower per car would be two objects tracking one car from different moments in the
    /// frame; they disagree across a lap boundary, and a car appears to gain and lose a lap in
    /// consecutive frames.
    /// </remarks>
    public static IReadOnlyList<TrafficDriver> All => Live;

    [Header("Looking ahead")]
    [Tooltip("How far ahead the probes sample when stopped, in metres. Too short and it cannot " +
             "see a wall in time; too long and it ignores what is directly in front of it.")]
    public float lookAhead = 22f;

    [Tooltip("EXTRA metres of lookahead per m/s of speed. This is what lets them commit.\n\n" +
             "A fixed lookahead is a fixed DISTANCE but a shrinking amount of TIME: 26 m at " +
             "30 m/s is 0.87 s of warning, which is not enough to turn a 1200 kg car, so the " +
             "only safe answer was to go slowly. At 0.9 the same car looks 1.6 s ahead and can " +
             "carry full throttle.")]
    public float lookAheadPerSpeed = 0.9f;

    [Tooltip("Probes in the fan. Odd numbers keep one pointing straight ahead.")]
    [Range(3, 15)] public int probes = 7;

    [Tooltip("Half-angle of the fan, in degrees.")]
    public float probeSpread = 55f;

    [Tooltip("Steering decisions per second. The probes only fire this often; steering is " +
             "smoothed between them, so raising it costs casts and buys very little.")]
    public float decisionsPerSecond = 14f;

    [Tooltip("Surfaces the probes can see. Default only, same as CarController's ground mask.")]
    public LayerMask groundMask = 1;

    [Header("Driving")]
    [Tooltip("Throttle when the way ahead is clear and straight.")]
    [Range(0f, 1f)] public float cruiseThrottle = 1f;

    [Tooltip("How much a hard turn cuts the throttle. 0 means it never lifts for a corner.\n\n" +
             "Low on purpose. These are meant to be flat out down the hill, and with the " +
             "lookahead now scaling with speed they see a corner early enough to take it " +
             "without braking for it.")]
    [Range(0f, 1f)] public float cornerLift = 0.28f;

    [Tooltip("How sharply the steering chases the chosen direction.")]
    public float steerRate = 6.5f;

    [Tooltip("Multiplies this car's top speed and engine power at Awake, so traffic can be " +
             "quicker than the player's car without editing the shared vehicle values.\n\n" +
             "Careful above about 1.2: traffic faster than the player disappears down the hill " +
             "in the first ten seconds and there is nothing left to crash into.")]
    [Range(0.5f, 2f)] public float speedBoost = 1.15f;

    [Tooltip("Penalty per degree off straight ahead, in metres of drop. Without it the car " +
             "weaves after every marginally better line instead of committing to one. Scaled " +
             "with the live lookahead, since a longer probe finds bigger drops and a fixed " +
             "penalty would be swamped by them at speed.")]
    public float straightBias = 0.035f;

    [Header("Dodging")]
    [Tooltip("Height above the car that the obstacle sweep starts, in metres. Together with " +
             "Hazard Radius this decides what counts as an obstacle: the sphere spans roughly " +
             "Height±Radius above the ground, so it should sit above the course's surface noise " +
             "(0.55 m) and still catch a boulder that only just protrudes.")]
    public float hazardHeight = 1.4f;

    [Tooltip("Radius of the obstacle sweep, in metres. Around the car's half-width. Larger " +
             "makes them give obstacles a wider berth and refuse tighter gaps.")]
    public float hazardRadius = 0.7f;

    [Tooltip("How much a blocked path counts against a direction, against metres of descent " +
             "gained. Too low and they drive through rocks to reach a better line; too high and " +
             "they refuse to pass anything and crawl down the middle.")]
    public float hazardWeight = 2.2f;

    [Header("Keeping control")]
    [Tooltip("Sideslip in degrees — the angle between where the car POINTS and where it is " +
             "actually going — beyond which it counts as sliding. Past this the steering stops " +
             "chasing the racing line and starts catching the slide.")]
    public float slipLimit = 14f;

    [Tooltip("How hard it counter-steers into a slide. 1 steers fully into it, 0 ignores the " +
             "slide entirely and keeps demanding the line that caused it.")]
    [Range(0f, 1f)] public float counterSteer = 0.8f;

    [Tooltip("Throttle cut at full slide. Lifting is most of how a real driver catches an " +
             "oversteer, and it costs the AI far less time than spinning does.")]
    [Range(0f, 1f)] public float slipLift = 0.55f;

    [Tooltip("Steering slew is divided by this much at top speed. A rate that feels responsive " +
             "at 10 m/s is a flick of the wheel at 40 and will spin the car.")]
    [Range(1f, 4f)] public float steerRateAtSpeed = 2.4f;

    [Header("Turning around")]
    [Tooltip("Full-circle scans per second, used to find downhill regardless of facing and to " +
             "decide arrival. 12 casts each.")]
    public float scansPerSecond = 3f;

    [Tooltip("Degrees away from downhill that counts as facing the wrong way.")]
    public float wrongWayAngle = 105f;

    [Tooltip("Seconds facing the wrong way before committing to a turnaround. Stops a spin " +
             "mid-crash from triggering one.")]
    public float wrongWayTime = 1.2f;

    [Header("Racing")]
    [Tooltip("Read-only here — set by whatever puts the car on the grid, and left empty on a " +
             "destruction map. When a track is present the steering rule changes from 'go where " +
             "the ground drops most' to 'go where the most TRACK is gained', and nothing else " +
             "about the driver changes: the hazard sweep, the interpolated angle, the separation " +
             "bias, the slide catch and the speed-scaled lookahead all apply to a race unaltered.")]
    public RaceTrack track;

    [Tooltip("Metres up the track the car aims when stopped. The pursuit target is always ON " +
             "the racing line, which is what makes corner cutting impossible rather than merely " +
             "expensive.\n\n" +
             "Short makes it hug the line and wobble; long makes it smooth and lazy into corners.")]
    public float aimAhead = 14f;

    [Tooltip("EXTRA metres of aim per m/s, so the car looks further ahead the faster it goes and " +
             "gets the same amount of TIME to react. Same argument as Look Ahead Per Speed.")]
    public float aimAheadPerSpeed = 0.55f;

    [Tooltip("Fraction of the HALF-WIDTH that the straight line to the aim point may stray from " +
             "the track before the aim is pulled in closer.\n\n" +
             "⚠ THIS IS THE ONE WAY PURE PURSUIT CAN STILL CUT A CORNER, so it is the number to " +
             "reach for if they do. Aiming at a point on the line is no defence if the line to " +
             "THAT point crosses the infield — which is exactly what a distant aim point does on " +
             "a hairpin. Lower is tighter and safer; too low and the car chases its own bumper " +
             "and weaves on the straights.")]
    [Range(0.1f, 1f)] public float aimDeviation = 0.35f;

    [Header("Racing — lanes and passing")]
    [Tooltip("Lanes considered across the road. Odd keeps one on the centreline. This is the " +
             "fan, repurposed: it used to choose the HEADING, which is what let it choose a " +
             "heading off the road. Now it chooses only how far ACROSS the road to aim.")]
    [Range(3, 9)] public int lanes = 5;

    [Tooltip("Metres of road kept clear at each edge, so picking the outside lane does not put " +
             "the wheels in the dirt. About half a car.")]
    public float laneMargin = 1.8f;

    [Tooltip("Metres per second the lane may slide across the road. A lane change is a " +
             "manoeuvre: snapping the target sideways is a flick of the wheel that unsettles the " +
             "car mid-corner and reads as twitching rather than as choosing a line.")]
    public float laneRate = 6f;

    [Tooltip("Pull back toward the centre of the road, per metre. This is what makes them sit " +
             "on the line when nothing is in the way — and unlike the penalty it replaced, it " +
             "can be small, because it is no longer the only thing preventing a corner cut.")]
    public float laneCentreBias = 0.35f;

    [Tooltip("Cost of moving off the lane currently held, per metre. Hysteresis: without it the " +
             "car swaps lanes whenever two score within a hair of each other.")]
    public float laneChangeCost = 0.25f;

    [Tooltip("How much a lane blocked by another car counts against it. THIS IS THE OVERTAKING " +
             "KNOB — raise it and they commit to a pass, lower it and they queue up behind.")]
    public float passWeight = 3f;

    [Tooltip("Metres ahead that another car still blocks a lane. Roughly how far ahead a pass " +
             "is planned.")]
    public float passLookahead = 30f;

    [Tooltip("Metres either side of a lane that another car occupies. About a car's width plus " +
             "room to get by.")]
    public float laneClearance = 2.6f;

    [Header("Racing — braking")]
    [Tooltip("Lateral acceleration the AI believes it has, in m/s². This sets corner entry " +
             "speed: a corner of radius R is taken at sqrt(grip x R), so 12 takes a 40 m corner " +
             "at 22 m/s.\n\n" +
             "Tune it by watching, not by deriving it from the tyre model — turn assist means " +
             "the car corners better than its grip values alone suggest. Too high and they run " +
             "wide at every corner exit; too low and they crawl round everything.")]
    public float cornerGrip = 12f;

    [Tooltip("Braking the AI plans on, in m/s². Decides how EARLY it lifts, not how hard it " +
             "stops — a low value makes it brake sooner and more gently, which is what a car " +
             "that looks smooth does. Below the car's real braking or it arrives too fast.")]
    public float brakeAccel = 7f;

    [Tooltip("Metres of track scanned ahead for corners to brake for. Must cover the braking " +
             "distance from top speed: at 32 m/s and 7 m/s² that is 73 m, so 90 has margin.")]
    public float brakeScan = 90f;

    [Tooltip("m/s the car may exceed its corner speed before it brakes rather than coasts. A " +
             "band, because a single threshold makes it alternate full throttle and full brake " +
             "every frame at the limit, which is both slower and visibly twitchy.")]
    public float speedTolerance = 1.5f;

    [Header("Separation")]
    [Tooltip("Cars closer than this push each other apart. 0 disables.")]
    public float separationRadius = 10f;

    [Tooltip("How hard they push. Too high and they refuse to race side by side.")]
    public float separationStrength = 0.7f;

    [Header("Stuck recovery")]
    [Tooltip("Below this speed the car counts as not making progress, in m/s.")]
    public float stuckSpeed = 1.8f;

    [Tooltip("Seconds of no progress before it reverses out.")]
    public float stuckTime = 1.8f;

    [Tooltip("Seconds spent reversing before trying again.")]
    public float reverseTime = 1.3f;

    [Header("Finishing")]
    [Tooltip("Best drop across the whole fan, in metres, below which the car considers itself " +
             "at the bottom and parks. Without this they mill around the stopping bowl forever.")]
    public float arrivedDrop = 0.35f;

    [Tooltip("Seconds of finding no descent before parking.")]
    public float arrivedTime = 2.5f;

    public float Throttle { get; private set; }
    public float Steer { get; private set; }
    public bool Handbrake { get; private set; }

    [Header("Read-only — watch these in play mode")]
    [SerializeField] float bestDropReadout;
    [SerializeField] float chosenAngleReadout;
    [SerializeField] bool reversingReadout;
    [SerializeField] bool arrivedReadout;
    [SerializeField] float speedReadout;
    [SerializeField] float reachReadout;
    [SerializeField] float slipReadout;
    [SerializeField] float wrongWayReadout;
    [SerializeField] bool turningAroundReadout;

    [Tooltip("How blocked the chosen direction is. Sitting at 0 while driving past rocks means " +
             "the hazard test is not seeing obstacles at all — which is exactly what a wrong " +
             "baseline looks like, and it looked like nothing else.")]
    [SerializeField] float hazardReadout;

    [Tooltip("Metres of track this car has covered, laps included. Only moves in race mode. " +
             "Sitting at 0 while the car drives means the follower never acquired the track.")]
    [SerializeField] float raceDistanceReadout;

    [Tooltip("Metres this car is from the centreline. Compare against half the track's Width: " +
             "a car that reads well over it while driving normally is following a line that is " +
             "not on the road, which is a WAYPOINT problem, not an AI one.")]
    [SerializeField] float offLineReadout;

    [Tooltip("Speed the corner ahead allows, in m/s. Watch it against the car's actual speed: " +
             "sitting at a low number on a straight means the track's corner radius is being " +
             "misread, which is a WAYPOINT spacing problem — three samples landing on one " +
             "straight segment report a hairpin as a straight and vice versa.")]
    [SerializeField] float targetSpeedReadout;

    [Tooltip("True while the AI is on the brakes for a corner it can see coming.")]
    [SerializeField] bool brakingReadout;

    [Tooltip("Metres right of the centreline the car is currently aiming. 0 is the middle of the " +
             "road; it moves off only to pass or to dodge something.")]
    [SerializeField] float laneReadout;

    /// <summary>Where this car is round the track, or null on a destruction map.</summary>
    /// <remarks>
    /// Public so a race director can read the standings without giving every car a SECOND
    /// follower. Two followers on one car is not merely wasteful: they update at different
    /// moments, so they disagree across a lap boundary and the car appears to gain and lose a
    /// lap in consecutive frames.
    /// </remarks>
    public RaceTrack.Follower Line { get; private set; }

    /// <summary>True while this car is racing a track rather than seeking the descent.</summary>
    public bool Racing => track != null && Line != null;

    CarController car;
    Rigidbody body;

    float nextDecisionAt;
    float chosenAngle;
    float slowSince = -1f;
    float noDescentSince = -1f;
    float reverseUntil = -1f;
    float nextScanAt;
    float lane;
    Vector3 aimPoint;
    Vector3 laneLeft;
    Vector3 laneRight;
    float wrongWaySince = -1f;
    bool turningAround;
    Vector3 downhill = Vector3.forward;
    float downhillDrop;
    float[] scores;
    bool arrived;

    void Awake()
    {
        car = GetComponent<CarController>();
        body = GetComponent<Rigidbody>();

        // Take the wheel. A traffic car built from the player's prefab may still carry a
        // CarInput; left enabled it would read the same keyboard the player is using and every
        // traffic car would mirror their steering.
        scores = new float[Mathf.Max(3, probes)];
        car.Driver = this;

        CarInput keyboard = GetComponent<CarInput>();
        if (keyboard != null) keyboard.enabled = false;

        // Applied to this INSTANCE's controller, so the shared vehicle tuning the player uses is
        // untouched. Engine power is scaled with top speed, or the car simply takes longer to
        // reach a higher ceiling and ends up no faster where it matters.
        if (!Mathf.Approximately(speedBoost, 1f))
        {
            car.topSpeed *= speedBoost;
            car.enginePower *= speedBoost;
        }
    }

    void OnEnable() => Live.Add(this);
    void OnDisable() => Live.Remove(this);

    void Start()
    {
        // Start, not Awake: RaceTrack builds its distance table in ITS Awake, and component
        // order across GameObjects is undefined. Acquiring here means the table exists.
        //
        // ⚠ ONLY IF THERE ISN'T ONE ALREADY. This used to assign unconditionally, which quietly
        // threw away the follower `Race` had just created and replaced it with an identical
        // second one. Anything that had already taken a reference to the first — the race
        // director does, at exactly this point in the frame — was then holding a follower that
        // nothing advances. Its distance sat at the start line forever, so every AI read as
        // having made no progress and the player was permanently P1.
        //
        // The rule this breaks is written at the top of RaceTrack: ONE follower per car. It is
        // worth noting that the fault was invisible in isolation, because the abandoned follower
        // is a perfectly valid object that simply never moves.
        if (track != null && Line == null) Line = track.Follow(transform.position);
    }

    /// <summary>Put this car on a track and switch it from descent-seeking to racing.</summary>
    /// <remarks>
    /// Called by whatever spawns the grid. Safe before or after Start — if the track is handed
    /// over early, Start acquires the follower; if late, this does.
    /// </remarks>
    public void Race(RaceTrack onTrack)
    {
        track = onTrack;
        Line = onTrack != null ? onTrack.Follow(transform.position) : null;

        // A race has no bottom of the hill to arrive at. The finish is the director's business,
        // and a car that parked itself three laps early would look exactly like a crash.
        arrived = false;
    }

    void Update()
    {
        if (Time.timeScale <= 0f) return;

        // Every frame, not at the decision rate. The follower is what the standings are read
        // from, and 14 Hz is enough for steering but visibly steppy on a position readout.
        if (Line != null)
        {
            Line.Advance(transform.position);
            raceDistanceReadout = Line.Distance;
            offLineReadout = Line.Offset;
        }

        if (Time.time >= nextDecisionAt)
        {
            nextDecisionAt = Time.time + 1f / Mathf.Max(1f, decisionsPerSecond);
            Decide();
        }

        if (Time.time >= nextScanAt)
        {
            nextScanAt = Time.time + 1f / Mathf.Max(0.5f, scansPerSecond);
            ScanForDownhill();
        }

        Drive();
    }

    // ---- deciding ---------------------------------------------------------------------------

    void Decide()
    {
        if (Racing)
        {
            RaceDecide();
            return;
        }

        Vector3 forward = Flat(transform.forward);
        if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;

        // Look further the faster you are going, so the car always has roughly the same amount
        // of TIME to react rather than the same distance.
        float speed = body != null ? body.linearVelocity.magnitude : 0f;
        float reach = lookAhead + speed * lookAheadPerSpeed;
        speedReadout = speed;
        reachReadout = reach;

        // A longer probe finds bigger drops, so a fixed per-degree penalty would be swamped at
        // speed and the car would start weaving exactly when it can least afford to.
        float bias = straightBias * (reach / Mathf.Max(1f, lookAhead));

        float here = GroundHeight(transform.position, 6f);
        int best = 0;
        float bestScore = float.NegativeInfinity;
        float bestDrop = 0f;
        float bestHazard = 0f;

        for (int i = 0; i < probes; i++)
        {
            float angle = ProbeAngle(i);
            Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * forward;
            Vector3 candidate = transform.position + dir * reach;
            float there = GroundHeight(candidate, 25f);

            // No ground found at all: a hole, or beyond the mesh. Treated as the worst option
            // rather than the best, or cars would dive off the outside of the map. Applies to
            // both rules — a race line that leaves the map is not a shortcut.
            // No ground found at all: a hole, or beyond the mesh. Treated as the worst option
            // rather than the best, or cars would dive off the outside of the map.
            float worth = float.IsNegativeInfinity(there) ? -1000f : here - there;

            float blocked = Hazard(dir, here, there, reach);
            scores[i] = worth - blocked * hazardWeight
                              - Mathf.Abs(angle) * bias;

            if (scores[i] <= bestScore) continue;
            bestScore = scores[i];
            bestHazard = blocked;
            bestDrop = worth;
            best = i;
        }

        float bestAngle = InterpolatedAngle(best);
        bestAngle += SeparationBias(forward);

        chosenAngle = bestAngle;
        bestDropReadout = bestDrop;
        hazardReadout = bestHazard;
        chosenAngleReadout = bestAngle;
    }

    /// <summary>
    /// Steering for a race: aim at a point ON the line, offset sideways into a chosen lane.
    /// </summary>
    /// <remarks>
    /// ⚠ THIS REPLACED A PENALTY, AND THE DIFFERENCE IS A GUARANTEE VERSUS A HOPE. Two rounds
    /// were spent making corner cutting expensive — squaring the penalty, sampling along the
    /// probe, pulling toward the centre — and cars kept cutting, because **anything scored
    /// against progress can be outweighed by enough progress.** A hairpin offers so much of it
    /// that no coefficient is safe: tune the penalty high enough to stop the worst corner and
    /// the cars will not move off the centreline to pass, which is the thing that was wanted.
    ///
    /// So the direction is no longer chosen from a fan at all. **The car steers at a point that
    /// is ON the racing line, so leaving the line is not an option it can score — it is not an
    /// option it has.** Cutting becomes geometrically impossible rather than merely unattractive,
    /// and it costs nothing to allow a full lane of movement for passing, because that movement
    /// is a sideways offset of a target that is still on the track.
    ///
    /// The probe fan does not disappear, it stops doing the wrong job. It used to choose the
    /// heading; now it chooses the LANE. Rocks, walls and slower cars push the car across the
    /// road rather than off it.
    ///
    /// THE ONE WAY PURE PURSUIT CAN STILL CUT is an aim point so far ahead that the straight line
    /// to it leaves the road — the classic failure, and on a hairpin it is severe. `SafeLookahead`
    /// shortens the aim until the chord to it stays within a fraction of the road width, so the
    /// car looks a long way down a straight and only as far as it can see round a bend.
    ///
    /// CHEAPER, TOO: one ground ray plus one sweep per lane, against the fan's ray-and-sweep per
    /// probe. Five lanes is six casts a decision where seven probes was fourteen.
    /// </remarks>
    void RaceDecide()
    {
        float speed = body != null ? body.linearVelocity.magnitude : 0f;
        speedReadout = speed;

        float half = track.width * 0.5f;

        // How far ahead to aim, then shortened until the straight line to it stays on the road.
        float desired = aimAhead + speed * aimAheadPerSpeed;
        float look = track.SafeLookahead(Line.LapDistance, desired,
                                         Mathf.Max(0.5f, half * aimDeviation), aimAhead * 0.5f);
        reachReadout = look;

        Vector3 centre = Line.Aim(look);
        Vector3 ahead = track.ForwardAtDistance(Line.LapDistance + look);
        Vector3 right = Vector3.Cross(Vector3.up, ahead).normalized;

        // Lanes stop short of the edge by half a car, so choosing the outside lane does not put
        // the wheels in the dirt.
        float usable = Mathf.Max(0f, half - laneMargin);
        float here = GroundHeight(transform.position, 6f);

        int count = Mathf.Max(3, lanes);
        float best = float.NegativeInfinity;
        float bestLane = 0f;
        float bestHazard = 0f;

        for (int i = 0; i < count; i++)
        {
            float candidate = Mathf.Lerp(-usable, usable, i / (float)(count - 1));
            Vector3 aim = centre + right * candidate;

            Vector3 offset = aim - transform.position;
            offset.y = 0f;
            float distance = offset.magnitude;
            if (distance < 1f) continue;

            Vector3 dir = offset / distance;

            // The aim point is ON the track, so its own height is the ground height there. That
            // is what removes a raycast per candidate: the fan had to ask the world where the
            // ground was, and a point on the racing line already knows.
            float blocked = Hazard(dir, here, aim.y, distance);

            float score = -blocked * hazardWeight
                          - Occupancy(candidate) * passWeight
                          - Mathf.Abs(candidate) * laneCentreBias
                          - Mathf.Abs(candidate - lane) * laneChangeCost;

            if (score <= best) continue;

            best = score;
            bestLane = candidate;
            bestHazard = blocked;
        }

        // Slewed, not snapped. A lane change is a manoeuvre — jumping the target three metres
        // sideways in one decision is a flick of the wheel that unsettles the car mid-corner,
        // and it reads as twitching rather than as choosing a line.
        lane = Mathf.MoveTowards(lane, bestLane, laneRate / Mathf.Max(1f, decisionsPerSecond));
        laneReadout = lane;

        aimPoint = centre + right * lane;
        laneLeft = centre - right * usable;
        laneRight = centre + right * usable;

        Vector3 toTarget = aimPoint - transform.position;
        toTarget.y = 0f;

        Vector3 forward = Flat(transform.forward);
        if (toTarget.sqrMagnitude > 1e-4f)
            chosenAngle = Vector3.SignedAngle(forward, toTarget.normalized, Vector3.up);

        // SeparationBias is deliberately NOT applied here. It is the destruction AI's way of
        // stopping a pack converging, and lane choice already does that job with better
        // information — the two together fight, and the car ends up steering away from a car it
        // has already picked a lane to avoid.
        hazardReadout = bestHazard;
        chosenAngleReadout = chosenAngle;
        bestDropReadout = 0f;
    }

    /// <summary>
    /// How occupied a lane is by other racers between here and <see cref="passLookahead"/>.
    /// </summary>
    /// <remarks>
    /// This is what makes passing a decision rather than an accident. A car sitting in your lane
    /// ahead makes that lane expensive, so the lane scan picks a clear one and the pursuit target
    /// slides across the road — a real overtake, on the racing line, without ever leaving it.
    ///
    /// Positions go through this car's OWN follower, which answers both questions at once and
    /// costs one projection each: how far ahead the other car is along the track, and how far
    /// across it. It also means the PLAYER is included without needing a follower of their own,
    /// which matters — an AI that only sees other AI drives straight through the one car the
    /// player is in.
    ///
    /// Cars BEHIND are ignored. Blocking a faster car is racecraft this game does not need, and
    /// a car that swerves for something already past it is just unstable.
    /// </remarks>
    float Occupancy(float candidate)
    {
        float busy = 0f;

        for (int i = 0; i < Live.Count; i++)
        {
            TrafficDriver other = Live[i];
            if (other == null || other == this) continue;
            busy += Blocking(candidate, other.transform.position);
        }

        if (PlayerCar.Current != null)
            busy += Blocking(candidate, PlayerCar.Current.transform.position);

        return busy;
    }

    float Blocking(float candidate, Vector3 position)
    {
        float gain = Line.Gain(position, out float across);

        // Slightly ahead, not alongside: a car level with you is dealt with by the collision, and
        // treating it as a blockage makes both cars dive for the same gap.
        if (gain <= 3f || gain > passLookahead) return 0f;
        if (Mathf.Abs(across - candidate) > laneClearance) return 0f;

        // Closer is worse, so a lane blocked 5 m ahead costs far more than one blocked at 30.
        return 1f - gain / passLookahead;
    }

    /// <summary>
    /// How badly a direction is blocked BETWEEN here and where the probe is aimed.
    /// </summary>
    /// <remarks>
    /// This is the fix for cars driving into boulders, and the bug it fixes is a blind spot
    /// rather than bad tuning. Each probe used to sample ONE point, at the far end of the ray.
    /// At speed the ray is ~49 m long, so the car read the ground height 49 m away and could not
    /// see a rock at 10 m at all. It knew where it wanted to go and nothing about what was in
    /// the way of getting there.
    ///
    /// Samples are spaced by a POWER CURVE rather than evenly, because the near field is what
    /// you hit first: at reach 49 m, four samples land at roughly 4, 12, 23 and 35 m instead of
    /// 10, 20, 29 and 39. Evenly spaced samples leave the first ten metres — the part you cannot
    /// steer around any more — the least covered.
    ///
    /// Anything raised counts, so this avoids kickers and humps as well as rocks. That is a
    /// deliberate trade: a car that keeps its wheels on the ground is worth more than one that
    /// takes every jump and spins. Raise `hazardHeight` if they should attack the ramps again.
    /// </remarks>
    float Hazard(Vector3 dir, float here, float far, float reach)
    {
        // No ground out there at all: a hole or the edge of the map. Worse than any rock.
        if (float.IsNegativeInfinity(far)) return 8f;

        // A SWEPT SPHERE, NOT POINT SAMPLES. Point sampling along the ray was the second wrong
        // answer to this: at reach 49 m the samples landed at 4, 12, 23 and 35 m, an 8 m gap in
        // the near field, and a boulder is up to 7 m wide. Rocks fell cleanly BETWEEN samples
        // and were never seen until the car was on top of one — reported as the hazard readout
        // only rising after the impact, while the car was already in the air.
        //
        // A sphere sweep cannot have gaps. It is also cheaper: one cast per direction instead
        // of four, so this halves the cast count while actually working.
        //
        // AIMED ALONG THE HILLSIDE, not horizontally. A horizontal sweep on a descent flies out
        // over the valley and hits nothing; on a crest it hits the crest. Following the slope
        // to the far sample means only things standing OUT of the ground get hit, which is the
        // same correction the point-sampling baseline needed and for the same reason.
        Vector3 slope = dir * reach + Vector3.up * (far - here);
        float distance = slope.magnitude;
        if (distance < 1f) return 0f;

        // Started above the surface and swept with a radius under the car's half-width, so the
        // sphere spans roughly the body of the car: high enough to clear the course's 0.55 m
        // surface noise, low enough to catch a boulder that only just protrudes.
        Vector3 origin = transform.position + Vector3.up * hazardHeight;

        if (!Physics.SphereCast(origin, hazardRadius, slope / distance, out RaycastHit hit,
                                distance, groundMask, QueryTriggerInteraction.Ignore))
            return 0f;

        // Closer is worse: the same rock is a nuisance at 35 m and unavoidable at 4 m.
        float closeness = 1f - Mathf.Clamp01(hit.distance / distance);
        return 1f + closeness * 3f;
    }

    float ProbeAngle(int i)
    {
        float t = probes == 1 ? 0.5f : i / (float)(probes - 1);
        return Mathf.Lerp(-probeSpread, probeSpread, t);
    }

    /// <summary>
    /// The best direction, interpolated BETWEEN probes rather than snapped to one.
    /// </summary>
    /// <remarks>
    /// Seven probes over 110 degrees are 18 degrees apart, so taking the single best one gives a
    /// steering signal that can only ever be one of seven values. Rounding an obstacle then
    /// means an 18-degree step change in demand, the car snaps to it, and on a chassis with less
    /// rear grip than front that is exactly how it spins — reported from play 2026-08-31 as
    /// oversteering round obstacles and losing control.
    ///
    /// Fitting a parabola through the winner and its two neighbours and taking the peak gives a
    /// continuous angle for the same seven casts. Costs three subtractions.
    /// </remarks>
    float InterpolatedAngle(int best)
    {
        if (best <= 0 || best >= probes - 1) return ProbeAngle(best);

        float left = scores[best - 1];
        float mid = scores[best];
        float right = scores[best + 1];

        float denominator = left - 2f * mid + right;
        if (Mathf.Abs(denominator) < 1e-5f) return ProbeAngle(best);

        // Peak offset in probe-index units, clamped to the neighbours so a flat or noisy
        // triple cannot throw the aim outside the fan it was measured over.
        float shift = Mathf.Clamp(0.5f * (left - right) / denominator, -1f, 1f);
        float step = probes > 1 ? (2f * probeSpread) / (probes - 1) : 0f;

        return ProbeAngle(best) + shift * step;
    }

    /// <summary>
    /// Coarse full-circle sweep for where downhill actually is, regardless of facing.
    /// </summary>
    /// <remarks>
    /// The forward fan only sees a 110-degree window, which is fine while pointing roughly the
    /// right way and useless once not. A car spun round by a crash finds every direction ahead
    /// rising, picks the least-bad, and either crawls uphill or trips the stuck timer and
    /// reverses — which finally goes downhill, so it settles into driving backwards down the
    /// mountain and never corrects. Reported from play 2026-08-31.
    ///
    /// A full sweep answers "which way is down" independently of which way the nose points, so
    /// the car can know it is facing the wrong way instead of inferring it from failure.
    ///
    /// Run at scansPerSecond (3 Hz) rather than every decision: 12 casts at 3 Hz is ~0.7 per
    /// physics step per car, against the fan's ~2.
    /// </remarks>
    void ScanForDownhill()
    {
        if (Racing)
        {
            // A race already knows which way is forward, so the twelve rays are not merely
            // unnecessary here — they answer the wrong question. On a flat dam road the
            // steepest descent is a drainage camber, and a car that treated that as "the way
            // out" would decide it was facing the wrong way on a straight.
            //
            // Everything after this block is identical for both modes: the same wrong-way
            // timer, the same commitment to a turnaround, the same recovery. Only the source
            // of "forward" changes.
            Vector3 ahead = Line.Aim(Mathf.Max(10f, lookAhead * 0.6f)) - transform.position;
            downhill = ahead.sqrMagnitude < 1e-4f ? Flat(transform.forward) : Flat(ahead);

            // Above arrivedDrop by a wide margin, so the arrival test can never fire. A race
            // finishes when the director says so, and a car that parked itself on the last lap
            // would be indistinguishable from one that had crashed.
            downhillDrop = 1000f;
        }
        else
        {
            const int rays = 12;
            float here = GroundHeight(transform.position, 6f);
            float reach = lookAhead * 1.4f;

            float bestDrop = float.NegativeInfinity;
            Vector3 bestDir = Flat(transform.forward);

            for (int i = 0; i < rays; i++)
            {
                float angle = i * (360f / rays);
                Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
                float there = GroundHeight(transform.position + dir * reach, 25f);
                if (float.IsNegativeInfinity(there)) continue;

                float drop = here - there;
                if (drop <= bestDrop) continue;

                bestDrop = drop;
                bestDir = dir;
            }

            downhill = bestDir;
            downhillDrop = float.IsNegativeInfinity(bestDrop) ? 0f : bestDrop;
        }

        // Arrival is decided HERE, not from the forward fan. "No descent ahead of me" is also
        // true of a car facing a wall; "no descent in any direction" is what being at the
        // bottom actually means.
        UpdateArrival(downhillDrop);

        float off = Vector3.SignedAngle(Flat(transform.forward), downhill, Vector3.up);
        wrongWayReadout = off;

        // Speed-independent, unlike the stuck timer. A car reversing downhill at 20 m/s is
        // never "stuck", which is why it could keep doing it indefinitely.
        bool facingAway = Mathf.Abs(off) > wrongWayAngle && downhillDrop > arrivedDrop;

        if (!facingAway)
        {
            wrongWaySince = -1f;
            if (turningAround && Mathf.Abs(off) < wrongWayAngle * 0.45f) turningAround = false;
            return;
        }

        if (wrongWaySince < 0f) wrongWaySince = Time.time;
        else if (Time.time - wrongWaySince >= wrongWayTime) turningAround = true;
    }

    /// <summary>
    /// Height of the ground under a point, or negative infinity if there is none.
    /// </summary>
    /// <remarks>
    /// Cast from above and downward rather than forward along the fan, because a forward ray
    /// only reports THAT something is in the way while this reports how high it is — which is
    /// the number the whole steering rule is built on.
    /// </remarks>
    float GroundHeight(Vector3 at, float up)
    {
        Vector3 from = at + Vector3.up * up;
        float distance = up + 40f;

        return Physics.Raycast(from, Vector3.down, out RaycastHit hit, distance,
                               groundMask, QueryTriggerInteraction.Ignore)
            ? hit.point.y
            : float.NegativeInfinity;
    }

    float SeparationBias(Vector3 forward)
    {
        if (separationRadius <= 0f) return 0f;

        float bias = 0f;
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        for (int i = 0; i < Live.Count; i++)
        {
            TrafficDriver other = Live[i];
            if (other == null || other == this) continue;

            Vector3 offset = other.transform.position - transform.position;
            float distance = offset.magnitude;
            if (distance > separationRadius || distance < 0.01f) continue;

            // Only cars roughly AHEAD matter. Steering away from something already behind is
            // how a pack ends up spreading itself into the walls.
            if (Vector3.Dot(offset.normalized, forward) < 0.1f) continue;

            float closeness = 1f - distance / separationRadius;
            bias -= Mathf.Sign(Vector3.Dot(offset, right)) * closeness * separationStrength * probeSpread;
        }

        return bias;
    }

    void UpdateArrival(float bestDrop)
    {
        if (arrived) return;

        if (bestDrop >= arrivedDrop)
        {
            noDescentSince = -1f;
            return;
        }

        if (noDescentSince < 0f) noDescentSince = Time.time;
        else if (Time.time - noDescentSince >= arrivedTime) arrived = true;

        arrivedReadout = arrived;
    }

    // ---- driving ----------------------------------------------------------------------------

    void Drive()
    {
        if (arrived)
        {
            Throttle = 0f;
            Steer = 0f;
            Handbrake = true;
            return;
        }

        Handbrake = false;
        reversingReadout = Time.time < reverseUntil;

        if (reversingReadout)
        {
            // Back out with the wheels the other way, which is what unwinds a car that has
            // nosed into a wall. Steer is deliberately held at full lock rather than chasing
            // the probes, whose answer is what got it stuck.
            Throttle = -1f;
            Steer = -Mathf.Sign(chosenAngle == 0f ? 1f : chosenAngle);
            return;
        }

        CheckStuck();
        turningAroundReadout = turningAround;

        Vector3 flat = Vector3.ProjectOnPlane(
            body != null ? body.linearVelocity : Vector3.zero, Vector3.up);
        float speed = flat.magnitude;

        // Sideslip: the angle between where the car POINTS and where it is actually GOING.
        // Below walking pace the direction of travel is noise, so it is not measured there.
        float slip = speed > 2f
            ? Vector3.SignedAngle(Flat(transform.forward), flat.normalized, Vector3.up)
            : 0f;
        slipReadout = slip;

        float target = turningAround
            ? TurnaroundSteer()
            : Mathf.Clamp(chosenAngle / Mathf.Max(1f, car.maxSteerAngle), -1f, 1f);

        // CATCH THE SLIDE. Past slipLimit the steering stops chasing the line it wanted and
        // starts steering INTO the slide, which is what a driver does and what keeps a car with
        // less rear grip than front from spinning. Without it the AI keeps demanding the turn
        // that started the slide, which tightens it.
        float slide = Mathf.Clamp01((Mathf.Abs(slip) - slipLimit) / Mathf.Max(1f, slipLimit));
        if (slide > 0f)
        {
            float catchIt = Mathf.Clamp(slip / Mathf.Max(1f, car.maxSteerAngle), -1f, 1f);
            target = Mathf.Lerp(target, catchIt, slide * counterSteer);
        }

        // Slew the wheel more slowly the faster it is going. A rate that feels responsive at
        // 10 m/s is a flick of the wrist at 40 and puts the car sideways on its own.
        float fast = Mathf.Clamp01(speed / Mathf.Max(1f, car.topSpeed));
        float slew = steerRate / Mathf.Lerp(1f, steerRateAtSpeed, fast);
        Steer = Mathf.MoveTowards(Steer, target, slew * Time.deltaTime);

        if (turningAround)
        {
            // Ease off while swinging round. Full throttle on full lock just understeers wide
            // and turns a three-second correction into a lap of the valley floor.
            Throttle = 0.45f;
            return;
        }

        if (Racing)
        {
            RaceThrottle(speed, slide);
            return;
        }

        // Lift for a corner, and lift MORE while sliding. Lifting is most of how a real driver
        // catches an oversteer, and it costs far less time than spinning does.
        float lift = 1f - Mathf.Abs(Steer) * cornerLift - slide * slipLift;
        Throttle = cruiseThrottle * Mathf.Clamp01(lift);
    }

    /// <summary>
    /// Throttle and brake against a corner speed worked out from the track ahead.
    /// </summary>
    /// <remarks>
    /// THE DESTRUCTION AI LIFTS BECAUSE IT IS ALREADY TURNING, WHICH IS TOO LATE BY DEFINITION.
    /// `1 - |Steer| x cornerLift` only reacts once the steering is wound on, so the car arrives
    /// at a corner at whatever speed it was doing and scrubs off the excess by understeering
    /// through the exit. On a descent full of rocks that reads as chaotic and is fine; in a race
    /// it reads as a car that cannot drive.
    ///
    /// With a track there is a real answer available: the tightest corner within braking distance
    /// sets a speed for HERE, so the car lifts before the corner and is back on the throttle at
    /// the apex. That single change is most of what "slow down and speed up when they should"
    /// means.
    ///
    /// The slide lift is kept on top of it. Braking early is planning; lifting mid-slide is
    /// reacting, and both are things a driver does.
    /// </remarks>
    void RaceThrottle(float speed, float slide)
    {
        float limit = track.SpeedLimit(Line.LapDistance, brakeScan, cornerGrip, brakeAccel);
        targetSpeedReadout = Mathf.Min(limit, car.topSpeed);

        if (speed > limit + speedTolerance)
        {
            // Full brake. Negative throttle against forward motion is what CarController reads
            // as braking; it only becomes reverse below 1 m/s, which no corner speed reaches.
            Throttle = -1f;
            brakingReadout = true;
            return;
        }

        brakingReadout = false;

        // Coast through the band rather than switching between full throttle and full brake at a
        // single threshold, which is slower AND visibly twitchy.
        if (speed > limit)
        {
            Throttle = 0f;
            return;
        }

        Throttle = cruiseThrottle * Mathf.Clamp01(1f - slide * slipLift);
    }

    /// <summary>Steering demand while deliberately turning round to face downhill again.</summary>
    /// <remarks>
    /// Full lock toward wherever downhill is. The corridor is 26 m wide and the car turns in far
    /// less than that, so driving round is quicker and much less fragile than a three-point turn
    /// — and if it does wedge itself, the existing stuck timer reverses it out.
    /// </remarks>
    float TurnaroundSteer()
    {
        float off = Vector3.SignedAngle(Flat(transform.forward), downhill, Vector3.up);
        return Mathf.Sign(off);
    }

    void CheckStuck()
    {
        bool slow = body != null && body.linearVelocity.sqrMagnitude < stuckSpeed * stuckSpeed;

        if (!slow)
        {
            slowSince = -1f;
            return;
        }

        if (slowSince < 0f)
        {
            slowSince = Time.time;
        }
        else if (Time.time - slowSince >= stuckTime)
        {
            reverseUntil = Time.time + reverseTime;
            slowSince = -1f;
        }
    }

    void OnDrawGizmosSelected()
    {
        Vector3 forward = Flat(transform.forward);
        if (forward.sqrMagnitude < 1e-4f) return;

        if (Racing)
        {
            // The aim point and the lanes it was chosen from. This is the whole race steering
            // rule made visible: if the gold line ever leaves the road, the aim is reaching too
            // far past a corner and `aimDeviation` is the number to lower.
            Gizmos.color = new Color(0.35f, 0.85f, 1f, 0.55f);
            Gizmos.DrawLine(laneLeft, laneRight);

            Gizmos.color = brakingReadout
                ? new Color(1f, 0.3f, 0.25f, 1f)
                : new Color(1f, 0.78f, 0.15f, 1f);
            Gizmos.DrawLine(transform.position, aimPoint);
            Gizmos.DrawSphere(aimPoint, 0.7f);
            return;
        }

        float fan = reachReadout > 1f ? reachReadout : lookAhead;
        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.8f);
        for (int i = 0; i < probes; i++)
        {
            float t = probes == 1 ? 0.5f : i / (float)(probes - 1);
            float angle = Mathf.Lerp(-probeSpread, probeSpread, t);
            Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * forward;
            Gizmos.DrawLine(transform.position, transform.position + dir * fan);
        }

        // Red while braking, so it is visible from the scene view whether the car is planning for
        // the corner or arriving at it hopefully.
        Gizmos.color = brakingReadout
            ? new Color(1f, 0.3f, 0.25f, 1f)
            : new Color(1f, 0.78f, 0.15f, 1f);
        Vector3 chosen = Quaternion.AngleAxis(chosenAngle, Vector3.up) * forward;
        Gizmos.DrawLine(transform.position, transform.position + chosen * (fan * 1.15f));
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.normalized;
    }
}
