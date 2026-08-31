using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives a traffic car down the valley by looking for the way down, not by following a path.
/// </summary>
/// <remarks>
/// THE WHOLE STEERING RULE IS "GO WHERE THE GROUND DROPS MOST". A fan of short downward probes
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
/// fan at 14 Hz plus a 12-ray full-circle scan at 3 Hz is about 2.7 casts per physics step per
/// car, against the 4 sphere casts the car itself already does. Measured on a school Chromebook
/// 2026-08-31: the whole game holds 60 FPS, so there is room to make this smarter still.
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

    CarController car;
    Rigidbody body;

    float nextDecisionAt;
    float chosenAngle;
    float slowSince = -1f;
    float noDescentSince = -1f;
    float reverseUntil = -1f;
    float nextScanAt;
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

    void Update()
    {
        if (Time.timeScale <= 0f) return;

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

        for (int i = 0; i < probes; i++)
        {
            float angle = ProbeAngle(i);
            Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * forward;
            float there = GroundHeight(transform.position + dir * reach, 25f);

            // No ground found at all: a hole, or beyond the mesh. Treated as the worst option
            // rather than the best, or cars would dive off the outside of the map.
            float drop = float.IsNegativeInfinity(there) ? -1000f : here - there;

            scores[i] = drop - Mathf.Abs(angle) * bias;

            if (scores[i] <= bestScore) continue;
            bestScore = scores[i];
            bestDrop = drop;
            best = i;
        }

        float bestAngle = InterpolatedAngle(best);
        bestAngle += SeparationBias(forward);

        chosenAngle = bestAngle;
        bestDropReadout = bestDrop;
        chosenAngleReadout = bestAngle;
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

        // Lift for a corner, and lift MORE while sliding. Lifting is most of how a real driver
        // catches an oversteer, and it costs far less time than spinning does.
        float lift = 1f - Mathf.Abs(Steer) * cornerLift - slide * slipLift;
        Throttle = cruiseThrottle * Mathf.Clamp01(lift);
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

        float fan = reachReadout > 1f ? reachReadout : lookAhead;
        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.8f);
        for (int i = 0; i < probes; i++)
        {
            float t = probes == 1 ? 0.5f : i / (float)(probes - 1);
            float angle = Mathf.Lerp(-probeSpread, probeSpread, t);
            Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * forward;
            Gizmos.DrawLine(transform.position, transform.position + dir * fan);
        }

        Gizmos.color = new Color(1f, 0.78f, 0.15f, 1f);
        Vector3 chosen = Quaternion.AngleAxis(chosenAngle, Vector3.up) * forward;
        Gizmos.DrawLine(transform.position, transform.position + chosen * (fan * 1.15f));
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.normalized;
    }
}
