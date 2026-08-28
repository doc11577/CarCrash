using System;
using UnityEngine;

/// <summary>
/// Turns impacts into lost parts.
///
/// Each part is a named point on the car with its own health. An impact finds the nearest
/// part to the contact and damages it; at zero health the part detaches and a debris prop
/// is thrown into the world.
///
/// Wheels are special: losing one is a handling change, not just a visual. See
/// <see cref="CarController.DetachWheel"/> — that corner loses its spring, its drive and
/// its grip, so the body drops onto its collider and drags.
///
/// Kenney's car bodies are a single welded mesh, so panels cannot actually be removed from
/// it. Detachment is faked by throwing the matching generic debris prop. At this poly count
/// and camera distance the flying part is what the eye follows, not the hole it left.
/// </summary>
[RequireComponent(typeof(CarController))]
public class CarDamage : MonoBehaviour
{
    [Serializable]
    public class Part
    {
        [Tooltip("For your own reference in the Inspector.")]
        public string name = "part";

        [Tooltip("Where on the car this part lives, and where its debris spawns from.")]
        public Transform anchor;

        [Tooltip("Debris prop thrown when this part comes off.")]
        public GameObject debrisPrefab;

        [Tooltip("Damage this part absorbs before it comes off.")]
        public float health = 100f;

        [Tooltip("Wheel index in CarController, or -1 for a body panel.")]
        public int wheelIndex = -1;

        [HideInInspector] public bool detached;
        [HideInInspector] public float startingHealth;
    }

    [Header("Parts")]
    public Part[] parts;

    [Header("Impact response")]
    [Tooltip("Impacts gentler than this are ignored entirely. Stops kerbs shedding bumpers.")]
    public float minimumImpulse = 900f;

    [Tooltip("Damage dealt per unit of collision impulse.")]
    public float damagePerImpulse = 0.045f;

    [Tooltip("How far from a contact point a part can be and still take the hit, in metres.")]
    public float partReach = 1.6f;

    [Header("Debris throw")]
    [Tooltip("Extra outward speed given to a detached part, on top of the car's velocity.")]
    public float ejectSpeed = 3.5f;

    [Tooltip("Random tumble applied to detached parts, in radians per second.")]
    public float ejectSpin = 9f;

    [Header("Layers")]
    [Tooltip("Only collisions with these layers can cause damage. Exclude the car's own layer.")]
    public LayerMask damagingLayers = ~0;

    /// <summary>Raised on every damaging impact, with the damage dealt. Hook scoring here.</summary>
    public event Action<float, Vector3> Damaged;

    /// <summary>Raised when a part comes off, with the part that went.</summary>
    public event Action<Part> PartLost;

    /// <summary>Total damage this car has taken. The basis for the gear payout.</summary>
    public float TotalDamage { get; private set; }

    CarController controller;
    Rigidbody body;

    void Awake()
    {
        controller = GetComponent<CarController>();
        body = GetComponent<Rigidbody>();

        if (parts == null) return;
        foreach (Part part in parts)
            if (part != null) part.startingHealth = part.health;
    }

    void OnCollisionEnter(Collision collision)
    {
        if ((damagingLayers.value & (1 << collision.gameObject.layer)) == 0) return;

        float impulse = collision.impulse.magnitude;
        if (impulse < minimumImpulse) return;

        Vector3 contact = collision.GetContact(0).point;
        float damage = (impulse - minimumImpulse) * damagePerImpulse;
        if (damage <= 0f) return;

        TotalDamage += damage;
        Damaged?.Invoke(damage, contact);

        Part hit = NearestPart(contact);
        if (hit == null) return;

        hit.health -= damage;
        if (hit.health <= 0f) Detach(hit, contact);
    }

    Part NearestPart(Vector3 worldContact)
    {
        if (parts == null) return null;

        Part best = null;
        float bestSqr = partReach * partReach;

        foreach (Part part in parts)
        {
            if (part == null || part.detached || part.anchor == null) continue;

            float sqr = (part.anchor.position - worldContact).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = part;
            }
        }

        return best;
    }

    void Detach(Part part, Vector3 contact)
    {
        part.detached = true;

        // A lost wheel is a handling change, not just a missing mesh.
        if (part.wheelIndex >= 0)
            controller.DetachWheel(part.wheelIndex);

        ThrowDebris(part, contact);
        PartLost?.Invoke(part);
    }

    void ThrowDebris(Part part, Vector3 contact)
    {
        if (part.debrisPrefab == null || DebrisPool.Instance == null || part.anchor == null) return;

        Vector3 spawnAt = part.anchor.position;

        // Push the piece away from the car's centre so it doesn't spawn inside the body
        // and get flung by the depenetration solver.
        Vector3 outward = (spawnAt - body.worldCenterOfMass).normalized;
        if (outward.sqrMagnitude < 0.01f) outward = transform.up;

        Vector3 velocity = body.GetPointVelocity(spawnAt) + outward * ejectSpeed;
        Vector3 spin = UnityEngine.Random.insideUnitSphere * ejectSpin;

        DebrisPool.Instance.Spawn(part.debrisPrefab, spawnAt, part.anchor.rotation, velocity, spin);
    }

    /// <summary>Put every part back. Used by the restart path and the garage.</summary>
    public void Repair()
    {
        TotalDamage = 0f;
        if (parts == null) return;

        foreach (Part part in parts)
        {
            if (part == null) continue;
            part.detached = false;
            part.health = part.startingHealth;
        }
    }

    void OnDrawGizmosSelected()
    {
        if (parts == null) return;

        foreach (Part part in parts)
        {
            if (part == null || part.anchor == null) continue;

            Gizmos.color = part.detached ? Color.red : new Color(1f, 0.78f, 0.15f);
            Gizmos.DrawWireSphere(part.anchor.position, 0.22f);
        }
    }
}
