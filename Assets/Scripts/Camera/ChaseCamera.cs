using UnityEngine;

/// <summary>
/// Automatic chase camera. Takes no player input at all — the player only drives.
///
/// Fixes the four things the reference game gets wrong:
///   1. It follows the direction the car is MOVING, not the direction it is facing,
///      so a spin or a broadside hit doesn't whip the view around.
///   2. It aims at the road ahead rather than at the car, so on a steep descent you
///      see where you are going instead of asphalt.
///   3. It leans with the slope, so the framing holds on hills.
///   4. It pulls back and widens with speed.
///
/// Cost: two physics casts per frame. Everything else is transform maths.
/// </summary>
[RequireComponent(typeof(Camera))]
public class ChaseCamera : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The car to follow.")]
    public Transform target;

    [Tooltip("The car's Rigidbody. Used to read velocity. Auto-found from Target if empty.")]
    public Rigidbody targetBody;

    [Header("Framing")]
    [Tooltip("Resting distance behind the car, in metres.")]
    public float distance = 7f;

    [Tooltip("Resting height above the car, in metres.")]
    public float height = 3.2f;

    [Tooltip("How far up from the aim point the camera looks. Raise to sit the car lower in frame.")]
    public float lookHeight = 1.4f;

    [Header("Speed response")]
    [Tooltip("Speed (m/s) at which pull-back and FOV widening are fully applied.")]
    public float speedForFullEffect = 28f;

    [Tooltip("Extra distance added at full speed.")]
    public float speedDistance = 3f;

    [Tooltip("Extra height added at full speed.")]
    public float speedHeight = 0.7f;

    [Tooltip("Field of view when stationary.")]
    [Range(40f, 90f)] public float baseFov = 60f;

    [Tooltip("Field of view at full speed. The widening is most of the sensation of speed.")]
    [Range(40f, 110f)] public float fastFov = 76f;

    [Header("Looking ahead")]
    [Tooltip("How far down the road the camera aims, in metres. This is the downhill fix.")]
    public float aimAhead = 9f;

    [Tooltip("0 = aim at the car. 1 = aim fully at the road ahead.")]
    [Range(0f, 1f)] public float aimAheadWeight = 0.75f;

    [Tooltip("How far the aim point swings outward through a turn, in metres.")]
    public float turnLead = 2.5f;

    [Header("Slope")]
    [Tooltip("How much the rig leans with the ground. 0 = always world-up, 1 = fully on the slope.")]
    [Range(0f, 1f)] public float slopeAlign = 0.55f;

    [Tooltip("Surfaces treated as drivable ground when sampling slope.")]
    public LayerMask groundMask = ~0;

    [Header("Smoothing")]
    [Tooltip("Higher is snappier. 6-10 is a good range.")]
    public float positionSharpness = 8f;

    [Tooltip("Higher is snappier.")]
    public float rotationSharpness = 11f;

    [Tooltip("How fast the camera swings around behind the car. Keep this LOW — it is what stops spins being nauseating.")]
    public float yawSharpness = 3.5f;

    [Tooltip("Below this speed (m/s) the camera uses the car's facing instead of its velocity.")]
    public float velocityYawThreshold = 2.5f;

    [Header("Collision")]
    [Tooltip("Pull the camera in when scenery would block the view.")]
    public bool avoidGeometry = true;

    [Tooltip("Solid geometry the camera should not pass through. Exclude the car's own layer.")]
    public LayerMask collisionMask = ~0;

    [Tooltip("Keeps the near plane clear of walls.")]
    public float cameraRadius = 0.35f;

    Camera cam;
    float yaw;
    float fov;
    Vector3 groundNormal = Vector3.up;
    Vector3 aimPoint;
    bool initialised;

    void Awake()
    {
        cam = GetComponent<Camera>();
        fov = baseFov;

        if (targetBody == null && target != null)
            targetBody = target.GetComponentInParent<Rigidbody>();
    }

    void OnEnable()
    {
        initialised = false;
    }

    void LateUpdate()
    {
        if (target == null) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 velocity = targetBody != null ? targetBody.linearVelocity : Vector3.zero;
        Vector3 flatVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
        float speed = flatVelocity.magnitude;
        float speed01 = Mathf.Clamp01(speed / Mathf.Max(0.01f, speedForFullEffect));

        UpdateYaw(flatVelocity, speed, dt);
        UpdateGroundNormal(dt);

        Vector3 rigUp = Vector3.Slerp(Vector3.up, groundNormal, slopeAlign);
        Vector3 rigForward = Vector3.ProjectOnPlane(YawDirection(), rigUp).normalized;
        if (rigForward.sqrMagnitude < 0.0001f) rigForward = YawDirection();

        Quaternion basis = Quaternion.LookRotation(rigForward, rigUp);

        Vector3 desiredPosition = target.position
            + basis * new Vector3(
                0f,
                height + speedHeight * speed01,
                -(distance + speedDistance * speed01));

        Vector3 pivot = target.position + rigUp * (height * 0.5f);
        if (avoidGeometry)
            desiredPosition = PullInFromGeometry(pivot, desiredPosition);

        Vector3 desiredAim = ResolveAimPoint(rigForward, rigUp, speed01);

        if (!initialised)
        {
            transform.position = desiredPosition;
            aimPoint = desiredAim;
            initialised = true;
        }
        else
        {
            transform.position = Vector3.Lerp(transform.position, desiredPosition, Damp(positionSharpness, dt));
            aimPoint = Vector3.Lerp(aimPoint, desiredAim, Damp(rotationSharpness, dt));
        }

        Vector3 toAim = aimPoint - transform.position;
        if (toAim.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(toAim, rigUp);

        fov = Mathf.Lerp(fov, Mathf.Lerp(baseFov, fastFov, speed01), Damp(4f, dt));
        cam.fieldOfView = fov;
    }

    /// <summary>
    /// Track the direction of travel, not the car's facing. A car that is spinning or
    /// sliding sideways still has a sensible velocity vector, and following it keeps the
    /// view calm through exactly the moments this game is built around.
    /// </summary>
    void UpdateYaw(Vector3 flatVelocity, float speed, float dt)
    {
        Vector3 facing = Vector3.ProjectOnPlane(target.forward, Vector3.up);
        Vector3 desired;

        if (speed > velocityYawThreshold)
        {
            // Reversing should not spin the camera around to look at the boot.
            desired = Vector3.Dot(flatVelocity, facing) < 0f ? facing : flatVelocity;
        }
        else
        {
            desired = facing;
        }

        if (desired.sqrMagnitude < 0.0001f) return;

        float desiredYaw = Mathf.Atan2(desired.x, desired.z) * Mathf.Rad2Deg;

        if (!initialised) yaw = desiredYaw;
        else yaw = Mathf.LerpAngle(yaw, desiredYaw, Damp(yawSharpness, dt));
    }

    void UpdateGroundNormal(float dt)
    {
        Vector3 origin = target.position + Vector3.up * 2f;
        Vector3 sampled = Vector3.up;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 12f, groundMask, QueryTriggerInteraction.Ignore))
            sampled = hit.normal;

        // Airborne or over a gap: ease back to level rather than snapping.
        groundNormal = Vector3.Slerp(groundNormal, sampled, Damp(5f, dt));
    }

    /// <summary>
    /// Aim at the road ahead rather than at the car. On a descent the road ahead is below
    /// the car, so the camera pitches down and the horizon stays visible.
    /// </summary>
    Vector3 ResolveAimPoint(Vector3 rigForward, Vector3 rigUp, float speed01)
    {
        Vector3 carPoint = target.position + rigUp * lookHeight;

        Vector3 ahead = target.position + rigForward * (aimAhead * Mathf.Lerp(0.6f, 1f, speed01));
        Vector3 probe = ahead + Vector3.up * 6f;

        Vector3 aheadPoint;
        if (Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 30f, groundMask, QueryTriggerInteraction.Ignore))
            aheadPoint = hit.point + Vector3.up * lookHeight;
        else
            aheadPoint = ahead + rigUp * lookHeight;   // over a jump or a cliff edge

        Vector3 blended = Vector3.Lerp(carPoint, aheadPoint, aimAheadWeight);

        // Swing the aim outward through a turn so the player sees into the corner.
        float yawError = Mathf.DeltaAngle(yaw, transform.eulerAngles.y);
        Vector3 right = Vector3.Cross(rigUp, rigForward).normalized;
        blended += right * Mathf.Clamp(yawError / 45f, -1f, 1f) * turnLead;

        return blended;
    }

    Vector3 PullInFromGeometry(Vector3 pivot, Vector3 desired)
    {
        Vector3 delta = desired - pivot;
        float length = delta.magnitude;
        if (length < 0.01f) return desired;

        if (Physics.SphereCast(pivot, cameraRadius, delta / length, out RaycastHit hit,
                               length, collisionMask, QueryTriggerInteraction.Ignore))
        {
            return pivot + (delta / length) * Mathf.Max(0.4f, hit.distance);
        }

        return desired;
    }

    Vector3 YawDirection()
    {
        return new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));
    }

    /// <summary>Frame-rate independent exponential smoothing.</summary>
    static float Damp(float sharpness, float dt)
    {
        return 1f - Mathf.Exp(-sharpness * dt);
    }

    /// <summary>Snap straight to the target. Call after a respawn or a scene load.</summary>
    public void SnapToTarget()
    {
        initialised = false;
    }

    void OnDrawGizmosSelected()
    {
        if (target == null) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(aimPoint, 0.4f);
        Gizmos.DrawLine(transform.position, aimPoint);

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(target.position, target.position + groundNormal * 3f);
    }
}
