using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-vertex denting. Pushes mesh vertices inward around an impact so panels visibly
/// crumple, without touching the colliders.
/// </summary>
/// <remarks>
/// This is the affordable stand-in for soft body, not an approximation of it. Node/beam soft
/// body needs hundreds of mass-spring nodes solved per car per step across several cores; we
/// have one WASM thread and physics is already the most expensive system here. See CLAUDE.md,
/// "Soft-body deformation -- ruled out, do not revisit".
///
/// Splitting the body into panels made this CHEAPER, not harder: a hit to the door touches a
/// 931-vertex mesh instead of one containing the whole car.
///
/// Deliberately visual only. The three body BoxColliders are never re-cooked -- building a
/// collider from a deformed mesh at runtime costs a frame hitch, and nobody can tell that the
/// dent they are looking at is not quite the shape they are colliding with.
/// </remarks>
[DisallowMultipleComponent]
public class CarDeformation : MonoBehaviour
{
    [Header("Meshes")]
    [Tooltip("Leave EMPTY to auto-collect on Awake: every child MeshFilter except the wheels " +
             "(read from CarController, not guessed by name) and the InteriorShell. Fill this " +
             "in by hand only to override that.")]
    public MeshFilter[] panels;

    // Every field below is in REAL METRES. Dent() converts them into each panel's own mesh units
    // using its lossyScale, so a model imported at scale 100 -- as the E30 is -- needs no special
    // numbers. Do not "helpfully" pre-scale these to match a particular model.
    [Header("Dent shape")]
    [Tooltip("Radius of one dent, in metres. Vertices further than this from the impact are " +
             "untouched. This is the single biggest control over whether damage reads as a DENT " +
             "or as the whole panel being shoved: at 0.45 the crater is 0.9 m across on a 1.68 m " +
             "wide car, which is most of the panel.")]
    public float radius = 0.55f;

    [Tooltip("Metres bitten out per point of damage by ONE hit. Keep this well under " +
             "maxDisplacement -- that is what makes damage accumulate over several impacts " +
             "instead of the first one instantly maxing the panel out. Coupled to CarDamage's " +
             "minimumImpulse and damagePerImpulse; retune whenever either of those moves.\n" +
             "MEASURED 2026-08-29: a wall hit reports impulse ~16,500, so damage ~702. Sized so " +
             "that a hit that hard just reaches maxDisplacement, leaving lighter scrapes visibly " +
             "shallower. Set it much higher and every hit saturates the cap, which throws away " +
             "all gradation -- watch lastRequestedCm vs lastAppliedCm to see it happening.")]
    public float strengthPerDamage = 0.0022f;

    [Tooltip("Hard cap on how far any vertex may end up from where it started, however many " +
             "times it is hit. This is normally the BINDING constraint on how visible damage is: " +
             "the requested depth is usually far larger, so this number alone decides what you " +
             "see. Raise it freely -- it used to be limited by the InteriorShell erupting through " +
             "the paint, but the shell is deformed alongside the body now and cannot be caught.")]
    public float maxDisplacement = 1.0f;

    [Tooltip("How much the dent is roughened, 0-1. A pure falloff curve is a perfectly smooth " +
             "bowl and reads as a dent pressed by a beach ball. ONE-SIDED: the jitter can only " +
             "make a vertex travel less than the smooth profile, never more, so it can never " +
             "overshoot its neighbours into a spike. Hashed from POSITION and per cell, so hard " +
             "edges do not split. 0 gives the smooth bowl back.")]
    [Range(0f, 1f)] public float crumple = 0.5f;

    [Tooltip("How much the surrounding metal is dragged IN toward the impact point, rather than " +
             "just pushed along the impact axis. 0 gives a smooth bowl; higher gathers material " +
             "into the crater, which is what crushing actually looks like and is the difference " +
             "between reading as caved in and reading as dimpled. Applied as a SEPARATE sideways " +
             "move scaled by each vertex's own distance from the impact axis, so it can only ever " +
             "close a fraction of that distance -- it cannot reach the axis, overshoot it, or drag " +
             "bodywork up over the roof. The along-axis part is stripped out first, so nothing is " +
             "pushed back out through the paint either.")]
    [Range(0f, 0.9f)] public float crush = 0.75f;

    [Tooltip("Height of a ring of metal pushed OUTWARD around the crater, as a fraction of the " +
             "dent depth. Physically what displaced metal does, but it is the ONLY part of the " +
             "profile that moves geometry outward, so on a mesh this coarse it is also the only " +
             "thing that can produce spikes sticking out of a panel. Left at 0. Raise it only if " +
             "the panels are ever subdivided.")]
    [Range(0f, 1f)] public float rimBulge = 0f;

    [Tooltip("Size of one crumple facet, in metres. This is what stops crumple turning into " +
             "spikes. Vertices inside the same cell share a jitter value, so the roughness comes " +
             "out as broad folds rather than per-vertex noise. It MUST stay well above the " +
             "spacing between vertices -- the E30 body is 2,910 tris, so neighbours sit 5-15 cm " +
             "apart, and anything under about 0.15 gives every vertex its own value and spikes " +
             "the mesh. Bigger = fewer, broader folds.")]
    public float crumpleScale = 0.22f;

    [Tooltip("Where the crater stops and the raised ring starts, as a fraction of the radius.")]
    [Range(0.3f, 0.9f)] public float rimStart = 0.6f;

    [Header("Interior shell")]
    [Tooltip("How much wider the shell dents than the bodywork. The shell is heavily decimated, " +
             "so its dent is coarse; denting it over a wider area stops the corners of that coarse " +
             "dent poking back through the paint.")]
    public float shellRadiusScale = 1.7f;

    [Tooltip("How much deeper the shell dents than the bodywork. Must stay above 1 or the paint " +
             "catches up with the shell and the panel renders near-black again.")]
    public float shellDepthScale = 1.25f;

    [Header("Cost control")]
    [Tooltip("Re-light the dent. Roughly 0.05-0.2 ms on a panel-sized mesh, at impact time only. " +
             "Off leaves stale normals: the dent is there, but lit as though it were still flat.")]
    public bool recalculateNormals = true;

    [Tooltip("Ceiling on dents applied per physics step. A multi-contact scrape fires several " +
             "collisions in one step, and every one of them is a vertex buffer upload.")]
    public int maxDentsPerStep = 2;

    [Header("Read-only — watch these in play mode")]
    [Tooltip("Damage value CarDamage handed to the last Dent call.")]
    [SerializeField] float lastDamage;

    [Tooltip("Depth that hit asked for, in CENTIMETRES, before the maxDisplacement clamp. If " +
             "this is under about 1 the dent is too shallow to see and strengthPerDamage is " +
             "the number to raise.")]
    [SerializeField] float lastRequestedCm;

    [Tooltip("Vertices actually moved by the last hit. Zero means the impact missed every " +
             "panel; a handful means the mesh is too coarse for the radius to bite.")]
    [SerializeField] int lastVertsMoved;

    [Tooltip("Deepest displacement the last hit ACTUALLY applied, in centimetres of world space, " +
             "after the maxDisplacement clamp. This is the number that decides whether you can " +
             "see a dent. If it is far below lastRequestedCm then maxDisplacement is the only " +
             "lever that matters and strengthPerDamage is doing nothing.")]
    [SerializeField] float lastAppliedCm;

    [Tooltip("Which mesh took the deepest dent. If this is not the panel you were looking at " +
             "when you hit the wall, the impact is landing somewhere you cannot see -- that is a " +
             "matching problem, not a depth problem.")]
    [SerializeField] string lastPanelHit;

    [Tooltip("Whether that mesh is still the one its MeshFilter renders. False means the dents " +
             "are being written into a mesh nothing draws, which looks exactly like no dents.")]
    [SerializeField] bool lastPanelStillRendered;

    [Tooltip("How many contact points the last impact supplied. 1 is a point impact; several " +
             "means a broad landing, which is what lets a roof crush flatten instead of dimple.")]
    [SerializeField] int lastDentCount;

    // Where the last dent landed, so OnDrawGizmos can show it. Seeing the spheres sit somewhere
    // unexpected is far faster than reasoning about which panel matched.
    Vector3 lastDentPoint;
    readonly Vector3[] lastDentPoints = new Vector3[MaxContactPoints];
    bool hasDented;

    /// <summary>Upper bound on contact points per impact, for the scratch buffers.</summary>
    public const int MaxContactPoints = 8;

    // Scratch, reused every impact so a broad hit allocates nothing.
    readonly Vector3[] localPoints = new Vector3[MaxContactPoints];

    class Panel
    {
        public MeshFilter filter;
        public Transform transform;
        public Renderer renderer;
        public Mesh mesh;
        public Vector3[] original;   // never written to: the reference pose for clamping and Repair
        public Vector3[] current;    // reused for every dent, so no per-hit allocation
        public float[] budget;       // per-vertex depth allowance, shaped by falloff, grows only
        public bool isShell;         // the InteriorShell: dents wider and deeper so it stays hidden
        public bool dirty;
    }

    readonly List<Panel> panelList = new List<Panel>();
    int dentsThisStep;

    void Awake()
    {
        IEnumerable<MeshFilter> source = panels != null && panels.Length > 0 ? panels : Collect();

        foreach (MeshFilter filter in source)
        {
            if (filter == null || filter.sharedMesh == null) continue;

            if (!filter.sharedMesh.isReadable)
            {
                // Worth shouting about. Without Read/Write the vertex array comes back empty
                // and every dent silently does nothing, with no other symptom.
                Debug.LogError("CarDeformation: mesh on '" + filter.name + "' is not readable. " +
                               "Tick Read/Write Enabled on the model importer or it will never dent.",
                               filter);
                continue;
            }

            // Clone. Writing dents into the imported asset persists them across play sessions,
            // which in the Editor permanently damages the FBX's mesh.
            Mesh clone = Instantiate(filter.sharedMesh);
            clone.name = filter.sharedMesh.name + " (deformable)";
            clone.MarkDynamic();
            filter.mesh = clone;

            Vector3[] verts = clone.vertices;

            panelList.Add(new Panel
            {
                filter = filter,
                transform = filter.transform,
                renderer = filter.GetComponent<Renderer>(),
                mesh = clone,
                original = (Vector3[])verts.Clone(),
                current = verts,
                budget = new float[verts.Length],
                isShell = filter.name == "InteriorShell",
            });
        }
    }

    /// <summary>
    /// Everything under the car that should crumple: bodywork and detachable panels.
    /// </summary>
    /// <remarks>
    /// Wheels are excluded by reading CarController's own wheel list rather than by matching on
    /// the name. Name matching is exactly how this project has been bitten before -- "trim"
    /// contains "rim" -- and the controller already holds the authoritative answer.
    ///
    /// InteriorShell is INCLUDED, and that is what removes the ceiling on dent depth. While the
    /// shell stayed still, any dent deeper than its clearance pushed the paint through it and the
    /// panel rendered near-black -- so depth was capped by the shell's scale, not by anything to
    /// do with damage. Denting the shell wider and deeper than the body (shellRadiusScale,
    /// shellDepthScale) keeps it behind the paint at any depth. The cost is that a panel which has
    /// BOTH detached and been dented shows a slightly deeper cavity, which is a far smaller
    /// problem than being permanently limited to dents nobody can see.
    /// </remarks>
    IEnumerable<MeshFilter> Collect()
    {
        HashSet<Transform> skip = new HashSet<Transform>();

        CarController controller = GetComponent<CarController>();
        if (controller != null && controller.wheels != null)
        {
            foreach (CarController.Wheel wheel in controller.wheels)
                if (wheel != null && wheel.visual != null) skip.Add(wheel.visual);
        }

        List<MeshFilter> found = new List<MeshFilter>();
        foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
        {
            if (skip.Contains(filter.transform)) continue;
            found.Add(filter);
        }

        return found;
    }

    void FixedUpdate()
    {
        dentsThisStep = 0;
    }

    /// <summary>
    /// Dent every panel with geometry near <paramref name="point"/>.
    /// </summary>
    /// <param name="point">World-space impact position.</param>
    /// <param name="direction">
    /// World-space push direction. Guarded below: a direction pointing away from the car is
    /// replaced with a straight-inward one, so a wrong sign convention can never blow panels
    /// outward. The worst it can do is make dents less directional.
    /// </param>
    /// <param name="damage">CarDamage's damage number for this impact.</param>
    public void Dent(Vector3 point, Vector3 direction, float damage)
    {
        singlePoint[0] = point;
        Dent(singlePoint, 1, direction, damage);
    }

    readonly Vector3[] singlePoint = new Vector3[1];

    /// <summary>
    /// Dent around SEVERAL contact points at once, taking the deepest of them per vertex.
    /// </summary>
    /// <remarks>
    /// This is what lets a roof landing flatten the roof instead of punching one crater in the
    /// middle of it. A flat impact produces a broad contact patch, and using only
    /// `collision.GetContact(0)` threw all of that away -- every impact, however it landed, became
    /// a single round dip. Feeding the spread of contacts in means the shape of the damage follows
    /// the shape of the impact: broad and flat where the car lands on its roof, tight and deep
    /// where it hits a post.
    ///
    /// Per vertex it takes the MAXIMUM profile across the points rather than the sum. Summing
    /// would make overlapping craters twice as deep as either, so a flat landing would gouge a
    /// trench exactly where it should be flattest.
    ///
    /// All the points share one vertex-buffer upload per panel, so the cost of a broad impact is
    /// the same as a narrow one apart from the extra distance tests.
    /// </remarks>
    public void Dent(Vector3[] points, int count, Vector3 direction, float damage)
    {
        if (damage <= 0f || panelList.Count == 0) return;
        if (points == null || count <= 0) return;
        if (dentsThisStep >= maxDentsPerStep) return;
        dentsThisStep++;

        count = Mathf.Min(count, points.Length);

        Vector3 centre = Vector3.zero;
        for (int p = 0; p < count; p++) centre += points[p];
        centre /= count;

        Vector3 inward = transform.position - centre;
        if (inward.sqrMagnitude < 0.0001f) return;
        inward.Normalize();

        if (direction.sqrMagnitude < 0.0001f || Vector3.Dot(direction.normalized, inward) < 0f)
            direction = inward;
        else
            direction = direction.normalized;

        // NOT clamped to maxDisplacement. This is the per-hit bite; maxDisplacement is the
        // eventual limit. Keeping them separate is what lets damage build up over several
        // impacts instead of the first hit instantly maxing the panel out.
        float depth = damage * strengthPerDamage;

        lastDamage = damage;
        lastRequestedCm = depth * 100f;
        lastVertsMoved = 0;
        lastAppliedCm = 0f;
        lastPanelHit = "(none)";
        lastDentPoint = centre;
        lastDentCount = count;
        for (int p = 0; p < count && p < lastDentPoints.Length; p++) lastDentPoints[p] = points[p];
        hasDented = true;

        if (depth <= 0.0001f) return;

        // The reject below has to cover the whole contact patch, not one point of it, or a panel
        // touched only by the far edge of a broad roof landing is skipped entirely.
        float spread = 0f;
        for (int p = 0; p < count; p++)
            spread = Mathf.Max(spread, (points[p] - centre).magnitude);

        float reach = radius + spread;
        float reachSqr = reach * reach;

        foreach (Panel panel in panelList)
        {
            // Cheap reject on the world AABB. Most panels are nowhere near any given hit, and
            // this skips their entire vertex loop for the price of one distance test.
            if (panel.renderer != null && panel.renderer.bounds.SqrDistance(centre) > reachSqr)
                continue;

            for (int p = 0; p < count; p++)
                localPoints[p] = panel.transform.InverseTransformPoint(points[p]);

            Vector3 localDir = panel.transform.InverseTransformDirection(direction);

            // MESH-LOCAL UNITS ARE NOT METRES, and assuming they were is the single worst bug
            // this component has had. Blender wrote the E30 in centimetres, so every FBX node
            // carries a x100 scale and the whole 4.16 m car spans about 0.042 local units. A
            // radius of 0.28 "metres" compared raw against those units covers the entire car six
            // times over -- every vertex inside the falloff at ~1.0, so the mesh translates
            // bodily instead of denting -- while a depth of 0.08 meant eight METRES.
            //
            // Converting here rather than demanding model-specific numbers keeps every field on
            // this component in real metres for any model, whatever its import scale.
            Vector3 lossy = panel.transform.lossyScale;
            float scale = (Mathf.Abs(lossy.x) + Mathf.Abs(lossy.y) + Mathf.Abs(lossy.z)) / 3f;
            float toLocal = scale > 1e-6f ? 1f / scale : 1f;

            // The shell dents wider and deeper than the paint, so it always retreats ahead of the
            // body instead of being caught by it.
            float rScale = panel.isShell ? shellRadiusScale : 1f;
            float dScale = panel.isShell ? shellDepthScale : 1f;

            float localRadius = radius * rScale * toLocal;
            float localRadiusSqr = localRadius * localRadius;
            float localDepth = depth * dScale * toLocal;
            float localMax = maxDisplacement * dScale * toLocal;

            panel.dirty = false;

            for (int i = 0; i < panel.current.Length; i++)
            {
                // Nearest of the contact points wins. Measured against the ORIGINAL position, not
                // the current one: using the deformed position lets the affected set drift with
                // each hit, so a panel that is already caved in keeps recruiting new vertices and
                // the damage spreads across the whole panel instead of deepening one crater.
                float distSqr = float.MaxValue;
                int nearest = 0;

                for (int p = 0; p < count; p++)
                {
                    float d = (panel.original[i] - localPoints[p]).sqrMagnitude;
                    if (d >= distSqr) continue;
                    distSqr = d;
                    nearest = p;
                }

                if (distSqr > localRadiusSqr) continue;

                Vector3 localPoint = localPoints[nearest];
                float u = Mathf.Sqrt(distSqr) / localRadius;   // 0 at the impact, 1 at the rim

                // Smoothstep crater, not the quadratic this used to be. Quadratic is only 0.25
                // deep at half the radius, so most vertices in range barely move and the dent
                // stays invisible however deep the centre goes. Smoothstep is 0.5 at half radius.
                float t = 1f - u;
                float falloff = t * t * (3f - 2f * t);

                // A ring of metal pushed OUTWARD around the crater. Displaced material has to go
                // somewhere, and the bright ridge this makes beside the dark crater does far more
                // for readability than raw depth -- it gives the eye an edge to catch, which a
                // smooth bowl on a flat panel never does. Goes to zero at both ends so the
                // profile stays continuous.
                if (rimBulge > 0f && u > rimStart)
                {
                    float ring = (u - rimStart) / Mathf.Max(0.01f, 1f - rimStart);
                    falloff -= rimBulge * 4f * ring * (1f - ring);
                }

                // Roughen it, but ONE-SIDED: the jitter can only ever make a vertex travel LESS
                // than the smooth profile, never more. That is what guarantees no spikes. A
                // symmetric jitter around 1.0 lets individual vertices overshoot their neighbours,
                // and on a mesh this coarse -- neighbours 5-15 cm apart -- an overshooting vertex
                // has nothing nearby to blend with, so it reads as a spike rather than a fold.
                //
                // Hashed per CELL, not per vertex, for the same reason: millimetre-precision
                // hashing gives every vertex an independent value. Quantising to cells wider than
                // the vertex spacing makes neighbours share a value, so roughness comes out as
                // broad folds. Co-located vertices at a hard edge still match, which is what
                // stops the seams tearing open.
                float cell = Mathf.Max(0.01f, crumpleScale);
                float jitter = Mathf.Abs(Hash(panel.original[i] * (scale / cell)));
                float shaped = falloff * (1f - crumple * jitter);
                if (shaped <= 0.0001f) continue;

                // Each vertex earns a permanent depth allowance SHAPED by the falloff, and that
                // allowance only ever grows. This is what keeps a crater a crater. With one flat
                // maxDisplacement for every vertex, everything inside the radius saturates at the
                // same depth after a couple of hits and the dent flattens into a slab -- which
                // reads as the whole panel being bodily shoved rather than dented.
                // Abs, because the rim ring is deliberately negative. The budget caps how far a
                // vertex may travel, not which way.
                float earned = Mathf.Min(localMax, localMax * Mathf.Abs(shaped));
                if (earned > panel.budget[i]) panel.budget[i] = earned;

                // Two separate movements, NOT a blended direction.
                //
                //   inward -- along the impact axis, scaled by the depth profile.
                //   gather -- sideways toward the impact axis, scaled by how far out the vertex
                //             already is.
                //
                // The gather being proportional to the vertex's OWN tangential distance is what
                // bounds it. Blending the two into a unit direction and then travelling `depth`
                // along it does not: a vertex 20 cm from the impact would be dragged the full
                // depth sideways, overshooting the impact point and carrying on out the far side.
                // For a vertex below the impact, "toward the impact" is upward, which is how that
                // version hauled bodywork up over the roof.
                //
                // Here the sideways travel is crush * profile * tangentLength, and crush is capped
                // below 1, so a vertex can only ever move a FRACTION of the way to the axis. It
                // cannot reach it, let alone pass it.
                Vector3 delta = localDir * (localDepth * shaped);

                if (crush > 0f)
                {
                    Vector3 toCentre = localPoint - panel.original[i];

                    // Strip the along-axis part first. Anything sitting deeper into the car than
                    // the contact point has a toCentre pointing back OUT through the bodywork, and
                    // keeping that component pushes it out through the paint as a bulge.
                    Vector3 tangent = toCentre - localDir * Vector3.Dot(toCentre, localDir);
                    delta += tangent * (crush * shaped);
                }

                Vector3 offset = panel.current[i] + delta - panel.original[i];
                float offsetMag = offset.magnitude;
                if (offsetMag > panel.budget[i])
                    offset *= panel.budget[i] / offsetMag;

                panel.current[i] = panel.original[i] + offset;
                panel.dirty = true;

                // The shell is invisible by design, so it must not touch the readouts -- they
                // would otherwise report its wider, deeper dent and flatter the bodywork.
                if (panel.isShell) continue;

                lastVertsMoved++;

                // Back into world centimetres, so the readout is in units you can hold a ruler
                // against. Requested vs applied is the whole diagnosis: if applied is far lower,
                // maxDisplacement is the only lever and strengthPerDamage is doing nothing.
                float appliedCm = Mathf.Min(offsetMag, panel.budget[i]) * scale * 100f;
                if (appliedCm > lastAppliedCm)
                {
                    lastAppliedCm = appliedCm;
                    lastPanelHit = panel.transform.name;
                    lastPanelStillRendered = panel.filter != null && panel.filter.sharedMesh == panel.mesh;
                }
            }

            if (!panel.dirty) continue;

            panel.mesh.SetVertices(panel.current);
            if (recalculateNormals) panel.mesh.RecalculateNormals();

            // Bounds are deliberately NOT recalculated. Dents only ever move vertices inward,
            // so the stale bounds stay conservative and culling stays correct.
        }
    }

    /// <summary>
    /// Draw the last dent sphere. Seeing where the impact actually landed is far faster than
    /// reasoning about which panel matched -- if the sphere is not where you hit the wall, the
    /// problem is impact matching and no amount of depth tuning will help.
    /// </summary>
    void OnDrawGizmos()
    {
        if (!hasDented) return;

        Gizmos.color = Color.red;
        for (int p = 0; p < lastDentCount && p < lastDentPoints.Length; p++)
            Gizmos.DrawWireSphere(lastDentPoints[p], radius);
    }

    /// <summary>
    /// Stable -1..1 noise from a vertex index. Deliberately integer maths and no allocation --
    /// this runs per affected vertex per impact, and Random would give a different shape every
    /// hit, so a second hit in the same place would fight the first instead of deepening it.
    /// </summary>
    static float Hash(Vector3 p)
    {
        unchecked
        {
            // The caller has already divided by the crumple cell size, so this rounds to a cell
            // index. Two things fall out of that: the several vertices a hard edge stores at one
            // position hash identically (hashing the vertex index instead splits the seam open),
            // and neighbouring vertices inside a cell share a value, which is what turns the
            // roughness into folds instead of spikes.
            int n = Mathf.RoundToInt(p.x) * 73856093
                  ^ Mathf.RoundToInt(p.y) * 19349663
                  ^ Mathf.RoundToInt(p.z) * 83492791;

            n = (n << 13) ^ n;
            n = n * (n * n * 15731 + 789221) + 1376312589;
            return 1f - (n & 0x7fffffff) / 1073741824f;
        }
    }

    /// <summary>Undent everything. Called by CarDamage.Repair.</summary>
    public void Repair()
    {
        foreach (Panel panel in panelList)
        {
            System.Array.Copy(panel.original, panel.current, panel.original.Length);
            System.Array.Clear(panel.budget, 0, panel.budget.Length);
            panel.mesh.SetVertices(panel.current);
            if (recalculateNormals) panel.mesh.RecalculateNormals();
        }
    }

    void OnDestroy()
    {
        // filter.mesh created one instance per panel, and nothing else owns them.
        foreach (Panel panel in panelList)
            if (panel.mesh != null) Destroy(panel.mesh);
    }
}
