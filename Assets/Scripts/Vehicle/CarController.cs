using UnityEngine;

/// <summary>
/// Arcade car built on raycast suspension — four raycasts per physics step instead of
/// Unity's WheelCollider.
///
/// Why not WheelCollider: it is a simulation-grade solver that is expensive, notoriously
/// twitchy to tune, and fights you when you want arcade behaviour. Raycast suspension is
/// cheaper, completely predictable, and every number in it means something you can feel.
///
/// Cost: 4 raycasts per car per FixedUpdate. Traffic cars stay kinematic until struck,
/// so only a handful of cars ever run this at once.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class CarController : MonoBehaviour
{
    [System.Serializable]
    public class Wheel
    {
        [Tooltip("Empty GameObject at the top of this wheel's suspension travel.")]
        public Transform anchor;

        [Tooltip("Optional wheel mesh. Gets positioned, steered and spun automatically.")]
        public Transform visual;

        public bool steers;
        public bool powered;

        [HideInInspector] public bool grounded;
        [HideInInspector] public float compression;
        [HideInInspector] public float spin;
        [HideInInspector] public Vector3 contactPoint;

        /// <summary>Wheel has come off. No suspension, no drive, no grip on this corner.</summary>
        [HideInInspector] public bool detached;
    }

    [Header("Wheels")]
    public Wheel[] wheels = new Wheel[4];

    [Tooltip("Rotation correction for the wheel MESH, applied in the mesh's own space. Zero " +
             "assumes the tyre is authored with its axle along local X, the Unity convention. " +
             "The split E30 is authored axle-along-Y and needs (0, 0, -90). Symptom of a wrong " +
             "value: wheels sit sideways and tumble end-over-end instead of rolling.")]
    public Vector3 wheelVisualEuler = new Vector3(0f, 0f, -90f);

    [Tooltip("Surfaces the wheels can rest on. Must exclude the car's own layer.")]
    public LayerMask groundMask = ~0;

    [Header("Suspension")]
    [Tooltip("Wheel radius in metres. Also how far below the anchor the wheel sits at full droop.")]
    public float wheelRadius = 0.48f;

    [Tooltip("How far the suspension can compress, in metres.")]
    public float suspensionTravel = 0.30f;

    [Tooltip("Sweep a wheel-sized sphere instead of a thin ray. Far better over rough ground. " +
             "Turn off only to compare against the old behaviour.")]
    public bool wheelSphereCast = true;

    [Tooltip("Steepest surface, in degrees from the car's up, that a SPHERE hit is allowed to " +
             "count as ground. Anything steeper is a wall the sphere brushed on its way past, " +
             "not something the tyre is resting on, and the cast falls back to a straight ray. " +
             "Without this, scraping a tilted face makes that wheel visibly climb it. Raise it " +
             "and ledge-climbing comes back; lower it much below ~45 and legitimate ramps stop " +
             "counting as ground.")]
    [Range(20f, 89f)] public float maxGroundAngle = 55f;

    [Tooltip("Stiffness. Too low and the car wallows; too high and it skips over bumps.")]
    public float springStrength = 9000f;

    [Tooltip("Bounce absorption. Roughly 8-12% of spring strength is a sane starting point.")]
    public float damperStrength = 3000f;

    [Tooltip("Compression at which the bump stop starts to bite, 0-1. Below this the spring is " +
             "linear; above it the rate climbs quadratically so the last of the travel resists " +
             "hard instead of being given away. Lower = firmer, less usable travel.")]
    [Range(0.3f, 0.95f)] public float bumpStopStart = 0.70f;

    [Tooltip("Extra newtons at FULL compression from the bump stop, on top of springStrength. " +
             "This is what stops the body sitting on its belly under downforce and cornering " +
             "load. Too low and it still bottoms; too high and hard landings bounce.")]
    public float bumpStopStrength = 60000f;

    [Tooltip("Anti-roll bar rate, per axle, in newtons per unit of left-right compression " +
             "difference. Resists ROLL ONLY -- straight-line ride and single-wheel bumps are " +
             "untouched. Too high lifts the inside wheels mid-corner and makes the car flip. " +
             "0 disables it.")]
    public float antiRollStrength = 4000f;

    [Header("Engine")]
    [Tooltip("Forward force per powered wheel at full throttle.")]
    public float enginePower = 3200f;

    [Tooltip("Speed in m/s at which the engine stops adding force. 28 m/s is about 100 km/h.")]
    public float topSpeed = 32f;

    [Tooltip("Braking force when reversing into forward motion.")]
    public float brakeForce = 5500f;

    [Tooltip("Slowdown when no throttle is applied.")]
    public float coastDrag = 900f;

    [Header("Steering")]
    [Tooltip("Steering angle at a standstill, in degrees.")]
    public float maxSteerAngle = 32f;

    [Tooltip("Steering angle at top speed. Lower keeps the car stable when quick.")]
    public float highSpeedSteerAngle = 13f;

    [Tooltip("How fast the wheels turn to the requested angle. Higher is twitchier.")]
    public float steerRate = 7f;

    [Tooltip("Turn-in assist. Yaw comes only from tyre side force otherwise, which is honest and " +
             "feels vague — the car has to build a slip angle before it bites, so turn-in lags " +
             "the wheel and it ploughs on first.\n\n" +
             "This drives the car toward the yaw rate its own geometry implies, so the steering " +
             "angle still means what it says; it just gets there without waiting for the tyres. " +
             "0 is the old pure-physics behaviour. 4 is responsive. Above ~8 it starts to feel " +
             "like the car is on rails.")]
    public float turnAssist = 4f;

    [Tooltip("Largest yaw-rate error the assist will correct, in radians per second. This is " +
             "what keeps a slide a slide: past this the car is genuinely sideways and has to be " +
             "caught, rather than being quietly straightened out.")]
    public float maxTurnAssist = 1.6f;

    [Tooltip("Below this speed the assist is off, or it spins a parked car on the spot.")]
    public float turnAssistMinSpeed = 2.5f;

    [Tooltip("Understeer gradient. How much MORE steering a corner needs as speed rises — the " +
             "difference between a car and a slot car. Raise it and the car pushes wide at speed; " +
             "lower it and high-speed corners get unrealistically tight. 0 is pure Ackermann, " +
             "which asks for a 9 m turn radius at 108 km/h and is not a car.")]
    public float understeer = 0.006f;

    [Header("Grip")]
    [Tooltip("How hard the tyres resist sliding sideways. 1 is full grip, 0 is ice.")]
    [Range(0f, 1f)] public float frontGrip = 0.85f;

    [Range(0f, 1f)] public float rearGrip = 0.75f;

    [Tooltip("Rear grip multiplier while the handbrake is held. Low values let it slide.")]
    [Range(0f, 1f)] public float handbrakeGrip = 0.18f;

    [Tooltip("Caps the sideways force one tyre can apply. Stops the solver exploding on hard hits.")]
    public float maxGripForce = 14000f;

    [Header("Stability")]
    [Tooltip("Lowers the centre of mass. Without this the car rolls over constantly.")]
    public Vector3 centreOfMassOffset = new Vector3(0f, -0.6f, 0f);

    [Tooltip("Downforce at top speed, as a multiple of the car's weight.")]
    [Range(0f, 3f)] public float downforce = 0.9f;

    [Tooltip("Lets the player rotate the car in mid-air. Arcade convention, and it makes jumps landable.")]
    public float airControlTorque = 9000f;

    [Tooltip("Angular damping while the wheels are down. This is the Rigidbody's own value and " +
             "it is what stops the body wobbling on its springs — leave it alone.")]
    public float groundedAngularDamping = 2f;

    [Tooltip("Angular damping once airborne. The Rigidbody ships with 2, which bleeds off a " +
             "spin in well under a second, so a car left the ramp rotating and simply stopped. " +
             "It must be MUCH lower than that to roll at all — but not zero: 0.04 preserved a " +
             "spin so faithfully that a knock on the lip had the car tumbling the whole flight. " +
             "0.3 keeps the momentum and lets it bleed off, which reads as driven rather than " +
             "thrown.")]
    public float airAngularDamping = 0.30f;

    [Tooltip("Degrees per second the car may spin in the air before air control stops adding " +
             "to it. Input can still slow a spin past this; it just cannot wind it up forever. " +
             "190 is about half a rotation a second — enough to roll, calm enough to watch.")]
    public float maxAirSpin = 190f;

    [Header("Read-only")]
    [SerializeField] float debugSpeed;

    Rigidbody rb;
    float steerAngle;

    /// <summary>Front-to-rear anchor distance, measured at Awake. Feeds the turn assist.</summary>
    float wheelbase = 2.5f;

    /// <summary>
    /// Who is driving. Defaults to whatever <see cref="ICarDriver"/> is on this GameObject —
    /// normally <see cref="CarInput"/> — and can be replaced at runtime, which is how a traffic
    /// car swaps the keyboard for an AI without the controller knowing the difference.
    /// </summary>
    public ICarDriver Driver { get; set; }

    /// <summary>Forward speed in metres per second. Negative when reversing.</summary>
    public float ForwardSpeed { get; private set; }

    /// <summary>Speed regardless of direction, in metres per second.</summary>
    public float Speed { get; private set; }

    /// <summary>True while at least one wheel is touching the ground.</summary>
    public bool Grounded { get; private set; }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        Driver = GetComponent<ICarDriver>();
        rb.centerOfMass += centreOfMassOffset;

        MeasureWheelbase();
        CheckGrip();
    }

    /// <summary>
    /// Shout if the car has been given so little grip force that it cannot corner.
    /// </summary>
    /// <remarks>
    /// Written after `maxGripForce` was set to **26** instead of 26,000 on three prefabs, from a
    /// table that wrote the numbers with comma separators. The symptom is total and baffling —
    /// the car drives on ice — and NOTHING says why: no error, no warning, and the value looks
    /// plausible sitting in the Inspector next to fields that really are single digits.
    ///
    /// Expressed in G because it makes an off-by-a-thousand obvious at a glance. Note this is the
    /// CLAMP on the corrective impulse, not sustained cornering load — the tuned cars measure
    /// 6-11 g by this yardstick and are not pulling anything like that in a steady corner. The
    /// check only catches a value so small the tyres cannot act at all; the misconfigured E30
    /// managed 0.009 g, against 8.8 when set correctly.
    /// </remarks>
    void CheckGrip()
    {
        if (rb == null || wheels == null || wheels.Length == 0) return;

        float weight = rb.mass * Physics.gravity.magnitude;
        if (weight <= 0f) return;

        float lateralG = maxGripForce * wheels.Length / weight;
        if (lateralG >= 0.3f) return;

        Debug.LogError(
            $"CarController on '{name}': maxGripForce is {maxGripForce:0.##}, which is only " +
            $"{lateralG:0.###} g of lateral grip across {wheels.Length} wheels — the car will " +
            "slide as though it is on ice and steering will do almost nothing.\n" +
            $"For {rb.mass:0} kg, 1 g needs about {weight / wheels.Length:0} per wheel. " +
            "The tuned cars in this project measure 6-11 g by this yardstick. Check for a " +
            "missing thousand.", this);
    }

    /// <summary>
    /// Front-to-rear anchor distance, for the turn assist's target yaw rate.
    /// </summary>
    /// <remarks>
    /// Measured from the anchors rather than exposed as a field, because it is a FACT about the
    /// car that is already in the scene — an Inspector value would be a second copy to get wrong,
    /// and a wrong wheelbase makes the assist quietly aim at the wrong yaw rate on one car only.
    /// The E30 is 2.57 m, the Aventador 2.75, the truck 3.35.
    /// </remarks>
    void MeasureWheelbase()
    {
        float front = 0f, rear = 0f;
        int fronts = 0, rears = 0;

        foreach (Wheel wheel in wheels)
        {
            if (wheel == null || wheel.anchor == null) continue;

            float z = transform.InverseTransformPoint(wheel.anchor.position).z;
            if (wheel.steers) { front += z; fronts++; }
            else { rear += z; rears++; }
        }

        if (fronts > 0 && rears > 0)
            wheelbase = Mathf.Abs(front / fronts - rear / rears);
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        Vector3 velocity = rb.linearVelocity;
        ForwardSpeed = Vector3.Dot(velocity, transform.forward);
        Speed = velocity.magnitude;
        debugSpeed = Speed;

        float throttle  = Driver != null ? Driver.Throttle : 0f;
        float steerWish = Driver != null ? Driver.Steer : 0f;
        bool handbrake  = Driver != null && Driver.Handbrake;

        UpdateSteering(steerWish, dt);

        int groundedCount = 0;
        foreach (Wheel wheel in wheels)
        {
            if (wheel == null || wheel.anchor == null) continue;
            if (ApplyWheel(wheel, throttle, handbrake, dt)) groundedCount++;
        }

        // Anti-roll bars, after the loop because they need all four compressions resolved.
        ApplyAntiRoll(0, 1);
        ApplyAntiRoll(2, 3);

        Grounded = groundedCount > 0;

        // Angular damping is switched, not constant. On the ground it is what keeps the body
        // from wobbling on its springs; in the air the SAME value is what was eating every
        // rotation the car had, so a jump ended in whatever attitude it started in. Switching
        // it is the whole fix for "you cannot roll" and for momentum not carrying over.
        //
        // Keyed on Touching rather than Grounded, because the question here is "is this in free
        // flight". A car sliding along on its roof has no wheel down but is very much in contact,
        // and giving it air damping would leave it spinning freely against the scenery.
        rb.angularDamping = Touching ? groundedAngularDamping : airAngularDamping;

        if (Grounded)
        {
            ApplyDownforce();
            ApplyTurnAssist();
        }
        else
        {
            ApplyAirControl(throttle, steerWish);
        }
    }

    /// <summary>
    /// Turn-in assist: drive the car toward the yaw rate its steering geometry implies.
    /// </summary>
    /// <remarks>
    /// Yaw previously came ONLY from lateral tyre force, which is realistic and feels vague. The
    /// car has to build a slip angle before the front tyres generate enough side force to rotate
    /// it, and `maxGripForce` caps how fast that force can arrive — so turn-in lags the wheel by
    /// a noticeable moment and the car ploughs on before it bites.
    ///
    /// The TARGET is the honest one: a car of this wheelbase at this speed and this steering
    /// angle should be yawing at `v x tan(steer) / wheelbase`. That is textbook Ackermann, not an
    /// invented number, so the car still corners like a car and the steering angle still means
    /// something. What is arcade about it is that the yaw is helped along directly rather than
    /// waiting for the tyres — the difference between "realistic" and "realistic but responsive".
    ///
    /// ForceMode.Acceleration, so it ignores the inertia tensor and behaves identically on a
    /// 1,150 kg supercar and a 3,000 kg truck without per-car tuning.
    ///
    /// Three guards, all load-bearing:
    ///   * GROUNDED only. In the air this would fight air control and let you steer on nothing.
    ///   * Above a minimum speed, or it spins a parked car on the spot.
    ///   * The correction is CLAMPED, so a big slip angle cannot be papered over — the car can
    ///     still be made to slide and still has to be caught.
    /// </remarks>
    void ApplyTurnAssist()
    {
        if (turnAssist <= 0f || !Grounded) return;

        float speed = ForwardSpeed;
        if (Mathf.Abs(speed) < turnAssistMinSpeed) return;

        // The steady-state bicycle model WITH an understeer gradient, not raw Ackermann.
        //
        // Raw `v x tan(d) / L` assumes the tyres never slip, and at speed that is wildly
        // optimistic: it asks the E30 for a NINE METRE turn radius at 108 km/h. The assist would
        // sit permanently saturated trying to deliver it and the car would corner like a slot car.
        //
        // Adding `understeer x v^2` to the wheelbase is the standard way to express that a real
        // car needs more steering angle for the same corner as speed rises. It keeps the low
        // speed behaviour honest and pulls the high speed target back to something a car can
        // actually do — about 28 m at 108 km/h.
        float speedTerm = wheelbase + understeer * speed * speed;
        float desired = speed * Mathf.Tan(steerAngle * Mathf.Deg2Rad) / Mathf.Max(0.5f, speedTerm);
        float current = Vector3.Dot(rb.angularVelocity, transform.up);

        float correction = Mathf.Clamp(desired - current, -maxTurnAssist, maxTurnAssist);
        rb.AddTorque(transform.up * (correction * turnAssist), ForceMode.Acceleration);
    }

    void UpdateSteering(float steerWish, float dt)
    {
        float speed01 = Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / Mathf.Max(1f, topSpeed));
        float allowed = Mathf.Lerp(maxSteerAngle, highSpeedSteerAngle, speed01);
        float target = steerWish * allowed;

        steerAngle = Mathf.Lerp(steerAngle, target, 1f - Mathf.Exp(-steerRate * dt));
    }

    /// <returns>True if this wheel is touching the ground.</returns>
    bool ApplyWheel(Wheel wheel, float throttle, bool handbrake, float dt)
    {
        // A detached wheel contributes nothing: no spring holding that corner up, no
        // power through it, no sideways grip. The body drops onto its collider and
        // starts dragging, which is exactly the behaviour we want.
        if (wheel.detached)
        {
            wheel.grounded = false;
            wheel.compression = 0f;
            return false;
        }

        Vector3 origin = wheel.anchor.position;
        Vector3 down = -transform.up;

        Vector3 wheelForward = wheel.steers
            ? Quaternion.AngleAxis(steerAngle, transform.up) * transform.forward
            : transform.forward;
        Vector3 wheelRight = Vector3.Cross(transform.up, wheelForward).normalized;

        // A tyre is not a line. A single ray is infinitely thin, so it drops into cracks,
        // misses a rock edge by a centimetre, and stabs through polygon seams — each of
        // which silently loses a corner's support for a frame and reads as the car
        // catching on nothing. Sweeping a sphere of the wheel's own radius is what a real
        // contact patch does: it cannot fall into a gap narrower than the wheel, and it
        // rides up over an edge instead of past it. Costs roughly 2-3x a raycast, and
        // there are only four per physics step, so it does not show up in a profile.
        // Start the sweep ABOVE the anchor, never at it. Unity does not report colliders the
        // sphere is already overlapping when a sweep begins, so casting from the anchor goes
        // blind the moment the body sinks within one wheelRadius of the road. That kills the
        // spring at exactly the point it is needed most, so the corner falls further, which
        // keeps it blind -- a runaway with no way out. Lifting the start by one radius buys a
        // full suspensionTravel of sink before the cast can fail, and lets centreTravel go
        // negative so a bottomed-out corner still reports compression 1 and pushes back hard.
        float overshoot = wheelRadius;
        Vector3 castStart = origin + transform.up * overshoot;

        RaycastHit hit = default;
        bool sphereHit = false;

        if (wheelSphereCast)
        {
            // The sphere accounts for its own radius, so the sweep length is travel alone
            // (plus the overshoot, which is subtracted back off below).
            if (Physics.SphereCast(castStart, wheelRadius, down, out RaycastHit swept,
                                   suspensionTravel + overshoot,
                                   groundMask, QueryTriggerInteraction.Ignore)
                && Vector3.Angle(swept.normal, transform.up) <= maxGroundAngle)
            {
                hit = swept;
                sphereHit = true;
            }
        }

        // A sphere sweep reports whatever the sphere TOUCHES, which includes anything beside
        // the wheel as well as under it. Graze a tilted face and it returns a contact point
        // well above the ground the tyre is actually on, the spring reads that as compression
        // and lifts the corner -- the wheel visibly climbs a ledge it only brushed.
        //
        // Rejecting steep normals and falling back to a straight ray fixes it without giving
        // up what the sphere is here for: a ray can only ever report what is directly
        // beneath, so it cannot be fooled sideways, and the sphere still handles seams and
        // edges everywhere that it is genuinely looking at ground.
        //
        // The fallback costs one raycast, and only in the frames where a wheel is near a wall.
        bool grounded = sphereHit;
        if (!grounded)
        {
            grounded = Physics.Raycast(castStart, down, out RaycastHit straight,
                                       suspensionTravel + wheelRadius + overshoot,
                                       groundMask, QueryTriggerInteraction.Ignore);
            if (grounded) hit = straight;
        }

        if (!grounded)
        {
            wheel.grounded = false;
            wheel.compression = 0f;
            UpdateVisual(wheel, origin + down * suspensionTravel, wheelForward, 0f, dt);
            return false;
        }

        wheel.grounded = true;

        // Both casts answer "how far did the wheel centre travel before contact", but by
        // different routes: the sphere sweep already excludes the radius, the ray does not.
        // Both then give back the overshoot. Negative means compressed past the stop.
        float centreTravel = (sphereHit ? hit.distance : hit.distance - wheelRadius) - overshoot;

        // Only a degenerate hit -- distance 0, meaning the sphere was overlapping at the very
        // start of the sweep -- has a meaningless point. An over-compressed corner is a real
        // hit on real ground, so its hit.point is good and the wheel still renders on the road.
        wheel.contactPoint = hit.distance > 0f ? hit.point : origin + down * wheelRadius;

        // Clamp01 is what turns a negative centreTravel into compression 1: maximum push-back,
        // which is what gets a bottomed-out car back off its belly.
        wheel.compression = Mathf.Clamp01((suspensionTravel - centreTravel) / suspensionTravel);

        Vector3 wheelVelocity = rb.GetPointVelocity(origin);

        // --- suspension -----------------------------------------------------
        float spring = wheel.compression * springStrength;

        // Bump stop. A purely linear spring gives its travel away in a straight line: the
        // last centimetre costs no more than the first, so downforce and load transfer walk
        // the corner to full compression and the body sits on its belly. Past bumpStopStart
        // the rate climbs QUADRATICALLY, so small bumps still get the supple ride the
        // SphereCast is there to provide, while the bottom of the travel resists hard.
        // Cheaper and better-behaved than simply raising springStrength, which would stiffen
        // the ride everywhere and change the rest ride height the anchors were derived for.
        if (wheel.compression > bumpStopStart)
        {
            float over = (wheel.compression - bumpStopStart) / Mathf.Max(0.01f, 1f - bumpStopStart);
            spring += over * over * bumpStopStrength;
        }

        float damper = -Vector3.Dot(wheelVelocity, transform.up) * damperStrength;
        float suspension = Mathf.Max(0f, spring + damper);
        rb.AddForceAtPosition(transform.up * suspension, origin);

        // --- sideways grip --------------------------------------------------
        // Cancel the sideways component of this wheel's velocity. Doing it as an
        // impulse rather than a spring is what makes arcade cars feel planted.
        float grip = wheel.steers ? frontGrip : rearGrip;
        if (handbrake && !wheel.steers) grip *= handbrakeGrip;

        float lateralSpeed = Vector3.Dot(wheelVelocity, wheelRight);
        float lateralForce = -lateralSpeed * grip * (rb.mass / wheels.Length) / dt;
        lateralForce = Mathf.Clamp(lateralForce, -maxGripForce, maxGripForce);
        // Applied at the CONTACT PATCH, not at the anchor. A tyre's grip acts where the
        // rubber meets the road, and the height it is applied at decides which way the body
        // rolls. The effective centre of mass sits at y ~= 0.24 (the three box colliders
        // average to ~0.84, plus centreOfMassOffset's -0.6), so the anchor at y = 0.50 is
        // ABOVE it: pushing there tipped the car INTO the corner, inside wheel dipping, like
        // a motorbike. From the contact patch at y = 0 the couple reverses and the car leans
        // out of the turn the way a car should. Drive and brake forces deliberately stay at
        // the anchor -- the squat and dive they produce is good arcade feel, and nobody has
        // complained about the pitch.
        rb.AddForceAtPosition(wheelRight * lateralForce, wheel.contactPoint);

        // --- drive and brake ------------------------------------------------
        float forwardSpeedAtWheel = Vector3.Dot(wheelVelocity, wheelForward);

        if (wheel.powered && Mathf.Abs(throttle) > 0.01f)
        {
            bool braking = Mathf.Sign(throttle) != Mathf.Sign(ForwardSpeed) && Mathf.Abs(ForwardSpeed) > 1f;

            if (braking)
            {
                rb.AddForceAtPosition(wheelForward * (throttle * brakeForce), origin);
            }
            else
            {
                // Taper power to nothing as top speed approaches.
                float headroom = 1f - Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / topSpeed);
                rb.AddForceAtPosition(wheelForward * (throttle * enginePower * headroom), origin);
            }
        }
        else
        {
            rb.AddForceAtPosition(wheelForward * (-forwardSpeedAtWheel * coastDrag * dt), origin);
        }

        // --- visual ---------------------------------------------------------
        UpdateVisual(wheel, wheel.contactPoint + transform.up * wheelRadius, wheelForward, forwardSpeedAtWheel, dt);
        return true;
    }

    void UpdateVisual(Wheel wheel, Vector3 position, Vector3 wheelForward, float rollSpeed, float dt)
    {
        if (wheel.visual == null) return;

        wheel.visual.position = position;
        wheel.spin += (rollSpeed / Mathf.Max(0.01f, wheelRadius)) * Mathf.Rad2Deg * dt;

        // The correction goes LAST, so it is applied in the mesh's own space. That re-labels
        // which mesh axis is the axle without touching the steering direction or the sign of
        // the roll -- both are decided by the two rotations to its left. Composing it the
        // other way round would steer the wheel with the correction baked in and break both.
        //
        // Quaternion.Euler is deliberately not cached: four per physics step is single-digit
        // microseconds per second, and leaving it live means the value can be dialled in from
        // the Inspector while the game is playing, which is the only sane way to find it.
        wheel.visual.rotation = Quaternion.LookRotation(wheelForward, transform.up)
                                * Quaternion.Euler(wheel.spin, 0f, 0f)
                                * Quaternion.Euler(wheelVisualEuler);
    }

    /// <summary>
    /// Anti-roll bar across one axle. Couples the two corners so the compressed side is
    /// pushed back up and the extended side is pulled down, in proportion to the difference
    /// between them.
    /// </summary>
    /// <remarks>
    /// This is the right tool for "it bottoms out in the corners" specifically, because it
    /// only ever acts on the DIFFERENCE between the two sides. Straight-line ride, single-wheel
    /// bumps and landings are untouched -- a stiffer spring or a shorter travel would tax all
    /// of those to fix a problem that only happens mid-corner.
    ///
    /// Free, in performance terms: no casts, no allocations, two AddForceAtPosition calls per
    /// axle per physics step, reusing compressions the wheel loop already computed.
    ///
    /// Both corners must be grounded for the bar to act. A bar that pushes off a wheel hanging
    /// in the air is inventing force from nothing and will pitch a two-wheeled car over.
    /// </remarks>
    void ApplyAntiRoll(int leftIndex, int rightIndex)
    {
        if (antiRollStrength <= 0f) return;
        if (wheels == null || leftIndex >= wheels.Length || rightIndex >= wheels.Length) return;

        Wheel left = wheels[leftIndex];
        Wheel right = wheels[rightIndex];
        if (left == null || right == null) return;
        if (left.anchor == null || right.anchor == null) return;
        if (!left.grounded || !right.grounded) return;

        // Positive when the LEFT corner is the more compressed of the two.
        float force = (left.compression - right.compression) * antiRollStrength;

        rb.AddForceAtPosition(transform.up * force, left.anchor.position);
        rb.AddForceAtPosition(transform.up * -force, right.anchor.position);
    }

    void ApplyDownforce()
    {
        float speed01 = Mathf.Clamp01(Speed / Mathf.Max(1f, topSpeed));
        float force = downforce * speed01 * rb.mass * Physics.gravity.magnitude;
        rb.AddForce(-transform.up * force);
    }

    /// <summary>
    /// Mid-air rotation. Not realistic, entirely standard for arcade racers, and the
    /// difference between a jump that feels good and one that feels like a punishment.
    /// </summary>
    void ApplyAirControl(float pitch, float roll)
    {
        if (airControlTorque <= 0f) return;

        // Cap the spin the INPUT can build, not the spin itself. A crash or a ramp lip can
        // legitimately send the car spinning faster than this and that rotation is kept; air
        // control simply stops adding to an axis already over the limit. Clamping the
        // rigidbody's angular velocity instead would throw away exactly the momentum this
        // change exists to preserve.
        float limit = maxAirSpin * Mathf.Deg2Rad;
        Vector3 spin = rb.angularVelocity;

        float pitchTorque = -pitch * airControlTorque;
        float rollTorque = -roll * airControlTorque;

        if (Vector3.Dot(spin, transform.right) * pitchTorque > 0f &&
            Mathf.Abs(Vector3.Dot(spin, transform.right)) > limit)
            pitchTorque = 0f;

        if (Vector3.Dot(spin, transform.forward) * rollTorque > 0f &&
            Mathf.Abs(Vector3.Dot(spin, transform.forward)) > limit)
            rollTorque = 0f;

        rb.AddTorque(transform.right * pitchTorque);
        rb.AddTorque(transform.forward * rollTorque);
    }

    /// <summary>
    /// True when the wheels are down OR the bodywork is against something.
    /// </summary>
    /// <remarks>
    /// <see cref="Grounded"/> is a WHEEL test — it is what the suspension, drive and grip are
    /// built on, and it is correct for all of them. It is the wrong question for "has the car
    /// landed", because a car that comes down on its roof, its side, or across a boulder has
    /// very much landed and has no wheel touching anything. Airtime scoring on `Grounded` alone
    /// keeps counting through the entire barrel roll and only pays out if the car happens to
    /// end up on its tyres.
    ///
    /// Driven from OnCollisionStay with a short expiry rather than a Stay/Exit pair, because
    /// OnCollisionExit is easy to miss when a collider is disabled, destroyed or teleported —
    /// and a stuck "still touching" flag would end airtime permanently.
    /// </remarks>
    public bool Touching => Grounded || Time.time - lastBodyTouch < bodyTouchMemory;

    /// <summary>Seconds a body contact keeps counting after the last OnCollisionStay.</summary>
    const float bodyTouchMemory = 0.15f;

    float lastBodyTouch = -99f;

    void OnCollisionStay(Collision collision)
    {
        // Same mask the wheels use, so "ground" means one thing across the whole component.
        // Debris and boulders are on Default too, and landing across a boulder IS a landing.
        if ((groundMask.value & (1 << collision.gameObject.layer)) == 0) return;
        lastBodyTouch = Time.time;
    }

    /// <summary>
    /// Knock a wheel off. Returns false if the index is bad or it is already gone.
    /// </summary>
    /// <param name="hideVisual">
    /// False when the caller is going to take the wheel mesh over as debris. Pass false in
    /// that case or the mesh is deactivated out from under them.
    /// </param>
    public bool DetachWheel(int index, bool hideVisual = true)
    {
        if (wheels == null || index < 0 || index >= wheels.Length) return false;

        Wheel wheel = wheels[index];
        if (wheel == null || wheel.detached) return false;

        wheel.detached = true;

        // Only hide the mesh when nobody else is going to take it. CarDamage throws the REAL
        // wheel when the part has a visual, and an inactive GameObject neither simulates nor
        // renders -- adding a Rigidbody to it and setting a velocity succeeds silently and
        // does nothing. The wheel would vanish on contact instead of tumbling away, and
        // DebrisPool would be handed a dead object to manage.
        if (hideVisual && wheel.visual != null) wheel.visual.gameObject.SetActive(false);
        return true;
    }

    /// <summary>
    /// Put a detached wheel back into service. The inverse of <see cref="DetachWheel"/>.
    /// </summary>
    /// <remarks>
    /// Bolting the mesh back on is only half of a repair. This flag is what gates the corner's
    /// spring, drive force and lateral grip, so a repair that misses it leaves a car that looks
    /// whole and permanently drags one corner.
    /// </remarks>
    public bool ReattachWheel(int index)
    {
        if (wheels == null || index < 0 || index >= wheels.Length) return false;

        Wheel wheel = wheels[index];
        if (wheel == null || !wheel.detached) return false;

        wheel.detached = false;
        wheel.spin = 0f;
        if (wheel.visual != null) wheel.visual.gameObject.SetActive(true);
        return true;
    }

    /// <summary>How many wheels are still attached.</summary>
    public int WheelsRemaining()
    {
        if (wheels == null) return 0;

        int count = 0;
        foreach (Wheel wheel in wheels)
            if (wheel != null && !wheel.detached) count++;

        return count;
    }

    void OnDrawGizmosSelected()
    {
        if (wheels == null) return;

        foreach (Wheel wheel in wheels)
        {
            if (wheel == null || wheel.anchor == null) continue;

            Vector3 origin = wheel.anchor.position;
            Gizmos.color = Color.grey;
            Gizmos.DrawLine(origin, origin - transform.up * (suspensionTravel + wheelRadius));

            Gizmos.color = wheel.grounded ? Color.green : Color.red;
            Gizmos.DrawWireSphere(origin - transform.up * (suspensionTravel * (1f - wheel.compression)), wheelRadius);
        }

        if (Application.isPlaying) return;
        Rigidbody body = GetComponent<Rigidbody>();
        if (body != null)
        {
            // Awake does `centerOfMass += centreOfMassOffset`, so the offset is NOT the centre
            // of mass -- it is a nudge applied to Unity's collider-derived one. Drawing the
            // raw offset put this marker 0.6 m under the road while the real centre of mass
            // sat at about y 0.24, which is exactly the number you need when reasoning about
            // which way the car rolls. Show the sum.
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.TransformPoint(body.centerOfMass + centreOfMassOffset), 0.12f);
        }
    }
}
