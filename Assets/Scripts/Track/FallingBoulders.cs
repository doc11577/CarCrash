using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Boulders that come down the valley sides and roll across the track.
/// </summary>
/// <remarks>
/// **Spawned relative to the PLAYER, not from hand-placed points.** A 1,800 m course would need
/// dozens of markers, they would have to be re-placed for every map, and every one of them would
/// be a thing to get wrong in a scene file. Spawning around the car works on any course, costs
/// nothing while nobody is there, and keeps every live boulder somewhere that can matter.
///
/// **They are dropped ahead, up the valley side, and AIMED AT WHERE THE CAR WILL BE.** Aiming at
/// where it is now always lands behind a moving target, which is why an unaimed rockfall reads as
/// scenery no matter how much of it there is. `AimedLaunch` leads the car with a two-pass
/// fixed-point intercept and then deliberately misses by `aimSpread`.
///
/// **Big and few beats small and many**, both for how it reads and for what it costs — a 10 m
/// boulder blocking half the corridor is a bigger event than six 3 m ones, and it is six fewer
/// rigidbodies.
///
/// **The rim is found by raycasting, not assumed.** A cast straight down from high above the
/// chosen point finds whatever the valley wall is at that station, so this needs to know nothing
/// about how the course was generated and does not go stale when the generator changes. It also
/// self-corrects when a bearing points off the edge of the map: no ground, no boulder.
///
/// **A SECOND cast decides whether that point is wall or road**, and it is the reason boulders
/// stopped appearing on the track. See `TrySpawn` — comparing against the car's own height cannot
/// work on a descending course.
///
/// **Meshes are generated at Awake, not imported.** A boulder is a subdivided icosahedron with
/// its vertices pushed about by a position hash — 80 triangles, flat shaded, which is exactly the
/// faceted silhouette that makes a rock read as a rock rather than a dune. Generating them costs
/// a few milliseconds once and **adds nothing to the download**, which matters more than usual
/// right now: `carcrash.data.unityweb` is 14.83 MB against a hard 20 MB cap.
///
/// **Convex hulls are cooked ONCE at Awake, for a handful of variants, then pooled.** Cooking a
/// convex MeshCollider at runtime costs a frame hitch — that is why detached panels get a
/// BoxCollider instead. Here the cost is paid at load for `variants` meshes and never again,
/// because instances are recycled rather than created.
///
/// **Its own pool, deliberately not DebrisPool.** Sharing one would mean a big crash evicts every
/// boulder, or a rockfall evicts the panels the player just knocked off. Two hazards with
/// different lifetimes competing for one cap is a bug waiting to be misdiagnosed as either
/// system misbehaving. The rigidbody BUDGET is still shared, so keep `maxLive` modest and count
/// it against the four cars and the debris.
///
/// A boulder is on the Default layer so `CarDamage` treats it as damaging and the wheels' ground
/// SphereCast sees it — you can drive over a settled one.
/// </remarks>
[DisallowMultipleComponent]
public class FallingBoulders : MonoBehaviour
{
    [Header("Spawning")]
    [Tooltip("Seconds between attempts, randomised +/- half. Set to 0 to switch the hazard off.")]
    public float interval = 0.9f;

    [Tooltip("How many boulders may be alive at once. They are rigidbodies, so this counts " +
             "against the same budget as the cars and the panel debris.")]
    [Range(0, 60)] public int maxLive = 16;

    [Tooltip("Metres AHEAD along the course. They are dropped well down the track so they are " +
             "already rolling by the time the player reaches them — the player should be driving " +
             "into a rockfall in progress, not watching one start beside them.")]
    public Vector2 aheadRange = new Vector2(65f, 175f);

    [Tooltip("Metres to the SIDE of the track line, randomised and flipped to either side. This " +
             "is how far up the valley wall they start.")]
    public Vector2 sideRange = new Vector2(32f, 95f);

    [Tooltip("How far ABOVE the track a spawn point must be to count as valley wall. This is " +
             "what stops boulders appearing on the road — see TrySpawn. Lower it if too few " +
             "spawn on a shallow map; raise it if any still land on the drivable surface.")]
    public float minRise = 10f;

    [Tooltip("Height above the found surface to release from.")]
    public float dropHeight = 10f;

    [Tooltip("How high above the spawn point to start the search cast.")]
    public float probeHeight = 250f;

    [Tooltip("What counts as the mountainside. Default only — the same mask everything else " +
             "in this project treats as ground.")]
    public LayerMask groundMask = 1;

    [Header("The rock")]
    [Tooltip("Radius range, in metres. The car is 4.2 m long; the corridor is 26 m wide. At 5.2 " +
             "a boulder is 10.4 m across and blocks 40% of the road, which is the point — one of " +
             "those is a bigger event than six small ones AND costs five fewer rigidbodies.")]
    public Vector2 radiusRange = new Vector2(2.4f, 5.2f);

    [Tooltip("Mass of a 1 m radius boulder, in kg. Scaled by radius SQUARED, not cubed — see " +
             "MassFor for why that is deliberate rather than a mistake.")]
    public float massAtOneMetre = 1100f;

    [Tooltip("Hard ceiling on boulder mass. This is a physics-stability limit, not a taste one: " +
             "PhysX solves a contact badly once the mass ratio passes roughly 10:1, and the car " +
             "is 1,200 kg.")]
    public float maxMass = 12000f;

    [Tooltip("How many different boulder meshes to build. Each costs one convex hull cook at " +
             "load. Four is plenty — they tumble, so repeats are not noticeable.")]
    [Range(1, 8)] public int variants = 4;

    [Tooltip("How lumpy. 0 is a smooth ball, 0.35 is a proper rock. Above ~0.45 the hull starts " +
             "to disagree visibly with the mesh you can see.")]
    [Range(0f, 0.45f)] public float lumpiness = 0.3f;

    [Header("How it comes down")]
    [Tooltip("Clamp on the launch speed, in m/s. The speed is SOLVED for — distance over fall " +
             "time — so this is the band it is allowed to land in, not a value picked at random. " +
             "A boulder needing more than the high end lands short; one needing less than the " +
             "low end overshoots. Widen it if too many miss.")]
    public Vector2 launchSpeed = new Vector2(20f, 40f);

    [Tooltip("How hard they lead the player. 0 aims where the car is NOW, which always lands " +
             "behind a moving target. 1 aims where it will be when the boulder arrives. Above 1 " +
             "over-leads and lands in front, which is worth having some of — it is the one that " +
             "makes you brake.")]
    [Range(0f, 1.5f)] public float lead = 1f;

    [Tooltip("Random miss applied to the aim point, in metres. THIS IS NOT OPTIONAL: at 0 every " +
             "boulder is a homing missile and the hazard stops being a hazard and becomes a " +
             "scripted death. The corridor is 26 m wide, so a spread near that means most miss " +
             "and some connect.")]
    public float aimSpread = 16f;

    [Tooltip("Random initial spin, in degrees per second.")]
    public float spin = 320f;

    [Header("Clean-up")]
    [Tooltip("Seconds before a boulder is recycled no matter what it is doing.")]
    public float lifetime = 26f;

    [Tooltip("Below this speed for restDelay seconds it is considered settled and recycled.")]
    public float restSpeed = 0.6f;

    [Tooltip("Seconds a boulder must stay slow before being recycled. Long enough that one " +
             "resting in the road is a real obstacle for a while.")]
    public float restDelay = 10f;

    [Tooltip("Recycle a boulder this far behind the car. It cannot matter any more and it is " +
             "still a rigidbody.")]
    public float behindDistance = 140f;

    [Header("Read-only")]
    [SerializeField] int live;
    [SerializeField] int spawnedTotal;
    [SerializeField] string lastResult = "(nothing yet)";

    /// <summary>
    /// Live boulder count, for the on-screen dev readout.
    /// </summary>
    /// <remarks>
    /// Static because the Inspector does not exist on a Chromebook, and this is the one number
    /// that decides whether the hazard is affordable there. A serialized field can only be read
    /// at a desk on a machine that is not the one the budget is about.
    /// </remarks>
    public static int Live { get; private set; }

    void OnDisable()
    {
        Live = 0;
    }

    class Rock
    {
        public GameObject go;
        public Rigidbody body;
        public MeshFilter filter;
        public MeshCollider collider;
        public float expiresAt;
        public float slowSince;
    }

    readonly List<Rock> pool = new List<Rock>();
    CarController trackedCar;
    Rigidbody trackedBody;
    Mesh[] meshes;
    float nextSpawnAt;

    void Awake()
    {
        meshes = new Mesh[Mathf.Max(1, variants)];
        for (int i = 0; i < meshes.Length; i++)
            meshes[i] = BuildBoulder(i * 7919 + 13, lumpiness);

        if (rockMaterial == null) rockMaterial = FindCourseRockMaterial();
    }

    /// <summary>
    /// Borrow the map's own rock material, so a boulder matches the wall it came off without
    /// anything being wired. Same trick <see cref="CarInteriorProps"/> uses for the interior.
    /// </summary>
    /// <remarks>
    /// Matches on the object name because the course generator names its meshes `CourseRock*`,
    /// and this is a cosmetic fallback rather than a load-bearing lookup — the cost of matching
    /// nothing is a magenta boulder, which is loud enough to notice immediately and fixable by
    /// dragging a material into the field. Every name-matching bug this project has had was in
    /// code where a wrong match was silent; this one cannot be.
    /// </remarks>
    Material FindCourseRockMaterial()
    {
        foreach (MeshRenderer renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (!renderer.gameObject.name.StartsWith("CourseRock")) continue;
            if (renderer.sharedMaterial == null) continue;
            return renderer.sharedMaterial;
        }

        Debug.LogWarning(
            "FallingBoulders: no CourseRock* renderer found to borrow a material from, so the " +
            "boulders will draw magenta. Drag the map's rock material into Rock Material.", this);
        return null;
    }

    void OnDestroy()
    {
        if (meshes == null) return;
        foreach (Mesh m in meshes)
            if (m != null) Destroy(m);
    }

    void Update()
    {
        Recycle();

        if (interval <= 0f) return;

        CarController car = PlayerCar.Current != null ? PlayerCar.Current.Controller : null;
        if (car == null) return;

        // Cached rather than fetched per spawn, and refreshed when the car changes — the garage
        // spawns a different prefab each run, so holding one forever would leave the aim reading
        // the velocity of a destroyed car, which is zero and silently disables the lead.
        if (car != trackedCar)
        {
            trackedCar = car;
            trackedBody = car.GetComponent<Rigidbody>();
        }

        if (Time.time < nextSpawnAt) return;
        nextSpawnAt = Time.time + interval * Random.Range(0.5f, 1.5f);

        if (live >= maxLive)
        {
            lastResult = "at the cap";
            return;
        }

        TrySpawn(car);
    }

    void TrySpawn(CarController controller)
    {
        Transform car = controller.transform;

        Vector3 forward = car.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) return;
        forward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, forward);

        // Split the offset into ALONG the course and ACROSS it, because the two are tested
        // differently: the along part just places it down the track, the across part is what has
        // to land on the wall rather than the road.
        float ahead = Random.Range(aheadRange.x, aheadRange.y);
        float side = (Random.value < 0.5f ? -1f : 1f) * Random.Range(sideRange.x, sideRange.y);

        Vector3 trackPoint = car.position + forward * ahead;
        Vector3 ground = trackPoint + right * side;

        // Reference cast: the height of the TRACK at this station, straight ahead of the car.
        //
        // This is the whole trick for "do not spawn on the road". Comparing the candidate
        // against the CAR's height cannot work — the course descends, so a point 300 m ahead is
        // ~45 m below the car whether it is on the road or up the wall, and every forward spawn
        // would be rejected. Comparing against the track at the SAME station removes the
        // descent from the question and leaves only "is this up the side".
        if (!Physics.Raycast(trackPoint + Vector3.up * probeHeight, Vector3.down,
                             out RaycastHit trackHit, probeHeight * 2f, groundMask,
                             QueryTriggerInteraction.Ignore))
        {
            lastResult = "no track under the reference point";
            return;
        }

        // Find the valley wall. Casting DOWN from high above is what keeps this independent of
        // how the course was made -- it reports whatever surface is actually there.
        if (!Physics.Raycast(ground + Vector3.up * probeHeight, Vector3.down,
                             out RaycastHit hit, probeHeight * 2f, groundMask,
                             QueryTriggerInteraction.Ignore))
        {
            // Off the edge of the world, or the wall is taller than probeHeight. Skipping is
            // correct: a boulder spawned in mid-air with nothing under it just falls forever.
            lastResult = "no ground found";
            return;
        }

        // Not high enough above the track to be a wall, so this is the corridor or its shoulder.
        // Dropping here is what put boulders on the road.
        float rise = hit.point.y - trackHit.point.y;
        if (rise < minRise)
        {
            lastResult = "on the track (rise " + rise.ToString("F1") + " m)";
            return;
        }

        float radius = Random.Range(radiusRange.x, radiusRange.y);
        Rock rock = Take();

        rock.go.transform.position = hit.point + Vector3.up * (dropHeight + radius);
        rock.go.transform.rotation = Random.rotation;
        rock.go.transform.localScale = Vector3.one * radius;

        rock.body.mass = MassFor(radius);
        rock.body.linearVelocity = AimedLaunch(rock.go.transform.position, controller, right);
        rock.body.angularVelocity = Random.insideUnitSphere * (spin * Mathf.Deg2Rad);

        rock.expiresAt = Time.time + lifetime;
        rock.slowSince = -1f;

        spawnedTotal++;
        lastResult = "dropped r=" + radius.ToString("F1") + " m=" + rock.body.mass.ToString("F0");
    }

    /// <summary>
    /// Launch velocity aimed at where the car is GOING to be, not where it is.
    /// </summary>
    /// <remarks>
    /// Aiming at the car's current position always lands behind it — at 25 m/s a boulder with a
    /// four second flight arrives 100 m late, which is why an unaimed rockfall feels like
    /// scenery no matter how much of it there is.
    ///
    /// The intercept is a **fixed-point solve, two passes**, and that is all it needs to be:
    /// guess the flight time from the current distance, move the target along by that much,
    /// then re-time against the new target. Two passes converge well inside the error that the
    /// terrain introduces anyway, and the closed-form quadratic would be false precision — the
    /// boulder does not travel in a straight line once it is bouncing down a rock face.
    ///
    /// **`aimSpread` is load-bearing.** A perfect intercept is not a hazard, it is a scripted
    /// death that arrives however well the player is driving. The spread is what turns it into
    /// something to read and avoid: most miss, some connect, and the ones that nearly connect
    /// are the good ones.
    ///
    /// Vertical is stripped and left to gravity. Launching a boulder upward at the player would
    /// read as a catapult rather than a rockfall.
    /// </remarks>
    Vector3 AimedLaunch(Vector3 from, CarController car, Vector3 right)
    {
        Vector3 carPos = car.transform.position;
        Vector3 carVel = trackedBody != null ? trackedBody.linearVelocity : Vector3.zero;
        carVel.y = 0f;

        // How long it takes to COME DOWN. This is the whole fix for boulders sailing over the
        // track: a spawn point 50 m up the mountainside is in the air for 3.2 s, and at a fixed
        // 30 m/s that is 96 m of travel before it lands — well past a 26 m corridor.
        //
        // Free fall is an approximation, because a boulder actually bounces down a slope rather
        // than dropping, but it is the right shape of answer and far closer than assuming the
        // rock arrives the instant it has covered the horizontal distance.
        float drop = Mathf.Max(1f, from.y - carPos.y);
        float flight = Mathf.Sqrt(2f * drop / 9.81f);

        Vector3 aim = carPos + carVel * (flight * lead);

        // SPEED IS SOLVED FOR, not picked. Given the boulder is in the air for `flight`, the
        // horizontal speed that puts it on the aim point is simply distance / time. Choosing a
        // speed at random first and then aiming with it was the error: it made the launch speed
        // and the arrival point independent, so a rock from high up overshot and one from low
        // down dribbled short.
        //
        // HORIZONTAL distance, not 3D. The vertical part of the trip is gravity's job and is
        // already accounted for in `flight`; including it here inflates the estimate and leads
        // the player too far.
        float speed = 0f;
        for (int i = 0; i < 2; i++)
        {
            Vector3 flat = aim - from;
            flat.y = 0f;

            speed = Mathf.Clamp(flat.magnitude / Mathf.Max(0.1f, flight),
                                launchSpeed.x, launchSpeed.y);

            // Re-lead at the speed actually settled on, so the two agree.
            float reach = flat.magnitude / Mathf.Max(0.1f, speed);
            aim = carPos + carVel * (Mathf.Max(flight, reach) * lead);
        }

        // Spread ACROSS the course and ALONG it, rather than in a sphere: a boulder that misses
        // sideways is a near miss the player drives past, which is the good outcome, and one
        // that misses vertically is just wrong.
        aim += right * Random.Range(-aimSpread, aimSpread)
             + car.transform.forward * Random.Range(-aimSpread * 0.5f, aimSpread * 0.5f);

        Vector3 toAim = aim - from;
        toAim.y = 0f;
        if (toAim.sqrMagnitude < 0.01f) toAim = -right * Mathf.Sign(Vector3.Dot(from - carPos, right));

        return toAim.normalized * speed;
    }

    /// <summary>
    /// Boulder mass, scaled by radius SQUARED and capped.
    /// </summary>
    /// <remarks>
    /// True volume scaling (r³ × density) is the physically correct answer and it is wrong here.
    /// Rock is ~2,700 kg/m³, so a 3.5 m radius boulder is **480 tonnes** — and even at a
    /// generously light 900 kg/m³ it is 160. Against a 1,200 kg car that is a mass ratio of
    /// 130:1, and **PhysX solves a contact badly past roughly 10:1**: the car jitters, gets
    /// squeezed through the ground, or is launched. The boulder also becomes literally
    /// immovable, which looks less like a rock and more like a moving wall.
    ///
    /// r² keeps a big boulder decisively heavier than a small one — 2.4x from end to end of the
    /// default range — while staying in the band the solver handles. `maxMass` then caps the
    /// ratio at about 7:1 whatever anyone types into the Inspector.
    ///
    /// This is a case where the realistic number and the working number are different, and the
    /// working one wins. Do not "fix" it back to cubic.
    /// </remarks>
    float MassFor(float radius)
    {
        return Mathf.Clamp(massAtOneMetre * radius * radius, 60f, Mathf.Max(60f, maxMass));
    }

    void Recycle()
    {
        Transform car = PlayerCar.Current != null ? PlayerCar.Current.transform : null;

        live = 0;
        foreach (Rock rock in pool)
        {
            if (!rock.go.activeSelf) continue;
            live++;

            bool done = Time.time >= rock.expiresAt;

            if (!done)
            {
                if (rock.body.linearVelocity.sqrMagnitude < restSpeed * restSpeed)
                {
                    if (rock.slowSince < 0f) rock.slowSince = Time.time;
                    else if (Time.time - rock.slowSince > restDelay) done = true;
                }
                else
                {
                    rock.slowSince = -1f;
                }
            }

            // Behind the car it cannot matter any more, and it is still a rigidbody. Safe to
            // compare against the raw distance now that every boulder is spawned AHEAD — while
            // they spawned in a full ring this had to be clamped past the spawn radius, or one
            // dropped behind the car was culled on its very first frame.
            if (!done && car != null)
            {
                Vector3 delta = rock.go.transform.position - car.position;
                if (Vector3.Dot(delta, car.forward) < -behindDistance) done = true;
            }

            if (done)
            {
                rock.go.SetActive(false);
                live--;
            }
        }

        Live = live;
    }

    Rock Take()
    {
        foreach (Rock rock in pool)
        {
            if (rock.go.activeSelf) continue;
            rock.body.linearVelocity = Vector3.zero;
            rock.body.angularVelocity = Vector3.zero;
            rock.go.SetActive(true);
            return rock;
        }

        GameObject go = new GameObject("Boulder");
        go.transform.SetParent(transform, false);
        go.layer = 0;   // Default: damaging to the car, and solid to the wheel casts.

        Mesh mesh = meshes[Random.Range(0, meshes.Length)];

        MeshFilter filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = rockMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        MeshCollider collider = go.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        collider.convex = true;
        if (rockPhysics != null) collider.sharedMaterial = rockPhysics;

        Rigidbody body = go.AddComponent<Rigidbody>();
        body.interpolation = RigidbodyInterpolation.Interpolate;

        // A 2 m rock at 20 m/s moves 40 cm a physics step. Discrete detection tunnels it
        // straight through the road and the car.
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        Rock created = new Rock
        {
            go = go,
            body = body,
            filter = filter,
            collider = collider,
        };

        pool.Add(created);
        return created;
    }

    [Header("Look")]
    [Tooltip("Material for the boulders. Leave empty and they use the course rock material if " +
             "one can be found, so they match the map they are falling down.")]
    public Material rockMaterial;

    [Tooltip("Optional physics material. Without one they use Unity's default friction, which " +
             "rolls acceptably.")]
    public PhysicsMaterial rockPhysics;

    /// <summary>
    /// A subdivided icosahedron with hashed per-vertex displacement, flat shaded.
    /// </summary>
    /// <remarks>
    /// Flat shading is the point — smooth normals on a lumpy ball read as a beanbag, and the
    /// faceted silhouette is the whole reason a rock looks like a rock. That costs three
    /// vertices per triangle (240 for 80 faces), which is nothing at this size.
    ///
    /// Displacement is INWARD only (`1 - amount * hash`). Pushing vertices outward can turn a
    /// face inside out on a mesh this coarse, and the same one-sided rule is why
    /// `CarDeformation.crumple` stopped spiking panels.
    /// </remarks>
    static Mesh BuildBoulder(int seed, float lumpiness)
    {
        float t = (1f + Mathf.Sqrt(5f)) * 0.5f;

        Vector3[] baseVerts =
        {
            new Vector3(-1,  t,  0), new Vector3( 1,  t,  0),
            new Vector3(-1, -t,  0), new Vector3( 1, -t,  0),
            new Vector3( 0, -1,  t), new Vector3( 0,  1,  t),
            new Vector3( 0, -1, -t), new Vector3( 0,  1, -t),
            new Vector3( t,  0, -1), new Vector3( t,  0,  1),
            new Vector3(-t,  0, -1), new Vector3(-t,  0,  1),
        };

        int[] baseTris =
        {
            0,11,5,  0,5,1,   0,1,7,   0,7,10,  0,10,11,
            1,5,9,   5,11,4,  11,10,2, 10,7,6,  7,1,8,
            3,9,4,   3,4,2,   3,2,6,   3,6,8,   3,8,9,
            4,9,5,   2,4,11,  6,2,10,  8,6,7,   9,8,1,
        };

        for (int i = 0; i < baseVerts.Length; i++) baseVerts[i].Normalize();

        // One subdivision: 20 faces -> 80. Enough to carry a lumpy silhouette, few enough that
        // the convex hull is trivial to cook.
        List<Vector3> verts = new List<Vector3>(240);
        List<int> tris = new List<int>(240);

        for (int f = 0; f < baseTris.Length; f += 3)
        {
            Vector3 a = baseVerts[baseTris[f]];
            Vector3 b = baseVerts[baseTris[f + 1]];
            Vector3 c = baseVerts[baseTris[f + 2]];
            Vector3 ab = (a + b).normalized;
            Vector3 bc = (b + c).normalized;
            Vector3 ca = (c + a).normalized;

            AddFace(verts, tris, a, ab, ca, seed, lumpiness);
            AddFace(verts, tris, ab, b, bc, seed, lumpiness);
            AddFace(verts, tris, ca, bc, c, seed, lumpiness);
            AddFace(verts, tris, ab, bc, ca, seed, lumpiness);
        }

        Mesh mesh = new Mesh { name = "Boulder" + seed };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.UploadMeshData(false);
        return mesh;
    }

    static void AddFace(List<Vector3> verts, List<int> tris,
                        Vector3 a, Vector3 b, Vector3 c, int seed, float lumpiness)
    {
        int start = verts.Count;

        // Radius is hashed from the DIRECTION, so the three faces meeting at a shared corner all
        // move it to the same place and the surface stays closed. Hashing per face would tear
        // the rock into 80 loose triangles.
        verts.Add(a * Lump(a, seed, lumpiness));
        verts.Add(b * Lump(b, seed, lumpiness));
        verts.Add(c * Lump(c, seed, lumpiness));

        tris.Add(start);
        tris.Add(start + 1);
        tris.Add(start + 2);
    }

    static float Lump(Vector3 dir, int seed, float lumpiness)
    {
        // Quantised so co-located corners from different faces hash identically.
        int x = Mathf.RoundToInt(dir.x * 512f);
        int y = Mathf.RoundToInt(dir.y * 512f);
        int z = Mathf.RoundToInt(dir.z * 512f);

        unchecked
        {
            uint h = (uint)(seed * 374761393 + x * 668265263 + y * 2246822519 + z * 3266489917);
            h ^= h >> 13;
            h *= 2246822519u;
            h ^= h >> 16;

            // 0.5 radius, so the boulder is a unit-diameter ball before scaling. Inward only.
            return 0.5f * (1f - lumpiness * (h % 1000u) / 1000f);
        }
    }
}
