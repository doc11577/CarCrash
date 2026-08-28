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
    }

    [Header("Wheels")]
    public Wheel[] wheels = new Wheel[4];

    [Tooltip("Surfaces the wheels can rest on. Must exclude the car's own layer.")]
    public LayerMask groundMask = ~0;

    [Header("Suspension")]
    [Tooltip("Wheel radius in metres. Also how far below the anchor the wheel sits at full droop.")]
    public float wheelRadius = 0.48f;

    [Tooltip("How far the suspension can compress, in metres.")]
    public float suspensionTravel = 0.30f;

    [Tooltip("Stiffness. Too low and the car wallows; too high and it skips over bumps.")]
    public float springStrength = 9000f;

    [Tooltip("Bounce absorption. Roughly 8-12% of spring strength is a sane starting point.")]
    public float damperStrength = 3000f;

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

    [Header("Read-only")]
    [SerializeField] float debugSpeed;

    Rigidbody rb;
    CarInput input;
    float steerAngle;

    /// <summary>Forward speed in metres per second. Negative when reversing.</summary>
    public float ForwardSpeed { get; private set; }

    /// <summary>Speed regardless of direction, in metres per second.</summary>
    public float Speed { get; private set; }

    /// <summary>True while at least one wheel is touching the ground.</summary>
    public bool Grounded { get; private set; }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        input = GetComponent<CarInput>();
        rb.centerOfMass += centreOfMassOffset;
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        Vector3 velocity = rb.linearVelocity;
        ForwardSpeed = Vector3.Dot(velocity, transform.forward);
        Speed = velocity.magnitude;
        debugSpeed = Speed;

        float throttle  = input != null ? input.Throttle : 0f;
        float steerWish = input != null ? input.Steer : 0f;
        bool handbrake  = input != null && input.Handbrake;

        UpdateSteering(steerWish, dt);

        int groundedCount = 0;
        foreach (Wheel wheel in wheels)
        {
            if (wheel == null || wheel.anchor == null) continue;
            if (ApplyWheel(wheel, throttle, handbrake, dt)) groundedCount++;
        }

        Grounded = groundedCount > 0;

        if (Grounded)
        {
            ApplyDownforce();
        }
        else
        {
            ApplyAirControl(throttle, steerWish);
        }
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
        Vector3 origin = wheel.anchor.position;
        Vector3 down = -transform.up;
        float castLength = suspensionTravel + wheelRadius;

        Vector3 wheelForward = wheel.steers
            ? Quaternion.AngleAxis(steerAngle, transform.up) * transform.forward
            : transform.forward;
        Vector3 wheelRight = Vector3.Cross(transform.up, wheelForward).normalized;

        if (!Physics.Raycast(origin, down, out RaycastHit hit, castLength, groundMask, QueryTriggerInteraction.Ignore))
        {
            wheel.grounded = false;
            wheel.compression = 0f;
            UpdateVisual(wheel, origin + down * suspensionTravel, wheelForward, 0f, dt);
            return false;
        }

        wheel.grounded = true;
        wheel.contactPoint = hit.point;
        wheel.compression = Mathf.Clamp01((castLength - hit.distance) / suspensionTravel);

        Vector3 wheelVelocity = rb.GetPointVelocity(origin);

        // --- suspension -----------------------------------------------------
        float spring = wheel.compression * springStrength;
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
        rb.AddForceAtPosition(wheelRight * lateralForce, origin);

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
        UpdateVisual(wheel, hit.point + transform.up * wheelRadius, wheelForward, forwardSpeedAtWheel, dt);
        return true;
    }

    void UpdateVisual(Wheel wheel, Vector3 position, Vector3 wheelForward, float rollSpeed, float dt)
    {
        if (wheel.visual == null) return;

        wheel.visual.position = position;
        wheel.spin += (rollSpeed / Mathf.Max(0.01f, wheelRadius)) * Mathf.Rad2Deg * dt;
        wheel.visual.rotation = Quaternion.LookRotation(wheelForward, transform.up)
                                * Quaternion.Euler(wheel.spin, 0f, 0f);
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

        rb.AddTorque(transform.right * (-pitch * airControlTorque));
        rb.AddTorque(transform.forward * (-roll * airControlTorque));
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
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.TransformPoint(centreOfMassOffset), 0.12f);
        }
    }
}
