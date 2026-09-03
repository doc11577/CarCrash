using System;
using UnityEngine;

/// <summary>
/// Turns impacts into lost parts.
///
/// Each part is a named point on the car with its own health. An impact finds the nearest
/// part to the contact and damages it; at zero health the part comes off.
///
/// Wheels are special: losing one is a handling change, not just a visual. See
/// <see cref="CarController.DetachWheel"/> — that corner loses its spring, its drive and
/// its grip, so the body drops onto its collider and drags.
///
/// Two detachment modes, chosen per part:
///
///   REAL (set <see cref="Part.visual"/>) — the actual panel mesh is unparented from the car
///   and thrown. This is what the split cars from tools/blender/split_car.py support: the
///   door leaves a genuine hole, and the InteriorShell behind it reads as a dark cabin.
///
///   FAKED (set <see cref="Part.debrisPrefab"/>) — a generic debris prop is thrown instead
///   and the body keeps its geometry. Kenney's bodies are a single welded mesh, so traffic
///   cars still use this. At traffic distance the flying part is what the eye follows.
///
/// Real is preferred where the geometry exists. If both are set, real wins.
/// </summary>
[RequireComponent(typeof(CarController))]
public class CarDamage : MonoBehaviour
{
    [Serializable]
    public class Part
    {
        [Tooltip("For your own reference in the Inspector. Never shown to the player — set " +
                 "Display Name for that.")]
        public string name = "part";

        [Tooltip("What the player sees when this part comes off, e.g. \"Wheel Front Right\". " +
                 "Used EXACTLY as typed, so capitalise it how you want it to read. Leave empty " +
                 "and the Inspector name is used in capitals instead, which is why an unset " +
                 "wheel reads WHEELFR.")]
        public string displayName = "";

        /// <summary>
        /// The player-facing label. Falls back to the Inspector name in capitals, which is
        /// legible but ugly for anything with a suffix — `wheelFR` is fine as a field name and
        /// poor as a caption.
        /// </summary>
        /// <remarks>
        /// Deliberately a plain field rather than something derived from the name by rule.
        /// Expanding suffixes automatically looks trivial and is not: `bumperR` is REAR while
        /// `doorR` is RIGHT, and nothing in the string says which. This project has been bitten
        /// three times by matching on names (`trim` contains `rim`, `steering_centre` filed as a
        /// wheel, mirrors stealing door hits) and a caption is not worth a fourth.
        /// </remarks>
        public string Label =>
            string.IsNullOrWhiteSpace(displayName) ? name.ToUpperInvariant() : displayName;

        [Tooltip("Where on the car this part lives. Leave empty to use the visual's own position.")]
        public Transform anchor;

        [Tooltip("Real geometry to detach and throw. Preferred over debrisPrefab when set.")]
        public Transform visual;

        [Tooltip("Generic prop thrown when there is no real geometry to remove.")]
        public GameObject debrisPrefab;

        [Tooltip("Damage this part absorbs before it comes off.")]
        public float health = 100f;

        [Tooltip("Wheel index in CarController, or -1 for a body panel.")]
        public int wheelIndex = -1;

        [Tooltip("What KIND of part this is, for feats like \"lose every mirror\". Use the same " +
                 "word on every car — \"mirror\", \"door\", \"bumper\" — or a feat that fires on " +
                 "the E30 will do nothing on the P72.\n\n" +
                 "Wheels need no group: a wheel is identified by having a Wheel Index, which is " +
                 "a fact rather than a guess.")]
        public string group = "";

        /// <summary>
        /// The part's kind, for feat matching. Falls back to "wheel" for anything with a wheel
        /// index, since that is already an unambiguous fact and saves setting it by hand on
        /// four parts per car.
        /// </summary>
        public string Group =>
            !string.IsNullOrWhiteSpace(group) ? group.Trim().ToLowerInvariant()
                                              : (wheelIndex >= 0 ? "wheel" : "");

        [HideInInspector] public bool detached;
        [HideInInspector] public float startingHealth;

        // Where the visual sat before it came off, so Repair can bolt it back on.
        [HideInInspector] public Transform homeParent;
        [HideInInspector] public Vector3 homeLocalPosition;
        [HideInInspector] public Quaternion homeLocalRotation;
        [HideInInspector] public int homeLayer;
    }

    [Header("Parts")]
    public Part[] parts;

    [Header("Impact response")]
    [Tooltip("Impacts gentler than this are ignored entirely. Stops kerbs shedding bumpers.")]
    public float minimumImpulse = 900f;

    [Tooltip("Damage dealt per unit of collision impulse.")]
    public float damagePerImpulse = 0.045f;

    [Tooltip("Most damage ONE impact can do to a single part. Without a cap, a wall hit does " +
             "over 1000 damage to parts that have 100-160 health and the entire shell leaves " +
             "the car in one crash. Does not cap TotalDamage, which still scores the full hit.")]
    public float maxDamagePerImpact = 60f;

    [Tooltip("Multiplies how hard a car-to-car hit CRUMPLES the bodywork. Deformation only — the " +
             "score and how readily panels come off are untouched, so turning this up makes " +
             "hits look worse without making them worth more or stripping the car faster.\n\n" +
             "This is the dial that makes two cars meeting read as an event. Environment " +
             "damage is deliberately not affected. It applies to BOTH cars, since each runs " +
             "its own collision callback for the same impact.")]
    public float carVsCarCrumple = 3f;

    [Tooltip("Multiplies the DAMAGE NUMBER of a car-to-car hit — the score it pays and the " +
             "health it takes off a panel. Left at 1 on purpose: car-to-car hits already pay " +
             "at their own rate (RunScore.gearsPerPvpDamage) and amplifying here as well " +
             "double-counts. Raise it only if panels should come off more readily in a " +
             "collision than against scenery; for a hit that merely LOOKS bigger, use " +
             "carVsCarCrumple instead.")]
    public float carVsCarDamage = 1f;

    [Tooltip("How far from a contact point a part can be and still take the hit, in metres.")]
    public float partReach = 1.6f;

    [Header("Debris throw")]
    [Tooltip("Extra outward speed given to a detached part, on top of the car's velocity.")]
    public float ejectSpeed = 3.5f;

    [Tooltip("Random tumble applied to detached parts, in radians per second.")]
    public float ejectSpin = 9f;

    [Tooltip("Seconds a freshly detached part ignores the car it came from. Its collider is " +
             "created INSIDE the bodywork, and PhysX resolves that overlap by firing the part " +
             "away at the depenetration limit. 0 disables the grace period.")]
    public float detachGrace = 0.4f;

    [Header("Detached part physics")]
    [Tooltip("Mass given to a detached panel, in kg. Light enough to be thrown around.")]
    public float partMass = 18f;

    [Tooltip("Layer detached parts move to. Must be able to collide with the car that shed it.")]
    public int detachedLayer = 0;

    [Tooltip("Stops WHEELS ever detaching, however much damage they take. Set by the race " +
             "director at the start of a race and left off everywhere else — losing a wheel is " +
             "good destruction and an instant retirement from a race.\n\n" +
             "Everything else on the car still comes off. The car is meant to end a race looking " +
             "wrecked; it just has to still be driveable.")]
    public bool protectWheels;

    [Header("Layers")]
    [Tooltip("Only collisions with these layers can cause damage. Exclude the car's own layer.")]
    public LayerMask damagingLayers = ~0;

    /// <summary>
    /// Raised on every damaging impact: the damage dealt, where it landed, and whether it was
    /// SUSTAINED contact rather than a fresh hit. Hook scoring here.
    /// </summary>
    /// <remarks>
    /// The sustained flag matters to anything that rewards impacts. A fresh hit raises this
    /// once; a grind down a wall raises it every <see cref="sustainedInterval"/> (0.08 s), so a
    /// combo meter that counted both would reach its cap in half a second of scraping.
    /// </remarks>
    /// <summary>(source, damage, where, sustained, causedByThePlayersCar)</summary>
    /// <remarks>
    /// The SOURCE is on the event because a listener otherwise cannot tell which car was hurt,
    /// and `byPlayer` because it cannot tell who did it. RunScore needs both: damage to your own
    /// car always scores, damage to traffic scores only when YOU caused it, and traffic wrecking
    /// itself on a wall must pay nothing — which is the exact objection that kept
    /// TrafficSpawner.scoreTrafficDamage switched off.
    /// </remarks>
    public event Action<CarDamage, float, Vector3, bool, bool> Damaged;

    /// <summary>Raised when a part comes off, with the part that went.</summary>
    /// <summary>
    /// A part came off. Carries WHOSE car shed it and WHO caused it, for the same reason
    /// <see cref="Damaged"/> does.
    /// </summary>
    /// <remarks>
    /// It used to carry only the part, which meant a listener could not tell a panel the player
    /// knocked off from one a traffic car lost hitting a rock by itself — so with
    /// `scoreTrafficDamage` on, every AI wreck paid the player and spammed a popup. Worst on
    /// Everest, where obstacle avoidance is off by design and the field destroys itself on the
    /// rocks. `Damaged` had already been given source and byPlayer for exactly this; this event
    /// was simply missed at the time.
    /// </remarks>
    public event Action<CarDamage, Part, bool> PartLost;

    /// <summary>Total damage this car has taken. The basis for the gear payout.</summary>
    public float TotalDamage { get; private set; }

    [Header("Read-only — watch these in play mode")]
    [Tooltip("collision.impulse.magnitude from the last qualifying hit. MEASURED: a wall hit " +
             "reports about 16,500, which matches the arithmetic for a 1200 kg car losing 20 m/s. " +
             "Everything downstream is sized off this, so read it before changing minimumImpulse " +
             "or damagePerImpulse rather than reasoning about what PhysX ought to report.")]
    [SerializeField] float lastImpulse;

    [Tooltip("Damage that impulse produced, before maxDamagePerImpact. Compare against part " +
             "health (100-160) to see how many hits a panel should survive.")]
    [SerializeField] float lastDamage;

    CarController controller;
    CarDeformation deformation;
    CarGlass glass;
    Rigidbody body;
    Collider[] ownColliders;

    /// <summary>A detached part's collider, and when it stops ignoring the car.</summary>
    struct Grace
    {
        public Collider part;
        public float until;
    }

    readonly System.Collections.Generic.List<Grace> graced = new System.Collections.Generic.List<Grace>();

    void Awake()
    {
        controller = GetComponent<CarController>();
        deformation = GetComponent<CarDeformation>();
        glass = GetComponent<CarGlass>();
        body = GetComponent<Rigidbody>();
        ownColliders = GetComponents<Collider>();

        if (parts == null) return;

        foreach (Part part in parts)
        {
            if (part == null) continue;
            part.startingHealth = part.health;
            RememberHome(part);
        }
    }

    void RememberHome(Part part)
    {
        if (part.visual == null) return;

        part.homeParent = part.visual.parent;
        part.homeLocalPosition = part.visual.localPosition;
        part.homeLocalRotation = part.visual.localRotation;
        part.homeLayer = part.visual.gameObject.layer;
    }

    void OnCollisionEnter(Collision collision)
    {
        HandleImpact(collision, 1f, false);
    }

    /// <summary>
    /// Sustained contact keeps crushing: sliding along on the roof, grinding down a wall.
    /// </summary>
    /// <remarks>
    /// Without this a roof landing gets exactly ONE dent. OnCollisionEnter fires when contact
    /// begins, and a car that lands upside down and slides never stops touching, so every metre of
    /// grinding after the first frame did nothing at all.
    ///
    /// It is safe to feed the same path because the gate is already an impulse threshold. A car
    /// simply resting on its roof transmits about `mass * g * fixedDeltaTime` per step -- roughly
    /// 235 for this car -- which is far below minimumImpulse, so resting does no damage. Only
    /// contact violent enough to clear the same bar a real hit clears gets through.
    ///
    /// Scaled and rate-limited anyway, because this fires every physics step rather than once:
    /// unscaled it would be ~50 impacts a second and strip the car in well under a second.
    /// </remarks>
    void OnCollisionStay(Collision collision)
    {
        if (sustainedScale <= 0f) return;
        if (Time.time < nextSustainedAt) return;

        nextSustainedAt = Time.time + sustainedInterval;
        HandleImpact(collision, sustainedScale, true);
    }

    void HandleImpact(Collision collision, float scale, bool sustained)
    {
        if ((damagingLayers.value & (1 << collision.gameObject.layer)) == 0) return;

        float impulse = collision.impulse.magnitude;
        if (impulse < minimumImpulse) return;

        Vector3 contact = collision.GetContact(0).point;
        float damage = (impulse - minimumImpulse) * damagePerImpulse * scale;

        // Car on car hits far harder than car on scenery. Two vehicles meeting is the moment
        // this game is about, and at the shared rate it read as no more eventful than brushing
        // a rock. Environment damage is deliberately untouched — only this multiplier moved.
        //
        // It applies to BOTH cars, because each one runs its own OnCollisionEnter for the same
        // impact, which is what makes a big hit mutually destructive rather than one-sided.
        CarDamage other = collision.gameObject.GetComponentInParent<CarDamage>();
        bool byPlayer = other != null && PlayerCar.Current != null
                                     && other == PlayerCar.Current.Damage;

        if (other != null) damage *= carVsCarDamage;

        if (damage <= 0f) return;

        lastImpulse = impulse;
        lastDamage = damage;

        TotalDamage += damage;
        Damaged?.Invoke(this, damage, contact, sustained, byPlayer);

        // Crumple the panel before deciding whether it comes off, so a hit that happens to be
        // the fatal one still leaves its dent on the piece that flies away.
        //
        // collision.impulse points along the collision response, and its sign convention
        // depends on which body Unity considers first. CarDeformation guards it -- anything
        // pointing away from the car is replaced with a straight-inward push -- so the worst a
        // flipped convention costs here is directionality, never panels blown outward.
        if (deformation != null)
        {
            int spread = GatherContacts(collision);

            // Car-on-car crumples harder than car-on-scenery, and ONLY here. Amplifying the
            // damage number instead would inflate the score and strip panels off faster, which
            // is not what "make hits look better" asks for -- the ask is visual, so the
            // multiplier is applied to the visual.
            float crumple = other != null ? damage * carVsCarCrumple : damage;
            deformation.Dent(dentPoints, spread, collision.impulse, crumple);
        }

        Part hit = NearestPart(contact);
        if (hit == null) return;

        // Cap what ONE impact can take off a single part. A 1200 kg car stopping against a
        // wall at 20 m/s reports an impulse near 24,000, which through the linear formula is
        // over 1,000 damage against parts that have 100-160 health. Every panel then dies on
        // the first frames of one crash and the whole shell leaves the car at once. Damage
        // has to be progressive to read as damage at all.
        //
        // TotalDamage above is deliberately NOT capped: the score should still reflect how
        // hard the hit was, even though no single panel takes all of it.
        hit.health -= Mathf.Min(damage, maxDamagePerImpact);

        // A LOST WHEEL ENDS A RACE, so in race mode wheels do not come off. Identified by
        // wheelIndex, which is a fact the part already carries rather than a name to match on —
        // this project has been bitten three times by implicit matching, and "trim" contains
        // "rim" is exactly the trap here.
        //
        // Health is CLAMPED, not merely left undetached. Letting it sit at zero means the wheel
        // falls off the instant the protection is lifted, which would make returning to the
        // garage between races shed four wheels at once.
        if (protectWheels && hit.wheelIndex >= 0) hit.health = Mathf.Max(1f, hit.health);

        if (hit.health <= 0f) Detach(hit, contact, byPlayer);
    }

    [Tooltip("Damage multiplier for SUSTAINED contact -- sliding on the roof, grinding a wall -- " +
             "as opposed to the first moment of a hit. This is what lets a roof crush deepen as " +
             "the car slides rather than stopping at the single dent the landing made. 0 disables " +
             "sustained damage entirely.")]
    public float sustainedScale = 0.6f;

    [Tooltip("Minimum seconds between sustained damage applications. OnCollisionStay fires every " +
             "physics step, so without this a grind would land ~50 impacts a second and strip the " +
             "car in well under a second.")]
    public float sustainedInterval = 0.08f;

    float nextSustainedAt;

    [Tooltip("Contact points at least this far apart are kept and fed to the deformation, so the " +
             "shape of a dent follows the shape of the impact. Landing flat on the roof gives a " +
             "broad patch and flattens it; hitting a post gives one point and gouges it.")]
    public float contactSpacing = 0.25f;

    readonly Vector3[] dentPoints = new Vector3[CarDeformation.MaxContactPoints];
    static ContactPoint[] contactScratch = new ContactPoint[32];

    /// <summary>
    /// Pick a spread-out subset of the collision's contact points into <see cref="dentPoints"/>.
    /// </summary>
    /// <remarks>
    /// PhysX often reports many contacts clustered within a few millimetres of each other, which
    /// would all dent the same spot and waste the whole point of using more than one. Keeping only
    /// points at least contactSpacing apart gives an even sample across the real contact patch.
    ///
    /// Both buffers are reused, so a crash allocates nothing however many contacts PhysX reports.
    /// </remarks>
    int GatherContacts(Collision collision)
    {
        if (collision.contactCount > contactScratch.Length)
            contactScratch = new ContactPoint[Mathf.NextPowerOfTwo(collision.contactCount)];

        int found = collision.GetContacts(contactScratch);
        if (found <= 0) return 0;

        float spacingSqr = contactSpacing * contactSpacing;
        int kept = 0;

        for (int i = 0; i < found && kept < dentPoints.Length; i++)
        {
            Vector3 candidate = contactScratch[i].point;

            bool tooClose = false;
            for (int k = 0; k < kept; k++)
            {
                if ((dentPoints[k] - candidate).sqrMagnitude >= spacingSqr) continue;
                tooClose = true;
                break;
            }

            if (!tooClose) dentPoints[kept++] = candidate;
        }

        return kept;
    }

    /// <summary>Does this part have enough set up to be hit at all?</summary>
    static bool IsWired(Part part)
    {
        return part != null && (part.anchor != null || part.visual != null);
    }

    /// <summary>
    /// Where a part counts as being, for impact matching and ejection.
    ///
    /// This is the panel's MESH CENTRE, not its transform position. split_car.py puts each
    /// panel's origin on its hinge so it swings correctly, which makes the origin a bad
    /// answer to "where is this part": a door's origin sits on its front edge, roughly a
    /// metre from the door itself. Matching on the origin let the mirror -- whose origin is
    /// mid-door -- steal hits aimed at the middle of the door and snap off on a scrape.
    ///
    /// An explicit <see cref="Part.anchor"/> still overrides this if you need to hand-place one.
    /// </summary>
    Vector3 PartPosition(Part part)
    {
        if (part.anchor != null) return part.anchor.position;
        if (part.visual == null) return transform.position;

        if (part.visual.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
            return part.visual.TransformPoint(filter.sharedMesh.bounds.center);

        return part.visual.position;
    }

    /// <summary>Orientation to throw a generic debris prop at.</summary>
    Quaternion PartRotation(Part part)
    {
        if (part.anchor != null) return part.anchor.rotation;
        return part.visual != null ? part.visual.rotation : transform.rotation;
    }

    Part NearestPart(Vector3 worldContact)
    {
        if (parts == null) return null;

        Part best = null;
        float bestSqr = partReach * partReach;

        foreach (Part part in parts)
        {
            if (part == null || part.detached || !IsWired(part)) continue;

            float sqr = (PartPosition(part) - worldContact).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = part;
            }
        }

        return best;
    }

    /// <summary>
    /// Stop a freshly detached part from colliding with the car it came from, for
    /// <see cref="detachGrace"/> seconds.
    /// </summary>
    void StartGrace(Collider shape)
    {
        if (shape == null || ownColliders == null || detachGrace <= 0f) return;

        foreach (Collider own in ownColliders)
            if (own != null) Physics.IgnoreCollision(shape, own, true);

        graced.Add(new Grace { part = shape, until = Time.time + detachGrace });
    }

    void Update()
    {
        for (int i = graced.Count - 1; i >= 0; i--)
        {
            Grace g = graced[i];

            // Gone, pooled, or bolted back on. Physics.IgnoreCollision errors on a collider
            // whose GameObject is inactive, and DebrisPool deactivates spent debris, so both
            // checks are load-bearing rather than defensive noise.
            if (g.part == null || !g.part.gameObject.activeInHierarchy)
            {
                graced.RemoveAt(i);
                continue;
            }

            if (Time.time < g.until) continue;

            // Hand the part back to normal collision: debris is meant to be able to hit the
            // car that shed it, it just must not be born inside it.
            foreach (Collider own in ownColliders)
                if (own != null) Physics.IgnoreCollision(g.part, own, false);

            graced.RemoveAt(i);
        }
    }

    void Detach(Part part, Vector3 contact, bool byPlayer)
    {
        part.detached = true;

        // A lost wheel is a handling change, not just a missing mesh. Hand over the mesh
        // rather than letting the controller hide it whenever this part owns real geometry --
        // ThrowRealPart is about to unparent and throw that exact GameObject, and it cannot
        // do either while it is inactive.
        if (part.wheelIndex >= 0 && controller != null)
            controller.DetachWheel(part.wheelIndex, part.visual == null);

        if (part.visual != null) ThrowRealPart(part);
        else ThrowDebrisProp(part);

        PartLost?.Invoke(this, part, byPlayer);
    }

    /// <summary>
    /// Unparent the real panel and hand it to physics. This is what makes a hole.
    /// </summary>
    void ThrowRealPart(Part part)
    {
        GameObject go = part.visual.gameObject;

        // Keep the world pose: the panel must not jump when it stops being a child.
        part.visual.SetParent(null, true);
        go.layer = detachedLayer;

        // A box from the renderer bounds, not a convex MeshCollider. Cooking a convex hull
        // at runtime costs a frame hitch, and a flying door does not need a faithful hull.
        Collider shape = go.GetComponent<Collider>();
        if (shape == null)
        {
            BoxCollider box = go.AddComponent<BoxCollider>();
            if (go.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
            {
                box.center = filter.sharedMesh.bounds.center;
                box.size = filter.sharedMesh.bounds.size;
            }
            shape = box;
        }

        // That collider was just created INSIDE the bodywork -- a door box overlaps the Core
        // box almost completely. The instant the Rigidbody below goes dynamic, PhysX resolves
        // that overlap by pushing the two apart at the depenetration limit, and since the
        // panel is 18 kg against 1200 kg the panel takes all of it. Result: panels do not fall
        // off, they are fired sideways. Hold them off the car until they have cleared it.
        StartGrace(shape);

        if (!go.TryGetComponent(out Rigidbody rb))
            rb = go.AddComponent<Rigidbody>();

        rb.mass = partMass;
        rb.isKinematic = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;

        Vector3 at = part.visual.position;
        rb.linearVelocity = body.GetPointVelocity(at) + OutwardFrom(at) * ejectSpeed;
        rb.angularVelocity = UnityEngine.Random.insideUnitSphere * ejectSpin;

        // The pool does not own this object, but it still enforces the cap and the lifetime.
        if (DebrisPool.Instance != null) DebrisPool.Instance.Track(go, rb);
    }

    /// <summary>Fallback for bodies whose geometry cannot be removed, i.e. the Kenney traffic.</summary>
    void ThrowDebrisProp(Part part)
    {
        if (part.debrisPrefab == null || DebrisPool.Instance == null || !IsWired(part)) return;

        Vector3 spawnAt = PartPosition(part);
        Vector3 velocity = body.GetPointVelocity(spawnAt) + OutwardFrom(spawnAt) * ejectSpeed;
        Vector3 spin = UnityEngine.Random.insideUnitSphere * ejectSpin;

        DebrisPool.Instance.Spawn(part.debrisPrefab, spawnAt, PartRotation(part), velocity, spin);
    }

    /// <summary>
    /// Push a piece away from the car's centre so it does not spawn inside the body and get
    /// flung across the map by the depenetration solver.
    /// </summary>
    Vector3 OutwardFrom(Vector3 worldPoint)
    {
        Vector3 outward = (worldPoint - body.worldCenterOfMass).normalized;
        return outward.sqrMagnitude < 0.01f ? transform.up : outward;
    }

    /// <summary>Put every part back. Used by the restart path and the garage.</summary>
    public void Repair()
    {
        TotalDamage = 0f;
        if (deformation != null) deformation.Repair();
        if (glass != null) glass.Restore();
        if (parts == null) return;

        foreach (Part part in parts)
        {
            if (part == null) continue;

            if (part.detached && part.visual != null) BoltBackOn(part);

            // Bolting the mesh back on is only half of a wheel. CarController keeps its own
            // detached flag, and while it stays set that corner has no spring, no drive force
            // and no lateral grip -- a car that looks repaired and permanently drags one
            // corner. Read part.detached before it is cleared below.
            if (part.detached && part.wheelIndex >= 0 && controller != null)
                controller.ReattachWheel(part.wheelIndex);

            part.detached = false;
            part.health = part.startingHealth;
        }
    }

    void BoltBackOn(Part part)
    {
        GameObject go = part.visual.gameObject;

        // Drop the pool's claim first. A stale live entry would expire later and deactivate
        // a panel that is by then bolted back onto the car.
        if (DebrisPool.Instance != null) DebrisPool.Instance.Forget(go);

        // Strip the physics we added on the way out, or the panel will fall off the car.
        // Destroy is deferred to the end of the frame, so neutralise them first. A wheel is
        // the case that needs it: the moment ReattachWheel clears the detached flag,
        // CarController starts writing that transform again, and for the rest of this frame
        // it would be fighting a live Rigidbody that still has a collider on the car.
        if (go.TryGetComponent(out Rigidbody rb))
        {
            rb.isKinematic = true;
            Destroy(rb);
        }
        if (go.TryGetComponent(out Collider col))
        {
            col.enabled = false;
            Destroy(col);
        }

        part.visual.SetParent(part.homeParent, false);
        part.visual.localPosition = part.homeLocalPosition;
        part.visual.localRotation = part.homeLocalRotation;
        go.layer = part.homeLayer;
        go.SetActive(true);
    }

    void OnDrawGizmosSelected()
    {
        if (parts == null) return;

        foreach (Part part in parts)
        {
            if (!IsWired(part)) continue;

            Gizmos.color = part.detached ? Color.red : new Color(1f, 0.78f, 0.15f);
            Gizmos.DrawWireSphere(PartPosition(part), 0.22f);
        }
    }
}
