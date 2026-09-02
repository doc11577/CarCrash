using UnityEngine;

/// <summary>
/// Scores where the car lands on the Bullseye map's dartboard.
/// </summary>
/// <remarks>
/// Standard 5-colour ARCHERY scoring: ten equal-width concentric rings, gold in the middle,
/// scoring 10 down to 1 outward. No segments, no multipliers — the ring alone answers "how good
/// was that landing", which is the only question this map asks.
///
/// It started as a real dartboard and that was worse in both directions. A dartboard's single
/// band covers most of the disc, so it needed 20 numbered segments layered on top of the rings
/// just to spread the scores out, and the resulting face is visually busy at 200 m across. Ten
/// graded rings do the same job with a third of the machinery.
///
/// **The ring table must match `tools/blender/build_dartboard.py`.** They are duplicated across
/// a Python/C# boundary with no sane way to share a constant, so the generator PRINTS its table
/// and these defaults are set from that print. If a landing scores the wrong ring, that mismatch
/// is the first thing to check — and `lastDistance` in the readout tells you immediately, because
/// it is in metres and the generator prints its boundaries in metres too.
///
/// Landing is detected the same way <see cref="RunScore"/> detects it for airtime — a transition
/// from not-grounded to grounded — but deliberately NOT shared with it. Airtime pays for time in
/// the air anywhere on any map; this pays for where you came down on one specific object. Tying
/// them together would mean a bump on the run-up scoring a dartboard hit.
/// </remarks>
[DisallowMultipleComponent]
public class DartboardScore : MonoBehaviour
{
    [System.Serializable]
    public class Ring
    {
        public string label = "1";

        [Tooltip("Outer edge of this ring, as a fraction of the board radius.")]
        [Range(0f, 1f)] public float outer = 1f;

        [Tooltip("Archery points for landing in this ring, 10 in the gold down to 1 at the edge.")]
        public int score = 1;

        [Tooltip("Popup colour. Matches the ring's own colour on the board.")]
        public Color colour = Color.white;
    }

    [Header("The board")]
    [Tooltip("Centre of the dartboard. Put an empty at the middle of the board and drag it here.")]
    public Transform board;

    [Tooltip("Board radius in metres. Must match --radius in build_dartboard.py — currently 98.")]
    public float radius = 98f;

    [Tooltip("Rings from the centre outward. Ten equal bands, matching the table the generator " +
             "prints. The outer fractions are what must agree with the mesh.")]
    public Ring[] rings =
    {
        new Ring { label = "BULLSEYE", outer = 0.1f, score = 10, colour = new Color(1f, 0.84f, 0.15f) },
        new Ring { label = "GOLD 9",   outer = 0.2f, score =  9, colour = new Color(1f, 0.84f, 0.15f) },
        new Ring { label = "RED 8",    outer = 0.3f, score =  8, colour = new Color(0.95f, 0.30f, 0.28f) },
        new Ring { label = "RED 7",    outer = 0.4f, score =  7, colour = new Color(0.95f, 0.30f, 0.28f) },
        new Ring { label = "BLUE 6",   outer = 0.5f, score =  6, colour = new Color(0.40f, 0.70f, 1f) },
        new Ring { label = "BLUE 5",   outer = 0.6f, score =  5, colour = new Color(0.40f, 0.70f, 1f) },
        new Ring { label = "BLACK 4",  outer = 0.7f, score =  4, colour = new Color(0.70f, 0.70f, 0.74f) },
        new Ring { label = "BLACK 3",  outer = 0.8f, score =  3, colour = new Color(0.70f, 0.70f, 0.74f) },
        new Ring { label = "WHITE 2",  outer = 0.9f, score =  2, colour = Color.white },
        new Ring { label = "WHITE 1",  outer = 1.0f, score =  1, colour = Color.white },
    };

    [Header("Payout")]
    [Tooltip("Gears per archery point. A bullseye is 10 points, so 45 pays 450 gears — a bit " +
             "over twice what a good damage run earns, which is the right weight for the thing " +
             "the whole map is built around.")]
    public float gearsPerPoint = 45f;

    [Tooltip("Score at or above this gets the big popup. 8 means the gold and the inner red.")]
    public int majorScore = 8;

    [Tooltip("Ignore landings gentler than this drop speed, so rolling across the board after " +
             "the first landing does not score again and again.")]
    public float minLandingSpeed = 6f;

    [Tooltip("Seconds before the board can score the same run again. Belt and braces on top of " +
             "the speed test — a car that lands, bounces and lands again is ONE dart.")]
    public float rearmDelay = 3f;

    [Header("Read-only — watch these in play")]
    [Tooltip("Metres from the board centre where the car last came down. Compare against the " +
             "ring boundaries the generator printed, in metres.")]
    [SerializeField] float lastDistance = -1f;

    [SerializeField] string lastResult = "(nothing yet)";

    bool wasAirborne;
    float armedAt;
    CarController car;

    void Update()
    {
        CarController current = PlayerCar.Current != null ? PlayerCar.Current.Controller : null;
        if (current != car)
        {
            car = current;
            wasAirborne = false;
        }

        if (car == null || board == null) return;

        // Touching, not Grounded: landing upside down on the gold is still landing on the gold.
        bool airborne = !car.Touching;

        // Landing edge: was in the air, is not any more.
        if (wasAirborne && !airborne) Land();
        wasAirborne = airborne;
    }

    void Land()
    {
        if (Time.time < armedAt) return;

        Rigidbody body = car.GetComponent<Rigidbody>();
        float drop = body != null ? -body.linearVelocity.y : 0f;
        if (drop < minLandingSpeed)
        {
            // A skim or a settle, not a dart. Without this a car that touches down and skips
            // scores several times across one landing.
            return;
        }

        Vector3 delta = car.transform.position - board.position;
        delta.y = 0f;

        float distance = delta.magnitude;
        lastDistance = distance;

        if (distance > radius)
        {
            lastResult = "off the board (" + distance.ToString("F0") + " m)";
            return;
        }

        armedAt = Time.time + rearmDelay;

        Ring ring = RingAt(distance);
        int gears = Mathf.RoundToInt(ring.score * gearsPerPoint);

        lastResult = ring.label + " = " + ring.score + " pts, " + gears + " gears";

        if (RunScore.Instance != null)
        {
            RunScore.Instance.Award(gears, ring.label + "  +" + gears, car.transform.position,
                                    ring.colour, ring.score >= majorScore);
        }
    }

    Ring RingAt(float distance)
    {
        float u = radius > 0.001f ? distance / radius : 1f;

        foreach (Ring ring in rings)
            if (u <= ring.outer) return ring;

        return rings.Length > 0 ? rings[rings.Length - 1] : new Ring();
    }

    void OnDrawGizmosSelected()
    {
        if (board == null) return;

        // Draw the ring boundaries so they can be checked against the geometry by eye, which is
        // the only practical way to confirm the C# table matches the generated mesh.
        foreach (Ring ring in rings)
        {
            Gizmos.color = ring.colour;
            DrawCircle(board.position, radius * ring.outer);
        }
    }

    void DrawCircle(Vector3 centre, float r)
    {
        const int steps = 64;
        Vector3 prev = centre + new Vector3(r, 0f, 0f);
        for (int i = 1; i <= steps; i++)
        {
            float a = i / (float)steps * Mathf.PI * 2f;
            Vector3 next = centre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
}
