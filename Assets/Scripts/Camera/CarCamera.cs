using UnityEngine;

/// <summary>
/// Per-vehicle camera framing. Put it on a car prefab whose size the shared
/// <see cref="ChaseCamera"/> tuning does not suit.
/// </summary>
/// <remarks>
/// `ChaseCamera`'s numbers were tuned on the E30 — 4.16 m long, 1.36 m to the roof — and they
/// suit the P72 too, because a supercar is smaller again. The LCT 3000 broke them: it is
/// **2.1x taller and 1.4x longer**, so the default rig sits level with the box body and the
/// truck fills the screen with the road hidden behind it.
///
/// **The offsets are ADDITIVE, not absolute, and that is the whole design.** A car that frames
/// correctly needs no component at all — absent means zero, so the E30 and P72 keep the exact
/// behaviour they have now and cannot be disturbed by a change made for the truck. Each vehicle
/// states only how it DIFFERS from the shared rig, so retuning the base camera still moves every
/// car together instead of being silently overridden per prefab.
///
/// It lives on the CAR, not on the camera and not in `CarRoster`, because framing is a property
/// of the vehicle's shape. The camera is one object that follows whatever is spawned, and the
/// roster is about price and ownership. Putting it here means the truck carries its own framing
/// wherever it is used.
///
/// `ChaseCamera` reads it through the same lazy `PlayerCar.Current` path it uses to find the car
/// at all, so a swapped car swaps its framing with it and nothing has to be wired in the scene.
///
/// **Deriving the numbers, so the next vehicle is measured rather than guessed.** Take the body
/// bounds from `tools/blender/car_bounds.py` and keep the E30's proportions:
///
///   extraHeight   = (roof height  - 1.355) x 1.0    keeps the same clearance over the roof
///   extraDistance = (rear overhang - 2.578) + (roof height - 1.355) x 0.5
///   extraLookHeight = roof height - 1.355
///
/// The height term is the one that matters. Framing maths says distance barely changes the
/// screen fraction of a long vehicle — raising the camera over the roof is what actually
/// recovers the view of the road, which is also the first thing you notice is wrong.
/// </remarks>
[DisallowMultipleComponent]
public class CarCamera : MonoBehaviour
{
    [Tooltip("Metres ADDED to ChaseCamera.distance for this vehicle. Positive pulls the camera " +
             "back. Leave at 0 for a car the shared rig already suits.")]
    public float extraDistance = 0f;

    [Tooltip("Metres ADDED to ChaseCamera.height. Positive raises the camera. This is the one " +
             "that matters on a tall vehicle — it is what puts the road back on screen instead " +
             "of the roof.")]
    public float extraHeight = 0f;

    [Tooltip("Metres ADDED to ChaseCamera.lookHeight, the point on the car the camera aims at " +
             "while looking around. Roughly the roof height difference.")]
    public float extraLookHeight = 0f;

    /// <summary>Framing for a car, or zeroes when the car has no component. Never null-checks at the call site.</summary>
    public static void Read(Transform car, out float distance, out float height, out float lookHeight)
    {
        distance = 0f;
        height = 0f;
        lookHeight = 0f;

        if (car == null) return;

        // GetComponentInParent rather than GetComponent: ChaseCamera.target can be pointed at a
        // child in the Inspector, and the component belongs on the car root.
        CarCamera framing = car.GetComponentInParent<CarCamera>();
        if (framing == null) return;

        distance = framing.extraDistance;
        height = framing.extraHeight;
        lookHeight = framing.extraLookHeight;
    }
}
