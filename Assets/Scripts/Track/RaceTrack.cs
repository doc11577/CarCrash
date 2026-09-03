using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The racing line, as an ordered ring of waypoints. Answers "how far round is this car".
/// </summary>
/// <remarks>
/// WAYPOINTS, NOT A SPLINE, and the reason is that everything downstream wants a DISTANCE
/// rather than a curve. Race position is <c>lap x lapLength + distance along lap</c>, the AI
/// wants a point some metres further on, and a respawn wants the nearest point on the line
/// facing the right way. All three are cheap against straight segments and all three would need
/// an arc-length reparameterisation against a spline — which is a lot of machinery for a corner
/// nobody can see is a polyline at 30 m/s.
///
/// The points are CHILD TRANSFORMS, in order. A hand-placed empty is tedious exactly once and
/// visible forever afterwards: it draws in the scene view, it can be dragged, and it survives in
/// the scene file as something a human can read. A path generated from the mesh is a research
/// project, and a path recorded at runtime cannot persist out of play mode without Editor code.
///
/// ⚠ NOTHING HERE USES "NEAREST WAYPOINT" AS A POSITION. That is the obvious implementation and
/// it is wrong on any track that passes near itself — the dam road doubling back, a hairpin, a
/// bridge over its own approach. One frame the nearest point is 40 m ahead, the next it is 900 m
/// behind, and the standings flicker. Every racer instead carries a <see cref="Follower"/>, which
/// only ever looks at segments NEAR the one it was on last. That single restriction gives correct
/// behaviour at a crossover, lap counting for free (the search wraps past the end, so it can see
/// it happen), and cheap anti-cheat: a car cannot gain half a lap by cutting the infield, because
/// the follower will not accept a segment it could not have reached.
///
/// COST. One follower update per racer per frame searches <see cref="searchDistance"/> metres of track
/// either way — 13 point-to-segment projections at the default, about a hundred for a full grid
/// of eight. That is nothing next to the four SphereCasts each of those cars already does every
/// physics step.
/// </remarks>
[DisallowMultipleComponent]
public class RaceTrack : MonoBehaviour
{
    /// <summary>The track for the current scene, or null if the map has none.</summary>
    /// <remarks>
    /// Same self-registration pattern as <c>RunScore.Instance</c> and <c>PlayerCar.Current</c>,
    /// for the same reason: a scene reference cannot be wired on a prefab that is spawned into
    /// the scene at runtime, which is what every car in this game now is.
    /// </remarks>
    public static RaceTrack Instance { get; private set; }

    [Header("Shape")]
    [Tooltip("Whether the last waypoint joins back to the first. Almost always ON — a race is " +
             "laps. Turn it off for a point-to-point stage, which makes the last waypoint the " +
             "finish and stops lap counting meaning anything.")]
    public bool loop = true;

    [Tooltip("Drivable width of the road at the centreline, in metres. Used for the gizmo " +
             "ribbon, for deciding a car is off track, and as the amount the AI is allowed to " +
             "move off the centreline to take a line or pass. It does NOT have to be exact — " +
             "it is a racing corridor, not a collision volume.")]
    public float width = 18f;

    [Header("Following")]
    [Tooltip("How far along the track, IN METRES, a racer will look either side of where it " +
             "already is. This is the value that makes a crossover safe: anything further round " +
             "the lap than this is invisible, so a car under a bridge cannot be mistaken for one " +
             "on it.\n\n" +
             "⚠ IT MUST COMFORTABLY EXCEED THE AI'S PROBE REACH, which is about 60 m at racing " +
             "speed. A probe aimed at track outside this window has its progress silently " +
             "CLAMPED to the window edge, so several probes score the same and the car stops " +
             "steering for the corner it can see.\n\n" +
             "In metres rather than in waypoints on purpose — how densely a track is clicked in " +
             "is a layout choice, and it must not quietly change how far the AI can see.")]
    public float searchDistance = 130f;

    /// <summary>
    /// <see cref="searchDistance"/> converted to segments, using the average waypoint spacing.
    /// </summary>
    /// <remarks>
    /// Recomputed in <see cref="Rebuild"/>, so adding waypoints to a corner cannot shorten the
    /// AI's reach. This was a real fault and not a theoretical one: at six segments and a track
    /// clicked in every 9 m, the window was 54 m against a probe reach of 60 — so on a corner
    /// every probe returned "the far end of the window", the scores tied, the straightness bias
    /// broke the tie, and the car drove straight on into the scenery. It looks exactly like an
    /// AI that cannot see corners, because that is what it is.
    /// </remarks>
    int window = 6;

    [Tooltip("Metres of slack OUTSIDE the road, past which a racer counts as off track. About a " +
             "car and a half.\n\n" +
             "Deliberately a margin rather than an absolute distance: it is measured from Width, " +
             "so narrowing the road narrows this with it. As an absolute number it is a second " +
             "thing to remember to change, and forgetting reads as the AI happily driving into " +
             "the scenery — the off-track test is what stops it treating a corner cut as a " +
             "shortcut.")]
    public float offTrackMargin = 5f;

    /// <summary>Metres from the centreline at which a racer is off the track.</summary>
    public float OffTrackDistance => width * 0.5f + offTrackMargin;

    [Header("Validation")]
    [Tooltip("Waypoints closer together than this are reported as a mistake — usually a " +
             "duplicate created by a stray Ctrl+D.")]
    public float minSpacing = 6f;

    [Tooltip("Gaps longer than this are reported. A long straight is legitimate, so this is a " +
             "prompt to look rather than an error: a huge gap is also what a MISSING waypoint " +
             "looks like, and the two are told apart by looking at the gizmo.")]
    public float maxSpacing = 140f;

    [Tooltip("Corners sharper than this are reported. A real corner is rarely past 90 degrees; " +
             "a 170-degree one is almost always two waypoints in the wrong order, which reads " +
             "on the gizmo as the line stabbing backwards and returning.")]
    public float maxCornerAngle = 110f;

    [Header("Gizmo")]
    [Tooltip("Draw the corridor as rungs that are RAYCAST against the ground, so a corridor " +
             "hanging off the edge of the road shows up in red. Only drawn while the track is " +
             "SELECTED, and never in play mode — it is a few hundred raycasts a repaint.\n\n" +
             "This is the check that answers 'is the width actually on the road', which a plain " +
             "line cannot: a centreline through a gap the car does not fit through looks perfect.")]
    public bool checkGroundInGizmo = true;

    [Tooltip("Metres between the cross rungs of the corridor gizmo. Smaller finds a narrower " +
             "pinch point and costs more raycasts.")]
    public float rungSpacing = 12f;

    [Tooltip("Metres a corridor EDGE may sit above or below the road under the centreline before " +
             "the rung is drawn red. Past this the edge is over a wall, a barrier top or thin " +
             "air rather than over the road.")]
    public float edgeTolerance = 4f;

    [Tooltip("Ceiling on gizmo rungs, so a long track cannot make the scene view crawl.")]
    public int maxRungs = 160;

    /// <summary>Metres a waypoint may float above the road before it is a problem.</summary>
    /// <remarks>Shared by the validator and the gizmo, so what is drawn is what is reported.</remarks>
    const float MaxFloat = 6f;

    /// <summary>Metres a waypoint may sit below the road before it is a problem.</summary>
    const float MaxSink = 1.5f;

    [Header("Read-only — watch these in play mode")]
    [SerializeField] int waypoints;
    [SerializeField] float lapLength;

    [Tooltip("Search Distance converted into waypoints, from the average spacing of THIS track. " +
             "If this reads 3 on a densely clicked track, Search Distance is too small and the " +
             "AI cannot see far enough round a corner to steer for it.")]
    [SerializeField] int windowSegments;

    /// <summary>Waypoint positions in world space, in order. Index 0 is the start/finish line.</summary>
    Vector3[] points = new Vector3[0];

    /// <summary>Distance along the lap at the START of segment i. One entry per segment.</summary>
    float[] starts = new float[0];

    /// <summary>Length of segment i.</summary>
    float[] lengths = new float[0];

    /// <summary>Number of waypoints. Zero means the track is unusable.</summary>
    public int Count => points.Length;

    /// <summary>Number of segments. Equal to <see cref="Count"/> when looping, one less if not.</summary>
    public int Segments => loop ? points.Length : Mathf.Max(0, points.Length - 1);

    /// <summary>Total distance round one lap, in metres.</summary>
    public float Length { get; private set; }

    /// <summary>World position of waypoint i.</summary>
    public Vector3 Point(int i) => points.Length == 0 ? transform.position : points[Wrap(i)];

    void Awake()
    {
        Instance = this;
        Rebuild();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Re-read the child transforms and recompute the distance table.
    /// </summary>
    /// <remarks>
    /// Called from Awake, from the editor while drawing gizmos, and by hand after moving a
    /// waypoint at runtime. The positions are CACHED rather than read from the transforms on
    /// every query, because a follower update touches a dozen of them per racer per frame and
    /// <c>Transform.position</c> is a native call each time. The cost of that cache is the rule
    /// that a waypoint moved in play mode does nothing until this is called.
    /// </remarks>
    public void Rebuild()
    {
        int n = transform.childCount;
        if (points.Length != n) points = new Vector3[n];

        for (int i = 0; i < n; i++) points[i] = transform.GetChild(i).position;

        int segments = Segments;
        if (starts.Length != segments)
        {
            starts = new float[segments];
            lengths = new float[segments];
        }

        float running = 0f;
        for (int i = 0; i < segments; i++)
        {
            starts[i] = running;
            lengths[i] = Vector3.Distance(points[i], points[Wrap(i + 1)]);
            running += lengths[i];
        }

        Length = running;
        waypoints = n;
        lapLength = running;

        // The search window in SEGMENTS, derived from how densely this track happens to be
        // clicked in. Never fewer than 3, so a track of four enormous segments still has
        // somewhere to look, and never more than the whole track.
        float average = segments > 0 ? running / segments : 1f;
        window = segments == 0
            ? 3
            : Mathf.Clamp(Mathf.CeilToInt(searchDistance / Mathf.Max(1f, average)), 3, segments);
        windowSegments = window;
    }

    int Wrap(int i)
    {
        int n = points.Length;
        if (n == 0) return 0;
        i %= n;
        return i < 0 ? i + n : i;
    }

    // ---- geometry ----------------------------------------------------------------------------

    /// <summary>
    /// Closest point on segment i to <paramref name="at"/>, as a fraction along that segment.
    /// </summary>
    public float Project(int segment, Vector3 at, out Vector3 onLine)
    {
        Vector3 a = points[Wrap(segment)];
        Vector3 b = points[Wrap(segment + 1)];
        Vector3 ab = b - a;

        float sqr = ab.sqrMagnitude;
        float t = sqr < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(at - a, ab) / sqr);

        onLine = a + ab * t;
        return t;
    }

    /// <summary>Distance along the lap of a point given as a segment and a fraction along it.</summary>
    public float DistanceAt(int segment, float t)
    {
        if (Segments == 0) return 0f;
        int s = Mathf.Clamp(segment, 0, Segments - 1);
        return starts[s] + lengths[s] * Mathf.Clamp01(t);
    }

    /// <summary>The point on the centreline this many metres round the lap.</summary>
    public Vector3 PointAtDistance(float distance)
    {
        if (Count == 0) return transform.position;
        if (Segments == 0) return points[0];

        distance = loop ? Mathf.Repeat(distance, Length) : Mathf.Clamp(distance, 0f, Length);

        // Binary search, not a walk. This is called a couple of dozen times per car per decision
        // once corner speeds are being worked out, and a linear scan makes that cost scale with
        // how densely the track was clicked in — which is exactly the coupling the metre-based
        // search window exists to remove.
        int lo = 0, hi = Segments - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (starts[mid] <= distance) lo = mid;
            else hi = mid - 1;
        }

        float f = lengths[lo] < 1e-4f ? 0f : (distance - starts[lo]) / lengths[lo];
        return Vector3.Lerp(points[lo], points[Wrap(lo + 1)], Mathf.Clamp01(f));
    }

    /// <summary>Mean distance between waypoints, in metres.</summary>
    public float AverageSpacing => Segments > 0 ? Length / Segments : 1f;

    /// <summary>
    /// Radius of the corner at a point on the track, in metres. Huge on a straight.
    /// </summary>
    /// <remarks>
    /// The circle through three samples either side (Menger curvature): <c>R = abc / 4A</c>.
    ///
    /// <paramref name="step"/> must be at least a waypoint spacing or the three samples can land
    /// on ONE straight segment, the area comes out zero, and a hairpin is reported as a straight.
    /// That failure is silent and total, so callers derive the step from
    /// <see cref="AverageSpacing"/> rather than picking a number.
    ///
    /// FLATTENED, because the corner a car has to slow for is the one it steers through. A crest
    /// is a tight radius in three dimensions and no steering input at all, and braking for it
    /// would make the AI crawl over every rise on the map.
    /// </remarks>
    public float RadiusAt(float distance, float step)
    {
        Vector3 a = PointAtDistance(distance - step);
        Vector3 b = PointAtDistance(distance);
        Vector3 c = PointAtDistance(distance + step);

        a.y = 0f;
        b.y = 0f;
        c.y = 0f;

        float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
        if (area < 1e-3f) return float.PositiveInfinity;

        return Vector3.Distance(a, b) * Vector3.Distance(b, c) * Vector3.Distance(c, a)
               / (4f * area);
    }

    /// <summary>
    /// How far the straight line from one point on the track to another strays from the track.
    /// </summary>
    /// <remarks>
    /// Compares the chord against the equal-arc-length point on the line rather than taking a
    /// true perpendicular distance. That OVERSTATES the deviation slightly, which is the right
    /// way to be wrong here: the caller uses it to decide how far ahead it is safe to aim, and
    /// erring toward a shorter aim costs a little smoothness while erring long puts a car
    /// through a barrier.
    /// </remarks>
    public float ChordDeviation(float from, float span, int samples)
    {
        if (Segments == 0 || span <= 0.01f) return 0f;

        Vector3 a = PointAtDistance(from);
        Vector3 b = PointAtDistance(from + span);
        float worst = 0f;

        for (int i = 1; i < samples; i++)
        {
            float f = i / (float)samples;

            Vector3 chord = Vector3.Lerp(a, b, f);
            Vector3 onLine = PointAtDistance(from + span * f);

            chord.y = 0f;
            onLine.y = 0f;

            float miss = Vector3.Distance(chord, onLine);
            if (miss > worst) worst = miss;
        }

        return worst;
    }

    /// <summary>
    /// The furthest a car may aim ahead while the straight line to that point stays on the road.
    /// </summary>
    /// <remarks>
    /// ⚠ THIS IS WHAT STOPS PURE PURSUIT CUTTING CORNERS, and without it the whole approach fails
    /// in the one place it matters. Aiming at a point ON the line is no defence at all if the
    /// line TO that point crosses the infield — which is exactly what a distant aim point does on
    /// a hairpin, and a hairpin is where cars were cutting.
    ///
    /// Shortened by repeated thirds rather than solved for: the relationship between aim distance
    /// and deviation depends on the shape of the corner, four steps cover an 8:1 range, and each
    /// step is a handful of lerps. Precision here buys nothing — the answer feeds a steering
    /// target that is slewed anyway.
    ///
    /// The effect is a car that looks a long way down a straight and only as far as it can see
    /// round a bend, which is also what a driver does.
    /// </remarks>
    public float SafeLookahead(float from, float desired, float allowed, float floor)
    {
        float distance = Mathf.Max(floor, desired);

        for (int i = 0; i < 4 && distance > floor; i++)
        {
            if (ChordDeviation(from, distance, 4) <= allowed) break;
            distance = Mathf.Max(floor, distance * 0.7f);
        }

        return distance;
    }

    /// <summary>
    /// The fastest a car may be going HERE and still make every corner in the next
    /// <paramref name="scan"/> metres.
    /// </summary>
    /// <remarks>
    /// Two bits of physics, and the second is what makes it anticipatory rather than reactive:
    ///
    ///   * A corner of radius R can be taken at <c>sqrt(lateral x R)</c>.
    ///   * To be at that speed after braking over d metres, you may be at
    ///     <c>sqrt(v_corner^2 + 2 x brake x d)</c> now.
    ///
    /// Taking the minimum over every sample ahead means the car brakes for the corner it can see
    /// coming rather than for the one it is already in — which is the whole difference between an
    /// AI that looks like it is driving and one that looks like it is reacting.
    ///
    /// Returns infinity on a track with no corners in range; callers clamp against their own top
    /// speed.
    /// </remarks>
    public float SpeedLimit(float from, float scan, float lateral, float brake)
    {
        if (Segments == 0) return float.PositiveInfinity;

        // TWO different steps, and conflating them is a real fault rather than a tidiness point.
        // The curvature step must be at least a waypoint spacing or three samples land on one
        // straight segment and a hairpin reads as a straight. The SCAN step must be fine or a
        // sparsely clicked track skips over the corner entirely between samples and the car
        // brakes late. On a track clicked in every 30 m those want 45 m and 15 m respectively.
        float step = Mathf.Max(10f, AverageSpacing * 1.5f);
        float scanStep = Mathf.Min(step, 15f);
        float limit = float.PositiveInfinity;

        for (float d = 0f; d <= scan; d += scanStep)
        {
            float radius = Mathf.Min(RadiusAt(from + d, step), 2000f);
            float corner = Mathf.Sqrt(Mathf.Max(1f, lateral * radius));
            float allowed = Mathf.Sqrt(corner * corner + 2f * Mathf.Max(0.5f, brake) * d);

            if (allowed < limit) limit = allowed;
        }

        return limit;
    }

    /// <summary>Direction the track runs in, this many metres round the lap.</summary>
    public Vector3 ForwardAtDistance(float distance)
    {
        if (Segments == 0) return transform.forward;

        Vector3 here = PointAtDistance(distance);
        Vector3 ahead = PointAtDistance(distance + Mathf.Min(6f, Length * 0.5f));
        Vector3 dir = ahead - here;

        return dir.sqrMagnitude < 1e-6f ? transform.forward : dir.normalized;
    }

    /// <summary>
    /// Where to put a car that has to be put back on the track, and which way to point it.
    /// </summary>
    /// <remarks>
    /// Deliberately takes a DISTANCE rather than a position: the caller already has a follower,
    /// and the follower's answer is the trustworthy one. Handing this a raw world position would
    /// reintroduce the nearest-waypoint ambiguity at exactly the moment it matters most — a car
    /// that has just fallen off the dam is, in a straight line, extremely close to the road it
    /// fell from and also to the road below it.
    ///
    /// <paramref name="back"/> puts the car a little BEFORE where it left, so a respawn cannot
    /// be used to skip the corner that was being crashed at.
    /// </remarks>
    public void RespawnPose(float distance, float back, float lift,
                            out Vector3 position, out Quaternion rotation)
    {
        float at = distance - Mathf.Max(0f, back);
        position = PointAtDistance(at) + Vector3.up * lift;
        Vector3 forward = ForwardAtDistance(at);

        rotation = forward.sqrMagnitude < 1e-6f
            ? Quaternion.identity
            : Quaternion.LookRotation(forward, Vector3.up);
    }

    // ---- following ---------------------------------------------------------------------------

    /// <summary>
    /// One racer's place on the track. Every car that is being scored or positioned owns one.
    /// </summary>
    /// <remarks>
    /// This is the class that makes the whole thing work, and the important part of it is what
    /// it REFUSES to do: it never searches the whole track. <see cref="Advance"/> looks only at
    /// the segments within <see cref="searchDistance"/> metres of the one it is already on, so a car
    /// sitting on a bridge cannot be assigned to the road underneath, and a car that is
    /// teleported keeps its old place until something calls <see cref="Snap"/>.
    ///
    /// Lap counting falls straight out of that. The window search is done on an UNWRAPPED index,
    /// so when a car moves from the last segment to the first, the search sees index n rather
    /// than index 0 and the wrap is a fact rather than an inference. No trigger volumes, no
    /// "was the car near the start line" test, nothing to miss at speed.
    /// </remarks>
    public class Follower
    {
        readonly RaceTrack track;

        /// <summary>Segment the racer is on.</summary>
        public int Segment { get; private set; }

        /// <summary>Fraction along that segment, 0-1.</summary>
        public float Along { get; private set; }

        /// <summary>Completed laps since this follower was created or snapped.</summary>
        public int Lap { get; private set; }

        /// <summary>Metres from the centreline. Compare against <see cref="OffTrackDistance"/>.</summary>
        public float Offset { get; private set; }

        /// <summary>Closest point on the centreline to the racer.</summary>
        public Vector3 OnLine { get; private set; }

        /// <summary>
        /// Total distance travelled round the track, laps included. THE race position number.
        /// </summary>
        public float Distance => Lap * track.Length + track.DistanceAt(Segment, Along);

        /// <summary>Distance round the current lap only, 0 to the lap length.</summary>
        public float LapDistance => track.DistanceAt(Segment, Along);

        /// <summary>Fraction of the current lap completed, 0-1.</summary>
        public float LapFraction => track.Length < 1e-3f ? 0f : LapDistance / track.Length;

        /// <summary>The track this follower belongs to.</summary>
        public RaceTrack Track => track;

        public Follower(RaceTrack track, Vector3 at)
        {
            this.track = track;
            Snap(at);
        }

        /// <summary>
        /// Re-acquire from scratch, searching the WHOLE track. Laps are reset to zero.
        /// </summary>
        /// <remarks>
        /// The one place a full search is correct: at spawn, and after a teleport, there is no
        /// previous position to be near. Anywhere else this is the bug the follower exists to
        /// prevent, so call it deliberately and rarely.
        /// </remarks>
        public void Snap(Vector3 at)
        {
            int segments = track.Segments;
            if (segments == 0) return;

            float best = float.PositiveInfinity;
            int bestSegment = 0;
            float bestAlong = 0f;
            Vector3 bestPoint = at;

            for (int i = 0; i < segments; i++)
            {
                float t = track.Project(i, at, out Vector3 onLine);
                float sqr = (at - onLine).sqrMagnitude;
                if (sqr >= best) continue;

                best = sqr;
                bestSegment = i;
                bestAlong = t;
                bestPoint = onLine;
            }

            Segment = bestSegment;
            Along = bestAlong;
            OnLine = bestPoint;
            Offset = Mathf.Sqrt(best);
            Lap = 0;
        }

        /// <summary>
        /// Put the follower back on the track at a known DISTANCE, keeping the lap count.
        /// </summary>
        /// <remarks>
        /// For a checkpoint respawn, where the answer is known exactly and re-deriving it from
        /// the car's position would be a full search that could land anywhere.
        /// </remarks>
        public void SetDistance(float distance)
        {
            int segments = track.Segments;
            if (segments == 0) return;

            float lap = track.Length < 1e-3f ? 0f : Mathf.Floor(distance / track.Length);
            float along = distance - lap * track.Length;

            for (int i = 0; i < segments; i++)
            {
                if (along > track.starts[i] + track.lengths[i] && i < segments - 1) continue;

                Segment = i;
                Along = track.lengths[i] < 1e-4f
                    ? 0f
                    : Mathf.Clamp01((along - track.starts[i]) / track.lengths[i]);
                break;
            }

            Lap = track.loop ? Mathf.RoundToInt(lap) : 0;
            OnLine = track.PointAtDistance(along);
            Offset = 0f;
        }

        /// <summary>Update from the racer's current position. Call once a frame.</summary>
        public void Advance(Vector3 at)
        {
            int segments = track.Segments;
            if (segments == 0) return;

            int window = Mathf.Min(track.window, segments);
            float best = float.PositiveInfinity;
            int bestOffset = 0;
            float bestAlong = Along;
            Vector3 bestPoint = OnLine;

            for (int d = -window; d <= window; d++)
            {
                int raw = Segment + d;

                // A point-to-point track has no wrap, so looking past either end is meaningless
                // rather than merely unlikely.
                if (!track.loop && (raw < 0 || raw >= segments)) continue;

                float t = track.Project(raw, at, out Vector3 onLine);
                float sqr = (at - onLine).sqrMagnitude;

                // Strictly closer, so a tie keeps the segment the racer is already on. Ties are
                // common — a car sitting exactly on a waypoint is equidistant from the two
                // segments meeting there — and flipping between them every frame would make the
                // distance readout jitter across a waypoint boundary.
                if (sqr >= best) continue;

                best = sqr;
                bestOffset = d;
                bestAlong = t;
                bestPoint = onLine;
            }

            int unwrapped = Segment + bestOffset;

            // The wrap IS the lap. Because the search is done on an unwrapped index, crossing
            // the start line shows up here as an index past the end rather than as a jump back
            // to zero that would have to be guessed at.
            if (track.loop)
            {
                if (unwrapped >= segments) Lap++;
                else if (unwrapped < 0) Lap--;

                Segment = track.Wrap(unwrapped);
            }
            else
            {
                Segment = Mathf.Clamp(unwrapped, 0, segments - 1);
            }

            Along = bestAlong;
            OnLine = bestPoint;
            Offset = Mathf.Sqrt(best);
        }

        /// <summary>
        /// A point this many metres further round the track. What a race AI steers at.
        /// </summary>
        public Vector3 Aim(float lookAhead) => track.PointAtDistance(LapDistance + lookAhead);

        /// <summary>
        /// How much track a racer would GAIN by being at <paramref name="at"/>, in metres.
        /// </summary>
        /// <remarks>
        /// The race AI's whole steering rule, and the direct replacement for "how far does the
        /// ground drop that way". Positive is forward progress, negative is backwards.
        ///
        /// Searched in the same window as <see cref="Advance"/>, and for the same reason: a probe
        /// aimed sideways at a piece of track the car passed a minute ago must not score as most
        /// of a lap gained. Anything outside the window is reported as no gain at all, which
        /// makes a probe pointing at unreachable track the worst option rather than the best.
        ///
        /// <paramref name="offset"/> comes back SIGNED — positive is right of the direction of
        /// travel — because the callers that want it are choosing sides. "That car is 4 m from
        /// the centreline" does not say whether to pass it on the left or the right; "that car is
        /// at +4" does.
        /// </remarks>
        public float Gain(Vector3 at, out float offset)
        {
            offset = 0f;

            int segments = track.Segments;
            if (segments == 0) return 0f;

            int window = Mathf.Min(track.window, segments);
            float best = float.PositiveInfinity;
            int bestOffset = 0;
            float bestAlong = 0f;
            Vector3 bestPoint = at;

            for (int d = -window; d <= window; d++)
            {
                int raw = Segment + d;
                if (!track.loop && (raw < 0 || raw >= segments)) continue;

                float t = track.Project(raw, at, out Vector3 onLine);
                float sqr = (at - onLine).sqrMagnitude;
                if (sqr >= best) continue;

                best = sqr;
                bestOffset = d;
                bestAlong = t;
                bestPoint = onLine;
            }

            if (float.IsPositiveInfinity(best)) return 0f;

            int unwrapped = Segment + bestOffset;

            // Signed against the track's own right-hand side at the projected point, flattened —
            // the side of the road something is on is a question about the map, not about how
            // steep the road happens to be there.
            Vector3 run = track.Point(unwrapped + 1) - track.Point(unwrapped);
            run.y = 0f;

            Vector3 sideways = at - bestPoint;
            sideways.y = 0f;

            offset = run.sqrMagnitude < 1e-6f
                ? Mathf.Sqrt(best)
                : Vector3.Dot(sideways, Vector3.Cross(Vector3.up, run.normalized));
            float laps = 0f;
            if (track.loop)
            {
                if (unwrapped >= segments) laps = 1f;
                else if (unwrapped < 0) laps = -1f;
            }

            return track.DistanceAt(track.Wrap(unwrapped), bestAlong)
                   + laps * track.Length
                   - LapDistance;
        }

        /// <summary>True while the racer is further from the centreline than the track is wide.</summary>
        public bool OffTrack => Offset > track.OffTrackDistance;
    }

    /// <summary>Make a follower for a racer standing at <paramref name="at"/>.</summary>
    public Follower Follow(Vector3 at) => new Follower(this, at);

    // ---- validation --------------------------------------------------------------------------

    /// <summary>
    /// Everything wrong with the track as laid out, in plain sentences. Empty means it is fine.
    /// </summary>
    /// <remarks>
    /// A waypoint chain fails SILENTLY and in ways that look like AI bugs — a duplicated point
    /// makes a zero-length segment, two points in the wrong order make the AI drive at a wall
    /// and then turn round, a point floating above the road makes cars aim at the sky. All three
    /// are obvious the moment they are named and invisible otherwise, which is exactly what a
    /// validator is for. Called from the Inspector button and logged at Start.
    /// </remarks>
    public List<string> Validate()
    {
        List<string> problems = new List<string>();
        Rebuild();

        if (Count < 3)
        {
            problems.Add($"Only {Count} waypoints. A track needs at least 3, and 20-30 is the " +
                         "sort of number a real map wants.");
            return problems;
        }

        for (int i = 0; i < Segments; i++)
        {
            if (lengths[i] < minSpacing)
                problems.Add($"Waypoints {i} and {Wrap(i + 1)} are {lengths[i]:0.0} m apart, " +
                             $"under the {minSpacing:0} m minimum. Usually a duplicate — delete one.");

            if (lengths[i] > maxSpacing)
                problems.Add($"Waypoints {i} and {Wrap(i + 1)} are {lengths[i]:0} m apart, over " +
                             $"the {maxSpacing:0} m limit. Fine on a long straight; look at the " +
                             "gizmo, because a missing waypoint looks identical from here.");
        }

        int corners = loop ? Count : Count - 2;
        for (int i = 0; i < corners; i++)
        {
            Vector3 into = points[Wrap(i + 1)] - points[Wrap(i)];
            Vector3 outOf = points[Wrap(i + 2)] - points[Wrap(i + 1)];
            if (into.sqrMagnitude < 1e-4f || outOf.sqrMagnitude < 1e-4f) continue;

            float angle = Vector3.Angle(into, outOf);
            if (angle > maxCornerAngle)
                problems.Add($"Waypoint {Wrap(i + 1)} turns {angle:0} degrees, past the " +
                             $"{maxCornerAngle:0} degree limit. Almost always two waypoints in " +
                             "the wrong order — the line stabs backwards on the gizmo.");
        }

        for (int i = 0; i < Count; i++)
        {
            // Cast from well above so a waypoint left underground is still reported, rather
            // than the ray starting inside the road and finding nothing.
            Vector3 from = points[i] + Vector3.up * 60f;
            if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, 200f, ~0,
                                 QueryTriggerInteraction.Ignore))
            {
                problems.Add($"Waypoint {i} has no ground under it at all. A car sent there " +
                             "drives off the map.");
                continue;
            }

            float above = points[i].y - hit.point.y;
            if (above > MaxFloat)
                problems.Add($"Waypoint {i} floats {above:0.0} m above the ground. Drop it onto " +
                             "the road — respawns put a car exactly here.");
            else if (above < -MaxSink)
                problems.Add($"Waypoint {i} is {-above:0.0} m UNDER the ground. Respawns there " +
                             "put a car inside the scenery.");

            // The corridor, not just the line. `width` is what the AI is allowed to move across
            // to take a line or a pass, so a waypoint whose corridor hangs over a drop is an
            // instruction to drive off it — and that is invisible on a centreline.
            Vector3 along = points[Wrap(i + 1)] - points[i];
            if (along.sqrMagnitude < 1e-4f) continue;

            Vector3 side = Vector3.Cross(Vector3.up, along.normalized) * (width * 0.5f);
            EdgeProblem(problems, i, "left", points[i] + side, hit.point.y);
            EdgeProblem(problems, i, "right", points[i] - side, hit.point.y);
        }

        return problems;
    }

    void EdgeProblem(List<string> problems, int index, string which, Vector3 at, float roadY)
    {
        if (!Ground(at, out float y))
        {
            problems.Add($"Waypoint {index}: the {which} edge of the corridor has no ground " +
                         $"under it. Either the track is off the road here, or Width " +
                         $"({width:0} m) is wider than the road is.");
            return;
        }

        float step = y - roadY;
        if (Mathf.Abs(step) > edgeTolerance)
            problems.Add($"Waypoint {index}: the {which} edge of the corridor is {step:0.0} m " +
                         $"{(step > 0f ? "above" : "below")} the road under the centreline — a " +
                         $"wall, a barrier top or a drop. Move the waypoint over, or narrow " +
                         $"Width from {width:0} m.");
    }

    void Start()
    {
        List<string> problems = Validate();
        if (problems.Count == 0) return;

        // Loud, because every one of these reads as an AI bug rather than a track bug, and
        // there is no console on a Chromebook to find it in later.
        Debug.LogWarning($"RaceTrack '{name}' has {problems.Count} problem(s):\n  " +
                         string.Join("\n  ", problems), this);
    }

    // ---- gizmos ------------------------------------------------------------------------------

    void OnDrawGizmos()
    {
        // Always rebuilt while drawing, so dragging a waypoint updates the line as it moves.
        // Cheap: the editor is not the Chromebook.
        Rebuild();
        if (Count < 2) return;

        int segments = Segments;
        for (int i = 0; i < segments; i++)
        {
            Vector3 a = points[i];
            Vector3 b = points[Wrap(i + 1)];
            Vector3 span = b - a;
            if (span.sqrMagnitude < 1e-6f) continue;

            // The ribbon shows the corridor width, which is the thing that is hard to judge from
            // a line: a centreline through a gap the car cannot fit through looks perfect.
            Vector3 side = Vector3.Cross(Vector3.up, span.normalized) * (width * 0.5f);

            Gizmos.color = new Color(1f, 0.78f, 0.15f, 0.85f);
            Gizmos.DrawLine(a, b);

            Gizmos.color = new Color(1f, 0.78f, 0.15f, 0.28f);
            Gizmos.DrawLine(a + side, b + side);
            Gizmos.DrawLine(a - side, b - side);
            Gizmos.DrawLine(a - side, a + side);

            // Direction, drawn as a chevron rather than a line, because the single most common
            // waypoint mistake is a chain running the wrong way round.
            Vector3 mid = (a + b) * 0.5f;
            Vector3 dir = span.normalized;
            Vector3 out3 = side.normalized * 1.6f;
            Gizmos.color = new Color(0.35f, 0.85f, 1f, 0.9f);
            Gizmos.DrawLine(mid, mid - dir * 3f + out3);
            Gizmos.DrawLine(mid, mid - dir * 3f - out3);
        }

        // The start/finish line, drawn differently because it is the one waypoint with a meaning
        // beyond its position.
        Vector3 start = points[0];
        Vector3 across = Vector3.Cross(Vector3.up, ForwardAtDistance(0f)) * (width * 0.5f);
        Gizmos.color = Color.white;
        Gizmos.DrawLine(start + across, start - across);
        Gizmos.DrawLine(start + across + Vector3.up * 4f, start - across + Vector3.up * 4f);
        Gizmos.DrawLine(start + across, start + across + Vector3.up * 4f);
        Gizmos.DrawLine(start - across, start - across + Vector3.up * 4f);
    }

    void OnDrawGizmosSelected()
    {
        if (Count == 0) return;

        Gizmos.color = new Color(1f, 0.78f, 0.15f, 1f);
        for (int i = 0; i < Count; i++) Gizmos.DrawSphere(points[i], 1.2f);

        if (!checkGroundInGizmo) return;

        // Never in play mode. This is a few hundred raycasts per repaint, and the whole point of
        // it is laying the track out — by the time cars are driving it, the answer is on screen
        // in the form of cars falling off the road.
        if (Application.isPlaying) return;

        DrawGroundChecks();
    }

    /// <summary>
    /// Raycasts the corridor against the ground and draws what is NOT over road in red.
    /// </summary>
    /// <remarks>
    /// The reason this exists rather than a plain ribbon: a centreline drawn through a gap the
    /// car does not fit through looks perfect, and so does a corridor whose outer half is hanging
    /// over the side of a dam. A line in space cannot show either. Three casts per rung — the
    /// centre and both edges — turn "the width is 18" into something visible.
    ///
    /// The centre's ground height is the reference rather than the waypoint's own Y, because the
    /// question is whether the EDGES are on the same road as the middle, not whether they are at
    /// the height the waypoint happens to have been dropped at.
    /// </remarks>
    void DrawGroundChecks()
    {
        Color good = new Color(0.35f, 0.9f, 0.5f, 0.85f);
        Color bad = new Color(1f, 0.25f, 0.2f, 1f);

        int drawn = 0;
        float spacing = Mathf.Max(2f, rungSpacing);

        for (int i = 0; i < Segments && drawn < maxRungs; i++)
        {
            Vector3 a = points[i];
            Vector3 b = points[Wrap(i + 1)];
            Vector3 span = b - a;
            if (span.sqrMagnitude < 1e-6f) continue;

            Vector3 side = Vector3.Cross(Vector3.up, span.normalized) * (width * 0.5f);
            int rungs = Mathf.Max(1, Mathf.CeilToInt(span.magnitude / spacing));

            for (int k = 0; k < rungs && drawn < maxRungs; k++, drawn++)
            {
                Vector3 centre = Vector3.Lerp(a, b, k / (float)rungs);

                bool centreOk = Ground(centre, out float centreY);
                bool leftOk = Ground(centre + side, out float leftY);
                bool rightOk = Ground(centre - side, out float rightY);

                bool leftOnRoad = leftOk && Mathf.Abs(leftY - centreY) <= edgeTolerance;
                bool rightOnRoad = rightOk && Mathf.Abs(rightY - centreY) <= edgeTolerance;

                Gizmos.color = centreOk && leftOnRoad && rightOnRoad ? good : bad;
                Gizmos.DrawLine(centre + side, centre - side);

                // A stub standing up at whichever edge is off, so it says WHICH side is wrong
                // rather than only that something is. A rung red at both ends is a corridor too
                // wide for the road; red at one end is a centreline pushed off to that side.
                if (!leftOnRoad) Gizmos.DrawLine(centre + side, centre + side + Vector3.up * 4f);
                if (!rightOnRoad) Gizmos.DrawLine(centre - side, centre - side + Vector3.up * 4f);
            }
        }

        // A dropper under every waypoint, using the validator's own tolerances, so the thing the
        // Validate button reports is also the thing the scene view shows.
        for (int i = 0; i < Count; i++)
        {
            if (!Ground(points[i], out float y))
            {
                Gizmos.color = bad;
                Gizmos.DrawLine(points[i], points[i] - Vector3.up * 12f);
                continue;
            }

            float above = points[i].y - y;
            Gizmos.color = above > MaxFloat || above < -MaxSink ? bad : good;

            Vector3 onGround = new Vector3(points[i].x, y, points[i].z);
            Gizmos.DrawLine(points[i], onGround);
            Gizmos.DrawLine(onGround + Vector3.right * 1.2f, onGround - Vector3.right * 1.2f);
            Gizmos.DrawLine(onGround + Vector3.forward * 1.2f, onGround - Vector3.forward * 1.2f);
        }
    }

    /// <summary>Height of the ground under a point, cast from well above it.</summary>
    static bool Ground(Vector3 at, out float y)
    {
        // From 60 m up, so a point left UNDER the road is still found rather than the ray
        // starting inside the scenery and reporting nothing.
        if (Physics.Raycast(at + Vector3.up * 60f, Vector3.down, out RaycastHit hit, 200f, ~0,
                            QueryTriggerInteraction.Ignore))
        {
            y = hit.point.y;
            return true;
        }

        y = at.y;
        return false;
    }
}
