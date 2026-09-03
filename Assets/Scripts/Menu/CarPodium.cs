using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The garage display: a rotating car on a lit cylinder, against an animated backdrop.
/// </summary>
/// <remarks>
/// Built entirely in code, like the rest of the menu, so adding it to a scene is one component
/// and the layout is reviewable in a diff rather than buried in a scene file.
///
/// **The car is the real prefab, NEUTRALISED.** Using the actual roster prefab means the garage
/// can never show a car that differs from the one that spawns — the failure the roster
/// ScriptableObject exists to prevent, reintroduced at the display layer. But a roster prefab is
/// a live vehicle: it carries a Rigidbody, `CarController`, `CarInput`, colliders and
/// `PlayerCar`. Dropped into a menu untouched it falls through the world, reads the keyboard,
/// and — worst — `PlayerCar.OnEnable` claims `PlayerCar.Current`, so the chase camera and the
/// score would bind to a showroom model. Everything is stripped on spawn, in that order.
///
/// **Rendered in the world by the menu camera, not into a RenderTexture.** A RenderTexture would
/// mean a second camera pass every frame for something already on screen. The UI is Screen Space
/// Overlay and draws on top, so layering costs nothing: backdrop quad, podium, car, then the
/// buttons over all of it.
///
/// **It places itself relative to the camera** rather than asking for hand-placed transforms.
/// A menu scene has one camera and no reason for the podium to be anywhere else, and a hand
/// placed rig is three more numbers to get wrong.
/// </remarks>
[DisallowMultipleComponent]
public class CarPodium : MonoBehaviour
{
    [Header("Placement")]
    [Tooltip("Metres in front of the menu camera.")]
    public float distance = 8.5f;

    [Tooltip("Metres below the camera's line of sight. NEGATIVE raises the rig, which is what " +
             "you want once the camera is pitched down — the podium then sits on the view axis " +
             "and has to be lifted to leave room for the name and buttons underneath.")]
    public float drop = 1.15f;

    [Tooltip("Degrees the menu camera tilts DOWN onto the podium. 0 is a flat side-on view; " +
             "~18 is a showroom three-quarter that shows the roof and the shape of the car.\n\n" +
             "CarPodium takes ownership of the menu camera's ROTATION. That is safe because " +
             "nothing else in the front end uses it — every menu canvas is Screen Space " +
             "Overlay, which does not go through a camera at all.")]
    [Range(0f, 60f)] public float pitch = 18f;

    [Header("Podium")]
    public float podiumRadius = 3.1f;
    public float podiumHeight = 0.55f;
    [Tooltip("Plain black. The podium is a plinth for the car, not a feature — a lit rim was " +
             "tried and just competed with the backdrop for attention.")]
    public Color podiumColour = Color.black;

    [Header("Car")]
    [Tooltip("Degrees per second the car turns on the podium.")]
    public float spinSpeed = 18f;

    [Tooltip("Seconds the podium takes to settle after a swap. The new car drops in and the " +
             "spin eases up, so changing car reads as an event rather than a texture swap.")]
    public float settleTime = 0.45f;

    [Header("Backdrop")]
    public Shader backdropShader;
    [Tooltip("Near black. Everything visible on the backdrop is the lattice drawn over this.")]
    public Color backdropBase = new Color(0.012f, 0.011f, 0.015f);
    public Color backdropAccent = new Color(1f, 0.78f, 0.15f);

    [Tooltip("The lattice at rest. Grey, so the pattern is visible without the gold being on all " +
             "the time — the gold is what the pointer and the arrow sweeps bring with them.")]
    public Color backdropIdle = new Color(0.105f, 0.110f, 0.125f);

    [Tooltip("Seconds the accent wave takes to cross the screen after an arrow press.")]
    public float waveTime = 0.7f;

    [Tooltip("Seconds a click shockwave takes to expand and fade.")]
    public float rippleTime = 0.85f;

    [Tooltip("How far a shockwave travels, in QUAD HEIGHTS — roughly screen heights. 1.6 carries " +
             "it off every edge even from a corner click, so it never visibly stops.")]
    public float rippleReach = 1.6f;

    Camera view;
    Transform mount;
    GameObject current;

    /// <summary>True while a car is actually standing on the podium.</summary>
    /// <remarks>
    /// Exists so callers can ask rather than assume. `MenuUI` caches the prefab it last showed
    /// and skips the rebuild when it has not changed — which is right for the common case (a
    /// price label changing must not respawn an 11,000-triangle car) and wrong the moment the
    /// car goes missing for any reason: the cache says "already showing that one" forever and
    /// the podium stays empty until the scene reloads.
    /// </remarks>
    public bool HasCar => current != null;

    /// <summary>What Show() was last asked for, so a missing car can be rebuilt.</summary>
    GameObject lastPrefab;

    /// <summary>
    /// Set when a prefab cannot be shown at all, to stop the self-heal retrying it every frame.
    /// </summary>
    bool cannotShow;

    /// <summary>
    /// Metres to slide the plinth and car sideways. NEGATIVE is screen-left. 0 is centred.
    /// </summary>
    /// <remarks>
    /// Used by the paint shop, which needs the right half of the screen for swatches and moves
    /// the car out from under them.
    ///
    /// **Only the plinth and the car move — never the rig.** The backdrop quad is a child of the
    /// rig too, and it deliberately OVERFILLS the frustum; sliding it would drag the lattice off
    /// one edge and expose the clear colour at the other.
    ///
    /// The shift is computed from the CAMERA'S right vector and converted into rig space, rather
    /// than assuming which way the rig's local X points. The rig is rotated to face back at the
    /// camera, so its local X is screen-LEFT — exactly the kind of sign that gets guessed wrong
    /// once and then hard-coded around.
    /// </remarks>
    public float StageOffset { get; set; }

    float stageShift;
    Material backdropMaterial;
    Transform backdrop;
    Transform limbo;
    GameObject podium;

    float spin;
    float settle = 1f;
    float waveStart = -99f;
    float waveDir = 1f;
    Vector2 pointer = new Vector2(0.5f, 0.5f);
    Vector2 smoothPointer = new Vector2(0.5f, 0.5f);
    Vector2 viewScale = Vector2.one;

    [Header("Read-only — watch these if a car does not appear")]
    [Tooltip("Metres the last car was lifted to sit on the plinth. A sane value is under a metre; " +
             "several metres means the measurement failed and the car is somewhere above the frame.")]
    [SerializeField] float lastLift;

    [Tooltip("Prefab last put on the podium.")]
    [SerializeField] string lastShown = "(none)";

    struct Ripple
    {
        public Vector2 uv;
        public float start;
    }

    // Two, alternated. See StartRipple.
    readonly Ripple[] ripples = new Ripple[2];
    int nextRipple;

    // Which ring the last click started, and when — so an arrow press can cancel exactly that
    // one without disturbing a shockwave still travelling from somewhere else.
    int lastRippleSlot = -1;
    int lastRippleFrame = -1;
    int suppressRippleFrame = -1;

    // How long after a press an arrow may still cancel its shockwave. Covers a slow click: the
    // ring starts on pointer DOWN and the button only reports on pointer UP.
    const float pressWindow = 0.6f;

    // Cached property ids. These are set every frame, and the string overloads hash the name
    // on each call — free to avoid, and this runs while a menu is idle.
    static readonly int PointerId = Shader.PropertyToID("_Pointer");
    static readonly int WaveId = Shader.PropertyToID("_Wave");
    static readonly int[] RippleIds =
    {
        Shader.PropertyToID("_Ripple0"),
        Shader.PropertyToID("_Ripple1"),
    };

    void Awake()
    {
        view = Camera.main;
        if (view == null) view = FindFirstObjectByType<Camera>();
        if (view == null)
        {
            Debug.LogError("CarPodium found no camera, so nothing will be visible.", this);
            enabled = false;
            return;
        }

        Build();
    }

    void Start()
    {
        // Park both rings in the past, or slot 0 draws a ring at the centre on the first frame.
        for (int i = 0; i < ripples.Length; i++) ripples[i].start = -99f;
    }

    void Build()
    {
        Transform cam = view.transform;

        // Pitch the camera down BEFORE anything is placed. The backdrop quad and the rig are
        // both positioned from cam.forward and cam.up, so rotating afterwards would leave the
        // backdrop hanging at an angle off the side of the frame and the podium out of shot.
        //
        // Yaw is left alone, so the scene camera can still be aimed wherever it likes.
        cam.rotation = Quaternion.Euler(pitch, cam.eulerAngles.y, 0f);

        Vector3 centre = cam.position + cam.forward * distance - cam.up * drop;

        transform.position = centre;
        transform.rotation = Quaternion.LookRotation(
            Vector3.ProjectOnPlane(-cam.forward, Vector3.up).normalized, Vector3.up);

        BuildBackdrop(cam);
        BuildLights(cam);
        BuildPodium();

        mount = new GameObject("CarMount").transform;
        mount.SetParent(transform, false);
        mount.localPosition = new Vector3(0f, podiumHeight, 0f);
    }

    void BuildBackdrop(Transform cam)
    {
        if (backdropShader == null) backdropShader = Shader.Find("CarCrash/Garage Backdrop");
        if (backdropShader == null)
        {
            Debug.LogWarning("CarPodium: Garage Backdrop shader not found, so the background " +
                             "will be whatever the camera clears to.", this);
            return;
        }

        backdropMaterial = new Material(backdropShader);
        backdropMaterial.SetColor("_Base", backdropBase);
        backdropMaterial.SetColor("_Accent", backdropAccent);
        backdropMaterial.SetColor("_Idle", backdropIdle);

        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Backdrop";
        Destroy(quad.GetComponent<Collider>());

        backdrop = quad.transform;
        backdrop.SetParent(transform, false);

        // Far enough back that the car never intersects it, and scaled to overfill the frustum
        // at that depth so no edge is ever visible however wide the window is.
        float depth = distance + 26f;
        float height = 2f * depth * Mathf.Tan(view.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float width = height * Mathf.Max(view.aspect, 2.4f);

        backdrop.position = cam.position + cam.forward * depth;
        backdrop.rotation = Quaternion.LookRotation(cam.forward, cam.up);
        backdrop.localScale = new Vector3(width * 1.15f, height * 1.15f, 1f);

        float quadWidth = width * 1.15f;
        float quadHeight = height * 1.15f;

        // The lattice needs the quad's real aspect or its cells stretch into lozenges on a
        // backdrop three times wider than it is tall.
        backdropMaterial.SetFloat("_Aspect", quadWidth / Mathf.Max(0.001f, quadHeight));

        // How much of the quad the camera actually SEES. The quad deliberately overfills the
        // frustum so no edge is ever visible, which means quad uv is not screen uv — the cursor
        // at the left edge of the screen is only part way across the quad. Without this the glow
        // travels less far than the mouse and never reaches the corners.
        float visibleWidth = height * view.aspect;
        viewScale = new Vector2(
            visibleWidth / Mathf.Max(0.001f, quadWidth),
            height / Mathf.Max(0.001f, quadHeight));

        MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = backdropMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    void BuildLights(Transform cam)
    {
        // A menu scene has no lights — the reference one is just a camera and a canvas — so a
        // car dropped into it renders black. Two lights, no shadows: a key from over the
        // camera's shoulder and a cool fill from the far side so the unlit flank is not a
        // silhouette.
        GameObject key = new GameObject("PodiumKey");
        key.transform.SetParent(transform, false);
        Light keyLight = key.AddComponent<Light>();
        keyLight.type = LightType.Directional;
        keyLight.color = new Color(1f, 0.96f, 0.90f);
        keyLight.intensity = 1.5f;
        keyLight.shadows = LightShadows.None;
        key.transform.rotation = Quaternion.Euler(38f, cam.eulerAngles.y + 35f, 0f);

        GameObject fill = new GameObject("PodiumFill");
        fill.transform.SetParent(transform, false);
        Light fillLight = fill.AddComponent<Light>();
        fillLight.type = LightType.Directional;
        fillLight.color = new Color(0.55f, 0.62f, 0.85f);
        fillLight.intensity = 0.55f;
        fillLight.shadows = LightShadows.None;
        fill.transform.rotation = Quaternion.Euler(18f, cam.eulerAngles.y - 130f, 0f);
    }

    void BuildPodium()
    {
        GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = "Podium";
        podium = disc;
        Destroy(disc.GetComponent<Collider>());
        disc.transform.SetParent(transform, false);

        // Unity's cylinder primitive is 2 units tall, hence the halved height.
        disc.transform.localPosition = new Vector3(0f, podiumHeight * 0.5f, 0f);
        disc.transform.localScale = new Vector3(podiumRadius * 2f, podiumHeight * 0.5f,
                                                podiumRadius * 2f);

        MeshRenderer body = disc.GetComponent<MeshRenderer>();
        body.sharedMaterial = FlatMaterial(podiumColour);
        body.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    Material FlatMaterial(Color colour)
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        Material mat = new Material(lit != null ? lit : Shader.Find("Sprites/Default"));
        mat.color = colour;
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.35f);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
        return mat;
    }

    /// <summary>
    /// Tint the car on the podium, so a paint can be judged before it is bought.
    /// </summary>
    /// <remarks>
    /// Goes through the car's own <see cref="CarPaint"/>, which is the same component the spawner
    /// uses in a run — so what the podium shows and what drives out of the garage cannot disagree.
    /// It writes a MaterialPropertyBlock, so this is safe on a prefab instance and does not
    /// instantiate a material.
    ///
    /// `CarPaint` is DISABLED on the podium car like everything else, which does not matter:
    /// `Apply` is a plain method, not something that runs on Update.
    /// </remarks>
    public void Preview(Color colour)
    {
        if (current == null) return;

        CarPaint paint = current.GetComponent<CarPaint>();
        if (paint != null) paint.Apply(colour);
    }

    /// <summary>Put a car on the podium. Pass null to clear it.</summary>
    public void Show(GameObject prefab)
    {
        if (current != null) Destroy(current);
        current = null;
        settle = 0f;

        // Remembered so the self-heal in Update can put it back if it goes missing.
        lastPrefab = prefab;
        cannotShow = false;

        if (mount == null) return;

        if (prefab == null)
        {
            // Silent before, and it looks exactly like the spawn failing. A roster entry with no
            // prefab is a wiring mistake, so say which one.
            lastShown = "(null prefab)";
            cannotShow = true;
            Debug.LogWarning("CarPodium: the selected car has no prefab assigned in the " +
                             "CarRoster, so the podium is empty.", this);
            return;
        }

        // Instantiated into an INACTIVE parent, so no Awake or OnEnable on the car ever runs.
        //
        // This is not tidiness, it is the fix for a real bug: spawning it live and then
        // destroying its components ran `CarDeformation.OnDestroy`, which destroys the mesh
        // instances that component cloned in its own Awake. Every MeshFilter it had cloned was
        // left pointing at a destroyed mesh, so the body and all eight panels rendered nothing —
        // and because WHEELS are the one thing excluded from the deformation panel list, the
        // tyres were the only part of the car that still appeared.
        //
        // Never letting Awake run avoids that whole class of problem rather than unpicking it:
        // no cloned meshes to lose, no `PlayerCar.Current` claimed, no physics step on a prop.
        if (limbo == null)
        {
            limbo = new GameObject("PodiumLimbo").transform;
            limbo.SetParent(transform, false);
            limbo.gameObject.SetActive(false);
        }

        current = Instantiate(prefab, limbo);
        Neutralise(current);

        current.transform.SetParent(mount, false);
        current.transform.localPosition = Vector3.zero;
        current.transform.localRotation = Quaternion.identity;

        // Sit the car ON the podium rather than through it. Measured from the geometry, because
        // a roster prefab's own origin is wherever the model was authored and is not reliably at
        // the tyre contact patch.
        if (MeasureCar(current, out Bounds bounds))
        {
            lastLift = mount.position.y - bounds.min.y;
            current.transform.position += Vector3.up * lastLift;
            lastShown = prefab.name;
        }
        else
        {
            lastShown = prefab.name + " (NO MESHES)";
            cannotShow = true;
            Debug.LogWarning("CarPodium: '" + prefab.name + "' has no meshes to show.", this);
        }
    }

    /// <summary>
    /// World bounds of a car, from its MESH data rather than from Renderer.bounds.
    /// </summary>
    /// <remarks>
    /// **`Renderer.bounds` is the obvious call here and it is not reliable at this moment.** It
    /// is world-space and Unity refreshes it on its own schedule, so reading it in the same frame
    /// the car is reparented — and in which `CarDeformation.Awake` swaps every panel's mesh for a
    /// clone — can hand back stale bounds sitting at the world origin. `min.y` then comes out
    /// near zero, the lift becomes the podium's full height, and the car is flung metres into the
    /// air: an empty podium, intermittently, depending on what Unity had got round to updating.
    ///
    /// Transforming the mesh's own local bounds is immediate and deterministic. It also works on
    /// an inactive hierarchy, which `Renderer.bounds` does not, so the measurement no longer
    /// depends on whether the showcase happens to be switched on yet.
    /// </remarks>
    static bool MeasureCar(GameObject car, out Bounds bounds)
    {
        bounds = default;
        bool any = false;

        foreach (MeshFilter filter in car.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null) continue;

            Vector3 centre = mesh.bounds.center;
            Vector3 extents = mesh.bounds.extents;

            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 local = centre + new Vector3(
                    (corner & 1) == 0 ? -extents.x : extents.x,
                    (corner & 2) == 0 ? -extents.y : extents.y,
                    (corner & 4) == 0 ? -extents.z : extents.z);

                Vector3 world = filter.transform.TransformPoint(local);

                if (any) bounds.Encapsulate(world);
                else { bounds = new Bounds(world, Vector3.zero); any = true; }
            }
        }

        return any;
    }

    /// <summary>
    /// Turn a live vehicle prefab into a showroom model.
    /// </summary>
    /// <remarks>
    /// Called while the car is still parented under the inactive limbo, so none of these
    /// components has run a single line — there is no cloned mesh to lose, no `PlayerCar.Current`
    /// to release, nothing to unpick. That is the whole reason for the limbo.
    ///
    /// **DISABLED, not destroyed — and that is the second correction here.** Destroying looked
    /// tidier and cannot work, because the vehicle stack is a RequireComponent chain:
    ///
    ///     PlayerCar -> CarDamage -> CarController -> Rigidbody
    ///
    /// Unity REFUSES to remove a component another one depends on, so destroying in an arbitrary
    /// order silently left `CarController` in place, which in turn made the `Rigidbody`
    /// unremovable. The result was a live physics body whose suspension casts find nothing under
    /// a menu podium, so the car sank gently through it and off the bottom of the screen.
    ///
    /// Disabling has no ordering problem at all. `Awake` still runs when the car is reparented
    /// into the live hierarchy — Awake ignores `enabled` — but nothing else does: no `OnEnable`,
    /// so `PlayerCar` never claims `PlayerCar.Current`; no `Update` or `FixedUpdate`, so nothing
    /// drives or steers; and `CarDeformation` keeps the meshes it clones instead of freeing them
    /// out from under the renderers.
    ///
    /// The Rigidbody is made KINEMATIC rather than removed, which is what actually guarantees the
    /// car cannot move however the rest of it is configured.
    /// </remarks>
    static void Neutralise(GameObject car)
    {
        foreach (MonoBehaviour behaviour in car.GetComponentsInChildren<MonoBehaviour>(true))
            if (behaviour != null) behaviour.enabled = false;

        foreach (Rigidbody body in car.GetComponentsInChildren<Rigidbody>(true))
        {
            if (body == null) continue;
            body.isKinematic = true;
            body.detectCollisions = false;
        }

        // Nothing in a menu belongs in the physics broadphase, and the car's three body boxes
        // would otherwise sit there being swept for no reason.
        foreach (Collider collider in car.GetComponentsInChildren<Collider>(true))
            if (collider != null) collider.enabled = false;
    }

    /// <summary>
    /// Show or hide the plinth and the car, WITHOUT touching the backdrop.
    /// </summary>
    /// <remarks>
    /// The backdrop is the background for the whole front end, not a garage decoration, so it
    /// stays on for every page — the menu, map select and options all sit against it. Only the
    /// showcase itself belongs to the garage.
    ///
    /// This is why the component is never disabled as a whole: doing that took the backdrop, the
    /// lights and the pointer tracking down with it, and every other page fell back to a flat
    /// black rectangle.
    /// </remarks>
    public void SetShowcase(bool visible)
    {
        if (podium != null) podium.SetActive(visible);
        if (mount != null) mount.gameObject.SetActive(visible);
    }

    /// <summary>
    /// Kick off a sweep of accent light. +1 for right, -1 for left.
    /// </summary>
    /// <remarks>
    /// Also suppresses the click shockwave, because the sweep IS the visual for an arrow and
    /// running both makes two effects fight over one click.
    ///
    /// It has to work BOTH WAYS ROUND, because the order of a uGUI button's onClick against this
    /// component's Update is not defined: if the button fires first, the frame guard stops the
    /// ring being started at all; if this component's Update ran first, the ring has already
    /// begun and is cancelled retroactively. Relying on either one alone leaves a coin-flip.
    /// </remarks>
    public void Sweep(float direction)
    {
        waveStart = Time.unscaledTime;
        waveDir = direction >= 0f ? 1f : -1f;
        settle = 0f;

        // Cancel the ring this click already started.
        //
        // Matching on the FRAME was wrong and is why the ring kept appearing: a uGUI button
        // raises onClick on pointer UP, while the shockwave starts on pointer DOWN, so by the
        // time this runs the ring is several frames old and no frame comparison can catch it.
        // A press-and-release is comfortably inside pressWindow, and only the MOST RECENT ring
        // is ever cancelled, so a shockwave still travelling from an earlier click elsewhere is
        // left alone.
        if (lastRippleSlot >= 0 &&
            Time.unscaledTime - ripples[lastRippleSlot].start < pressWindow)
        {
            ripples[lastRippleSlot].start = -99f;
        }

        // And block one for the rest of this frame and the next, covering the case where the
        // button's onClick lands after Update has already looked at the mouse.
        suppressRippleFrame = Time.frameCount + 1;
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        // SELF-HEAL. An empty podium was reported as intermittent — sometimes no car at all,
        // once a car that appeared and vanished a second later. Rather than chase every way an
        // instance can go missing, the podium now notices and puts it back.
        //
        // Worth doing even once a specific cause is found, because the failure is silent and
        // total: nothing errors, the plinth is simply bare, and MenuUI's prefab cache means it
        // stays bare for the rest of the session. `Show` is only called when there is genuinely
        // no car, so this cannot loop, and `cannotShow` stops it retrying a prefab that has no
        // meshes or no prefab at all.
        //
        // It logs, because a self-heal that hides the problem is worse than the problem.
        if (current == null && lastPrefab != null && !cannotShow
            && mount != null && mount.gameObject.activeInHierarchy)
        {
            Debug.LogWarning($"CarPodium: '{lastPrefab.name}' went missing from the podium. " +
                             "Rebuilding it. If this repeats, something is destroying the car " +
                             "after it is placed.", this);
            Show(lastPrefab);
        }

        // Unscaled throughout: the menu runs at timeScale 0 whenever it is reached from a
        // paused run, and a podium that stops turning there looks broken.
        settle = Mathf.Min(1f, settle + dt / Mathf.Max(0.01f, settleTime));

        // Slide the stage toward wherever the paint shop wants it. Eased rather than snapped:
        // the car moving aside is the transition INTO the paint shop, and a jump reads as the
        // panel having shoved it.
        if (!Mathf.Approximately(stageShift, StageOffset) || stageShift != 0f)
        {
            stageShift = Mathf.Lerp(stageShift, StageOffset, 1f - Mathf.Exp(-dt * 9f));

            Vector3 world = (view != null ? view.transform.right : Vector3.right) * stageShift;
            Vector3 local = transform.InverseTransformVector(world);

            if (podium != null)
                podium.transform.localPosition =
                    new Vector3(local.x, podiumHeight * 0.5f, local.z);

            if (mount != null)
                mount.localPosition = new Vector3(local.x, podiumHeight, local.z);
        }

        if (mount != null)
        {
            // Eases in after a swap, so a change of car has a little motion to it.
            spin += spinSpeed * Mathf.Lerp(2.4f, 1f, settle) * dt;
            mount.localRotation = Quaternion.Euler(0f, spin, 0f);
        }

        UpdateBackdrop();
    }

    /// <summary>
    /// Start a shockwave from a point in SCREEN uv.
    /// </summary>
    /// <remarks>
    /// Two slots, used alternately, so clicking again while a ring is still travelling starts a
    /// second one instead of teleporting the first back to the cursor — which is what a single
    /// slot does, and it reads as a glitch rather than a second click.
    /// </remarks>
    void StartRipple(Vector2 screenUv)
    {
        // An arrow press is already saying it with the sweep; a ring on top is two effects
        // fighting over the same click.
        if (Time.frameCount <= suppressRippleFrame) return;

        lastRippleSlot = nextRipple;
        lastRippleFrame = Time.frameCount;

        ripples[nextRipple].uv = ToQuadUv(screenUv);
        ripples[nextRipple].start = Time.unscaledTime;
        nextRipple = (nextRipple + 1) % ripples.Length;
    }

    /// <summary>
    /// Screen uv -> quad uv. The quad overfills the frustum, so the two are different spaces
    /// and using one for the other puts everything off-centre by more the further out it is.
    /// </summary>
    Vector2 ToQuadUv(Vector2 screenUv)
    {
        return new Vector2(0.5f + (screenUv.x - 0.5f) * viewScale.x,
                           0.5f + (screenUv.y - 0.5f) * viewScale.y);
    }

    void UpdateBackdrop()
    {
        if (backdropMaterial == null) return;

        // Pointer in viewport space, which is the shader's uv.
        //
        // Mouse.current, NOT Input.mousePosition. This project uses the Input System package, so
        // the legacy Input class is switched off and reading it throws — the same reason
        // UiKit.EnsureEventSystem has to create an InputSystemUIInputModule rather than the
        // StandaloneInputModule, and the reason every button would silently be dead without it.
        //
        // Clamped rather than skipped when the cursor is off-window, so the glow parks at the
        // edge instead of snapping back to the middle.
        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            Vector2 p = mouse.position.ReadValue();
            pointer = new Vector2(
                Mathf.Clamp01(p.x / Mathf.Max(1f, Screen.width)),
                Mathf.Clamp01(p.y / Mathf.Max(1f, Screen.height)));
        }

        // Eased, so the glow trails the cursor slightly instead of teleporting with it. Costs
        // one lerp and is most of what makes it feel like light rather than a decal.
        smoothPointer = Vector2.Lerp(smoothPointer, pointer,
                                     1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));

        // On RELEASE, not press.
        //
        // A ring started on press is visible for as long as the button is held, so cancelling it
        // when an arrow finally reports its click showed a shockwave that started and then
        // vanished. Release is also the moment a uGUI button raises onClick, so the ring and the
        // suppression now happen in the SAME frame: whichever of the two runs first, the ring is
        // either never started or is cancelled before anything is drawn.
        //
        // Fired from the RAW pointer, not the eased one, so it starts exactly under the cursor
        // rather than wherever the trailing glow has caught up to.
        if (mouse != null && mouse.leftButton.wasReleasedThisFrame) StartRipple(pointer);

        Vector2 quadUv = ToQuadUv(smoothPointer);
        backdropMaterial.SetVector(PointerId, new Vector4(quadUv.x, quadUv.y, 0f, 0f));

        for (int i = 0; i < ripples.Length; i++)
        {
            float ringAge = (Time.unscaledTime - ripples[i].start) / Mathf.Max(0.01f, rippleTime);

            // Radius grows linearly, brightness falls off squared — an expanding ring that keeps
            // its speed while thinning out, which is what a shockwave looks like. A linear fade
            // reaches zero visibly and reads as the ring being switched off.
            float ringStrength = ringAge <= 1f ? (1f - ringAge) * (1f - ringAge) : 0f;

            backdropMaterial.SetVector(RippleIds[i],
                new Vector4(ripples[i].uv.x, ripples[i].uv.y,
                            ringAge * rippleReach, ringStrength));
        }

        float age = Time.unscaledTime - waveStart;
        float t = age / Mathf.Max(0.01f, waveTime);

        // Travels from just off one edge to just off the other, then switches itself off. w = 0
        // is the "no wave" state, so the shader does no work for it beyond one multiply.
        float strength = t <= 1f ? 1f - t * t : 0f;
        float position = Mathf.Lerp(-0.2f, 1.2f, Mathf.Clamp01(t));

        backdropMaterial.SetVector(WaveId, new Vector4(position, waveDir, 0.16f, strength));
    }

    void OnDestroy()
    {
        if (backdropMaterial != null) Destroy(backdropMaterial);
    }
}
