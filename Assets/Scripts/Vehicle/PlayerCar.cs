using UnityEngine;

/// <summary>
/// Marks a car as THE player's car, and announces it to whatever needs to know.
///
/// Nothing searches for the player car. The car says who it is when it becomes active and
/// withdraws when it stops, so a car spawned by the garage mid-session works exactly like one
/// placed in the scene by hand, and swapping cars is just one object going away and another
/// arriving.
/// </summary>
/// <remarks>
/// Why a marker component instead of the two obvious shortcuts:
///
///   FindFirstObjectByType&lt;CarDamage&gt;() returns an arbitrary car the moment traffic exists,
///   so scoring would silently attach itself to a random NPC.
///
///   Sniffing for whichever car carries a CarInput, or is tagged Player, is implicit matching —
///   the pattern that has already cost this project three separate bugs (trim/rim, the
///   steering_centre misclassification, mirrors stealing door hits). A component that means
///   exactly one thing cannot be matched by accident.
///
/// Ordering is handled from both directions, because component init order across GameObjects is
/// undefined: if <see cref="RunScore"/> already exists this registers with it directly, and if it
/// does not, RunScore reads <see cref="Current"/> in its own Start. Registration is idempotent,
/// so both firing is harmless.
/// </remarks>
[RequireComponent(typeof(CarDamage))]
[DisallowMultipleComponent]
public class PlayerCar : MonoBehaviour
{
    /// <summary>
    /// The player's car right now, or null between cars. Anything that needs to follow, score,
    /// read or repair the player's car should go through this rather than a scene reference —
    /// a scene reference cannot point at a car the garage has not spawned yet.
    /// </summary>
    public static PlayerCar Current { get; private set; }

    /// <summary>This car's damage component. The thing scoring actually subscribes to.</summary>
    public CarDamage Damage { get; private set; }

    /// <summary>This car's controller, for anything that needs speed or grounded state.</summary>
    public CarController Controller { get; private set; }

    void Awake()
    {
        Damage = GetComponent<CarDamage>();
        Controller = GetComponent<CarController>();
    }

    void OnEnable()
    {
        if (Current != null && Current != this)
        {
            Debug.LogWarning(
                $"Two PlayerCar markers are active at once ({Current.name} and {name}). The " +
                "newest wins. Split-screen will need this to become a list.", this);
        }

        Current = this;

        // Null when this car is part of the initial scene load and happened to initialise before
        // RunScore. That case is covered by RunScore.Start reading Current.
        if (RunScore.Instance != null) RunScore.Instance.Register(Damage);
    }

    void OnDisable()
    {
        if (RunScore.Instance != null) RunScore.Instance.Unregister(Damage);

        // Only stand down if this car is still the one on duty. A straight `Current = null` would
        // clear the incoming car when a swap disables the old one after enabling the new.
        if (Current == this) Current = null;
    }
}
