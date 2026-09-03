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
/// COST. One follower update per racer per frame searches <see cref="searchWindow"/> segments
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
    [Tooltip("How many segments either side of its current one a racer will consider when it " +
             "updates. This is the value that makes a crossover safe, so do not raise it far.\n\n" +
             "It has to be big enough that a car cannot skip past it in one frame: at 60 m/s and " +
             "20 m between waypoints, a frame is 1 m, so 6 is enormously safe. Raise it only if " +
             "waypoints are very closely spaced, and lower it toward 3 if a track doubles back " +
             "so tightly that the wrong lane is within reach.")]
    [Range(2, 24)] public int searchWindow = 6;

    [Tooltip("Metres from the centreline beyond which a racer counts as off track. Half the " +
             "width plus some slack. Only used for reporting and for respawn decisions — " +
             "nothing forces a car back on.")]
    public float offTrackDistance = 22f;

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

    [Header("Read-only — watch these in play mode")]
    [SerializeField] int waypoints;
    [SerializeField] float lapLength;

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

        for (int i = 0; i < Segments; i++)
        {
            if (distance > starts[i] + lengths[i] && i < Segments - 1) continue;

            float t = lengths[i] < 1e-4f ? 0f : (distance - starts[i]) / lengths[i];
            return Vector3.Lerp(points[i], points[Wrap(i + 1)], Mathf.Clamp01(t));
        }

        return points[0];
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
    /// the segments within <see cref="searchWindow"/> of the one it is already on, so a car
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

        /// <summary>Metres from the centreline. Compare against <see cref="offTrackDistance"/>.</summary>
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

            int window = Mathf.Clamp(track.searchWindow, 1, segments);
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
        /// <paramref name="offset"/> comes back as the distance from the centreline, so the
        /// caller can prefer a line that stays on the road. Without it the best-progress answer
        /// is always the inside of the corner, which on a dam wall is the wall.
        /// </remarks>
        public float Gain(Vector3 at, out float offset)
        {
            offset = 0f;

            int segments = track.Segments;
            if (segments == 0) return 0f;

            int window = Mathf.Clamp(track.searchWindow, 1, segments);
            float best = float.PositiveInfinity;
            int bestOffset = 0;
            float bestAlong = 0f;

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
            }

            if (float.IsPositiveInfinity(best)) return 0f;

            offset = Mathf.Sqrt(best);

            int unwrapped = Segment + bestOffset;
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
        public bool OffTrack => Offset > track.offTrackDistance;
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
            if (above > 6f)
                problems.Add($"Waypoint {i} floats {above:0.0} m above the ground. Drop it onto " +
                             "the road — respawns put a car exactly here.");
            else if (above < -1.5f)
                problems.Add($"Waypoint {i} is {-above:0.0} m UNDER the ground. Respawns there " +
                             "put a car inside the scenery.");
        }

        return problems;
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
    }
}
