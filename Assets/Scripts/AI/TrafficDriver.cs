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
/// COST, because three of these run alongside the player. Probes are a raycast each, taken at
/// `decisionsPerSecond` rather than every physics step: 7 probes plus one clearance cast at
/// 10 Hz is 80 casts a second per car, about 1.6 per physics step. The car itself is a normal
/// CarController, which is 4 sphere casts a step — the AI is the cheap half by a wide margin.
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
    [Tooltip("How far ahead the probes sample, in metres. Too short and it cannot see a wall " +
             "in time; too long and it ignores what is directly in front of it.")]
    public float lookAhead = 26f;

    [Tooltip("Probes in the fan. Odd numbers keep one pointing straight ahead.")]
    [Range(3, 15)] public int probes = 7;

    [Tooltip("Half-angle of the fan, in degrees.")]
    public float probeSpread = 55f;

    [Tooltip("Steering decisions per second. The probes only fire this often; steering is " +
             "smoothed between them, so raising it costs casts and buys very little.")]
    public float decisionsPerSecond = 10f;

    [Tooltip("Surfaces the probes can see. Default only, same as CarController's ground mask.")]
    public LayerMask groundMask = 1;

    [Header("Driving")]
    [Tooltip("Throttle when the way ahead is clear and straight.")]
    [Range(0f, 1f)] public float cruiseThrottle = 1f;

    [Tooltip("How much a hard turn cuts the throttle. 0 means it never lifts for a corner.")]
    [Range(0f, 1f)] public float cornerLift = 0.55f;

    [Tooltip("How sharply the steering chases the chosen direction.")]
    public float steerRate = 4.5f;

    [Tooltip("Penalty per degree off straight ahead, in metres of drop. Without it the car " +
             "weaves after every marginally better line instead of committing to one.")]
    public float straightBias = 0.035f;

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

    CarController car;
    Rigidbody body;

    float nextDecisionAt;
    float chosenAngle;
    float slowSince = -1f;
    float noDescentSince = -1f;
    float reverseUntil = -1f;
    bool arrived;

    void Awake()
    {
        car = GetComponent<CarController>();
        body = GetComponent<Rigidbody>();

        // Take the wheel. A traffic car built from the player's prefab may still carry a
        // CarInput; left enabled it would read the same keyboard the player is using and every
        // traffic car would mirror their steering.
        car.Driver = this;

        CarInput keyboard = GetComponent<CarInput>();
        if (keyboard != null) keyboard.enabled = false;
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

        Drive();
    }

    // ---- deciding ---------------------------------------------------------------------------

    void Decide()
    {
        Vector3 forward = Flat(transform.forward);
        if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;

        float here = GroundHeight(transform.position, 6f);
        float bestScore = float.NegativeInfinity;
        float bestAngle = 0f;
        float bestDrop = 0f;

        for (int i = 0; i < probes; i++)
        {
            // -spread .. +spread, with the middle probe dead ahead when `probes` is odd.
            float t = probes == 1 ? 0.5f : i / (float)(probes - 1);
            float angle = Mathf.Lerp(-probeSpread, probeSpread, t);

            Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * forward;
            Vector3 sample = transform.position + dir * lookAhead;

            float there = GroundHeight(sample, 25f);

            // No ground found at all: a hole, or beyond the mesh. Treated as the worst option
            // rather than the best, or cars would dive off the outside of the map.
            float drop = float.IsNegativeInfinity(there) ? -1000f : here - there;

            float score = drop - Mathf.Abs(angle) * straightBias;
            if (score <= bestScore) continue;

            bestScore = score;
            bestAngle = angle;
            bestDrop = drop;
        }

        bestAngle += SeparationBias(forward);

        chosenAngle = bestAngle;
        bestDropReadout = bestDrop;
        chosenAngleReadout = bestAngle;

        UpdateArrival(bestDrop);
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

        float wanted = Mathf.Clamp(chosenAngle / Mathf.Max(1f, car.maxSteerAngle), -1f, 1f);
        Steer = Mathf.MoveTowards(Steer, wanted, steerRate * Time.deltaTime);

        // Lift off for a corner. Without it they arrive at every turn at top speed, understeer
        // into the wall and spend the run reversing out of it.
        float lift = 1f - Mathf.Abs(Steer) * cornerLift;
        Throttle = cruiseThrottle * lift;
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

        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.8f);
        for (int i = 0; i < probes; i++)
        {
            float t = probes == 1 ? 0.5f : i / (float)(probes - 1);
            float angle = Mathf.Lerp(-probeSpread, probeSpread, t);
            Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * forward;
            Gizmos.DrawLine(transform.position, transform.position + dir * lookAhead);
        }

        Gizmos.color = new Color(1f, 0.78f, 0.15f, 1f);
        Vector3 chosen = Quaternion.AngleAxis(chosenAngle, Vector3.up) * forward;
        Gizmos.DrawLine(transform.position, transform.position + chosen * (lookAhead * 1.15f));
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.normalized;
    }
}
